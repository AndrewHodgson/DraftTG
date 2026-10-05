using System.Globalization;
using DraftTG.RecommendationEngine;

namespace DraftTG.Application;

public static class ArchetypeRecommendationPresentation
{
    private static string Number(double? value, string format = "0.0000") => value?.ToString(format, CultureInfo.InvariantCulture) ?? "unavailable";
    private static string Rank(int? rank) => rank is { } value ? $"#{value}" : "—";
    private static string Adjustment(double value) => Number(value * 100, "+0.0;-0.0;0.0") + " pp";
    public static string Summary(ArchetypeRecommendationResult? result, string? topName, bool loading = false)
    {
        if (loading) return "Context Pick: loading…";
        if (result is null) return string.Empty;
        var top = result.Cards.FirstOrDefault(c => c.IsTopContextualCandidate);
        if (top is null) return "Context Pick: insufficient comparable statistics" + LaneRecommendationPresentation.ContextEvidence(result.LaneRecommendation);
        return $"Context Pick\n{topName}\nStats rank: {Rank(top.StatisticalRecommendation.StatisticalRank)} · Pool rank: {Rank(top.LaneRecommendation.PoolRecommendation.ContextualRank)}"
            + $"\nLane rank: {Rank(top.LaneRecommendation.ContextualRank)} · Final rank: {Rank(top.ContextualRank)}"
            + $"\nPool: {Adjustment(top.LaneRecommendation.PoolRecommendation.ColorAdjustment)} · Lane: {Adjustment(top.LaneRecommendation.LaneAdjustment)}"
            + $"\nArchetype adjustment: {Adjustment(top.Affinity.Adjustment)}"
            + (top.Affinity.PairRawGih is { } gih ? $"\nPair GIH: {Number(gih * 100, "0.0")}% (n={top.Affinity.PairSample?.ToString("N0", CultureInfo.InvariantCulture)})" : "")
            + LaneRecommendationPresentation.ContextEvidence(result.LaneRecommendation);
    }
    public static string Archetype(ArchetypeRecommendationResult? result, ArchetypeDataStatus status)
    {
        if (result is null) return "Archetype\nUnsettled";
        if (result.Profile.Active is not { } active)
            return "Archetype\nUnsettled" + (result.Profile.Set is null ? " (no set profile)" : "\n" + string.Join("\n", result.Profile.Candidates.Take(2)
                .Select(c => $"{c.Definition.Pair.DisplayCode} {c.Definition.Name} · {Number(c.Confidence, "0.00")}")));
        return $"Archetype\n{active.Definition.Pair.DisplayCode} — {active.Definition.Name}\n{active.Definition.Description}"
            + $"\nConfidence: {Number(active.Confidence, "0.00")}\n{status.Text}";
    }
    public static string Diagnostics(ArchetypeRecommendationResult? result, ArchetypeDataStatus status)
    {
        if (result is null) return string.Empty;
        var configuration = result.Profile.Configuration;
        var text = $"Archetype model {configuration.ModelVersion}; minimum confidence {Number(configuration.MinimumArchetypeConfidence)}; lead {Number(configuration.MinimumArchetypeLead)}"
            + $"\nPair prior games {configuration.PairPriorEquivalentGames}; lift scale {Number(configuration.ArchetypeLiftScale)}; maximum ±{Number(configuration.MaxArchetypeAdjustment * 100, "0.0")} pp"
            + $"\nCurated descriptions: {result.Profile.Set?.Source ?? "no profile"}\n{status.Text}; {status.Diagnostic}"
            + $"\nPair baseline: {Number(result.PairStatistics?.Baseline)}";
        text += "\n" + string.Join("\n", result.Profile.Candidates.Select(c =>
            $"{c.Definition.Pair.DisplayCode}: coverage {Number(c.Coverage)}, balance {Number(c.Balance)}, pool fit {Number(c.PairPoolFit)}, progress {Number(c.ProgressFactor)}, confidence {Number(c.Confidence)}"));
        text += "\n" + string.Join("\n", result.Cards.Select(c => $"Slot {c.PackIndex}: {c.CardIdentifier}; Phase 8 {Number(c.StatisticalRecommendation.AdjustedValue)}; "
            + $"Lane value {Number(c.LaneRecommendation.ContextualValue)}; overall baseline {Number(result.LaneRecommendation.StatisticalRecommendation.EnvironmentBaseline)}; "
            + $"pair raw {Number(c.Affinity.PairRawGih)}; n {c.Affinity.PairSample}; pair adjusted {Number(c.Affinity.PairAdjusted)}; pair baseline {Number(result.PairStatistics?.Baseline)}; "
            + $"lift {Number(c.Affinity.Lift)}; affinity {Number(c.Affinity.NormalizedAffinity)}; adjustment {Adjustment(c.Affinity.Adjustment)}; "
            + $"final {Number(c.ContextualValue)}; final rank {Rank(c.ContextualRank)}; {c.Affinity.Diagnostic}"));
        return text;
    }
}
