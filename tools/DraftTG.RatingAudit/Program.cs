using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using DraftTG.Application;
using DraftTG.ArenaIntegration;
using DraftTG.Data;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;
using DraftTG.RatingAudit;
using Microsoft.Data.Sqlite;
using Microsoft.VisualBasic.FileIO;

var options = new Dictionary<string,string>();
for (var i=0; i<args.Length; i+=2)
    if (!args[i].StartsWith("--") || i+1>=args.Length || !options.TryAdd(args[i][2..],args[i+1]))
        throw new ArgumentException("--set FRA --format QuickDraft [--output artifacts/rating-audit] [--reference CSV] [--pack-ledger JSONL] [--app-data DIR]");
var set = options.GetValueOrDefault("set", "FRA").ToUpperInvariant();
if (set is "MH3" or "DFT") throw new ArgumentException("Untouched research holdouts excluded from this QA run.");
var providerFormat = Enum.Parse<SeventeenLandsFormat>(options.GetValueOrDefault("format","QuickDraft"));
if (!Enum.IsDefined(providerFormat)) throw new ArgumentException("Unsupported format.");
var format = providerFormat == SeventeenLandsFormat.TradDraft ? LimitedStatisticsFormat.TraditionalDraft
    : providerFormat == SeventeenLandsFormat.QuickDraft ? LimitedStatisticsFormat.QuickDraft : LimitedStatisticsFormat.PremierDraft;
