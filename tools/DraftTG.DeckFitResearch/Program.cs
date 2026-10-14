using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using DraftTG.Data;
using DraftTG.Domain;
using DraftTG.HistoricalEvaluation;
using DraftTG.RecommendationEngine;

// Offline Phase E only. Reuses the actual replay adapter, role/pip parsers and builder.
try {
 var opt=new Dictionary<string,string>();for(int i=0;i<args.Length;i+=2)opt.Add(args[i].TrimStart('-'),args[i+1]);
 if(opt["mode"]=="compose") {
  using var output=new StreamWriter(opt["output"]);
  foreach(var line in File.ReadLines(opt["input"])) {
   using var doc=JsonDocument.Parse(line);var row=doc.RootElement;
   var results=row.GetProperty("cards").EnumerateArray().Select(c=>{
    var weights=HistoricalJson.Read<PickScoreWeights>(c.GetProperty("Weights").GetRawText());
    var values=c.GetProperty("D").EnumerateArray().Select(d=>ExperimentalDeckFitSubstitution.Calibrate(weights,c.GetProperty("EffectiveQualityComponent").GetDouble(),c.GetProperty("LaneComponent").GetDouble(),c.GetProperty("ArchetypeComponent").GetDouble(),d.GetDouble())).ToArray();
    return values;
   }).ToArray();output.WriteLine(HistoricalJson.Serialize(results));
  }
  return;
 }
 var root="data/historical/normalized/phase-b1-v1/";
 var catalog=new ScryfallCardCatalogDecoder().Decode(File.ReadAllText(root+"catalog.json"));
 var sets=new[]{"DMU","BRO","WOE","LCI","MKM","OTJ","ONE","MOM"};
 var split=HistoricalJson.Read<SetSplitManifest>(File.ReadAllText("V2_PHASE_B1_SPLIT_MANIFEST.json"));
 var stats=HistoricalJson.Read<HistoricalStatsSnapshot[]>(File.ReadAllText(root+"compatible-stats.json"));
 var bonus=HistoricalJson.Read<Dictionary<string,string[]>>(File.ReadAllText(root+"bonus.json"));
 var resolver=new HistoricalIdentityResolver(catalog);
 if(opt["mode"]=="metadata") {
  var roles=new LimitedCardRoleClassifier();
  File.WriteAllText(opt["output"],HistoricalJson.Serialize(catalog.Cards.ToDictionary(c=>c.Identifier.Value,c=>new {
   name=c.Name,colors=c.Colors.Colors.Select(x=>(int)x).ToArray(),land=c.GameplayMetadata.IsLand,basic=c.GameplayMetadata.IsBasicLand,
   known=c.GameplayMetadata.CardTypes.IsKnown,manaValue=c.GameplayMetadata.ManaValue,creature=c.GameplayMetadata.IsCreature,
   roleProfile=roles.Classify(c),pips=Enum.GetValues<MagicColor>().Select(x=>DeckManaDemand.For(c).Colors[x]).ToArray(),
   pipsKnown=DeckManaDemand.For(c).IsKnown,produced=(c.GameplayMetadata.ProducedMana??[]).Select(x=>(int)x).ToArray()
  })));return;
 }
 Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(opt["output"]))!);
 var timer=Stopwatch.StartNew();
 var adapter=new HistoricalReplayAdapter(catalog,resolver,stats,bonus,cacheImmutableEvidence:true);
 if(opt["mode"]=="builder") {
  var envs=sets.ToDictionary(s=>s,s=>new LimitedCardStatisticsCatalog(stats.Single(x=>x.SetCode==s).Cards
   .Select(f=>(f,id:resolver.Resolve(f.SourceIdentity,s,bonus.GetValueOrDefault(s,[])).Identifier))
   .Where(x=>x.id is not null).Select(x=>new LimitedCardStatistics(x.id!,x.f.GIHGames,GameInHandWinRate:x.f.GIHWinRate)).DistinctBy(x=>x.CardIdentifier)));
  using var output=new StreamWriter(opt["output"]);long n=0;
  foreach(var line in File.ReadLines(opt["input"])) {
   using var doc=JsonDocument.Parse(line);var r=doc.RootElement;var s=r.GetProperty("set").GetString()!;
   if(!sets.Contains(s))throw new InvalidDataException("Protected set");
   var ids=r.GetProperty("pool").EnumerateObject().SelectMany(e=>Enumerable.Repeat(CardIdentifier.Create(e.Name),e.Value.GetInt32())).ToArray();
   var pool=new DraftPoolSnapshot(new DraftedCardPool(ids),DraftPoolCompleteness.Complete);
   var t=Stopwatch.StartNew();var result=new BaselineDeckBuilder().Build(new(pool,catalog,envs[s],envs[s],new(s,LimitedStatisticsFormat.PremierDraft)));
   output.WriteLine(HistoricalJson.Serialize(new {Set=s,Draft=r.GetProperty("draft").GetString(),result.Availability,result.Deck,Milliseconds=t.Elapsed.TotalMilliseconds}));
   if(++n%100==0)Console.WriteLine($"{n} builders; {timer.Elapsed.TotalSeconds:F1}s");
  }
  return;
 }
 if(opt["mode"]=="pairs") {
  var existing=opt.TryGetValue("existing",out var cachePath)?File.ReadLines(cachePath).Select(x=>JsonDocument.Parse(x)).ToDictionary(x=>x.RootElement.GetProperty("Key").GetString()!,x=>x.RootElement.GetRawText()):new Dictionary<string,string>();
  using var output=new StreamWriter(opt["output"]);long n=0;
  foreach(var line in File.ReadLines(opt["input"])) {
   using var doc=JsonDocument.Parse(line);var r=doc.RootElement;var s=r.GetProperty("set").GetString()!;
   if(!sets.Contains(s))throw new InvalidDataException("Protected set");
   var pool=r.GetProperty("pool").EnumerateObject().ToDictionary(x=>x.Name,x=>x.Value.GetInt32());
   foreach(var id in r.GetProperty("candidates").EnumerateArray().Select(x=>x.GetString()!)) {
    var cacheKey=s+"/"+r.GetProperty("draft").GetString()+"/"+id;
    if(existing.TryGetValue(cacheKey,out var cachedLine)){output.WriteLine(cachedLine);continue;}
    HistoricalCardIdentity Identity(string key)=>new(catalog.Find(CardIdentifier.Create(key))!.Name,key,s,IdentityStatus.ExplicitIdentifier,"Frozen catalog",[key]);
    var quantities=pool.Select(x=>new HistoricalCardQuantity(Identity(x.Key),x.Value-(x.Key==id?1:0))).Where(x=>x.Count>0).ToArray();
    var input=new PickTimeState(s,"PremierDraft",r.GetProperty("draft").GetString()!,null,3,14,44,[new(Identity(id),1)],quantities,[],true);
    var result=adapter.Replay(input,"contextual-pick-score-v1.6.2",EvaluationRegime.MatureFormatReplay);
    output.WriteLine(HistoricalJson.Serialize(new {Key=s+"/"+input.DraftId+"/"+id,D=result.Cards.Single().DeckNeedComponent}));
   }
   if(++n%100==0)Console.WriteLine($"{n} pair pools; {timer.Elapsed.TotalSeconds:F1}s");
  }
  return;
 }
 if(opt["mode"]!="replay")throw new ArgumentException("Unknown mode");
 using var store=new HistoricalStore(root+"bulk-replay.sqlite");
 using var outputFile=File.Create(opt["output"]);using var zip=new GZipStream(outputFile,CompressionLevel.Fastest);using var trace=new StreamWriter(zip);
 string? scope=null;var history=DraftPackObservationHistory.Empty;var past=new List<HistoricalPriorPick>();long count=0,complete=0;
 foreach(var d in store.ReadDecisions(split,sets:sets.ToHashSet())) {
  var input=d.Inputs;var key=input.SetCode+"/"+input.DraftId;
  if(scope!=key){scope=key;history=DraftPackObservationHistory.Empty;past.Clear();}
  input=input with {PriorPicks=past.ToArray()};
  var baseline=adapter.Replay(input,"contextual-pick-score-v1.6.2",EvaluationRegime.MatureFormatReplay,history);history=baseline.ObservationHistory;
  var agreement=HistoricalEvaluator.Agreement(d,baseline);
  bool ok=input.PoolIsComplete&&input.PoolBeforePick.All(c=>c.Card.Identifier is not null)&&input.OfferedCards.All(c=>c.Card.Identifier is not null)&&agreement.Rank is not null;
  if(ok)complete++;
  // Score/input export happens before label attachment. No observed build is read by this executable.
  var cards=baseline.Cards.Select(c=>new {Id=c.CardIdentifier.Value,c.PackIndex,c.CurrentPackRank,c.QualityComponent,c.EffectiveQualityComponent,
   c.LaneComponent,c.ArchetypeComponent,c.DeckNeedComponent,c.Weights,c.QualityRelevance,c.DraftProgress,c.ContextualValue,
   c.DeckImpact,c.ColorCommitment,c.StatisticalDataWeight,c.QualityProvenance,c.Availability}).ToArray();
  trace.WriteLine(HistoricalJson.Serialize(new {input.SetCode,input.DraftId,input.PackNumber,input.PickNumber,input.GlobalPickIndex,input.PoolBeforePick,
   Complete=ok,Cards=cards,Rank=agreement.Rank,ActualPicked=d.Label.ActualPickedCard.CanonicalId,baseline.InputColorCommitment,Outcome=d.Outcome}));
  past.Add(new(input.PackNumber,input.PickNumber,d.Label.ActualPickedCard));
  if(++count%1000==0)Console.WriteLine($"{count} decisions; {timer.Elapsed.TotalSeconds:F1}s");
 }
 File.WriteAllText(opt["output"]+".json",HistoricalJson.Serialize(new {Decisions=count,Complete=complete,Seconds=timer.Elapsed.TotalSeconds,PeakWorkingSetBytes=Process.GetCurrentProcess().PeakWorkingSet64}));
}catch(Exception ex){Console.Error.WriteLine(ex);Environment.ExitCode=1;}

