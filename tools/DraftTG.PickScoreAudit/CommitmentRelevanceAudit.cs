using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using DraftTG.Application;
using DraftTG.Data;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;
using DraftTG.TestScenarios;

/// <summary>Frozen real inputs and production scoring, with offline diagnostics. No provider or Arena access.</summary>
internal static class CommitmentRelevanceAudit
{
    internal static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    internal static async Task RunAsync(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("--commitment-relevance-audit <fixture directory> <output.json>");
        var directory = Path.GetFullPath(args[0]);
        var p1p8Path = Path.Combine(directory, "phase9e2b-p1p8-rg-failure.json");
        var comparisonPath = Path.Combine(directory, "phase9f2-live-pack-v1.6.1.json");
        using var input = JsonDocument.Parse(await File.ReadAllTextAsync(p1p8Path));
        using var comparison = JsonDocument.Parse(await File.ReadAllTextAsync(comparisonPath));
        var update = Replay(input.RootElement, comparison.RootElement, ContextualPickScoreVersion.V1_6_1);
        var after = Replay(input.RootElement, comparison.RootElement, ContextualPickScoreVersion.V1_6_2);
        var snapshot = update.Snapshot!; var catalog = update.Result!.CardCatalog;
        var inputDeck = new DeckBuildInput(new(snapshot.DraftedPool, DraftPoolCompleteness.Complete), catalog,
            update.Result.Catalog, update.Result.EnvironmentCatalog);
        var projections = new List<CandidateProjectionAudit>();
        _ = new CandidateDeckImpactEvaluator().Evaluate(snapshot, inputDeck, update.ContextualRecommendation!.Profile,
            environmentBaseline: update.Recommendation!.EnvironmentBaseline, diagnostic: projections.Add);
        var result = new
        {
            Source = "Exact real FRA QuickDraft P1P8; offline freeze/replay, no live evidence", Set = "FRA", Format = "QuickDraft", Pack = 1, Pick = 8,
            SourceFixtureSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(p1p8Path))),
            SparseFixtureSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(comparisonPath))),
            Pool = snapshot.DraftedPool.CardIdentifiers.Select(i => new { Identity = i.Value, Name = catalog.Find(i)!.Name, Colors = catalog.Find(i)!.Colors }),
            History = snapshot.History.Picks.Select(p => new { Pack = p.Position.Pack.Value, Pick = p.Position.Pick.Value, Card = p.SelectedCardIdentifier.Value }),
            Commitment = update.ContextualRecommendation.Profile, PairEvidence = DeckPlanSelector.RankPairs(inputDeck),
            Cards = update.PickScores!.Cards.Select(c => new
            {
                Name = catalog.Find(c.CardIdentifier)!.Name, Identity = c.CardIdentifier.Value, Colors = catalog.Find(c.CardIdentifier)!.Colors,
                Score = c, Exact = c.ContextualValue!.Value*50,
                Projections = projections.Single(p => p.Candidate == c.CardIdentifier).Pairs.Select(p => new
                { p.Pair, p.Quality, p.Spells, p.Shortfall, p.BlendWeight, p.CandidateEligibility, p.CandidateInDeck, p.CutlineMargin,
                    Main = p.Main.OrderBy(e => e.Key.Value).Select(e => new { Identity = e.Key.Value, e.Value }) }),
                Diagnostics = update.Occurrences[new(c.PackIndex, c.CardIdentifier)].DiagnosticText,
                After = after.PickScores!.Cards[c.PackIndex], AfterExact = after.PickScores.Cards[c.PackIndex].ContextualValue!.Value*50,
                AfterDiagnostics = after.Occurrences[new(c.PackIndex,c.CardIdentifier)].DiagnosticText
            }),
            UldarosNonRegression = Uldaros(directory,comparison.RootElement),
            SameRealOfferedPackTransitions = RealPackTransitions(update),
            Scenarios = Scenarios(), Continuity = Continuity(), NetworkRequests = 0
        };
        var output = Path.GetFullPath(args[1]); Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(result, Options));
        Console.WriteLine($"Frozen/replayed v1.6.1 P1P8: {output}; color commitment {update.ContextualRecommendation.Profile.CommitmentStrength:R}");
        foreach(var c in after.PickScores!.Cards.OrderBy(c=>c.CurrentPackRank))
            Console.WriteLine($"{catalog.Find(c.CardIdentifier)!.Name}: {c.ContextualValue!.Value*50:R} -> {c.Score0To50}; rank {c.CurrentPackRank}; C={c.ColorCommitment?.Strength:R}; R={c.QualityRelevance:R}");
    }

    private static object RealPackTransitions(LimitedStatisticsUpdate reference)
    {
        var original=reference.Snapshot!;
        return Enumerable.Range(0,8).Select(n=>
        {
            var snapshot=new DraftSnapshot(new(new(PackNumber.Create(1),PickNumber.Create(n+1)),original.CurrentPack.AvailableCardIdentifiers),
                new(original.History.Picks.Take(n)),DraftFormat.BestOfOne)
            {RecoveredPool=new(original.DraftedPool.CardIdentifiers.Take(n))};
            var result=new LimitedStatisticsUpdate(snapshot,false,reference.Result,draftPool:new(snapshot.DraftedPool,DraftPoolCompleteness.Complete),
                pickScoreConfiguration:new(version:ContextualPickScoreVersion.V1_6_2));
            return new{Picks=n,Cards=result.PickScores!.Cards.Select(c=>new{Name=reference.Result!.CardCatalog.Find(c.CardIdentifier)!.Name,Score=c,Exact=c.ContextualValue!.Value*50})};
        }).ToArray();
    }

    private static object Uldaros(string directory,JsonElement comparison)
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"phase9f2-live-pack-stat-provenance.json")));
        var root=doc.RootElement;var catalog=new ScryfallCardCatalogDecoder().DecodeCatalogData(root.GetProperty("Cards").GetRawText()).Catalog;
        var ids=root.GetProperty("OrderedIdentities").EnumerateArray().Select(i=>CardIdentifier.Create(i.GetString()!)).ToArray();
        var snapshot=new DraftSnapshot(new(new(PackNumber.Create(1),PickNumber.Create(1)),ids),new(),DraftFormat.BestOfOne);
        LimitedCardStatistics Stat(JsonElement s)=>new(CardIdentifier.Create(s.GetProperty("CardIdentifier").GetProperty("Value").GetString()!),Integer(s,"GameInHandGameCount"),
            GameInHandWinRate:Number(s,"GameInHandWinRate"),AverageLastSeenAt:Number(s,"AverageLastSeenAt"));
        var stats=new LimitedCardStatisticsCatalog(root.GetProperty("Expected").EnumerateArray().Select(r=>Stat(r.GetProperty("Statistics"))));
        var env=new LimitedCardStatisticsCatalog(root.GetProperty("EnvironmentStatistics").EnumerateArray().Select(Stat));
        var priors=comparison.GetProperty("AlternateQualityEvidence").EnumerateArray().Select(e=>new AlternateFormatQualityEvidence(
            CardIdentifier.Create(e.GetProperty("CardIdentifier").GetProperty("Value").GetString()!),new("FRA",LimitedStatisticsFormat.QuickDraft),new("FRA",LimitedStatisticsFormat.PremierDraft),
            e.GetProperty("RawGameInHandWinRate").GetDouble(),e.GetProperty("GameInHandGameCount").GetInt32(),e.GetProperty("SourceEnvironmentBaseline").GetDouble(),
            e.GetProperty("ProviderName").GetString()!,e.GetProperty("FetchedAt").GetDateTimeOffset(),e.GetProperty("Source").GetString()!,"ALL_TIME")).ToDictionary(e=>e.CardIdentifier);
        var context=new LimitedStatisticsContext("FRA",LimitedStatisticsFormat.QuickDraft);
        var resolutions=root.GetProperty("Expected").EnumerateArray().ToDictionary(r=>CardIdentifier.Create(r.GetProperty("Identifier").GetProperty("Value").GetString()!),
            r=>new StatisticalIdentityResolution(Enum.Parse<StatisticalIdentityKind>(r.GetProperty("StatisticalIdentity").GetProperty("Kind").GetString()!),r.GetProperty("LookupKey").GetString()));
        var result=new LimitedStatisticsLoadResult(context,context,stats,LimitedStatisticsSource.Cache,null,null)
        {CardCatalog=catalog,EnvironmentCatalog=env,AlternateQualityEvidence=priors,StatisticsResolutions=resolutions};
        var a=new LimitedStatisticsUpdate(snapshot,false,result,pickScoreConfiguration:new(version:ContextualPickScoreVersion.V1_6_1));
        var b=new LimitedStatisticsUpdate(snapshot,false,result,pickScoreConfiguration:new(version:ContextualPickScoreVersion.V1_6_2));
        return b.PickScores!.Cards.Select(c=>new{Name=catalog.Find(c.CardIdentifier)!.Name,Before=a.PickScores!.Cards[c.PackIndex],After=c,
            ScoreUnchanged=a.PickScores.Cards[c.PackIndex].ContextualValue==c.ContextualValue,PriorUnchanged=a.PickScores.Cards[c.PackIndex].QualityPrior==c.QualityPrior});
    }

    private static object Scenarios()
    {
        var blue=CommitmentRelevanceScenarios.Card("ordinary-blue","{2}{U}",.58);
        var bomb=CommitmentRelevanceScenarios.Card("premium-blue","{3}{U}",.70,manaValue:4);
        var green=CommitmentRelevanceScenarios.Card("on-plan-green","{2}{G}",.561);
        var transitions=Enumerable.Range(0,15).Select(n=>
        {
            var result=CommitmentRelevanceScenarios.Score([blue,green],CommitmentRelevanceScenarios.Pool(n,gold:true));
            return new{Picks=n,Score=result.Cards[0],Exact=result.Cards[0].ContextualValue!.Value*50,Other=result.Cards[1]};
        }).ToArray();
        var pivot=new[]{3,5,7,14}.Select(n=>new{Picks=n,Cards=CommitmentRelevanceScenarios.Score([bomb,green],CommitmentRelevanceScenarios.Pool(n,gold:true)).Cards}).ToArray();
        var fixing=new[]{0,1,3}.Select(n=>
        {
            var pack=new[]{CommitmentRelevanceScenarios.Card("single-blue","{3}{U}",.65,manaValue:4),CommitmentRelevanceScenarios.Card("double-blue","{1}{U}{U}",.65),
                CommitmentRelevanceScenarios.Card("triple-blue","{U}{U}{U}",.65),CommitmentRelevanceScenarios.Card("gold-partly-matching","{2}{R}{U}",.65,manaValue:4),
                CommitmentRelevanceScenarios.Card("colorless","{3}",.65),CommitmentRelevanceScenarios.Card("fixing-land","",.56,"Land",[ManaKind.Red,ManaKind.Blue])};
            return new{BlueFixingSources=n,Cards=CommitmentRelevanceScenarios.Score(pack,CommitmentRelevanceScenarios.Pool(7,n,gold:true)).Cards};
        }).ToArray();
        var open=CommitmentRelevanceScenarios.Score([blue,green],CommitmentRelevanceScenarios.OpenPool(7));
        return new{Source="Controlled synthetic pool/quality inputs through production engines, not live results",Transitions=transitions,OpenMixedPool=open.Cards,PremiumPivot=pivot,Fixing=fixing};
    }

    private static object Continuity()
    {
        var config=new ContextualPickScoreConfiguration();
        double Value(double q,double c,double p,double stage)
        {
            var r=config.CommitmentQualityRelevance(q,config.QualityRelevance(p,stage),c,p);
            var w=config.WeightsAt(stage);
            return 50*config.Calibrate(w.Quality*ContextualPickScoreConfiguration.EffectiveQuality(q,r)+w.Lane*.5+w.Archetype*.5+w.DeckNeed*.1);
        }
        var output=new List<object>();
        foreach(var axis in new[]{"Commitment","Playability","Quality","Stage"})
        {
            var values=Enumerable.Range(0,10001).Select(i=>
            {var x=i/10000d;return axis switch{"Commitment"=>Value(.6,x,0,7/42d),"Playability"=>Value(.6,1,x,7/42d),"Quality"=>Value(x,1,0,7/42d),_=>Value(.6,.75,0,x)};}).ToArray();
            var diffs=values.Zip(values.Skip(1),(a,b)=>b-a).ToArray();
            var sign=axis is "Quality" or "Playability"?1:-1;
            output.Add(new{Axis=axis,Samples=values.Length,MaximumExactScoreStep=diffs.Max(Math.Abs),Monotone=diffs.All(d=>sign*d>=-1e-12)});
        }
        return output;
    }

    internal static LimitedStatisticsUpdate Replay(JsonElement root, JsonElement comparison, ContextualPickScoreVersion version)
    {
        var catalog = new ScryfallCardCatalogDecoder().DecodeCatalogData(root.GetProperty("Cards").GetRawText()).Catalog;
        LimitedCardStatisticsCatalog Statistics(string field) => new(root.GetProperty(field).EnumerateArray().Select(s => new LimitedCardStatistics(
            CardIdentifier.Create(s.GetProperty("Identity").GetString()!), Integer(s,"GameInHandGameCount"),
            GameInHandWinRate: Number(s,"GameInHandWinRate"), AverageLastSeenAt: Number(s,"AverageLastSeenAt"))));
        var context = new LimitedStatisticsContext("FRA", LimitedStatisticsFormat.QuickDraft);
        var result = new LimitedStatisticsLoadResult(context, context, Statistics("ParticipantStatistics"), LimitedStatisticsSource.Cache, null, null)
        { CardCatalog = catalog, EnvironmentCatalog = Statistics("Statistics") };
        var alternateRows = comparison.GetProperty("AlternateProviderCache").GetProperty("Payload").GetProperty("data").EnumerateArray()
            .Select(r => new SeventeenLandsRating(r.GetProperty("name").GetString()!, Integer(r,"ever_drawn_game_count"), GameInHandWinRate: Number(r,"ever_drawn_win_rate"))).ToArray();
        var history = DraftPackObservationHistory.Empty; LimitedStatisticsUpdate? last = null;
        foreach (var pack in root.GetProperty("Packs").EnumerateArray())
        {
            var snapshot = new DraftSnapshot(new(new(PackNumber.Create(pack.GetProperty("Pack").GetInt32()), PickNumber.Create(pack.GetProperty("Pick").GetInt32())),
                pack.GetProperty("Offered").EnumerateArray().Select(i => CardIdentifier.Create(i.GetString()!))),
                new(pack.GetProperty("History").EnumerateArray().Select(p => new DraftPick(new(PackNumber.Create(p.GetProperty("Pack").GetInt32()), PickNumber.Create(p.GetProperty("Pick").GetInt32())),
                    CardIdentifier.Create(p.GetProperty("Identity").GetString()!)))), DraftFormat.BestOfOne)
            { RecoveredPool = new(pack.GetProperty("Pool").EnumerateArray().Select(i => CardIdentifier.Create(i.GetString()!))) };
            var alternate = LimitedStatisticsMapper.Map(alternateRows, catalog, new("FRA", LimitedStatisticsFormat.PremierDraft), snapshot);
            var evidence = snapshot.CurrentPack.AvailableCardIdentifiers.Where(i => result.Catalog.StatisticsFor(i) is { } row
                && !SparsePrimaryQualityPrior.HasUsableGih(row) && SparsePrimaryQualityPrior.HasUsableGih(alternate.Catalog.StatisticsFor(i)))
                .ToDictionary(i => i, i => new AlternateFormatQualityEvidence(i,context,new("FRA",LimitedStatisticsFormat.PremierDraft),
                    alternate.Catalog.StatisticsFor(i)!.GameInHandWinRate!.Value,alternate.Catalog.StatisticsFor(i)!.GameInHandGameCount!.Value,
                    comparison.GetProperty("AlternateBaseline").GetDouble(),alternate.ResolvedNames[i],
                    comparison.GetProperty("AlternateProviderCache").GetProperty("FetchedAt").GetDateTimeOffset(),"Cache","ALL_TIME"));
            last = new(snapshot,false,result with { AlternateQualityEvidence = evidence },observationHistory:history,
                draftPool:new(snapshot.DraftedPool,DraftPoolCompleteness.Complete),pickScoreConfiguration:new(version:version));
            history = last.ObservationHistory;
        }
        return last!;
    }

    internal static double? Number(JsonElement row,string field) => row.GetProperty(field).ValueKind == JsonValueKind.Null ? null : row.GetProperty(field).GetDouble();
    internal static int? Integer(JsonElement row,string field) => row.GetProperty(field).ValueKind == JsonValueKind.Null ? null : row.GetProperty(field).GetInt32();
}