var context = new LimitedStatisticsContext(set,format);
var directory = options.GetValueOrDefault("app-data", ApplicationDataPathProviderFactory.CreateDefault().GetApplicationDataDirectory());
var output = Path.GetFullPath(options.GetValueOrDefault("output","artifacts/rating-audit")); Directory.CreateDirectory(output);
var paths = new AuditPaths(directory);
var catalogPath = Path.Combine(directory,"card-data","scryfall-default-cards.jsonl.gz");
var imported = await new ScryfallJsonlGzipCardDataLoader().LoadAsync(catalogPath);
var catalog = imported.Catalog;
var database = ArenaCardDatabaseLocator.FindNewest() ?? throw new InvalidDataException("Arena local printing database missing.");
var groups = new List<int>();
using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=database,Mode=SqliteOpenMode.ReadOnly }.ToString()))
{
    connection.Open(); using var command=connection.CreateCommand();
    command.CommandText="select distinct GrpId from Cards where upper(ExpansionCode)=$set and IsToken=0 and IsRebalanced=0 order by GrpId";
    command.Parameters.AddWithValue("$set",set); using var reader=command.ExecuteReader(); while(reader.Read()) groups.Add(reader.GetInt32(0));
}
var resolver = new ArenaCardResolver(imported,ArenaDatabasePrintingIdentitySource.CreateDefault());
var resolutions=groups.ToDictionary(x=>x,x=>resolver.Resolve(ArenaCardIdentifier.Create(x)));
var ids=resolutions.Values.Where(x=>x.CardIdentifier is not null).Select(x=>x.CardIdentifier!).Distinct().ToArray();
var snapshot=new DraftSnapshot(new(new(PackNumber.Create(1),PickNumber.Create(1)),ids),new(),DraftFormat.BestOfOne);
// Reuse the production cache decoder and validation. HTTP is intercepted locally; this tool never acquires provider data.
using var http=new HttpClient(new OfflineHandler()) { BaseAddress=new("https://www.17lands.com") };
var client=new SeventeenLandsCardRatingsClient(http,paths);
var primary=await client.LoadAsync(set,providerFormat);
var mapped=LimitedStatisticsMapper.Map(primary.Rows,catalog,context,snapshot);
var environment=LimitedStatisticsMapper.MapEnvironment(primary.Rows,catalog,context);
var recommendations=new StatisticalRecommendationEngine().Recommend(snapshot.CurrentPack,mapped.Catalog,environment);
var baseline=recommendations.EnvironmentBaseline;
SeventeenLandsRatingsResult? alternate=null; LimitedStatisticsMappingResult? alternateMapped=null; double? alternateBaseline=null;
var sourceFormat=SparsePrimaryQualityPrior.SourceFormat(context);
if(sourceFormat is not null)
{
    alternate=await client.LoadAsync(set,SeventeenLandsFormat.PremierDraft);
    alternateMapped=LimitedStatisticsMapper.Map(alternate.Rows,catalog,new(set,sourceFormat.Value),snapshot);
    alternateBaseline=new StatisticalRecommendationEngine().Recommend(snapshot.CurrentPack,alternateMapped.Catalog,
        LimitedStatisticsMapper.MapEnvironment(alternate.Rows,catalog,new(set,sourceFormat.Value))).EnvironmentBaseline;
}
var recommendationById=recommendations.Cards.ToDictionary(x=>x.CardIdentifier);
var rows=groups.Select(grp=>
{
    var identity=resolutions[grp]; var card=identity.CardIdentifier is { } id ? catalog.Find(id):null;
    var stat=card is null?null:mapped.Catalog.StatisticsFor(card.Identifier);
    AlternateFormatQualityPrior? prior=null;
    var alternateStat=card is null?null:alternateMapped?.Catalog.StatisticsFor(card.Identifier);
    if(card is not null && baseline is { } b && alternateBaseline is { } ab && stat is not null
        && mapped.Resolutions.GetValueOrDefault(card.Identifier)?.IsResolved==true && SparsePrimaryQualityPrior.HasUsableGih(alternateStat)
        && primary.Metadata is { SourceEndpoint:"/api/card_data",TimePeriod:"ALL_TIME" }
        && alternate?.Metadata is { SourceEndpoint:"/api/card_data",TimePeriod:"ALL_TIME" })
        prior=SparsePrimaryQualityPrior.TryCreate(card,stat,b,new(card.Identifier,context,new(set,sourceFormat!.Value),
            alternateStat!.GameInHandWinRate!.Value,alternateStat.GameInHandGameCount!.Value,ab,
            alternateMapped!.ResolvedNames[card.Identifier],alternate.FetchedAt,alternate.Source.ToString(),alternate.Metadata.TimePeriod));
    var row=RatingAuditClassifier.Create(card,grp,identity.Status,card is null?null:mapped.Resolutions.GetValueOrDefault(card.Identifier),
        stat,card is null?null:recommendationById.GetValueOrDefault(card.Identifier),baseline,prior);
    return row with { AlternateGIH=alternateStat?.GameInHandWinRate,AlternateGames=alternateStat?.GameInHandGameCount,
        PrimaryRowPresent=stat is not null,AlternateRowPresent=alternateStat is not null,AlternateBaseline=alternateBaseline,
        IsBasicLand=card?.GameplayMetadata.IsBasicLand==true };
}).ToArray();
// Auxiliary identity provenance from the owned cache. No new domain/scoring schema or provider request.
var oracleByPrinting=new Dictionary<string,string>();
using(var file=File.OpenRead(catalogPath))
using(var gzip=new System.IO.Compression.GZipStream(file,System.IO.Compression.CompressionMode.Decompress))
using(var reader=new StreamReader(gzip))
    while(reader.ReadLine() is {} line)
    {
        using var document=JsonDocument.Parse(line);var root=document.RootElement;
        if(root.TryGetProperty("id",out var id)&&root.TryGetProperty("oracle_id",out var oracle)
            &&id.GetString() is {} identifier&&oracle.GetString() is {} logical)oracleByPrinting[identifier]=logical;
    }
