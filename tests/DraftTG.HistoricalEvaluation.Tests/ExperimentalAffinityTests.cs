using DraftTG.Domain;
using DraftTG.HistoricalEvaluation;
using DraftTG.RecommendationEngine;

namespace DraftTG.HistoricalEvaluation.Tests;

public sealed class ExperimentalAffinityTests
{
    [Fact]
    public void AffinityOnlySubstitutionPreservesEveryFrozenComponentAndColdFallback()
    {
        Card Card(string id)=>new(CardIdentifier.Create(id),id,new([MagicColor.Green]),CardRarity.Common,CardSetCode.Create("AAA"),CollectorNumber.Create(id))
            {GameplayMetadata=new(2,"{1}{G}","Creature")};
        var catalog=new CardCatalog([Card("a"),Card("b")]);var resolver=new HistoricalIdentityResolver(catalog);
        var input=new PickTimeState("AAA","PremierDraft","draft",null,1,1,0,[new(resolver.Resolve("a","AAA"),1),new(resolver.Resolve("b","AAA"),1)],[],[],true);
        var adapter=new HistoricalReplayAdapter(catalog,resolver,[new("AAA","PremierDraft",EvaluationRegime.MatureFormatReplay,null,"test",[new("a","AAA","PremierDraft",.6,1000),new("b","AAA","PremierDraft",.55,1000)])],new Dictionary<string,string[]>());
        var b=adapter.Replay(input,"contextual-pick-score-v1.6.2",EvaluationRegime.MatureFormatReplay).Cards[0];
        var c=ExperimentalAffinitySubstitution.Substitute(b,new(.8,.6),ExperimentalAffinitySubstitution.Version+"/B");
        Assert.Equal(b.QualityComponent,c.QualityComponent);Assert.Equal(b.EffectiveQualityComponent,c.EffectiveQualityComponent);
        Assert.Equal(b.LaneComponent,c.LaneComponent);Assert.Equal(b.DeckNeedComponent,c.DeckNeedComponent);Assert.Equal(b.Weights,c.Weights);
        Assert.Same(b.ColorCommitment,c.ColorCommitment);Assert.Same(b.DeckImpact,c.DeckImpact);Assert.Same(b.QualityPrior,c.QualityPrior);
        Assert.Equal(b.QualityRelevance,c.QualityRelevance);Assert.Equal(b.QualityProvenance,c.QualityProvenance);
        Assert.Equal(b.ArchetypeComponent+.6*(.8-b.ArchetypeComponent),c.ArchetypeComponent);Assert.NotEqual(b.ContextualValue,c.ContextualValue);
        foreach(var state in new[]{new ResearchAffinityState(.8,0),new(.8,1,false)})
        {
            var declined=ExperimentalAffinitySubstitution.Substitute(b,state,ExperimentalAffinitySubstitution.Version+"/B");
            Assert.Equal(b.ContextualValue,declined.ContextualValue);Assert.Equal(b.ArchetypeComponent,declined.ArchetypeComponent);
            Assert.Equal(b.CurrentPackRank,declined.CurrentPackRank);
        }
        Assert.Throws<ArgumentException>(()=>ExperimentalAffinitySubstitution.Substitute(b,new(.8,1),"contextual-pick-score-v2"));
        Assert.Equal("contextual-pick-score-v1.6.2",b.ModelVersion);
    }
    [Fact]
    public void AffinityStreamRejectsFutureDuplicateWrongOccurrenceAndInvalidBounds()
    {
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".jsonl");
        var input=new PickTimeState("AAA","PremierDraft","draft",null,1,1,0,[],[],[],true);
        const string good="""{"set":"AAA","format":"PremierDraft","draft":"draft","pack":1,"pick":1,"models":{"A":{},"B":{},"C":{}}}""";
        try
        {
            File.WriteAllText(path,good+"\n"+good);
            using(var stream=new ExperimentalAffinitySubstitution(path))
            {stream.Read(input);Assert.Throws<InvalidDataException>(()=>stream.Read(input));}
            File.WriteAllText(path,good.Replace("\"pick\":1","\"pick\":2",StringComparison.Ordinal));
            using(var stream=new ExperimentalAffinitySubstitution(path))Assert.Throws<InvalidDataException>(()=>stream.Read(input));
            File.WriteAllText(path,good.Replace("\"A\":{}","\"A\":{\"extra\":{\"fit\":0.5,\"confidence\":0}}",StringComparison.Ordinal));
            using(var stream=new ExperimentalAffinitySubstitution(path))Assert.Throws<InvalidDataException>(()=>stream.Read(input));
            Assert.Throws<InvalidDataException>(()=>ExperimentalAffinitySubstitution.Substitute(null!,new(double.NaN,.5),ExperimentalAffinitySubstitution.Version+"/A"));
        }
        finally {File.Delete(path);}
    }
}
