using DraftTG.Domain;
using DraftTG.HistoricalEvaluation;
using DraftTG.RecommendationEngine;

namespace DraftTG.HistoricalEvaluation.Tests;

public sealed class ExperimentalLaneTests
{
    [Fact]
    public void OfflineLaneChangesOnlyLaneAndRecomposedScore()
    {
        Card Card(string id)=>new(CardIdentifier.Create(id),id,new([MagicColor.Green]),CardRarity.Common,CardSetCode.Create("AAA"),CollectorNumber.Create(id))
            {GameplayMetadata=new(2,"{1}{G}","Creature")};
        var catalog=new CardCatalog([Card("a"),Card("b")]);var resolver=new HistoricalIdentityResolver(catalog);
        var input=new PickTimeState("AAA","PremierDraft","draft",null,1,1,0,[new(resolver.Resolve("a","AAA"),1),new(resolver.Resolve("b","AAA"),1)],[],[],true);
        var adapter=new HistoricalReplayAdapter(catalog,resolver,[new("AAA","PremierDraft",EvaluationRegime.MatureFormatReplay,null,"test",[new("a","AAA","PremierDraft",.6,1000),new("b","AAA","PremierDraft",.55,1000)])],new Dictionary<string,string[]>());
        var baseline=adapter.Replay(input,"contextual-pick-score-v1.6.2",EvaluationRegime.MatureFormatReplay).Cards[0];
        var changed=ExperimentalLaneSubstitution.Substitute(baseline,.8,ExperimentalLaneSubstitution.Version+"/A");
        Assert.Equal(baseline.QualityComponent,changed.QualityComponent);Assert.Equal(baseline.EffectiveQualityComponent,changed.EffectiveQualityComponent);
        Assert.Equal(baseline.ArchetypeComponent,changed.ArchetypeComponent);Assert.Equal(baseline.DeckNeedComponent,changed.DeckNeedComponent);
        Assert.Equal(baseline.Weights,changed.Weights);Assert.Same(baseline.DeckImpact,changed.DeckImpact);Assert.Same(baseline.ColorCommitment,changed.ColorCommitment);
        Assert.Same(baseline.QualityPrior,changed.QualityPrior);Assert.Equal(baseline.QualityRelevance,changed.QualityRelevance);Assert.Equal(baseline.QualityProvenance,changed.QualityProvenance);
        Assert.NotEqual(baseline.ContextualValue,changed.ContextualValue);Assert.Equal("contextual-pick-score-v1.6.2",baseline.ModelVersion);
        var reproduced=ExperimentalLaneSubstitution.Substitute(baseline,baseline.LaneComponent,ExperimentalLaneSubstitution.Version+"/A");
        Assert.Equal(baseline.ContextualValue,reproduced.ContextualValue);
        var originalResult=adapter.Replay(input,"contextual-pick-score-v1.6.2",EvaluationRegime.MatureFormatReplay);
        var fallback=ExperimentalLaneSubstitution.Apply(originalResult,new([0,0,0,0,0],0,Applicable:false),catalog,"A");
        Assert.Equal(originalResult.Cards.Select(c=>c.ContextualValue),fallback.Cards.Select(c=>c.ContextualValue));
        Assert.Equal(originalResult.Cards.Select(c=>c.LaneComponent),fallback.Cards.Select(c=>c.LaneComponent));
        Assert.Throws<ArgumentException>(()=>ExperimentalLaneSubstitution.Substitute(baseline,.8,"contextual-pick-score-v2"));
    }
    [Fact]
    public void LaneStreamRejectsFutureIdentityAndInvalidBounds()
    {
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".jsonl");var alsa=path+".alsa";
        try
        {
            File.WriteAllText(alsa,"[]");File.WriteAllText(path,"""{"set":"AAA","format":"PremierDraft","draft":"draft","pack":1,"pick":2,"models":{"A":{"colors":[0,0,0,0,0],"confidence":0},"B":{"colors":[0,0,0,0,0],"confidence":0},"C":{"colors":[0,0,0,0,0],"confidence":0}}}""");
            using var stream=new ExperimentalLaneSubstitution(path,alsa);
            Assert.Throws<InvalidDataException>(()=>stream.Read(new("AAA","PremierDraft","draft",null,1,1,0,[],[],[],true)));
            Assert.Throws<InvalidDataException>(()=>ExperimentalLaneSubstitution.Apply(null!,new([0,0,0,2,0],.5),new CardCatalog(),"A"));
        }
        finally {File.Delete(path);File.Delete(alsa);}
    }
}