var proven=rows.Where(r=>r.Identifier is not null&&oracleByPrinting.ContainsKey(r.Identifier))
    .GroupBy(r=>oracleByPrinting[r.Identifier!]).Where(g=>
    {
        var cards=g.Select(r=>catalog.Find(CardIdentifier.Create(r.Identifier!))!).ToArray();var first=cards[0];
        return cards.All(c=>c.Name==first.Name&&c.Colors==first.Colors&&c.GameplayMetadata.ColorsKnown
            &&c.GameplayMetadata.CardTypes.IsKnown&&c.GameplayMetadata.ManaValue is not null
            &&c.GameplayMetadata.TypeLine is not null&&c.GameplayMetadata.OracleText is not null
            &&c.GameplayMetadata.Equals(first.GameplayMetadata));
    }).Select(g=>g.Key).ToHashSet();
rows=rows.Select(r=>r.Identifier is {} id&&oracleByPrinting.TryGetValue(id,out var oracle)&&proven.Contains(oracle)
    ?r with {ProvenLogicalIdentity="scryfall-oracle:"+oracle}:r).ToArray();
var printing= new ArenaCardDatabaseReader(database).ReadPrintingIdentities(groups);
rows=rows.Select(row=>row.IdentityMissing && row.ArenaGrpId is { } grp && printing.Printings.TryGetValue(grp,out var p)
    ? row with { Name=p.EnglishTitle??"<unresolved>",Set=p.ExpansionCode,Collector=p.CollectorNumber }:row).ToArray();
var json=new JsonSerializerOptions { WriteIndented=true,Converters={new JsonStringEnumConverter()} };
void Write(string name,object value)=>File.WriteAllText(Path.Combine(output,name),JsonSerializer.Serialize(value,json));
string Hash(string path) { using var file=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(file)); }
Write("cards.json",rows);
if(options.TryGetValue("printing-proof",out var requestedPrintings))
{
    var policy=new PreferredBasicPrintingReconciler(resolver,catalog);
    Write("printing-proof.json",requestedPrintings.Split(',').Select(pair=>
    {
        var parts=pair.Split(':');if(parts.Length!=2)throw new ArgumentException("--printing-proof pod:visible,pod:visible");
        var pod=uint.Parse(parts[0]);var visible=uint.Parse(parts[1]);
        Card? Card(uint grp)=>catalog.Find(resolver.Resolve(ArenaCardIdentifier.Create((int)grp)).CardIdentifier!);
        return new {Proof=policy.Reconcile(pod,visible),PodCard=Card(pod),VisibleCard=Card(visible),
            Evidence="Owned metadata proof only; physical live evidence separately recorded"};
    }).ToArray());
}
var neutralGroups=StatRiskReview.Group(rows.Where(r=>r.NeutralFallback));
var priorGroups=StatRiskReview.Group(rows.Where(r=>r.Fallback));
object Distribution(IEnumerable<double> source)
{
    var values=source.Order().ToArray();
    double? Quantile(double p)=>values.Length==0?null:values[(int)Math.Floor((values.Length-1)*p)];
    return new {Count=values.Length,Min=Quantile(0),P25=Quantile(.25),Median=Quantile(.5),P75=Quantile(.75),Max=Quantile(1)};
}
Write("stat-risk-review.json",new {Schema="phase9f4-stat-risk-v1",ScoringChanged=false,
    Policy="Prospective plan-and-preservation.json thresholds; logical-card weighted distributions; per-printing rows retained",
    NeutralPrintingCount=rows.Count(r=>r.NeutralFallback&&!r.IsBasicLand),NeutralLogicalCount=neutralGroups.Length,Neutral=neutralGroups,
    BasicNeutralPrintingCount=rows.Count(r=>r.NeutralFallback&&r.IsBasicLand),
    PriorPrintingCount=rows.Count(r=>r.Fallback),PriorLogicalCount=priorGroups.Length,Prior=priorGroups,
    Distributions=new {PrimaryN=Distribution(priorGroups.Select(g=>g.Printings[0].PrimaryGames).OfType<int>().Select(x=>(double)x)),
        AlternateN=Distribution(priorGroups.Select(g=>g.Printings[0].AlternateGames).OfType<int>().Select(x=>(double)x)),
        AlternateDelta=Distribution(priorGroups.Select(g=>g.Printings[0]).Where(r=>r.AlternateGIH is not null&&r.AlternateBaseline is not null).Select(r=>r.AlternateGIH!.Value-r.AlternateBaseline!.Value)),
        Q=Distribution(priorGroups.Select(g=>g.Printings[0].Q).OfType<double>()),Shift=Distribution(priorGroups.Select(g=>g.PriorShift).OfType<double>())},
    Top20Absolute=priorGroups.Take(20),TopPositive=priorGroups.Where(g=>g.PriorShift>0).Take(20),TopNegative=priorGroups.Where(g=>g.PriorShift<0).Take(20),
    UntappedAvailable=false,ComparedCards=0,ProductionDefectEstablished=false});
