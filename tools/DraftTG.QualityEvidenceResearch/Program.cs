using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DraftTG.Data;
using DraftTG.Domain;
using DraftTG.HistoricalEvaluation;
using DraftTG.RecommendationEngine;

// Research runner: actual frozen production scoring once; optional Q-only recomposition.
// Outcome/selected-pick labels are joined only after Replay(PickTimeState).
try
{
    var options = new Dictionary<string,string>();
    for (var i=0;i<args.Length;i+=2) options.Add(args[i].TrimStart('-'),args[i+1]);
    string Required(string key)=>options.TryGetValue(key,out var value)?value:throw new ArgumentException(key+" required");
    var split=HistoricalJson.Read<SetSplitManifest>(File.ReadAllText(Required("splits")));
    var sets=Required("sets").Split(',').ToHashSet();
    if(sets.Any(s=>s is "BLB" or "DSK" or "MH3" or "DFT" || split.For(s)==DatasetPartition.Holdout))
        throw new InvalidDataException("Spent/new holdouts are unavailable in this runner.");
    var limit=int.Parse(options.GetValueOrDefault("draft-limit","2147483647"));
    var detailed=options.GetValueOrDefault("detailed","false")=="true";
    var cached=options.GetValueOrDefault("cache","false")=="true";
    var output=Required("output");Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
    var setup=Stopwatch.StartNew();
    var catalog=new ScryfallCardCatalogDecoder().Decode(File.ReadAllText(Required("catalog")));
    if(options.TryGetValue("export-card-metadata",out var metadataPath))
    {
        File.WriteAllText(metadataPath,HistoricalJson.Serialize(catalog.Cards.ToDictionary(c=>c.Identifier.Value,c=>new
            {colors=c.Colors.Colors.Select(x=>(int)x).ToArray(),land=c.GameplayMetadata.CardTypes.Contains(CardType.Land),rarity=c.Rarity.ToString(),name=c.Name})));
        return;
    }
    var stats=HistoricalJson.Read<HistoricalStatsSnapshot[]>(File.ReadAllText(Required("stats")));
    var bonus=HistoricalJson.Read<Dictionary<string,string[]>>(File.ReadAllText(Required("bonus")));
    if(stats.Any(s=>s.Regime!=EvaluationRegime.MatureFormatReplay || !sets.Contains(s.SetCode)))
        throw new InvalidDataException("Explicit exact-set mature snapshots only.");
    var adapter=new HistoricalReplayAdapter(catalog,new(catalog),stats,bonus,cacheImmutableEvidence:cached);
    var qvalues=new Dictionary<(string Set,string Format,string Id),double>();
    if(options.TryGetValue("q",out var qpath))
    {
        using var json=JsonDocument.Parse(File.ReadAllText(qpath));
        foreach(var row in json.RootElement.EnumerateArray())
        {
            if(!sets.Contains(row.GetProperty("set").GetString()!)) throw new InvalidDataException("Unexpected candidate set.");
            var q=row.GetProperty("candidateQ").GetDouble();
            if(!double.IsFinite(q)||q is <0 or >1 || !qvalues.TryAdd((row.GetProperty("set").GetString()!,row.GetProperty("format").GetString()!,row.GetProperty("canonicalId").GetString()!),q))
                throw new InvalidDataException("Invalid/duplicate Q identity.");
        }
    }
    using var store=new HistoricalStore(Required("db"));
    using var experimentalLane=options.TryGetValue("lane",out var lanePath)?new ExperimentalLaneSubstitution(lanePath,Required("lane-alsa")):null;
    if(experimentalLane is not null&&qvalues.Count>0)throw new ArgumentException("Q and L ablations cannot be combined.");
    using var laneTrace=experimentalLane is not null?new StreamWriter(output+".lanes.jsonl"):null;
    var setupSeconds=setup.Elapsed.TotalSeconds;
    using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    var metrics=new SortedDictionary<string,MetricAccumulator>();
    var changes=new SortedDictionary<string,long[]>();
    var missing=new SortedDictionary<string,long>();
    var draftCounts=new Dictionary<string,int>();
    long decisions=0,drafts=0,calls=0,scored=0,cardScores=0;double engineMs=0,replayMs=0,serializationMs=0,readMs=0,ablationMs=0;
    string? scope=null;var history=DraftPackObservationHistory.Empty;var past=new List<HistoricalPriorPick>();bool include=false;
    var timer=Stopwatch.StartNew();
    StreamWriter? trace=detailed?new StreamWriter(output+".trace.jsonl"):null;
    try
    {
        using var iterator=store.ReadDecisions(split,sets:sets).GetEnumerator();
        while(true)
        {
            var phase=Stopwatch.StartNew();var more=iterator.MoveNext();readMs+=phase.Elapsed.TotalMilliseconds;if(!more)break;
            var d=iterator.Current;var input=d.Inputs;
            var nextScope=input.SetCode+"/"+input.Format+"/"+d.Provenance.Provider+"/"+input.DraftId;
            if(scope!=nextScope)
            {
                scope=nextScope;history=DraftPackObservationHistory.Empty;past.Clear();
                var bucket=input.SetCode+"/"+input.Format;var n=draftCounts.GetValueOrDefault(bucket)+1;draftCounts[bucket]=n;include=n<=limit;
                if(include)drafts++;
            }
            if(!include)continue;
            input=input with {PriorPicks=past.Concat(input.PriorPicks).GroupBy(p=>(p.Pack,p.Pick)).Select(g=>g.First()).OrderBy(p=>p.Pack).ThenBy(p=>p.Pick).ToArray()};
            phase.Restart();var baseline=adapter.Replay(input,"contextual-pick-score-v1.6.2",EvaluationRegime.MatureFormatReplay,history);replayMs+=phase.Elapsed.TotalMilliseconds;
            history=baseline.ObservationHistory;calls+=baseline.OptimizerCalls;engineMs+=baseline.TotalMilliseconds;cardScores+=baseline.Cards.Count(c=>c.ContextualValue.HasValue);
            if(baseline.UnscorableCards==0&&baseline.Cards.Length==input.OfferedCards.Sum(c=>c.Count))scored++;
            var a=HistoricalEvaluator.Agreement(d,baseline);Record("current",baseline,a);
            if(experimentalLane is not null)
            {
                var prediction=experimentalLane.Read(input);var currentLane=experimentalLane.Current(input,baseline,catalog);
                var complete=input.PoolIsComplete&&input.PoolBeforePick.All(c=>c.Card.Identifier is not null)&&input.OfferedCards.All(c=>c.Card.Identifier is not null)
                    &&a.Rank is not null;
                var currentResult=ExperimentalLaneSubstitution.Apply(baseline,currentLane,catalog,"current-derived");
                var currentAgreement=HistoricalEvaluator.Agreement(d,currentResult);
                var agreements=new Dictionary<string,DecisionAgreement>{{"baseline",a},{"current-derived",currentAgreement}};
                var states=prediction.Models.Append(new KeyValuePair<string,ResearchLaneState>("current-derived",currentLane));
                if(complete)
                foreach(var pair in states)
                {
                    var substituted=ExperimentalLaneSubstitution.Apply(baseline,pair.Value,catalog,pair.Key);var b=HistoricalEvaluator.Agreement(d,substituted);
                    Record("lane-"+pair.Key,substituted,b,pair.Value.Confidence);agreements[pair.Key]=b;
                    var changed=!baseline.Cards.OrderBy(c=>c.CurrentPackRank).Select(c=>c.PackIndex).SequenceEqual(substituted.Cards.OrderBy(c=>c.CurrentPackRank).Select(c=>c.PackIndex));
                    foreach(var key in Strata(input,d.Outcome,baseline.InputColorCommitment).Append("lane-confidence="+(pair.Value.Confidence<.25?"0-.25":pair.Value.Confidence<.5?".25-.5":pair.Value.Confidence<.75?".5-.75":".75-1")))
                    {
                        var name=pair.Key+" | "+key;if(!changes.TryGetValue(name,out var count))changes[name]=count=new long[4];
                        count[0]++;count[1]+=changed?1:0;count[2]+=b.Rank<a.Rank?1:0;count[3]+=b.Rank>a.Rank?1:0;
                        if(pair.Key!="current-derived")
                        {
                            var other=pair.Key+"-vs-current-derived | "+key;
                            if(!changes.TryGetValue(other,out var altCount))changes[other]=altCount=new long[4];
                            var different=!currentResult.Cards.OrderBy(c=>c.CurrentPackRank).Select(c=>c.PackIndex).SequenceEqual(substituted.Cards.OrderBy(c=>c.CurrentPackRank).Select(c=>c.PackIndex));
                            altCount[0]++;altCount[1]+=different?1:0;altCount[2]+=b.Rank<currentAgreement.Rank?1:0;altCount[3]+=b.Rank>currentAgreement.Rank?1:0;
                        }
                    }
                }
                if(complete)Record("complete-baseline",baseline,a);
                laneTrace!.WriteLine(HistoricalJson.Serialize(new {input.SetCode,input.Format,input.DraftId,input.PackNumber,input.PickNumber,
                    Complete=complete,Current=currentLane,Models=prediction.Models,Commitment=baseline.InputColorCommitment,Agreements=agreements}));
            }
            foreach(var reason in baseline.MissingInputs)missing[reason]=missing.GetValueOrDefault(reason)+1;
            if(qvalues.Count>0)
            {
                phase.Restart();var candidate=Apply(input,baseline,qvalues);ablationMs+=phase.Elapsed.TotalMilliseconds;
                var b=HistoricalEvaluator.Agreement(d,candidate);Record("candidate",candidate,b);
                if(a.Rank is not null&&b.Rank is not null)
                {
                    var changed=!baseline.Cards.OrderBy(c=>c.CurrentPackRank).Select(c=>c.PackIndex).SequenceEqual(candidate.Cards.OrderBy(c=>c.CurrentPackRank).Select(c=>c.PackIndex));
                    foreach(var key in Strata(input,d.Outcome,baseline.InputColorCommitment))
                    {
                        if(!changes.TryGetValue(key,out var count))changes[key]=count=new long[4];
                        count[0]++;count[1]+=changed?1:0;count[2]+=b.Rank<a.Rank?1:0;count[3]+=b.Rank>a.Rank?1:0;
                    }
                }
            }
            if(detailed)
            {
                phase.Restart();
                // Every score field includes deck projections, explanations and provenance. Timing excluded only.
                var json=HistoricalJson.Serialize(new {input.SetCode,input.Format,input.DraftId,input.PackNumber,input.PickNumber,
                    baseline.Cards,baseline.ObservationHistory,baseline.MissingInputs,baseline.UnscorableCards,baseline.InputColorCommitment});
                hash.AppendData(Encoding.UTF8.GetBytes(json+"\n"));trace!.WriteLine(json);serializationMs+=phase.Elapsed.TotalMilliseconds;
            }
            past.Add(new(input.PackNumber,input.PickNumber,d.Label.ActualPickedCard));decisions++;
            if(decisions%1000==0)Console.WriteLine($"{decisions} decisions; {timer.Elapsed.TotalSeconds:F1}s");

            void Record(string model,HistoricalReplayResult r,DecisionAgreement agreement,double? laneConfidence=null)
            {
                foreach(var key in Strata(input,d.Outcome,baseline.InputColorCommitment).Concat(laneConfidence is {} lc
                    ?new[]{"lane-confidence="+(lc<.25?"0-.25":lc<.5?".25-.5":lc<.75?".5-.75":".75-1")}:[]))
                {
                    var name=model+" | "+key;if(!metrics.TryGetValue(name,out var accumulator))metrics[name]=accumulator=new();
                    accumulator.Add(agreement.Rank,agreement.Occurrences,agreement.ScoreGap,r.Cards.Count(c=>c.IsQualityEstimated),d.Label.ObservedMaindeckRate,
                        !input.PoolIsComplete||input.PoolBeforePick.Any(c=>c.Card.Identifier is null),r.Cards.Count(c=>c.QualityEvidence is PickScoreQualityEvidence.NoProviderRow or PickScoreQualityEvidence.ProviderRowWithoutUsableGih));
                }
            }
        }
    }
    finally {trace?.Dispose();}
    experimentalLane?.Complete();
    var result=new {Version="research-q-phase-b1-v1",Cache=cached,Detailed=detailed,Decisions=decisions,Drafts=drafts,FullyScoredDecisions=scored,CardScores=cardScores,
        Seconds=timer.Elapsed.TotalSeconds,SetupSeconds=setupSeconds,ReadMilliseconds=readMs,ReplayMilliseconds=replayMs,EngineMilliseconds=engineMs,SerializationMilliseconds=serializationMs,AblationMilliseconds=ablationMs,
        OptimizerCalls=calls,PeakWorkingSetBytes=Process.GetCurrentProcess().PeakWorkingSet64,OutputHash=detailed?Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant():null,
        Metrics=metrics.ToDictionary(p=>p.Key,p=>p.Value.Summary()),Changes=changes.ToDictionary(p=>p.Key,p=>new {Compared=p.Value[0],Changed=p.Value[1],Improved=p.Value[2],Regressed=p.Value[3]}),MissingInputs=missing,
        Inputs=new {Catalog=HistoricalStore.HashFile(Required("catalog")),Stats=HistoricalStore.HashFile(Required("stats")),Split=HistoricalStore.HashFile(Required("splits")),Candidate=options.TryGetValue("q",out var qp)?HistoricalStore.HashFile(qp):null}};
    File.WriteAllText(output,JsonSerializer.Serialize(result,HistoricalJson.Pretty));Console.WriteLine(output);
}
catch(Exception e){Console.Error.WriteLine(e);Environment.ExitCode=1;}

