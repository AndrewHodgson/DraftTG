using System.Text.Json;
using DraftTG.Domain;
using DraftTG.HistoricalEvaluation;
using DraftTG.RecommendationEngine;

namespace DraftTG.HistoricalEvaluation.Tests;

public sealed class HistoricalEvaluationTests
{
    static readonly Card A = Card("a","Alpha","AAA");
    static readonly Card B = Card("b","Beta","AAA");
    static readonly Card C = Card("c","Front // Back","AAA") with { GameplayMetadata=new(2,"{1}{G}","Creature",CardLayout.Transform,
        [new("Front","{1}{G}",2,new([MagicColor.Green]),"Creature",null,null,null),new("Back",null,2,new([MagicColor.Green]),"Creature",null,null,null)]) };
    static readonly CardCatalog Catalog = new([A,B,C]);
    static Card Card(string id,string name,string set) => new(CardIdentifier.Create(id),name,new([MagicColor.Green]),CardRarity.Common,
        CardSetCode.Create(set),CollectorNumber.Create(id)) { GameplayMetadata=new(2,"{1}{G}","Creature") };
    static SetSplitManifest Split => new("test-v1",new Dictionary<string,DatasetPartition>{{"AAA",DatasetPartition.Calibration},{"BBB",DatasetPartition.Holdout}});
    static DatasetImportOptions Options => new("tiny","PublicTest","https://example.org/public.csv","AAA","PremierDraft",DatasetPartition.Calibration,[],14);
    static HistoricalCardIdentity Identity(string name) => new HistoricalIdentityResolver(Catalog).Resolve(name,"AAA");
    static PickTimeState Input(int pick=1) => new("AAA","PremierDraft","draft",null,1,pick,pick-1,
        [new(Identity("Alpha"),2),new(Identity("Beta"),1)],pick==1?[]:[new(Identity("Alpha"),pick-1)],[],true);
    static HistoricalDraftDecision Decision(int pick=1) => new(HistoricalJson.Schema,"tiny",Input(pick),new(Identity("Alpha")),new(7,2),new("PublicTest","uri","hash",pick,"schema"));
    static HistoricalStatsSnapshot[] Stats => [new("AAA","PremierDraft",EvaluationRegime.MatureFormatReplay,null,"test aggregate",
        [new("Alpha","AAA","PremierDraft",.6,1000,ALSA:2),new("Beta","AAA","PremierDraft",.55,1000,ALSA:5),new("Front","AAA","PremierDraft",.56,1000,ALSA:6)])];
    static HistoricalReplayAdapter Adapter => new(Catalog,new(Catalog),Stats,new Dictionary<string,string[]>());

    [Fact]
    public void ImmutableEvidenceCachePreservesCompleteReplayAcrossVersionsAndHistory()
    {
        foreach(var model in new[]{"contextual-pick-score-v1.6","contextual-pick-score-v1.6.1","contextual-pick-score-v1.6.2"})
        {
            var stats=Stats;var normal=new HistoricalReplayAdapter(Catalog,new(Catalog),stats,new Dictionary<string,string[]>());
            var cached=new HistoricalReplayAdapter(Catalog,new(Catalog),stats,new Dictionary<string,string[]>(),cacheImmutableEvidence:true);
            var a=DraftPackObservationHistory.Empty;var b=DraftPackObservationHistory.Empty;
            for(var pick=1;pick<=3;pick++)
            {
                var original=normal.Replay(Input(pick),model,EvaluationRegime.MatureFormatReplay,a);
                var optimized=cached.Replay(Input(pick),model,EvaluationRegime.MatureFormatReplay,b);
                Assert.Equal(HistoricalJson.Serialize(original with {TotalMilliseconds=0}),HistoricalJson.Serialize(optimized with {TotalMilliseconds=0}));
                a=original.ObservationHistory;b=optimized.ObservationHistory;
            }
        }
    }

