using DraftTG.ArenaIntegration;
using DraftTG.Data;
using DraftTG.Domain;

namespace DraftTG.Application.Tests;

public sealed class DraftTGCardDataBootstrapperTests
{
    [Fact]
    public async Task SuccessfulBootstrapBuildsCatalogResolverAndSnapshotAdapter()
    {
        var updatedAt = DateTimeOffset.Parse("2026-09-20T00:00:00Z");
        var provider = new StubCardDataProvider(Result(
            ScryfallCardDataLoadSource.Downloaded,
            updatedAt));

        var bootstrap = await new DraftTGCardDataBootstrapper(provider).BootstrapAsync();

        Assert.Equal("domain-a", Assert.Single(bootstrap.Catalog.Cards).Identifier.Value);
        Assert.True(bootstrap.ArenaCardResolver.TryResolve(
            ArenaCardIdentifier.Create(101),
            out var resolved));
        Assert.Equal(CardIdentifier.Create("domain-a"), resolved);

        var arenaState = new ArenaDraftStateSnapshot(
            ArenaDraftSessionStatus.Active,
            ArenaDraftIdentifier.Create("draft-1"),
            ArenaDraftMode.Premier,
            "PremierDraft_TST",
            new ArenaDraftPackState(
                ArenaDraftIdentifier.Create("draft-1"),
                ArenaDraftCoordinate.Create(1, 1),
                new ArenaCardIdentifierList([ArenaCardIdentifier.Create(101)])),
            new ArenaDraftPickRecordList([]));
        var snapshotResult = bootstrap.SnapshotAdapter.Convert(arenaState);

        Assert.Equal(DraftSnapshotAvailability.Ready, snapshotResult.Availability);
        Assert.Equal("domain-a",
            Assert.Single(snapshotResult.Snapshot!.CurrentPack.AvailableCardIdentifiers).Value);
        Assert.Equal(ScryfallCardDataLoadSource.Downloaded, bootstrap.DataSource);
        Assert.Equal(updatedAt, bootstrap.DatasetUpdatedAt);
    }

    [Fact]
    public async Task CachedBootstrapPreservesSourceAndRefreshDiagnostic()
    {
        var updatedAt = DateTimeOffset.Parse("2026-09-01T00:00:00Z");
        var diagnostic = new ScryfallCardDataRefreshDiagnostic(
            "Scryfall was unavailable; cached data was retained.",
            ScryfallCardDataErrorKind.MetadataRequestFailed);
        var provider = new StubCardDataProvider(new ScryfallCardDataLoadResult(
            CatalogData(),
            ScryfallCardDataLoadSource.CacheAfterRefreshFailure,
            updatedAt,
            RefreshAttempted: true,
            diagnostic));

        var bootstrap = await new DraftTGCardDataBootstrapper(provider)
            .BootstrapAsync(forceRefresh: true);

        Assert.Equal(ScryfallCardDataLoadSource.CacheAfterRefreshFailure, bootstrap.DataSource);
        Assert.Equal(updatedAt, bootstrap.DatasetUpdatedAt);
        Assert.True(bootstrap.RefreshAttempted);
        Assert.Same(diagnostic, bootstrap.RefreshDiagnostic);
        Assert.True(provider.ForceRefreshRequested);
    }

    private static ScryfallCardDataLoadResult Result(
        ScryfallCardDataLoadSource source,
        DateTimeOffset updatedAt) =>
        new(CatalogData(), source, updatedAt, RefreshAttempted: true);

    private static ScryfallCardCatalogData CatalogData() =>
        new ScryfallCardCatalogDecoder().DecodeCatalogData("""
            [{
              "id":"domain-a","arena_id":101,"name":"Card A","colors":["W"],
              "rarity":"common","set":"tst","collector_number":"1"
            }]
            """);

    private sealed class StubCardDataProvider(ScryfallCardDataLoadResult result)
        : IScryfallCardDataProvider
    {
        public bool ForceRefreshRequested { get; private set; }

        public Task<ScryfallCardDataLoadResult> LoadAsync(
            bool forceRefresh = false,
            CancellationToken cancellationToken = default)
        {
            ForceRefreshRequested = forceRefresh;
            return Task.FromResult(result);
        }
    }
}
