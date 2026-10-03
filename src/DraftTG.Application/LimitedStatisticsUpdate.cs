using System.Collections.ObjectModel;
using System.Globalization;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;

namespace DraftTG.Application;

public sealed record LimitedCardStatisticsPresentation(string GameInHand, string AverageLastSeen,
    string SampleCount, bool IsLowSample)
{
    public string Summary => $"GIH {GameInHand}   ALSA {AverageLastSeen}"
        + (SampleCount.Length > 0 ? $"   {SampleCount}" : string.Empty)
        + (IsLowSample ? "   Low sample" : string.Empty);
    public static LimitedCardStatisticsPresentation Missing { get; } = new("—", "—", "", false);
    public static LimitedCardStatisticsPresentation Loading { get; } = new("…", "…", "", false);

    public static LimitedCardStatisticsPresentation From(LimitedCardStatistics? statistics) => new(
        statistics?.GameInHandWinRate is { } rate ? (rate * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%" : "—",
        statistics?.AverageLastSeenAt?.ToString("0.00", CultureInfo.InvariantCulture) ?? "—",
        statistics?.GameCount is { } count ? $"n={count.ToString("N0", CultureInfo.InvariantCulture)}" : "",
        statistics?.GameCount is < 500);
}

/// <summary>UI-ready values tied to the precise snapshot that was mapped.</summary>
public sealed class LimitedStatisticsUpdate
{
    public LimitedStatisticsUpdate(DraftSnapshot? snapshot, bool isLoading, LimitedStatisticsLoadResult? result,
        string? unavailableReason = null)
    {
        Snapshot = snapshot;
        IsLoading = isLoading;
        Result = result;
        Cards = new ReadOnlyDictionary<CardIdentifier, LimitedCardStatisticsPresentation>(
            (snapshot?.CurrentPack.AvailableCardIdentifiers ?? []).Distinct().ToDictionary(id => id,
                id => isLoading ? LimitedCardStatisticsPresentation.Loading
                    : LimitedCardStatisticsPresentation.From(result?.Catalog.StatisticsFor(id))));
        var format = result?.ActualSourceContext?.Format switch
        {
            LimitedStatisticsFormat.QuickDraft => "Quick Draft",
            LimitedStatisticsFormat.TraditionalDraft => "Traditional Draft",
            LimitedStatisticsFormat.PremierDraft => "Premier Draft",
            _ => null
        };
        StatusText = isLoading ? "Stats: loading…" : format is null
            ? "Stats: unavailable" : $"Stats: {format}" + (result!.IsFallback ? " (fallback)" : "")
                + (result.Source == LimitedStatisticsSource.StaleCache ? " · stale cache" : "");
        Diagnostic = unavailableReason ?? result?.Diagnostic;
    }
    public DraftSnapshot? Snapshot { get; }
    public bool IsLoading { get; }
    public LimitedStatisticsLoadResult? Result { get; }
    public IReadOnlyDictionary<CardIdentifier, LimitedCardStatisticsPresentation> Cards { get; }
    public string StatusText { get; }
    public string? Diagnostic { get; }
}