var comparison=options.TryGetValue("reference",out var csv) ? ReferenceComparison.Read(csv,rows,set,providerFormat.ToString())
    : new { Available=false,ComparedCards=0,Spearman=(double?)null,Reason="No legitimate user-visible reference supplied." } as object;
Write("comparison.json",comparison!);
if(options.TryGetValue("pack-reference",out var packReference))
{
    if(!options.TryGetValue("pack-ledger",out var packLedger))throw new ArgumentException("Pack comparison requires --pack-ledger and reference PackKey.");
    Write("pack-comparison.json",PackAudit.Compare(packLedger,packReference));
}
var summary=new
{
    Schema="drafttg-rating-audit-v1",Set=set,Format=providerFormat.ToString(),Model="contextual-pick-score-v1.6.2",TotalCards=rows.Length,
    Unit="Distinct non-token/non-rebalanced Arena GrpIds in exact set; includes basic lands and alternate art printings; not asserted booster collation.",
    Counts=rows.GroupBy(x=>x.Coverage.ToString()).ToDictionary(g=>g.Key,g=>g.Count()),
    ExactPrimary=rows.Count(x=>x.Coverage==RatingCoverage.ExactPrimary),SparsePrimary=rows.Count(x=>x.Coverage==RatingCoverage.SparsePrimary),
    SparsePrimaryWithPrior=0,AlternatePrior=rows.Count(x=>x.Fallback),Neutral=rows.Count(x=>x.NeutralFallback),
    MissingIdentity=rows.Count(x=>x.IdentityMissing),AmbiguousIdentity=rows.Count(x=>x.IdentityAmbiguous),
    MissingStatRow=rows.Count(x=>x.StatRowMissing),NullGIH=rows.Count(x=>x.GihNull),MissingSampleCount=rows.Count(x=>x.SampleCountMissing),
    PrimaryUsableCoveragePercent=100d*rows.Count(x=>x.Coverage is RatingCoverage.ExactPrimary or RatingCoverage.SparsePrimary)/Math.Max(1,rows.Length),
    QualityEvidenceCoveragePercent=100d*rows.Count(x=>!x.NeutralFallback&&!x.IdentityAmbiguous&&!x.IdentityMissing)/Math.Max(1,rows.Length),
    PrimaryBaseline=baseline,PrimarySource=primary.Source.ToString(),primary.FetchedAt,primary.Diagnostic,
    AlternateSource=alternate?.Source.ToString(),AlternateBaseline=alternateBaseline,
    InputHashes=new { Catalog=Hash(catalogPath),Database=Hash(database),
        PrimaryCache=File.Exists(Path.Combine(directory,"limited-data","17lands",$"{set}_{providerFormat}_ALL_TIME_v3.json"))
            ?Hash(Path.Combine(directory,"limited-data","17lands",$"{set}_{providerFormat}_ALL_TIME_v3.json")):null,
        AlternateCache=sourceFormat is not null&&File.Exists(Path.Combine(directory,"limited-data","17lands",$"{set}_PremierDraft_ALL_TIME_v3.json"))
            ?Hash(Path.Combine(directory,"limited-data","17lands",$"{set}_PremierDraft_ALL_TIME_v3.json")):null },
    TopRisks=rows.Where(x=>x.Risks.Length>0).OrderByDescending(x=>x.Fallback).ThenBy(x=>x.PrimaryGames??0).Take(25).ToArray(),
    CurrentPack=options.TryGetValue("pack-ledger",out var ledger)?PackAudit.Read(ledger):null,
    Comparison=comparison,NoNetworkAcquisition=true,ScoringChanged=false
};
Write("summary.json",summary); Console.WriteLine(JsonSerializer.Serialize(summary,json));

