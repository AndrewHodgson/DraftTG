using DraftTG.Domain;
using DraftTG.TestScenarios;

namespace DraftTG.RecommendationEngine.Tests;

public sealed class CommitmentRelevanceTests
{
    [Fact]
    public void GridPreservesOpenPlayablePoorAndPremiumCasesAndIsDenseContinuousAndMonotone()
    {
        var config=new ContextualPickScoreConfiguration();
        foreach(var c in new[]{0,.25,.5,.75,1}) foreach(var p in new[]{0,.25,.5,.75,1}) foreach(var q in new[]{.3,.5,.6,.8,.95})
        {
            var r=config.CommitmentQualityRelevance(q,1,c,p);
            Assert.InRange(r,.05,1);
            if(c==0 || p==1) Assert.Equal(1,r);
            if(q<=.5) Assert.Equal(q,ContextualPickScoreConfiguration.EffectiveQuality(q,r));
        }
        Assert.Equal(.05,config.CommitmentQualityRelevance(.6,1,1,0),12);
        Assert.Equal(.2,config.CommitmentQualityRelevance(.95,1,1,0),12);
        Assert.True(ContextualPickScoreConfiguration.EffectiveQuality(.95,.2)>.55);
        foreach(var axis in new[]{"Commitment","Playability","Quality","Stage"})
        {
            double? previous=null;
            for(var i=0;i<=10000;i++)
            {
                var x=i/10000d;var q=axis=="Quality"?x:.6;var c=axis=="Commitment"?x:1;
                var p=axis=="Playability"?x:0;var stage=axis=="Stage"?x:7/42d;
                var relevance=config.CommitmentQualityRelevance(q,config.QualityRelevance(p,stage),c,p);
                var value=ContextualPickScoreConfiguration.EffectiveQuality(q,relevance);
                if(previous is { } old)
                {
                    Assert.InRange(Math.Abs(value-old),0,.00011);
                    Assert.True(axis is "Quality" or "Playability" ? value>=old-1e-12 : value<=old+1e-12);
                }
                previous=value;
            }
        }
    }

    [Fact]
    public void CommitmentConsumesProjectionSeparationColoredEvidenceAndPartialInventoryCoverage()
    {
        var cards=CommitmentRelevanceScenarios.Pool(7,gold:true).Select(e=>e.Card).ToArray();
        var catalog=new CardCatalog(cards);var drafted=new DraftedCardPool(cards.Select(c=>c.Identifier));
        var colors=ColorCommitmentProfile.From(drafted,catalog);
        var pair=ArchetypeColorPair.Create("RG");
        var decisive=new[]{new PoolProjectionEvidence(pair,.56,7,0,1)};
        var full=ProjectedColorCommitment.From(colors,decisive,new(drafted,DraftPoolCompleteness.Complete),7);
        var partial=ProjectedColorCommitment.From(colors,decisive,new(drafted,DraftPoolCompleteness.Partial),21);
        Assert.Equal(1,full.Strength);Assert.Equal(1/3d,partial.Strength,12);
        var open=ProjectedColorCommitment.From(colors,[new(pair,.56,7,0,.5),new(ArchetypeColorPair.Create("UR"),.56,7,0,.5)],new(drafted,DraftPoolCompleteness.Complete),7);
        Assert.Equal(0,open.Strength);
        Assert.Equal(ProjectedColorCommitment.Open,ProjectedColorCommitment.From(colors,[],new(drafted,DraftPoolCompleteness.Complete),7));
    }