    [Fact]
    public void EvidenceCacheCopiesCallerOwnedSnapshotContainersBeforeFirstUse()
    {
        var stats=Stats;var cached=new HistoricalReplayAdapter(Catalog,new(Catalog),stats,new Dictionary<string,string[]>(),cacheImmutableEvidence:true);
        var expected=new HistoricalReplayAdapter(Catalog,new(Catalog),stats,new Dictionary<string,string[]>())
            .Replay(Input(),"contextual-pick-score-v1.6.2",EvaluationRegime.MatureFormatReplay);
        stats[0].Cards[0]=stats[0].Cards[0] with {GIHWinRate=.1,GIHGames=1};
        stats[0]=stats[0] with {Cards=[]};
        var actual=cached.Replay(Input(),"contextual-pick-score-v1.6.2",EvaluationRegime.MatureFormatReplay);
        Assert.Equal(HistoricalJson.Serialize(expected with {TotalMilliseconds=0}),HistoricalJson.Serialize(actual with {TotalMilliseconds=0}));
    }
    const string Header="draft_id,expansion,event_type,pack_number,pick_number,pick,event_match_wins,event_match_losses,pack_card_Alpha,pack_card_Beta,pool_Alpha,pool_Beta";
    const string Row="draft,AAA,PremierDraft,0,0,Alpha,7,2,2,1,0,0";