sealed class AuditPaths(string path):IApplicationDataPathProvider { public string GetApplicationDataDirectory()=>path; }
sealed class OfflineHandler:HttpMessageHandler
{ protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)); }

static class PackAudit
{
    public static object Compare(string ledger,string reference)
    {
        if(Read(ledger) is not JsonElement pack)throw new InvalidDataException("Coherent scored pack not recorded.");
        using var csv=new TextFieldParser(reference) { TextFieldType=FieldType.Delimited,HasFieldsEnclosedInQuotes=true };
        csv.SetDelimiters(",");var header=csv.ReadFields()??[];
        string[] required=["PackKey","Set","Format","CardName","ArenaGrpId","UntappedRating","Source","ObservedAt"];
        if(!required.All(header.Contains))throw new InvalidDataException("Pack reference needs PackKey plus standard reference columns.");
        var ratings=new Dictionary<int,double>();
        while(!csv.EndOfData)
        {
            var fields=csv.ReadFields()!;if(fields.Length!=header.Length)throw new InvalidDataException("Reference column count mismatch.");
            string Get(string key)=>fields[Array.IndexOf(header,key)];
            var source=Get("Source");
            if(Get("PackKey")!=pack.GetProperty("SemanticPackKey").GetString()||Get("Set")!=pack.GetProperty("Set").GetString()
                ||Get("Format")!=pack.GetProperty("Format").GetString() || source is not ("Manual" or "Screenshot" or "VisibleUI" or "PublicExport")
                ||!DateTimeOffset.TryParse(Get("ObservedAt"),out _))throw new InvalidDataException("Same observed pack, format and legitimate source required.");
            var grp=int.Parse(Get("ArenaGrpId"));var rating=double.Parse(Get("UntappedRating"),System.Globalization.CultureInfo.InvariantCulture);
            if(!double.IsFinite(rating)||!ratings.TryAdd(grp,rating)
                ||!pack.GetProperty("Cards").EnumerateArray().Any(c=>c.GetProperty("GrpId").GetInt32()==grp&&c.GetProperty("Name").GetString()==Get("CardName")))
                throw new InvalidDataException("Invalid/duplicate/unmatched reference card.");
        }
        var cards=pack.GetProperty("Cards").EnumerateArray().ToArray();
        if(cards.Any(c=>!ratings.ContainsKey(c.GetProperty("GrpId").GetInt32())||c.GetProperty("FinalCurrentScore").ValueKind!=JsonValueKind.Number))
            throw new InvalidDataException("No partial pack comparison.");
        return new { Pack=pack,Comparison=RatingRankComparison.Pack(cards.Select(c=>c.TryGetProperty("ExactScore0To50",out var exact)
                ?exact.GetDouble():c.GetProperty("FinalCurrentScore").GetDouble()).ToArray(),
            cards.Select(c=>ratings[c.GetProperty("GrpId").GetInt32()]).ToArray()) };
    }
    public static object Read(string path)
    {
        JsonElement? latest=null;
        using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);
        using var reader=new StreamReader(stream);
        while(reader.ReadLine() is {} line)
        {
            using var document=JsonDocument.Parse(line); var root=document.RootElement;
            if(root.GetProperty("Kind").GetString()=="RatingAudit")latest=root.GetProperty("Data").Clone();
        }
        return latest is { } value ? value : new { Available=false,Reason="No coherent current-pack RatingAudit event in supplied ledger." } as object;
    }
}

