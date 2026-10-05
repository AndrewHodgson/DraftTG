using DraftTG.ArenaIntegration;
using DraftTG.Data;
using DraftTG.Domain;

namespace DraftTG.Application;

public enum DraftTGCardDataStatus
{
    Cache,
    Downloaded,
    CacheAfterRefreshFailure
}

/// <summary>
/// The fully composed, UI-independent runtime used by the application shell.
/// Infrastructure details remain behind the coordinator and presentation-safe
/// card-data status.
/// </summary>
public sealed record DraftTGRuntime(
    DraftSessionCoordinator Coordinator,
    CardCatalog Catalog,
    DraftTGCardDataStatus CardDataStatus,
    DateTimeOffset? CardDataUpdatedAt,
    string? CardDataDiagnostic,
    LimitedStatisticsCoordinator? Statistics = null,
    DeckConstructionCoordinator? Decks = null);

public interface IDraftTGRuntimeFactory
{
    Task<DraftTGRuntime> CreateAsync(CancellationToken cancellationToken = default);
}

/// <summary>Production composition root for card data and Arena log monitoring.</summary>
public sealed class DraftTGRuntimeFactory : IDraftTGRuntimeFactory
{
    public async Task<DraftTGRuntime> CreateAsync(
        CancellationToken cancellationToken = default)
    {
        using var httpClient = new HttpClient();
        var bulkDataClient = new ScryfallBulkDataClient(httpClient);
        var cardDataProvider = new ScryfallCardDataProvider(
            bulkDataClient,
            new ScryfallJsonlGzipCardDataLoader(),
            ApplicationDataPathProviderFactory.CreateDefault());
        var bootstrap = await new DraftTGCardDataBootstrapper(cardDataProvider)
            .BootstrapAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var coordinator = new DraftSessionCoordinator(
            new FileArenaLogSource(
                ArenaLogLocationProviderFactory.CreateDefault().GetLocation()),
            new ArenaDraftLogParser(),
            new ArenaDraftStateEngine(),
            bootstrap.SnapshotAdapter);

        var statisticsHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var service = new LimitedStatisticsService(new SeventeenLandsCardRatingsClient(
                statisticsHttp, ApplicationDataPathProviderFactory.CreateDefault()), bootstrap.Catalog,
                trophyProvider: new SeventeenLandsTrophyClient(statisticsHttp, ApplicationDataPathProviderFactory.CreateDefault(),
                    canonicalCardName: (id, name) => bootstrap.ArenaCardResolver.TryResolve(ArenaCardIdentifier.Create(id), out var identifier)
                        && bootstrap.Catalog.Find(identifier) is { } card
                        && (card.Name == name || card.Name.StartsWith(name + " // ", StringComparison.Ordinal)) ? card.Name : null));
        var statistics = new LimitedStatisticsCoordinator(service, statisticsHttp);
        var decks = new DeckConstructionCoordinator(new DeckConstructionService(service));

        return new DraftTGRuntime(
            coordinator,
            bootstrap.Catalog,
            MapStatus(bootstrap.DataSource),
            bootstrap.DatasetUpdatedAt,
            bootstrap.RefreshDiagnostic?.Message,
            statistics, decks);
    }

    private static DraftTGCardDataStatus MapStatus(ScryfallCardDataLoadSource source) =>
        source switch
        {
            ScryfallCardDataLoadSource.Cache => DraftTGCardDataStatus.Cache,
            ScryfallCardDataLoadSource.Downloaded => DraftTGCardDataStatus.Downloaded,
            ScryfallCardDataLoadSource.CacheAfterRefreshFailure =>
                DraftTGCardDataStatus.CacheAfterRefreshFailure,
            _ => throw new ArgumentOutOfRangeException(nameof(source))
        };
}
