using System.Globalization;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;

namespace DraftTG.Application;

public static class LaneRecommendationPresentation
{
    private static string Number(double value, string format = "0.###") => value.ToString(format, CultureInfo.InvariantCulture);
    private static string Symbol(MagicColor color) => color switch
    { MagicColor.White => "W", MagicColor.Blue => "U", MagicColor.Black => "B", MagicColor.Red => "R", _ => "G" };
    private static string Rate(double? value) => value is { } rate ? Number(rate, "0.0000") : "unscored";
    private static string Rank(int? rank) => rank is { } value ? $"#{value}" : "—";
    private static string Adjustment(double value) => Number(value * 100, "+0.0;-0.0;0.0") + " pp";
    private static string Source(LimitedStatisticsFormat? format) => format switch
    {
        LimitedStatisticsFormat.PremierDraft => "Premier Draft / human",
        LimitedStatisticsFormat.TraditionalDraft => "Traditional Draft / human",
        LimitedStatisticsFormat.QuickDraft => "Quick Draft / bot",
        _ => "Unknown or unsupported / no lane adjustment"
    };

    public static string Summary(LaneDraftRecommendation? recommendation, string? topName, bool loading = false)
    {
        if (loading) return "Context Pick: loading…";
        if (recommendation is null) return string.Empty;
        var top = recommendation.Cards.FirstOrDefault(card => card.IsTopContextualCandidate);
        var text = top is null ? "Context Pick: insufficient comparable statistics"
            : $"Context Pick\n{topName}\nStats rank: {Rank(top.StatisticalRecommendation.StatisticalRank)} · "
                + $"Pool rank: {Rank(top.PoolRecommendation.ContextualRank)} · Context rank: {Rank(top.ContextualRank)}"
                + $"\nPool adjustment: {Adjustment(top.PoolRecommendation.ColorAdjustment)} · Lane: {Adjustment(top.LaneAdjustment)}";
        return text + ContextEvidence(recommendation);
    }

    // Shared explanatory context, without an additional pick label in the final rail.
    public static string ContextEvidence(LaneDraftRecommendation recommendation)
    {
        var pool = recommendation.PoolRecommendation.Profile;
        var lane = recommendation.Profile;
        var colors = string.Join("  ", pool.Colors.Where(c => c.Evidence > 0)
            .OrderByDescending(c => c.Evidence).ThenBy(c => c.Color)
            .Select(c => $"{Symbol(c.Color)} {Number(c.EffectiveSupport, "+0.00;-0.00;0.00")}"));
        var signals = string.Join("  ", lane.Colors.Where(c => c.EffectiveSupport > 0)
            .OrderByDescending(c => c.EffectiveSupport).ThenBy(c => c.Color).Take(2)
            .Select(c => $"{Symbol(c.Color)} {Number(c.EffectiveSupport, "+0.00;-0.00;0.00")}"));
        var text = $"\nPool colors: {(colors.Length == 0 ? "neutral" : colors)}"
            + $"\nCommitment progress: {Number(pool.ProgressFactor, "0.00")} ({pool.CompletedPickCount} picks)"
            + $"\nLane signal: {(signals.Length == 0 ? "no useful evidence" : signals)}"
            + $"\nLane source: {Source(recommendation.Format)}"
            + $"\nLane observations: {lane.EligibleObservationCount} · confidence {Number(lane.ObservationConfidence, "0.00")}";
        if (!recommendation.ObservationHistory.StartsAtBeginning && recommendation.ObservationHistory.FirstObservedPosition is { } first)
            text += $"\nObserved since P{first.Pack.Value}P{first.Pick.Value} (partial history)";
        return text + $"\nStats coverage: {recommendation.StatisticalRecommendation.ScoredCardCount} / {recommendation.Cards.Count}";
    }

    public static string Diagnostics(LaneDraftRecommendation? recommendation)
    {
        if (recommendation is null) return string.Empty;
        var profile = recommendation.Profile;
        var configuration = profile.Configuration;
        var text = $"Lane heuristic {configuration.ModelVersion}\n{Source(recommendation.Format)}; "
            + $"maximum ±{Number(recommendation.MaximumLaneAdjustment * 100, "0.0")} pp"
            + $"\nLateness scale {Number(configuration.LateArrivalScale)}; strength scale {Number(configuration.LaneStrengthScale)}"
            + $"\nPosition ramp picks {configuration.LaneSignalStartPick}–{configuration.LaneSignalFullPick} (inclusive)"
            + $"\nRecency {Number(configuration.CurrentPackEvidenceWeight)}/{Number(configuration.PreviousPackEvidenceWeight)}/{Number(configuration.TwoPacksBackEvidenceWeight)}"
            + $"\nEvidence scale {Number(configuration.LaneEvidenceScale)}; confidence {profile.EligibleObservationCount}/{configuration.FullLaneConfidenceAfterObservations}"
            + $"\nRecorded packs: {recommendation.ObservationHistory.Observations.Count}; meaningful packs: {profile.EligibleObservationCount}";
        text += "\n" + string.Join("\n", profile.Colors.Select(c => $"{Symbol(c.Color)} lane evidence {Number(c.Evidence)}, "
            + $"raw {Number(c.RawSupport)}, confidence {Number(profile.ObservationConfidence)}, effective {Number(c.EffectiveSupport)}"));
        text += "\nMeaningful late-card signals:\n" + string.Join("\n", profile.Signals.Select(s =>
            $"{s.CardIdentifier.Value} P{s.Position.Pack.Value}P{s.Position.Pick.Value} slot {s.PackIndex + 1} "
            + $"{string.Join("", s.Colors.Colors.Select(Symbol))}: ALSA {Number(s.AverageLastSeenAt)}, "
            + $"stats {Rate(s.Phase8AdjustedValue)}, baseline {Rate(s.EnvironmentBaseline)}, late {Number(s.LatenessWeight)}, "
            + $"strength {Number(s.StrengthWeight)}, position {Number(s.PositionWeight)}, evidence {Number(s.OpenEvidence)}, "
            + $"recency {Number(s.PackRecencyWeight)}, weighted {Number(s.WeightedOpenEvidence)}"));
        return text + "\nFinal occurrences:\n" + string.Join("\n", recommendation.Cards.Select(c =>
            $"Slot {c.PackIndex + 1}: stats {Rate(c.StatisticalRecommendation.AdjustedValue)}, "
            + $"pool {Rate(c.PoolRecommendation.ContextualValue)}, lane fit {Number(c.LaneFit)}, "
            + $"lane adjustment {Adjustment(c.LaneAdjustment)}, final {Rate(c.ContextualValue)}, "
            + $"ranks {Rank(c.StatisticalRecommendation.StatisticalRank)}/{Rank(c.PoolRecommendation.ContextualRank)}/{Rank(c.ContextualRank)}"));
    }
}
