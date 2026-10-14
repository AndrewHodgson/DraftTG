using DraftTG.Domain;
using DraftTG.HistoricalEvaluation;
using DraftTG.RecommendationEngine;
namespace DraftTG.HistoricalEvaluation.Tests;
public sealed class ExperimentalDeckFitTests
{
 [Fact]
 public void DOnlyPreservesQualityRelevanceCommitmentAndImmutableProjection()
 {
  var impact=new CandidateDeckImpact(false,false,null,null,0,0,0,0,false,false,true,null,null,0,.1,[]);
  var baseline=new ContextualPickScore(0,CardIdentifier.Create("x"),.6,.9,.4,.3,.2,.8,0,.7,new(.3,.1,.2,.4),PickScoreAvailability.Measured,"contextual-pick-score-v1.6.2",impact,new(LimitedCardRole.None,LimitedCardRole.None,false,"unknown"),[],[])
  {QualityRelevance=.1,EffectiveQualityComponent=.54,CurrentPackRank=1};
  var c=ExperimentalDeckFitSubstitution.Substitute(baseline,.8,"B");
  Assert.Equal(.8,c.DeckNeedComponent);Assert.Equal(.9,c.QualityComponent);Assert.Equal(.54,c.EffectiveQualityComponent);Assert.Equal(.1,c.QualityRelevance);
  Assert.Equal(baseline.Weights,c.Weights);Assert.Equal(baseline.LaneComponent,c.LaneComponent);Assert.Equal(baseline.ArchetypeComponent,c.ArchetypeComponent);
  Assert.Same(baseline.DeckImpact,c.DeckImpact);Assert.Same(baseline.ColorCommitment,c.ColorCommitment);Assert.Same(baseline.QualityPrior,c.QualityPrior);
  Assert.Equal(ContextualPickScoreCalibration.Calibrate(.3*.54+.1*.4+.2*.3+.4*.8),c.ContextualValue);
  Assert.Same(baseline,ExperimentalDeckFitSubstitution.Substitute(baseline,null,"A"));
  Assert.Throws<InvalidDataException>(()=>ExperimentalDeckFitSubstitution.Substitute(baseline,double.NaN,"C"));
  Assert.Throws<ArgumentException>(()=>ExperimentalDeckFitSubstitution.Substitute(baseline,.5,"production"));
 }
}
