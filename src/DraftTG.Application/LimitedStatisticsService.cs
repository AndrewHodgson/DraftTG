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
    public CardCatalog CardCatalog { get; init; } = new();
    public IReadOnlyDictionary<CardIdentifier, string> StatisticsResolvedNames { get; init; }
        = System.Collections.Frozen.FrozenDictionary<CardIdentifier, string>.Empty;
    public IReadOnlyDictionary<CardIdentifier, StatisticalIdentityResolution> StatisticsResolutions { get; init; }
        = System.Collections.Frozen.FrozenDictionary<CardIdentifier, StatisticalIdentityResolution>.Empty;
    public string DatasetDiagnosticText { get; init; } = string.Empty;
}

internal sealed record LoadedLimitedStatistics(LimitedStatisticsContext Requested,
    LimitedStatisticsContext Actual, SeventeenLandsRatingsResult Ratings,
    LimitedCardStatisticsCatalog? EnvironmentCatalog = null);

public sealed class LimitedStatisticsService(ISeventeenLandsCardRatingsClient client, CardCatalog catalog,
    ISetArchetypeProfileCatalog? archetypeProfiles = null, ArchetypeConfiguration? archetypeConfiguration = null,
    ISeventeenLandsPairRatingsClient? pairClient = null,
    ISuccessfulDeckProvider? trophyProvider = null, TrophyRecommendationConfiguration? trophyConfiguration = null)
{
    private readonly ISetArchetypeProfileCatalog _archetypes = archetypeProfiles ?? SetArchetypeProfileCatalog.Default;
    private readonly ISeventeenLandsPairRatingsClient? _pairClient = pairClient ?? client as ISeventeenLandsPairRatingsClient;
    internal ArchetypeConfiguration ArchetypeConfiguration { get; } = archetypeConfiguration ?? new();
    internal bool CanLoadPairStatistics => _pairClient is not null;
    internal bool CanLoadTrophyEvidence => trophyProvider is not null;
    internal TrophyRecommendationConfiguration TrophyConfiguration { get; } = trophyConfiguration ?? new();
    internal async Task<SuccessfulDeckLoadResult> LoadTrophyAsync(SuccessfulDeckKey key, CancellationToken token)
    {
        var result = await trophyProvider!.LoadAsync(key, token).ConfigureAwait(false);
        if (result.Key != key || (result.Corpus is not null && result.Corpus.Key != key)
            || (result.Source == SuccessfulDeckSource.Unavailable) != (result.Corpus is null))
            throw new InvalidDataException("Mismatched exact-format trophy evidence.");
        return result;
    }
    internal CardCatalog CardCatalog => catalog;
    internal async Task<LoadedArchetypeStatistics> LoadPairAsync(ArchetypeStatisticsKey key, CancellationToken token)
    {
        var ratings = await _pairClient!.LoadPairAsync(key.Context.Expansion, ProviderFormat(key.Context.Format), key.Pair, token).ConfigureAwait(false);
        if (ratings.Expansion != key.Context.Expansion || ratings.Format != ProviderFormat(key.Context.Format) || ratings.ColorPair != key.Pair)
            throw new InvalidDataException("Pair statistics have a different set, exact format or color-pair identity.");
        return new(key, ratings);
    }

    internal LimitedStatisticsUpdate CreateUpdate(LoadedLimitedStatistics loaded, DraftSnapshot? snapshot,
        DraftPackObservationHistory observations, LoadedArchetypeStatistics? pair = null, ArchetypeDataStatus? pairStatus = null,
        SuccessfulDeckLoadResult? trophy = null, TrophyDataStatus? trophyStatus = null, bool calculatePickScores = true,
        DraftPoolSnapshot? draftPool = null)
    {
        ArchetypePairStatistics? data = null;
        if (snapshot is not null && pair is not null && pair.Key.Context == loaded.Requested && pair.Ratings.Source != SeventeenLandsSource.Unavailable)
        {
            var mapped = LimitedStatisticsMapper.Map(pair.Ratings.Rows, catalog, loaded.Requested, snapshot);
            data = new(pair.Key.Context, pair.Key.Pair, mapped.Catalog,
                pair.Ratings.Rows.Select(r => new PairGihStatistics(r.GameInHandWinRate, r.GameInHandGameCount)));
        }
        return new(snapshot, false, Map(loaded, snapshot), observationHistory: EnrichObservations(loaded, observations),
            archetypeProfile: _archetypes.Find(loaded.Requested.Expansion), archetypeStatistics: data,
            archetypeConfiguration: ArchetypeConfiguration, archetypeDataStatus: pairStatus, useDefaultArchetypeProfile: false,
            trophyCorpus: trophy?.Corpus, trophyConfiguration: TrophyConfiguration, trophyDataStatus: trophyStatus,
            calculatePickScores: calculatePickScores, draftPool: draftPool);
    }
    internal DraftPackObservationHistory EnrichObservations(LoadedLimitedStatistics loaded,
        DraftPackObservationHistory observations)
    {
        foreach (var observation in observations.Observations.Where(o => !o.HasStatisticsSnapshot))
        {
            var snapshot = new DraftSnapshot(observation.Pack, new(),
                loaded.Actual.Format == LimitedStatisticsFormat.TraditionalDraft ? DraftFormat.BestOfThree : DraftFormat.BestOfOne);
            var result = Map(loaded, snapshot);
            var statistical = new StatisticalRecommendationEngine().Recommend(observation.Pack, result.Catalog, result.EnvironmentCatalog);
            observations = observations.WithStatistics(observation.Pack, catalog, statistical, result.Catalog);
        }
        return observations;
    }
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
            CardCatalog = catalog,
            StatisticsResolvedNames = mapping.ResolvedNames,
            StatisticsResolutions = mapping.Resolutions,
            DatasetDiagnosticText = loaded.Ratings.DatasetDiagnosticText
        };
    }
}