    [Fact]
    public void StreamingCsvHandlesQuotedFieldsNewlinesAndBounds()
    {
        using var csv=new StreamingCsv(new StringReader("a,\"two, \"\"quotes\"\"\",\"line\nbreak\"\r\n1,2,3\n"));
        Assert.Equal(new[]{"a","two, \"quotes\"","line\nbreak"},csv.Read());Assert.Equal(new[]{"1","2","3"},csv.Read());Assert.Null(csv.Read());
        using var bounded=new StreamingCsv(new StringReader("abcdefghij"),5);
        Assert.Throws<InvalidDataException>(()=>bounded.Read());
        using var invalid=new StreamingCsv(new StringReader("\"unterminated"));Assert.Throws<InvalidDataException>(()=>invalid.Read());
    }
    [Fact]
    public void IdentityRejectsReprintsAndAmbiguityAndSupportsFrontAndDeclaredSheets()
    {
        var r=new HistoricalIdentityResolver(new([A,B,C,Card("other-a","Alpha","BBB"),Card("sheet","Bonus","BON")]));
        Assert.Equal("a",r.Resolve("Alpha","AAA").CanonicalId);
        Assert.Equal(IdentityStatus.FrontFace,r.Resolve("Front","AAA").Status);
        Assert.Equal("c",r.Resolve("Front//Back","AAA").CanonicalId);
        Assert.Null(r.Resolve("Alpha","CCC").CanonicalId);Assert.Null(r.Resolve("other-a","AAA").CanonicalId);
        Assert.Null(r.Resolve("Bonus","AAA").CanonicalId);Assert.Equal("sheet",r.Resolve("Bonus","AAA",["BON"]).CanonicalId);
        var ambiguous=new HistoricalIdentityResolver(new([A,Card("second-a","Alpha","AAA")]));
        Assert.Equal(IdentityStatus.Ambiguous,ambiguous.Resolve("Alpha","AAA").Status);
        Assert.Equal(new[]{"a","second-a"},ambiguous.Resolve("Alpha","AAA").Candidates);
    }
    [Fact]
    public void ImportAccountsMalformedDuplicatesQuantitiesAndManifestHashCache()
    {
        using var temp=new Temp();var file=temp.File("input.csv",Header+"\n"+Row+"\n"+Row+"\n"+Row.Replace(",0,0,Alpha,",",0,no,Alpha,")+"\n");
        using var store=new HistoricalStore(temp.Path("store.sqlite"));var importer=new HistoricalImporter(store,new(Catalog),Split,"cataloghash");
        var m=importer.Import(file,Options);
        Assert.Equal(3,m.RawRows);Assert.Equal(1,m.PickCount);Assert.Equal(1,m.DuplicateRows);Assert.Equal(1,m.MalformedRows);
        Assert.Equal(HistoricalStore.HashFile(file),m.FileHash);Assert.Equal(4,m.IdentityResolvedCount);
        Assert.Equal(HistoricalJson.Serialize(m),HistoricalJson.Serialize(importer.Import(file,Options)));
        Assert.Throws<InvalidDataException>(()=>importer.Import(file,Options with {BonusSets=["BON"]}));
        var decision=Assert.Single(store.ReadDecisions(Split));Assert.Equal(2,decision.Inputs.OfferedCards[0].Count);
        Assert.Empty(decision.Inputs.PoolBeforePick);Assert.Equal(7,decision.Outcome.FinalWins);
        using var otherStore=new HistoricalStore(temp.Path("unfinished.sqlite"));
        new HistoricalImporter(otherStore,new(Catalog),Split,"hash").Import(temp.File("unfinished.csv",Header+"\n"+Row.Replace(",7,2,",",0,1,")+"\n"),Options);
        var unfinished=Assert.Single(otherStore.ReadDecisions(Split));Assert.Null(unfinished.Outcome.FinalWins);
        Assert.Equal(0,unfinished.Outcome.ReportedEventWins);Assert.Equal(1,unfinished.Outcome.ReportedEventLosses);
    }
    [Fact]
    public void SchemaErrorsAndConflictingDuplicatesRollback()
    {
        using var temp=new Temp();using var store=new HistoricalStore(temp.Path("store.sqlite"));var importer=new HistoricalImporter(store,new(Catalog),Split,"hash");
        Assert.Throws<InvalidDataException>(()=>importer.Import(temp.File("bad.csv","draft_id,pick\nx,Alpha\n"),Options));
        Assert.Throws<InvalidDataException>(()=>importer.Import(temp.File("conflict.csv",Header+"\n"+Row+"\n"+Row.Replace(",Alpha,7",",Beta,7")+"\n"),Options));
        Assert.Empty(store.Manifests());Assert.Empty(store.ReadDecisions(Split));
    }
    [Fact]
    public void CancellationRollsBackDiskStagingWithoutRetainingCorpusInMemory()
    {
        using var temp=new Temp();var file=temp.Path("stream.csv");
        using(var writer=new StreamWriter(file)){writer.WriteLine(Header);for(var i=0;i<1001;i++)writer.WriteLine(Row.Replace("draft,",$"draft{i},"));}
        using var store=new HistoricalStore(temp.Path("store.sqlite"));using var cts=new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(()=>new HistoricalImporter(store,new(Catalog),Split,"hash").Import(file,Options,_=>cts.Cancel(),cts.Token));
        Assert.Empty(store.Manifests());Assert.Empty(store.ReadDecisions(Split));
    }
    [Fact]
    public void WholeSetSplitAndDraftIsolationRejectConflictingAssignment()
    {
        using var temp=new Temp();using var store=new HistoricalStore(temp.Path("store.sqlite"));var catalog=new CardCatalog([A,B,Card("aa","Alpha","BBB"),Card("bb","Beta","BBB")]);
        var importer=new HistoricalImporter(store,new(catalog),Split,"hash");importer.Import(temp.File("a.csv",Header+"\n"+Row+"\n"),Options);
        Assert.Throws<InvalidDataException>(()=>importer.Import(temp.File("b.csv",Header+"\n"+Row.Replace("AAA","BBB")+"\n"),Options with {DatasetId="other",SetCode="BBB",Partition=DatasetPartition.Holdout}));
        Assert.Empty(store.ReadDecisions(Split,DatasetPartition.Holdout));Assert.Single(store.ReadDecisions(Split,DatasetPartition.Calibration));
    }
    [Fact]
    public void FutureHistoryAndFuturePoolAreRejectedAndOutcomeNeverEntersReplay()
    {
        var d=Decision(2);Assert.Throws<InvalidDataException>(()=>HistoricalImporter.ValidateDecision(d with {Inputs=d.Inputs with {PriorPicks=[new(1,2,Identity("Beta"))]}},Options));
        Assert.Throws<InvalidDataException>(()=>HistoricalImporter.ValidateDecision(d with {Inputs=d.Inputs with {PoolBeforePick=[new(Identity("Alpha"),2)]}},Options));
        var after=d with {Outcome=new(0,3,[new(Identity("Beta"),40)],[new(Identity("Beta"),23)],[new(Identity("Alpha"),17)])};
        var a=Adapter.Replay(d.Inputs,"contextual-pick-score-v1.6.2",EvaluationRegime.MatureFormatReplay);
        var b=Adapter.Replay(after.Inputs,"contextual-pick-score-v1.6.2",EvaluationRegime.MatureFormatReplay);
        Assert.Equal(HistoricalJson.Serialize(a.Cards),HistoricalJson.Serialize(b.Cards));
        Assert.DoesNotContain("FinalWins",HistoricalJson.Serialize(d.Inputs));Assert.DoesNotContain("FinalMainDeck",HistoricalJson.Serialize(d.Inputs));
    }
    [Fact]
    public void CompletedSourcePoolIsOutcomeOnlyAndKeepsDuplicateCounts()
    {
        using var temp=new Temp();using var store=new HistoricalStore(temp.Path("store.sqlite"));
        var file=temp.File("completed.csv",Header+"\n"+Row+"\ndraft,AAA,PremierDraft,2,13,Alpha,7,2,1,0,41,0\n");
        var manifest=new HistoricalImporter(store,new(Catalog),Split,"hash").Import(file,Options);
        Assert.Equal(2,manifest.FinalPoolCoveredPicks);
        var first=store.ReadDecisions(Split).First();Assert.Empty(first.Inputs.PoolBeforePick);
        Assert.Equal(42,Assert.Single(first.Outcome.FinalPool!).Count);Assert.Null(first.Outcome.FinalMainDeck);
        Assert.Equal(HistoricalJson.Serialize(Adapter.Replay(Input(),"contextual-pick-score-v1.6.2",EvaluationRegime.MatureFormatReplay).Cards),
            HistoricalJson.Serialize(Adapter.Replay(first.Inputs,"contextual-pick-score-v1.6.2",EvaluationRegime.MatureFormatReplay).Cards));
    }
    [Fact]
    public void ReplayVersionsAreExplicitDeterministicAndChronologicalFailsClosed()
    {
        foreach(var model in new[]{"contextual-pick-score-v1.6","contextual-pick-score-v1.6.1","contextual-pick-score-v1.6.2"})
        {
            var a=Adapter.Replay(Input(),model,EvaluationRegime.MatureFormatReplay);var b=Adapter.Replay(Input(),model,EvaluationRegime.MatureFormatReplay);
            Assert.All(a.Cards,c=>Assert.Equal(model,c.ModelVersion));Assert.Equal(HistoricalJson.Serialize(a.Cards),HistoricalJson.Serialize(b.Cards));
            Assert.Equal(3,a.Cards.Length);Assert.Equal(2,a.Cards.Count(c=>c.CardIdentifier==A.Identifier));
        }
        Assert.Throws<ArgumentException>(()=>HistoricalReplayAdapter.Version("contextual-pick-score-v2"));
        Assert.Throws<InvalidDataException>(()=>Adapter.Replay(Input() with {DraftTimestamp=DateTimeOffset.UtcNow},"contextual-pick-score-v1.6.2",EvaluationRegime.ChronologicalBacktest));
        var pick15=Adapter.Replay(Input(15),"contextual-pick-score-v1.6.2",EvaluationRegime.MatureFormatReplay);Assert.Empty(pick15.Cards);Assert.True(pick15.UnscorableCards>0);
    }
    [Fact]
    public void MetricsUseEligibleDenominatorAndOccurrenceRanks()
    {
        var m=new MetricAccumulator();m.Add(1,3,0);m.Add(3,3,2);m.Add(null,3,null);
        var s=m.Summary();Assert.Equal(3,s.Decisions);Assert.Equal(2,s.Eligible);Assert.Equal(.5,s.Top1);Assert.Equal(1,s.Top3);
        Assert.Equal(2d/3,s.MeanReciprocalRank!.Value,12);Assert.Equal(.5,s.MeanNormalizedRank);Assert.Equal(1,s.MeanScoreGap);
    }
    [Fact]
    public void SequentialReplayUsesOnlyPastSelectedCardsAndRetainsSourcePool()
    {
        using var temp=new Temp();using var store=new HistoricalStore(temp.Path("store.sqlite"));
        var options=Options with {SourceSchema=HistoricalJson.Schema};
        var file=temp.File("input.jsonl",string.Join('\n',Enumerable.Range(1,3).Select(p=>HistoricalJson.Serialize(Decision(p))))+"\n");
        new HistoricalImporter(store,new(Catalog),Split,"hash").Import(file,options);
        var summary=new HistoricalEvaluator(store,Adapter,Split).Evaluate(["contextual-pick-score-v1.6.2"],EvaluationRegime.MatureFormatReplay,temp.Path("replay.jsonl"),"build","hash","stats");
        Assert.Equal(3,summary.Decisions);Assert.Equal(1,summary.Drafts);Assert.Equal(3,summary.Metrics["contextual-pick-score-v1.6.2 | overall"].Eligible);
        var first=JsonDocument.Parse(System.IO.File.ReadLines(temp.Path("replay.jsonl")).First()).RootElement.GetProperty("Models")[0].GetProperty("Cards");
        Assert.Equal(HistoricalJson.Serialize(Adapter.Replay(Input(),"contextual-pick-score-v1.6.2",EvaluationRegime.MatureFormatReplay).Cards),first.GetRawText());
        var records=store.ReadDecisions(Split).ToArray();Assert.Equal(new[]{0,1,2},records.Select(r=>r.Inputs.PoolBeforePick.Sum(c=>c.Count)));Assert.All(records,r=>Assert.Empty(r.Inputs.PriorPicks));
    }
    [Fact]
    public void GameDecksRemainSeparateObservedBuildsAndNeverInventFinalDeck()
    {
        using var temp=new Temp();using var store=new HistoricalStore(temp.Path("store.sqlite"));
        var file=temp.File("games.csv","draft_id,build_index,game_number,expansion,event_type,won,deck_Alpha,deck_Beta\ndraft,0,1,AAA,PremierDraft,True,2,1\ndraft,0,1,AAA,PremierDraft,False,2,1\n");
        var importer=new HistoricalDeckImporter(store,Catalog,new(Catalog));importer.ImportGames(file,"games","AAA","PremierDraft",[],"public");
        importer.Export(temp.Path("decks.jsonl"),temp.Path("counts.json"));
        var deck=HistoricalJson.Read<HistoricalLimitedDeck>(System.IO.File.ReadAllText(temp.Path("decks.jsonl")));
        Assert.Equal(3,deck.Summary.Creatures);Assert.Null(deck.Sideboard);Assert.Null(deck.FullPool);Assert.Null(deck.Wins);Assert.Contains("not a recovered final deck",deck.Evidence);
        Assert.Empty(store.ReadDecisions(Split));
        var creatureLand=Card("land-creature","Land creature","AAA") with {GameplayMetadata=new(0,"","Land Creature")};
        var landCatalog=new CardCatalog([creatureLand]);var landIdentity=new HistoricalIdentityResolver(landCatalog).Resolve("Land creature","AAA");
        var features=HistoricalDeckFeatures.Summarize([new(landIdentity,1)],landCatalog);
        Assert.Equal(1,features.Lands);Assert.Equal(1,features.Creatures);Assert.Equal(0,features.NonCreatureSpells);
    }
    sealed class Temp:IDisposable
    {
        readonly string root=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"DraftTG-historical-tests-"+Guid.NewGuid().ToString("N"));
        public Temp()=>Directory.CreateDirectory(root);
        public string Path(string name)=>System.IO.Path.Combine(root,name);
        public string File(string name,string text){var path=Path(name);System.IO.File.WriteAllText(path,text);return path;}
        public void Dispose()=>Directory.Delete(root,true);
    }
}
