using System.Globalization;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;

namespace DraftTG.Application;

public static class ContextualRecommendationPresentation
{
    private static string Number(double value, string format) => value.ToString(format, CultureInfo.InvariantCulture);
    private static string Symbol(MagicColor color) => color switch
    { MagicColor.White => "W", MagicColor.Blue => "U", MagicColor.Black => "B", MagicColor.Red => "R", _ => "G" };

    public static string Summary(ContextualDraftRecommendation? recommendation, string? topName, bool loading = false)
    {
        if (loading) return "Context Pick: loading…";
        if (recommendation is null) return string.Empty;
        var profile = recommendation.Profile;
        var top = recommendation.Cards.FirstOrDefault(card => card.IsTopContextualCandidate);
        var pick = top is null ? "Context Pick: insufficient comparable statistics"
            : $"Context Pick\n{topName}\nStats rank: #{top.StatisticalRecommendation.StatisticalRank} · Context rank: #{top.ContextualRank}"
                + $"\nColor adjustment: {Number(top.ColorAdjustment * 100, "+0.0;-0.0;0.0")} pp";
        var support = string.Join("  ", profile.Colors.Where(c => c.Evidence > 0)
            .OrderByDescending(c => c.Evidence).ThenBy(c => c.Color)
            .Select(c => $"{Symbol(c.Color)} {Number(c.EffectiveSupport, "+0.00;-0.00;0.00")}"));
        return pick + $"\nCurrent colors: {(support.Length == 0 ? "neutral" : support)}"
            + $"\nCommitment progress: {Number(profile.ProgressFactor, "0.00")} ({profile.CompletedPickCount} picks)"
            + $"\nStats coverage: {recommendation.StatisticalRecommendation.ScoredCardCount} / {recommendation.Cards.Count}";
    }

    public static string Diagnostics(ContextualDraftRecommendation? recommendation, DraftPackObservationHistory observations)
    {
        var coverage = $"Observed packs: {observations.Observations.Count} · "
            + (observations.StartsAtBeginning ? "from P1P1" : "partial observation history")
            + $"\nObserved completed picks: {observations.ObservedCompletedPickCount} / {observations.KnownCompletedPickCount}";
        if (recommendation is null) return coverage;
        var profile = recommendation.Profile;
        var colors = string.Join("\n", profile.Colors.Select(c => $"{Symbol(c.Color)} evidence {Number(c.Evidence, "0.###")}, "
            + $"raw {Number(c.RawSupport, "0.###")}, effective {Number(c.EffectiveSupport, "0.###")}"));
        var cards = string.Join("\n", recommendation.Cards.Select(c => $"Slot {c.PackIndex + 1}: "
            + $"stats {Value(c.StatisticalRecommendation.AdjustedValue)}, fit {Number(c.ColorFit, "0.###")}, "
            + $"adjustment {Number(c.ColorAdjustment * 100, "+0.00;-0.00;0.00")} pp, context {Value(c.ContextualValue)}, "
            + $"ranks {c.StatisticalRecommendation.StatisticalRank?.ToString() ?? "—"}/{c.ContextualRank?.ToString() ?? "—"}"));
        var configuration = profile.Configuration;
        return $"Pool color heuristic: scale {Number(configuration.EvidenceScale, "0.###")}, "
            + $"full commitment after {configuration.FullCommitmentAfterPicks} picks, "
            + $"max ±{Number(configuration.MaxColorAdjustment * 100, "0.###")} pp\n"
            + coverage + $"\nUnresolved pool cards: {profile.UnresolvedPickCount}\n" + colors + "\n" + cards;
    }
    private static string Value(double? value) => value is { } score ? Number(score, "0.0000") : "unscored";
}
