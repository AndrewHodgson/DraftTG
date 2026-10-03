using System.Globalization;
using DraftTG.RecommendationEngine;

namespace DraftTG.Application;

public static class RecommendationPresentation
{
    public static string Summary(DraftRecommendation? recommendation, string? topCardName, bool loading = false)
    {
        if (loading) return "Stats Pick: loading\u2026";
        if (recommendation is null) return "Statistical recommendation unavailable";
        var ready = recommendation.TopRecommendedPackIndex is not null;
        var text = ready ? $"Stats Pick\n{topCardName}" : "Statistical recommendation unavailable";
        text += $"\nRank model: GIH + sample shrinkage\nCoverage: {recommendation.ScoredCardCount} / {recommendation.TotalPackCardCount}";
        if (recommendation.EnvironmentBaseline is { } baseline)
            text += "\nBaseline: " + (baseline * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%";
        return text + (recommendation.Availability switch
        {
            RecommendationAvailability.ReadyPartialCoverage => "\nPartial statistics",
            RecommendationAvailability.InsufficientComparableStatistics => "\nFewer than two scored cards",
            RecommendationAvailability.NoEnvironmentBaseline => "\nNo environment baseline",
            _ => string.Empty
        });
    }
}