static class ReferenceComparison
{
    public static object Read(string path,RatingAuditRow[] rows,string set,string format)
    {
        using var csv=new TextFieldParser(path) { TextFieldType=FieldType.Delimited,HasFieldsEnclosedInQuotes=true };
        csv.SetDelimiters(","); var header=csv.ReadFields()??throw new InvalidDataException("CSV header missing.");
        string[] required=["Set","Format","CardName","ArenaGrpId","UntappedRating","UntappedTier","Source","ObservedAt","Notes"];
        if(!required.All(header.Contains))throw new InvalidDataException("Reference schema columns missing.");
        var pairs=new List<(RatingAuditRow Row,double Rating)>(); var rejected=new List<string>(); var seen=new HashSet<string>();
        while(!csv.EndOfData)
        {
            var fields=csv.ReadFields()!; if(fields.Length!=header.Length)throw new InvalidDataException("CSV column count mismatch.");
            string Get(string name)=>fields[Array.IndexOf(header,name)];
            if(Get("Set")!=set||Get("Format")!=format) { rejected.Add(Get("CardName")+": FormatMismatch");continue; }
            if(Get("Source") is not ("Manual" or "Screenshot" or "VisibleUI" or "PublicExport")
                ||!DateTimeOffset.TryParse(Get("ObservedAt"),out _))throw new InvalidDataException("Legitimate source and observation date required.");
            if(!double.TryParse(Get("UntappedRating"),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var rating)
                ||!double.IsFinite(rating))throw new InvalidDataException("Finite manually supplied rating required.");
            var candidates=int.TryParse(Get("ArenaGrpId"),out var grp)?rows.Where(x=>x.ArenaGrpId==grp).ToArray():rows.Where(x=>x.Name==Get("CardName")).ToArray();
            if(candidates.Length==0||candidates.Any(x=>x.Name!=Get("CardName"))||candidates.Select(x=>x.Q).Distinct().Count()!=1||candidates[0].Q is null)
            { rejected.Add(Get("CardName")+": IdentityIssue/MissingStats");continue; }
            var row=candidates[0];var logicalKey=row.Name;
            if(!seen.Add(logicalKey))throw new InvalidDataException("Duplicate logical reference rating; supply one row per card.");
            pairs.Add((row,rating));
        }
        var q=pairs.Select(x=>x.Row.Q!.Value).ToArray();var external=pairs.Select(x=>x.Rating).ToArray();
        var qp=RatingRankComparison.Percentiles(q);var ep=RatingRankComparison.Percentiles(external);
        var disagreements=pairs.Select((p,i)=>new {p.Row.Name,p.Row.ArenaGrpId,QPercentile=qp[i],UntappedPercentile=ep[i],
            Difference=qp[i]-ep[i],p.Row.PrimaryGIH,p.Row.PrimaryGames,p.Row.Fallback,p.Row.Coverage,
            Explanation=p.Row.IdentityAmbiguous||p.Row.IdentityMissing?"IdentityIssue":p.Row.NeutralFallback?"MissingStats":
                p.Row.PrimaryGames<500?"SparseStats":p.Row.Fallback?"FormatMismatch":"Unknown" }).OrderByDescending(x=>Math.Abs(x.Difference)).ToArray();
        return new { Available=true,ComparedCards=pairs.Count,Scope="Matched reference subset; set-wide percentiles require complete set reference.",
            Spearman=RatingRankComparison.Spearman(q,external),Rejected=rejected,TopDisagreements=disagreements.Take(25).ToArray(),
            LargeGapThreshold=.3,LargeDisagreements=disagreements.Count(x=>Math.Abs(x.Difference)>=.3),
            DataGapAssociated=disagreements.Count(x=>Math.Abs(x.Difference)>=.3&&x.Explanation!="Unknown"),
            PackTop1Agreement=(double?)null,PackTop3Overlap=(double?)null,ReferenceIsGroundTruth=false };
    }
}
