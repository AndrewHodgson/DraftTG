using System.Collections.Concurrent;
using DraftTG.ArenaIntegration;
using DraftTG.Data;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;

namespace DraftTG.Application.Tests;

public sealed class TrophyOrchestrationTests
{
    private static ScryfallCardCatalogData Data() => new ScryfallCardCatalogDecoder().DecodeCatalogData("""
        [
          {"id":"a","arena_id":101,"name":"A","colors":["G"],"rarity":"common","set":"woe","collector_number":"1"},
          {"id":"b","arena_id":102,"name":"B","colors":["G"],"rarity":"common","set":"woe","collector_number":"2"},
          {"id":"c","arena_id":103,"name":"C","colors":["B"],"rarity":"common","set":"woe","collector_number":"3"},
          {"id":"d","arena_id":104,"name":"D","colors":["U"],"rarity":"common","set":"woe","collector_number":"4"}
        ]
        """);
    private static DraftSessionUpdate Update(ScryfallCardCatalogData data, int[] pool, string draft = "draft", string set = "WOE")
    {
        var id = ArenaDraftIdentifier.Create(draft);
        var state = new ArenaDraftStateSnapshot(ArenaDraftSessionStatus.Active, id, ArenaDraftMode.Quick, "QuickDraft_" + set,
            new(id, ArenaDraftCoordinate.Create(pool.Length / 14 + 1, pool.Length % 14 + 1), new([ArenaCardIdentifier.Create(101), ArenaCardIdentifier.Create(102)])),
            new(pool.Select((card, i) => new ArenaDraftPickRecord(id, ArenaDraftCoordinate.Create(i / 14 + 1, i % 14 + 1), new([ArenaCardIdentifier.Create(card)])))));
        return new(state, new ArenaDraftSnapshotAdapter(new(data)).Convert(state));
    }
    private static int[] Pool(int color = 103) => Enumerable.Repeat(color, 7).Concat(Enumerable.Repeat(101, 7)).ToArray();
    private sealed class Ratings : ISeventeenLandsCardRatingsClient
    {
        public Task<SeventeenLandsRatingsResult> LoadAsync(string expansion, SeventeenLandsFormat format, bool forceRefresh = false,
            CancellationToken cancellationToken = default) => Task.FromResult(new SeventeenLandsRatingsResult(expansion, format,
                new[] { "A", "B", "C", "D" }.Select(name => new SeventeenLandsRating(name, GameInHandWinRate: .58, GameInHandGameCount: 1000)), SeventeenLandsSource.Live));
    }
    private sealed record Pending(SuccessfulDeckKey Key, TaskCompletionSource<SuccessfulDeckLoadResult> Response);
    private sealed class Provider(bool ignoreCancellation = false) : ISuccessfulDeckProvider
    {
        public ConcurrentQueue<Pending> Requests { get; } = new();
        public Task<SuccessfulDeckLoadResult> LoadAsync(SuccessfulDeckKey key, CancellationToken cancellationToken = default)
        {
            var response = new TaskCompletionSource<SuccessfulDeckLoadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            Requests.Enqueue(new(key, response)); return ignoreCancellation ? response.Task : response.Task.WaitAsync(cancellationToken);
        }
        public async Task<Pending> Request(int index)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (Requests.Count <= index) await Task.Delay(10, timeout.Token);
            return Requests.ToArray()[index];
        }
    }
    private static SuccessfulDeckLoadResult Success(SuccessfulDeckKey key)
    {
        var time = DateTimeOffset.UtcNow;
        var counts = new Dictionary<string, int> { ["A"] = 2, ["B"] = 1 };
        var sample = new SuccessfulDeckSample(key, "event", new(7, 1), 7, 0, time, counts, counts);
        return new(key, new(key, [sample], time, new("synthetic", "recent", "exact pair", 100, 20, 1, "final")), SuccessfulDeckSource.Live);
    }
    private static async Task<LimitedStatisticsUpdate> Read(IAsyncEnumerator<LimitedStatisticsUpdate> reader, Func<LimitedStatisticsUpdate, bool> predicate)
    { do { Assert.True(await reader.MoveNextAsync()); } while (!predicate(reader.Current)); return reader.Current; }

    [Fact]
    public async Task ActiveArchetypeLoadsOnceWithoutBlockingEarlierRecommendationsAndEvidenceUpdatesInPlace()
    {
        var data = Data(); var provider = new Provider();
        await using var coordinator = new LimitedStatisticsCoordinator(new(new Ratings(), data.Catalog, trophyProvider: provider));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)); await using var reader = coordinator.ReadUpdatesAsync(timeout.Token).GetAsyncEnumerator();
        coordinator.Observe(Update(data, [])); var early = await Read(reader, u => u.TrophyRecommendation is not null);
        Assert.Null(early.TrophyRecommendation!.Archetype.Profile.Active); Assert.Empty(provider.Requests);
        var current = Update(data, Pool()); coordinator.Observe(current);
        var loading = await Read(reader, u => u.TrophyDataStatus.IsLoading);
        Assert.NotNull(loading.ArchetypeRecommendation); Assert.Null(loading.TrophyRecommendation!.Evidence);
        Assert.Equal(loading.ArchetypeRecommendation!.Cards.Select(c => c.ContextualValue), loading.TrophyRecommendation.Cards.Select(c => c.FinalValue));
        var request = await provider.Request(0); Assert.Equal(SuccessfulDeckFormat.QuickDraft, request.Key.Format);
        request.Response.SetResult(Success(request.Key)); var ready = await Read(reader, u => u.TrophyDataStatus.Source == SuccessfulDeckSource.Live);
        Assert.Same(current.SnapshotResult.Snapshot, ready.Snapshot); Assert.NotNull(ready.TrophyRecommendation!.Evidence);
        coordinator.Observe(current); await Read(reader, u => u.TrophyDataStatus.Source == SuccessfulDeckSource.Live); Assert.Single(provider.Requests);
    }

    [Fact]
    public async Task LateOldPairResponseCannotApplyWhileTheNewArchetypeLoads()
    {
        var data = Data(); var provider = new Provider();
        await using var coordinator = new LimitedStatisticsCoordinator(new(new Ratings(), data.Catalog, trophyProvider: provider));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)); await using var reader = coordinator.ReadUpdatesAsync(timeout.Token).GetAsyncEnumerator();
        coordinator.Observe(Update(data, Pool())); await Read(reader, u => u.TrophyDataStatus.IsLoading);
        var bg = await provider.Request(0);
        coordinator.Observe(Update(data, Pool(104))); await Read(reader, u => u.ArchetypeRecommendation?.Profile.Active?.Definition.Pair.Code == "UG");
        var gu = await provider.Request(1); bg.Response.SetResult(Success(bg.Key));
        var afterOld = await Read(reader, u => u.TrophyDataStatus.IsLoading);
        Assert.Equal("UG", afterOld.ArchetypeRecommendation!.Profile.Active!.Definition.Pair.Code); Assert.Null(afterOld.TrophyRecommendation!.Evidence);
        gu.Response.SetResult(Success(gu.Key)); var ready = await Read(reader, u => u.TrophyDataStatus.Source == SuccessfulDeckSource.Live);
        Assert.Equal("UG", ready.TrophyRecommendation!.Evidence!.Corpus.Key.Pair.Code);
    }

    [Fact]
    public async Task LateResponseFromPreviousDraftCannotEnterTheNewDraftEvenWithSamePair()
    {
        var data = Data(); var provider = new Provider(ignoreCancellation: true);
        await using var coordinator = new LimitedStatisticsCoordinator(new(new Ratings(), data.Catalog, trophyProvider: provider));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)); await using var reader = coordinator.ReadUpdatesAsync(timeout.Token).GetAsyncEnumerator();
        coordinator.Observe(Update(data, Pool())); await Read(reader, u => u.TrophyDataStatus.IsLoading); var old = await provider.Request(0);
        var current = Update(data, Pool(), draft: "new"); coordinator.Observe(current); await Read(reader, u => u.TrophyDataStatus.IsLoading);
        var next = await provider.Request(1); old.Response.SetResult(Success(old.Key)); coordinator.Observe(current);
        var waiting = await Read(reader, u => u.TrophyDataStatus.IsLoading);
        Assert.Same(current.SnapshotResult.Snapshot, waiting.Snapshot); Assert.Null(waiting.TrophyRecommendation!.Evidence);
        next.Response.SetResult(Success(next.Key)); var ready = await Read(reader, u => u.TrophyDataStatus.Source == SuccessfulDeckSource.Live);
        Assert.Same(current.SnapshotResult.Snapshot, ready.Snapshot);
    }

    [Fact]
    public async Task UnavailableOrForeignFormatResultPreservesPhase9CAndDoesNotRetryPerPick()
    {
        var data = Data(); var provider = new Provider();
        await using var coordinator = new LimitedStatisticsCoordinator(new(new Ratings(), data.Catalog, trophyProvider: provider));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)); await using var reader = coordinator.ReadUpdatesAsync(timeout.Token).GetAsyncEnumerator();
        var current = Update(data, Pool()); coordinator.Observe(current); await Read(reader, u => u.TrophyDataStatus.IsLoading);
        var request = await provider.Request(0);
        request.Response.SetResult(Success(new("WOE", SuccessfulDeckFormat.PremierDraft, request.Key.Pair)));
        var ready = await Read(reader, u => u.TrophyRecommendation is not null && !u.TrophyDataStatus.IsLoading);
        Assert.Equal(SuccessfulDeckSource.Unavailable, ready.TrophyDataStatus.Source); Assert.Null(ready.TrophyRecommendation!.Evidence);
        Assert.Equal(ready.ArchetypeRecommendation!.Cards.Select(c => c.ContextualValue), ready.TrophyRecommendation.Cards.Select(c => c.FinalValue));
        coordinator.Observe(current); await Read(reader, u => !u.IsLoading); Assert.Single(provider.Requests);
    }

    [Fact]
    public async Task CorpusBudgetBoundsPairChangesAndUnsupportedSetNeverTriggersARequest()
    {
        var data = Data(); var provider = new Provider();
        await using var coordinator = new LimitedStatisticsCoordinator(new(new Ratings(), data.Catalog,
            trophyProvider: provider, trophyConfiguration: new(maxCorporaPerDraft: 1)));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)); await using var reader = coordinator.ReadUpdatesAsync(timeout.Token).GetAsyncEnumerator();
        coordinator.Observe(Update(data, Pool())); await Read(reader, u => u.TrophyDataStatus.IsLoading); var bg = await provider.Request(0);
        bg.Response.SetResult(Success(bg.Key)); await Read(reader, u => u.TrophyDataStatus.Source == SuccessfulDeckSource.Live);
        coordinator.Observe(Update(data, Pool(104))); var capped = await Read(reader, u => u.ArchetypeRecommendation?.Profile.Active?.Definition.Pair.Code == "UG");
        Assert.Contains("budget", capped.TrophyDataStatus.Diagnostic); Assert.Null(capped.TrophyRecommendation!.Evidence); Assert.Single(provider.Requests);
        coordinator.Observe(Update(data, Pool(), draft: "new", set: "TST")); var unsupported = await Read(reader, u => u.TrophyRecommendation is not null);
        Assert.Null(unsupported.ArchetypeRecommendation!.Profile.Active); Assert.Single(provider.Requests);
    }
}
