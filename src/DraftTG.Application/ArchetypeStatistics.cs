using DraftTG.Data;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;

namespace DraftTG.Application;

internal sealed record ArchetypeStatisticsKey(LimitedStatisticsContext Context, ArchetypeColorPair Pair);
internal sealed record LoadedArchetypeStatistics(ArchetypeStatisticsKey Key, SeventeenLandsRatingsResult Ratings);

public sealed record ArchetypeDataStatus(bool IsLoading, LimitedStatisticsSource Source, string? Diagnostic = null)
{
    public string Text => IsLoading ? "Pair data: loading; using Lane result"
        : "Pair data: " + (Source switch { LimitedStatisticsSource.Live => "network", LimitedStatisticsSource.Cache => "cache",
            LimitedStatisticsSource.StaleCache => "stale cache", _ => "unavailable; using Lane result" });
    public static ArchetypeDataStatus Unavailable { get; } = new(false, LimitedStatisticsSource.Unavailable);
}
