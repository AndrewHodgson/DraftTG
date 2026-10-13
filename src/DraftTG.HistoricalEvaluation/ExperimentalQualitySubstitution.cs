using System.Text.Json;
using DraftTG.RecommendationEngine;

namespace DraftTG.HistoricalEvaluation;

/// <summary>Offline ablation only. Retains the production L/A/D, weights, commitment AND already-computed R.
/// Does not rerun optimizers with experimental statistics, mutate production configuration, or authorize V2.</summary>
public sealed class ExperimentalQualitySubstitution
{
    public const string Version = "research-q-phase-b-v1";
    private readonly Dictionary<(string Set, string Format, string Id, string Candidate), double> _quality = [];
    public string[] Models { get; }
    public ExperimentalQualitySubstitution(string json)
    {
        using var document = JsonDocument.Parse(json);
        foreach (var row in document.RootElement.EnumerateArray())
        {
            var set = row.GetProperty("set").GetString()!;
            var format = row.GetProperty("format").GetString()!;
            var id = row.GetProperty("canonicalId").GetString()!;
            foreach (var candidate in new[] { "A", "B", "C" })
            {
                var q = row.GetProperty("qualities").GetProperty(candidate).GetDouble();
                if (!double.IsFinite(q) || q is < 0 or > 1) throw new InvalidDataException("Invalid experimental Q.");
                if (!_quality.TryAdd((set,format,id,candidate),q)) throw new InvalidDataException("Conflicting/duplicate experimental identity.");
            }
        }
        Models = [Version+"/A",Version+"/B",Version+"/C"];
    }
    public static ContextualPickScore Substitute(ContextualPickScore baseline, double quality, string model)
    {
        if (!new[] { Version+"/A",Version+"/B",Version+"/C" }.Contains(model) || !double.IsFinite(quality) || quality is < 0 or > 1)
            throw new ArgumentException("Explicit versioned research candidate and bounded Q required.");
        var effective = ContextualPickScoreConfiguration.EffectiveQuality(quality,baseline.QualityRelevance);
        var weighted = baseline.Weights.Quality*effective+baseline.Weights.Lane*baseline.LaneComponent
            +baseline.Weights.Archetype*baseline.ArchetypeComponent+baseline.Weights.DeckNeed*baseline.DeckNeedComponent;
        return baseline with { QualityComponent=quality, EffectiveQualityComponent=effective,
            ContextualValue=baseline.ContextualValue is null?null:ContextualPickScoreCalibration.Calibrate(weighted),
            ModelVersion=model, CurrentPackRank=null, IsContextPick=false,
            Reasons=["Offline Q-only substitution; frozen production L/A/D, weights, commitment, R and evidence tie breaks. Neutral Q for missing candidate evidence."],
            Contributions=[] };
    }
    public HistoricalReplayResult Apply(PickTimeState inputs, HistoricalReplayResult baseline, string model)
    {
        var candidate=model[(Version.Length+1)..];
        var cards=baseline.Cards.Select(c=>Substitute(c,_quality.GetValueOrDefault((inputs.SetCode,inputs.Format,c.CardIdentifier.Value,candidate),.5),model)).ToArray();
        var ordered=cards.Where(c=>c.ContextualValue.HasValue).OrderByDescending(c=>c.ContextualValue)
            .ThenByDescending(c=>c.QualityComponent).ThenByDescending(c=>c.StatisticalDataWeight).ThenBy(c=>c.PackIndex).ToArray();
        if (ordered.Length>=2)
            for (var i=0;i<ordered.Length;i++)
            {
                var index=Array.FindIndex(cards,c=>c.PackIndex==ordered[i].PackIndex);
                cards[index]=ordered[i] with { CurrentPackRank=i+1,IsContextPick=i==0 };
            }
        return baseline with { ModelVersion=model,Cards=cards,OptimizerCalls=0,TotalMilliseconds=0,
            MissingInputs=baseline.MissingInputs.Append("Experimental Q-only ablation; relevance R and all contextual evidence frozen from v1.6.2. Not a V2 model.").ToArray() };
    }
}
