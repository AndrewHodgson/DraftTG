using DraftTG.Data;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;

namespace DraftTG.Application;

public enum LimitedStatisticsSource { Live, Cache, StaleCache, Unavailable }

public sealed record LimitedStatisticsLoadResult(
    LimitedStatisticsContext RequestedContext,
    LimitedStatisticsContext? ActualSourceContext,
    LimitedCardStatisticsCatalog Catalog,
    LimitedStatisticsSource Source,
    DateTimeOffset? FetchedAt,
    string? Diagnostic)
{
    public bool IsFallback => ActualSourceContext is { } actual && actual.Format != RequestedContext.Format;
    // An absent environment must not silently become a pack/history-derived baseline.
    public LimitedCardStatisticsCatalog EnvironmentCatalog { get; init; } = new();
    public string DatasetDiagnosticText { get; init; } = string.Empty;
}

internal sealed record LoadedLimitedStatistics(LimitedStatisticsContext Requested,
    LimitedStatisticsContext Actual, SeventeenLandsRatingsResult Ratings,
    LimitedCardStatisticsCatalog? EnvironmentCatalog = null);

public sealed class LimitedStatisticsService(ISeventeenLandsCardRatingsClient client, CardCatalog catalog)
{
    public LimitedStatisticsContext? Resolve(DraftSessionUpdate update)
    {
        var expansion = DraftExpansionResolver.Resolve(update.ArenaState.EventName, update.SnapshotResult.Snapshot, catalog);
        var format = DraftExpansionResolver.FormatFor(update.ArenaState.Mode?.Kind);
        return expansion is not null && format is not null ? new(expansion, format.Value) : null;
    }

    public static SeventeenLandsFormat ProviderFormat(LimitedStatisticsFormat format) => format switch
    {
        LimitedStatisticsFormat.QuickDraft => SeventeenLandsFormat.QuickDraft,
        LimitedStatisticsFormat.PremierDraft => SeventeenLandsFormat.PremierDraft,
        LimitedStatisticsFormat.TraditionalDraft => SeventeenLandsFormat.TradDraft,
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    public async Task<LimitedStatisticsLoadResult> LoadAsync(LimitedStatisticsContext context,
        DraftSnapshot? snapshot, CancellationToken cancellationToken = default) =>
        Map(await LoadEnvironmentAsync(context, cancellationToken).ConfigureAwait(false), snapshot);

    internal async Task<LoadedLimitedStatistics> LoadEnvironmentAsync(LimitedStatisticsContext context,
        CancellationToken cancellationToken)
    {
        var exact = await client.LoadAsync(context.Expansion, ProviderFormat(context.Format),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        // An empty or sparse exact-format result is never evidence for another format.
        return Prepare(context, context, exact);
    }

    private LoadedLimitedStatistics Prepare(LimitedStatisticsContext requested, LimitedStatisticsContext actual,
        SeventeenLandsRatingsResult ratings) => new(requested, actual, ratings,
            LimitedStatisticsMapper.MapEnvironment(ratings.Rows, catalog, actual));

    internal LimitedStatisticsLoadResult Map(LoadedLimitedStatistics loaded, DraftSnapshot? snapshot)
    {
        // A live gap between packs is not a request for a global printing audit.
        var mapping = snapshot is null
            ? new LimitedStatisticsMappingResult(new(), null)
            : LimitedStatisticsMapper.Map(loaded.Ratings.Rows, catalog, loaded.Actual, snapshot);
        var source = loaded.Ratings.Source switch
        {
            SeventeenLandsSource.Live => LimitedStatisticsSource.Live,
            SeventeenLandsSource.Cache => LimitedStatisticsSource.Cache,
            SeventeenLandsSource.StaleCache => LimitedStatisticsSource.StaleCache,
            _ => LimitedStatisticsSource.Unavailable
        };
        if (loaded.Ratings.Rows.Count == 0) source = LimitedStatisticsSource.Unavailable;
        var diagnostic = string.Join(" ", new[] { loaded.Ratings.Diagnostic, mapping.Diagnostic }
            .Where(message => !string.IsNullOrEmpty(message)));
        return new(loaded.Requested, source == LimitedStatisticsSource.Unavailable ? null : loaded.Actual,
            mapping.Catalog, source, loaded.Ratings.FetchedAt, diagnostic.Length == 0 ? null : diagnostic)
        {
            EnvironmentCatalog = loaded.EnvironmentCatalog ?? new(),
            DatasetDiagnosticText = loaded.Ratings.DatasetDiagnosticText
        };
    }
}
