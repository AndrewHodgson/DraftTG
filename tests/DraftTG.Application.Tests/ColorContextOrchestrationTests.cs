using DraftTG.ArenaIntegration;
using DraftTG.Data;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;

namespace DraftTG.Application.Tests;

public sealed class ColorContextOrchestrationTests
{
    private static ScryfallCardCatalogData Data() => new ScryfallCardCatalogDecoder().DecodeCatalogData("""
        [
          {"id":"green","arena_id":101,"name":"Green","colors":["G"],"rarity":"common","set":"woe","collector_number":"1"},
          {"id":"blue","arena_id":102,"name":"Blue","colors":["U"],"rarity":"common","set":"woe","collector_number":"2"},
          {"id":"blank","arena_id":103,"name":"Blank","colors":[],"rarity":"common","set":"woe","collector_number":"3"}
        ]
        """);
    private static SeventeenLandsRatingsResult Ratings(string expansion, SeventeenLandsFormat format) =>
        new(expansion, format, [new("Green", GameInHandWinRate: 0.58, GameInHandGameCount: 5000),
            new("Blue", GameInHandWinRate: 0.59, GameInHandGameCount: 5000)], SeventeenLandsSource.Live);
    private sealed class Client(Func<string, SeventeenLandsFormat, Task<SeventeenLandsRatingsResult>>? load = null)
        : ISeventeenLandsCardRatingsClient
    {
        public Task<SeventeenLandsRatingsResult> LoadAsync(string expansion, SeventeenLandsFormat format,
            bool forceRefresh = false, CancellationToken cancellationToken = default) =>
            load?.Invoke(expansion, format) ?? Task.FromResult(Ratings(expansion, format));
    }
    private static DraftSessionUpdate Update(ScryfallCardCatalogData data, int[]? pack, int completed = 0,
        string draft = "draft", ArenaDraftSessionStatus status = ArenaDraftSessionStatus.Active)
    {
        var id = ArenaDraftIdentifier.Create(draft);
        var state = new ArenaDraftStateSnapshot(status, id, ArenaDraftMode.Quick, "QuickDraft_WOE",
            pack is null ? null : new(id, ArenaDraftCoordinate.Create(completed / 14 + 1, completed % 14 + 1),
                new(pack.Select(ArenaCardIdentifier.Create))),
            new(Enumerable.Range(0, completed).Select(i => new ArenaDraftPickRecord(id,
                ArenaDraftCoordinate.Create(i / 14 + 1, i % 14 + 1), new([ArenaCardIdentifier.Create(101)])))));
        return new(state, new ArenaDraftSnapshotAdapter(new(data)).Convert(state));
    }
    private static async Task<LimitedStatisticsUpdate> ReadReady(IAsyncEnumerator<LimitedStatisticsUpdate> reader)
    {
        do { Assert.True(await reader.MoveNextAsync()); } while (reader.Current.IsLoading);
        return reader.Current;
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(5, 0)]
    [InlineData(14, 0)]
    public async Task ApplicationComposesIndependentPhase8AndUpdatedDraftPool(int picks, int top)
    {
        var data = Data();
        var snapshot = Update(data, [101, 102], picks).SnapshotResult.Snapshot!;
        var service = new LimitedStatisticsService(new Client(), data.Catalog);
        var result = await service.LoadAsync(new("WOE", LimitedStatisticsFormat.QuickDraft), snapshot);
        var update = new LimitedStatisticsUpdate(snapshot, false, result);
        Assert.Equal(picks, update.ContextualRecommendation!.Profile.For(MagicColor.Green).Evidence);
        Assert.Equal(top, update.ContextualRecommendation.TopRecommendedPackIndex);
        Assert.Equal(1, update.Recommendation!.TopRecommendedPackIndex);
        Assert.Equal(new StatisticalRecommendationEngine().Recommend(snapshot.CurrentPack, result.Catalog, result.EnvironmentCatalog).Cards,
            update.Recommendation.Cards);
        Assert.Same(update.Recommendation, update.ContextualRecommendation.StatisticalRecommendation);
    }

    [Fact]
    public async Task CoordinatorReplayIsIdempotentPickGapCompletesObservationAndNextPackRecomputes()
    {
        var data = Data();
        await using var coordinator = new LimitedStatisticsCoordinator(new(new Client(), data.Catalog));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var reader = coordinator.ReadUpdatesAsync(timeout.Token).GetAsyncEnumerator();
        var first = Update(data, [101, 102, 101]);
        coordinator.Observe(first);
        var loaded = await ReadReady(reader);
        Assert.Single(loaded.ObservationHistory.Observations);
        Assert.NotNull(loaded.ObservationHistory.Observations[0].Cards[0].Phase8AdjustedValue);
        coordinator.Observe(first);
        var replay = await ReadReady(reader);
        Assert.Single(replay.ObservationHistory.Observations);
        Assert.Equal(0, replay.ContextualRecommendation!.Profile.CompletedPickCount);
        var gap = Update(data, null, 1);
        coordinator.Observe(gap);
        var picked = await ReadReady(reader);
        Assert.Null(picked.ContextualRecommendation);
        Assert.Null(picked.Recommendation);
        var observation = Assert.Single(picked.ObservationHistory.Observations);
        Assert.Equal(CardIdentifier.Create("green"), observation.SelectedCardIdentifier);
        Assert.Null(observation.SelectedPackIndex);
        Assert.Equal([0, 2], observation.MatchingSelectedPackIndexes);
        Assert.Equal(1, picked.ObservationHistory.CompletedPickCoverage);
        coordinator.Observe(Update(data, [101, 102], 1));
        var next = await ReadReady(reader);
        Assert.Equal(2, next.ObservationHistory.Observations.Count);
        Assert.Equal(1, next.ContextualRecommendation!.Profile.CompletedPickCount);
        Assert.Equal(1, next.ContextualRecommendation.Profile.For(MagicColor.Green).Evidence);
        Assert.Equal(1, next.ContextualRecommendation.TopRecommendedPackIndex);
        Assert.Equal(1.0 / 14, next.ContextualRecommendation.Profile.ProgressFactor, 12);
    }