    [Fact]
    public void ControlledOpenToCommittedEvolutionIsGradualAndPremiumPivotRemainsPossible()
    {
        var blue=CommitmentRelevanceScenarios.Card("blue","{2}{U}",.58);
        var green=CommitmentRelevanceScenarios.Card("green","{2}{G}",.561);
        var scores=Enumerable.Range(0,15).Select(n=>CommitmentRelevanceScenarios.Score([blue,green],CommitmentRelevanceScenarios.Pool(n,gold:true))).ToArray();
        Assert.Equal(1,scores[0].Cards[0].CurrentPackRank);Assert.Equal(1,scores[1].Cards[0].CurrentPackRank);
        Assert.Equal(2,scores[7].Cards[0].CurrentPackRank);
        Assert.All(scores.Zip(scores.Skip(1)),t=>Assert.InRange(50*(t.First.Cards[0].ContextualValue!.Value-t.Second.Cards[0].ContextualValue!.Value),-1e-9,2.1));
        var bomb=CommitmentRelevanceScenarios.Card("bomb","{3}{U}",.70,manaValue:4);
        var moderate=CommitmentRelevanceScenarios.Score([bomb,green],CommitmentRelevanceScenarios.Pool(3,gold:true));
        var strong=CommitmentRelevanceScenarios.Score([bomb,green],CommitmentRelevanceScenarios.Pool(7,gold:true));
        Assert.InRange(moderate.Cards[0].ColorCommitment!.Strength,.25,.5);Assert.Equal(1,moderate.Cards[0].CurrentPackRank);
        Assert.Equal(1,strong.Cards[0].CurrentPackRank);Assert.True(strong.Cards[0].QualityRelevance>0);
        var open=CommitmentRelevanceScenarios.Score([blue,green],CommitmentRelevanceScenarios.OpenPool(7));
        Assert.Equal(1,open.Cards[0].CurrentPackRank);Assert.InRange(open.Cards[0].ColorCommitment!.Strength,0,.35);
        Assert.Equal(strong.Cards.Select(c=>c.ContextualValue),CommitmentRelevanceScenarios.Score([bomb,green],CommitmentRelevanceScenarios.Pool(7,gold:true)).Cards.Select(c=>c.ContextualValue));
    }

    [Fact]
    public void ExistingFixingEligibilityProtectsColorlessLandsAndSinglePipGoldButNotDoubleOrTriplePips()
    {
        var pack=new[]{CommitmentRelevanceScenarios.Card("single","{3}{U}",.65,manaValue:4),CommitmentRelevanceScenarios.Card("double","{1}{U}{U}",.65),
            CommitmentRelevanceScenarios.Card("triple","{U}{U}{U}",.65),CommitmentRelevanceScenarios.Card("gold","{2}{R}{U}",.65,manaValue:4),
            CommitmentRelevanceScenarios.Card("colorless","{3}",.65),CommitmentRelevanceScenarios.Card("land","",.56,"Land",[ManaKind.Red,ManaKind.Blue])};
        var without=CommitmentRelevanceScenarios.Score(pack,CommitmentRelevanceScenarios.Pool(7,gold:true));
        var with=CommitmentRelevanceScenarios.Score(pack,CommitmentRelevanceScenarios.Pool(7,3,gold:true));
        var legacy=CommitmentRelevanceScenarios.Score(pack,CommitmentRelevanceScenarios.Pool(7,3,gold:true),ContextualPickScoreVersion.V1_6_1);
        Assert.Equal(.6,with.Cards[0].DeckImpact.Playability,12);Assert.Equal(.6,with.Cards[3].DeckImpact.Playability,12);
        Assert.Equal(0,with.Cards[1].DeckImpact.Playability);Assert.Equal(0,with.Cards[2].DeckImpact.Playability);
        Assert.True(with.Cards[0].QualityRelevance>without.Cards[0].QualityRelevance);
        foreach(var i in new[]{4,5}){Assert.Equal(1,with.Cards[i].DeckImpact.Playability);Assert.Equal(1,with.Cards[i].QualityRelevance);}
        Assert.Equal(legacy.Cards.Select(c=>(c.DeckImpact.Playability,c.DeckNeedComponent,c.LaneComponent,c.ArchetypeComponent,c.QualityComponent)),
            with.Cards.Select(c=>(c.DeckImpact.Playability,c.DeckNeedComponent,c.LaneComponent,c.ArchetypeComponent,c.QualityComponent)));
    }
}
