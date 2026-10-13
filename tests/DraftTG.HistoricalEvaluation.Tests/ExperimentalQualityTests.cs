using DraftTG.HistoricalEvaluation;
using DraftTG.RecommendationEngine;
using DraftTG.Domain;

namespace DraftTG.HistoricalEvaluation.Tests;

public sealed class ExperimentalQualityTests
{
    [Fact]
    public void SubstitutionFreezesContextWeightsAndCommitmentRelevanceAndVersionsResearch()
    {
        // Real production golden values for committed P1P8 Unsummon. The full golden tests remain authoritative.
        var config=new ContextualPickScoreConfiguration();
        Assert.Equal("contextual-pick-score-v1.6.2",config.ModelVersion);
        Assert.Equal("research-q-phase-b-v1",ExperimentalQualitySubstitution.Version);
        Assert.Throws<ArgumentException>(()=>ExperimentalQualitySubstitution.Substitute(null!,.9,"contextual-pick-score-v2"));
        var relevance=.10262390670553934;
        Card Card(string id) => new(CardIdentifier.Create(id),id,new([MagicColor.Green]),CardRarity.Common,
            CardSetCode.Create("AAA"),CollectorNumber.Create(id)) { GameplayMetadata=new(2,"{1}{G}","Creature") };
        var catalog=new CardCatalog([Card("a"),Card("b")]);var resolver=new HistoricalIdentityResolver(catalog);
        var input=new PickTimeState("AAA","PremierDraft","draft",null,1,1,0,
            [new(resolver.Resolve("a","AAA"),1),new(resolver.Resolve("b","AAA"),1)],[],[],true);
        var adapter=new HistoricalReplayAdapter(catalog,resolver,[new("AAA","PremierDraft",EvaluationRegime.MatureFormatReplay,null,"test",
            [new("a","AAA","PremierDraft",.6,1000),new("b","AAA","PremierDraft",.55,1000)])],new Dictionary<string,string[]>());
        var baseline=adapter.Replay(input,"contextual-pick-score-v1.6.2",EvaluationRegime.MatureFormatReplay).Cards[0] with {QualityRelevance=relevance};
        var originalQ=baseline.QualityComponent;
        var substituted=ExperimentalQualitySubstitution.Substitute(baseline,.9,ExperimentalQualitySubstitution.Version+"/A");
        Assert.Equal(originalQ,baseline.QualityComponent);
        Assert.Equal(baseline.Weights,substituted.Weights);
        Assert.Equal(baseline.LaneComponent,substituted.LaneComponent);
        Assert.Equal(baseline.ArchetypeComponent,substituted.ArchetypeComponent);
        Assert.Equal(baseline.DeckNeedComponent,substituted.DeckNeedComponent);
        Assert.Equal(baseline.DeckImpact,substituted.DeckImpact);
        Assert.Equal(baseline.ColorCommitment,substituted.ColorCommitment);
        Assert.Equal(relevance,substituted.QualityRelevance);
        Assert.Equal(.5+relevance*.4,substituted.EffectiveQualityComponent!.Value,12);
        Assert.Throws<InvalidDataException>(()=>new ExperimentalQualitySubstitution("""
            [{"set":"AAA","format":"PremierDraft","canonicalId":"id","qualities":{"A":1.1,"B":0.5,"C":0.5}}]
            """));
    }

    [Fact]
    public void DuplicateExperimentalIdentityCannotSelectFirstRow()
    {
        const string row="""{"set":"AAA","format":"PremierDraft","canonicalId":"id","qualities":{"A":0.5,"B":0.5,"C":0.5}}""";
        Assert.Throws<InvalidDataException>(()=>new ExperimentalQualitySubstitution("["+row+","+row+"]"));
        Assert.Equal(3,new ExperimentalQualitySubstitution("["+row+"]").Models.Length);
    }
}