    [Fact]
    public async Task MidDraftStartupTracksPartialCoverageAndNewSessionResetsBothContexts()
    {
        var data = Data();
        await using var coordinator = new LimitedStatisticsCoordinator(new(new Client(), data.Catalog));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var reader = coordinator.ReadUpdatesAsync(timeout.Token).GetAsyncEnumerator();
        coordinator.Observe(Update(data, [101, 102], 17));
        var mid = await ReadReady(reader);
        Assert.Single(mid.ObservationHistory.Observations);
        Assert.False(mid.ObservationHistory.StartsAtBeginning);
        Assert.Equal(17, mid.ObservationHistory.KnownCompletedPickCount);
        Assert.Equal(0, mid.ObservationHistory.ObservedCompletedPickCount);
        Assert.Equal(0, mid.ObservationHistory.CompletedPickCoverage);
        coordinator.Observe(Update(data, null, 18));
        var gap = await ReadReady(reader);
        Assert.Equal(1, gap.ObservationHistory.ObservedCompletedPickCount);
        Assert.Equal(1.0 / 18, gap.ObservationHistory.CompletedPickCoverage!.Value, 12);
        coordinator.Observe(Update(data, [102, 101], draft: "new-draft"));
        var fresh = await ReadReady(reader);
        Assert.Single(fresh.ObservationHistory.Observations);
        Assert.True(fresh.ObservationHistory.StartsAtBeginning);
        Assert.Equal(0, fresh.ContextualRecommendation!.Profile.CompletedPickCount);
        Assert.Equal(0, fresh.ObservationHistory.KnownCompletedPickCount);
    }

    [Fact]
    public async Task DelayedStatisticsEnrichOnlyActuallyObservedPacksAndCannotPublishOldContext()
    {
        var response = new TaskCompletionSource<SeventeenLandsRatingsResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var data = Data();
        await using var coordinator = new LimitedStatisticsCoordinator(new(new Client((_, _) => response.Task), data.Catalog));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var reader = coordinator.ReadUpdatesAsync(timeout.Token).GetAsyncEnumerator();
        coordinator.Observe(Update(data, [102, 101], 4));
        Assert.True(await reader.MoveNextAsync());
        Assert.True(reader.Current.IsLoading);
        var next = Update(data, [101, 102], 5);
        coordinator.Observe(next);
        response.SetResult(Ratings("WOE", SeventeenLandsFormat.QuickDraft));
        var ready = await ReadReady(reader);
        Assert.Same(next.SnapshotResult.Snapshot, ready.Snapshot);
        Assert.Equal(5, ready.ContextualRecommendation!.Profile.CompletedPickCount);
        Assert.Equal(0, ready.ContextualRecommendation.TopRecommendedPackIndex);
        Assert.Equal(2, ready.ObservationHistory.Observations.Count);
        Assert.All(ready.ObservationHistory.Observations, o => Assert.True(o.HasStatisticsSnapshot));
        Assert.NotNull(ready.ObservationHistory.Observations[0].Cards[0].Phase8AdjustedValue);
        Assert.Equal(1, ready.ObservationHistory.ObservedCompletedPickCount);
        Assert.Equal(0.2, ready.ObservationHistory.CompletedPickCoverage);
    }

    [Theory]
    [InlineData(ArenaDraftSessionStatus.Active)]
    [InlineData(ArenaDraftSessionStatus.Completed)]
    public async Task AdapterResolvesKnownCompletedHistoryWithoutManufacturingActivePack(ArenaDraftSessionStatus status)
    {
        var data = Data();
        await using var coordinator = new LimitedStatisticsCoordinator(new(new Client(), data.Catalog));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var reader = coordinator.ReadUpdatesAsync(timeout.Token).GetAsyncEnumerator();
        coordinator.Observe(Update(data, [101, 102]));
        await ReadReady(reader);
        var completed = Update(data, null, 1, status: status);
        Assert.Null(completed.SnapshotResult.Snapshot);
        Assert.Equal(1, completed.SnapshotResult.ResolvedHistory!.Count);
        coordinator.Observe(completed);
        var ready = await ReadReady(reader);
        Assert.Null(ready.ContextualRecommendation);
        Assert.Equal(CardIdentifier.Create("green"), ready.ObservationHistory.Observations[0].SelectedCardIdentifier);
    }

    [Fact]
    public void LoadingUnavailableAndNoPackCannotKeepContextualRecommendation()
    {
        var data = Data();
        var snapshot = Update(data, [101, 102], 14).SnapshotResult.Snapshot;
        Assert.Null(new LimitedStatisticsUpdate(snapshot, true, null).ContextualRecommendation);
        Assert.Null(new LimitedStatisticsUpdate(snapshot, false, null).ContextualRecommendation);
        Assert.Null(new LimitedStatisticsUpdate(null, false, null).ContextualRecommendation);
    }

    [Fact]
    public void ProviderNeutralEngineReferencesOnlyDomain()
    {
        var references = typeof(ContextualRecommendationEngine).Assembly.GetReferencedAssemblies()
            .Where(a => a.Name!.StartsWith("DraftTG.", StringComparison.Ordinal));
        Assert.Equal(["DraftTG.Domain"], references.Select(a => a.Name));
    }
}
