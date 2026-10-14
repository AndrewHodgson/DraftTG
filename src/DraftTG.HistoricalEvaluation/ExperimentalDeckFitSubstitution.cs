using DraftTG.RecommendationEngine;
namespace DraftTG.HistoricalEvaluation;

/// <summary>Offline D-only recomposition. Never referenced by live application scoring.</summary>
public static class ExperimentalDeckFitSubstitution
{
 public const string Version="research-d-phase-e-v1";
 public static double Calibrate(PickScoreWeights weights,double effectiveQ,double lane,double affinity,double deckFit)
 {
  if(!double.IsFinite(deckFit)||deckFit is <0 or >1)throw new InvalidDataException("Finite bounded D required.");
  return ContextualPickScoreCalibration.Calibrate(weights.Quality*effectiveQ+weights.Lane*lane+weights.Archetype*affinity+weights.DeckNeed*deckFit);
 }
 public static ContextualPickScore Substitute(ContextualPickScore baseline,double? deckFit,string family)
 {
  if(family is not ("A" or "B" or "C"))throw new ArgumentException("Offline Phase E family required.");
  if(deckFit is null)return baseline;
  var value=Calibrate(baseline.Weights,baseline.EffectiveQualityComponent??.5,baseline.LaneComponent,baseline.ArchetypeComponent,deckFit.Value);
  var weighted=baseline.Weights.Quality*(baseline.EffectiveQualityComponent??.5)+baseline.Weights.Lane*baseline.LaneComponent+baseline.Weights.Archetype*baseline.ArchetypeComponent+baseline.Weights.DeckNeed*deckFit.Value;
  var linear=.5+ContextualPickScoreCalibration.DefaultGain*(weighted-.5);
  var contributions=baseline.Contributions.Select(c=>c.Component=="Deck Need"?c with {Value=deckFit.Value,PointsFromNeutral=50*ContextualPickScoreCalibration.DefaultGain*baseline.Weights.DeckNeed*(deckFit.Value-.5)}
   :c.Component=="Soft limit"?c with {Value=linear,PointsFromNeutral=50*(value-linear)}:c).ToArray();
  return baseline with {DeckNeedComponent=deckFit.Value,ContextualValue=baseline.ContextualValue is null?null:value,ModelVersion=Version+"/"+family,CurrentPackRank=null,IsContextPick=false,Contributions=contributions};
 }
}