static IEnumerable<string> Strata(PickTimeState input,DraftOutcome outcome,double commitment)
{
    yield return "overall";yield return "set="+input.SetCode;yield return "pack="+input.PackNumber;yield return "pick="+input.PickNumber;
    yield return "stage="+input.PackNumber+"/"+input.PickNumber;
    yield return "commitment="+(commitment<.25?"0-.25":commitment<.5?".25-.5":commitment<.75?".5-.75":".75-1");
    yield return "outcome="+(outcome.FinalWins is not {} wins?"unknown":wins<=2?"0-2 wins":wins<=4?"3-4 wins":wins+" wins");
}

static HistoricalReplayResult Apply(PickTimeState input,HistoricalReplayResult baseline,Dictionary<(string,string,string),double> values)
{
    const string model="research-q-phase-b1-v1/compatible-count-prior";
    var cards=baseline.Cards.Select(c=>
    {
        // Reuse the frozen research recomposition, then assign this study's explicit label.
        var substituted=ExperimentalQualitySubstitution.Substitute(c,values.GetValueOrDefault((input.SetCode,input.Format,c.CardIdentifier.Value),.5),ExperimentalQualitySubstitution.Version+"/A");
        return substituted with {ModelVersion=model,Reasons=["B.1 Q-only ablation; production L/A/D, weights, commitment, relevance and deck projections held fixed."]};
    }).ToArray();
    var ordered=cards.Where(c=>c.ContextualValue.HasValue).OrderByDescending(c=>c.ContextualValue).ThenByDescending(c=>c.QualityComponent).ThenByDescending(c=>c.StatisticalDataWeight).ThenBy(c=>c.PackIndex).ToArray();
    if(ordered.Length>=2)for(var i=0;i<ordered.Length;i++){var index=Array.FindIndex(cards,c=>c.PackIndex==ordered[i].PackIndex);cards[index]=ordered[i] with {CurrentPackRank=i+1,IsContextPick=i==0};}
    return baseline with {ModelVersion=model,Cards=cards,OptimizerCalls=0,TotalMilliseconds=0};
}
