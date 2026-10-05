using System.Collections.Concurrent;
using DraftTG.ArenaIntegration;
using DraftTG.Data;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;

namespace DraftTG.Application.Tests;

public sealed class ArchetypeOrchestrationTests
{
    private static ScryfallCardCatalogData Data() => new ScryfallCardCatalogDecoder().DecodeCatalogData("""
        [
          {"id":"alpha","arena_id":101,"name":"Alpha","colors":["G"],"rarity":"common","set":"woe","collector_number":"1"},
          {"id":"beta","arena_id":102,"name":"Beta","colors":["G"],"rarity":"common","set":"woe","collector_number":"2"},
          {"id":"black","arena_id":103,"name":"Black","colors":["B"],"rarity":"common","set":"woe","collector_number":"3"},
          {"id":"blue","arena_id":104,"name":"Blue","colors":["U"],"rarity":"common","set":"woe","collector_number":"4"}
        ]
        """);
    private static DraftSessionUpdate Update(ScryfallCardCatalogData data, int[] pool, string draft = "draft")
    {
        var id = ArenaDraftIdentifier.Create(draft);
        var state = new ArenaDraftStateSnapshot(ArenaDraftSessionStatus.Active, id, ArenaDraftMode.Quick, "QuickDraft_WOE",
            new(id, ArenaDraftCoordinate.Create(pool.Length / 14 + 1, pool.Length % 14 + 1), new([ArenaCardIdentifier.Create(101), ArenaCardIdentifier.Create(102)])),
            new(pool.Select((card, i) => new ArenaDraftPickRecord(id, ArenaDraftCoordinate.Create(i / 14 + 1, i % 14 + 1), new([ArenaCardIdentifier.Create(card)])))));
        return new(state, new ArenaDraftSnapshotAdapter(new(data)).Convert(state));
    }
    private static int[] BgPool => Enumerable.Repeat(103, 7).Concat(Enumerable.Repeat(101, 7)).ToArray();
    private static SeventeenLandsRatingsResult Ratings(ArchetypeColorPair? pair = null, SeventeenLandsFormat format = SeventeenLandsFormat.QuickDraft) =>
        new("WOE", format, [new("Alpha", GameInHandWinRate: pair is null ? .58 : .65, GameInHandGameCount: 5000),
            new("Beta", GameInHandWinRate: pair is null ? .586 : .52, GameInHandGameCount: 5000),
            new("Black", GameInHandWinRate: .56, GameInHandGameCount: 5000), new("Blue", GameInHandWinRate: .56, GameInHandGameCount: 5000)],
            SeventeenLandsSource.Live, colorPair: pair);
    private sealed class Client : ISeventeenLandsCardRatingsClient, ISeventeenLandsPairRatingsClient
    {
        public ConcurrentQueue<ArchetypeColorPair> Requests { get; } = new();
        public ConcurrentDictionary<string, TaskCompletionSource<SeventeenLandsRatingsResult>> Responses { get; } = new();
        public Task<SeventeenLandsRatingsResult> LoadAsync(string expansion, SeventeenLandsFormat format,
            bool forceRefresh = false, CancellationToken cancellationToken = default) => Task.FromResult(Ratings(format: format));
        public Task<SeventeenLandsRatingsResult> LoadPairAsync(string expansion, SeventeenLandsFormat format,
            ArchetypeColorPair pair, CancellationToken cancellationToken = default)
        { Requests.Enqueue(pair); return Responses.GetOrAdd(pair.Code, _ => new(TaskCreationOptions.RunContinuationsAsynchronously)).Task.WaitAsync(cancellationToken); }
        public async Task Complete(string code, SeventeenLandsFormat format = SeventeenLandsFormat.QuickDraft)
        {
            var start = DateTimeOffset.UtcNow;
            while (!Responses.ContainsKey(code))
            { if (DateTimeOffset.UtcNow - start > TimeSpan.FromSeconds(5)) throw new TimeoutException(); await Task.Delay(10); }
            Responses[code].SetResult(Ratings(ArchetypeColorPair.Create(code), format));
        }
    }
    private static async Task<LimitedStatisticsUpdate> Read(IAsyncEnumerator<LimitedStatisticsUpdate> reader, Func<LimitedStatisticsUpdate, bool> predicate)
    { do { Assert.True(await reader.MoveNextAsync()); } while (!predicate(reader.Current)); return reader.Current; }

    [Fact]
    public async Task ActivePairLoadsLazilyKeepsLaneWhileLoadingThenRecomputesAndCaches()
    {
        var data = Data(); var client = new Client(); await using var coordinator = new LimitedStatisticsCoordinator(new(client, data.Catalog));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)); await using var reader = coordinator.ReadUpdatesAsync(timeout.Token).GetAsyncEnumerator();
        coordinator.Observe(Update(data, [101]));
        var early = await Read(reader, u => u.ArchetypeRecommendation is not null); Assert.Null(early.ArchetypeRecommendation!.Profile.Active); Assert.Empty(client.Requests);
        var current = Update(data, BgPool); coordinator.Observe(current);
        var loading = await Read(reader, u => u.ArchetypeDataStatus.IsLoading);
        Assert.Equal("BG", loading.ArchetypeRecommendation!.Profile.Active!.Definition.Pair.Code);
        Assert.Equal(loading.LaneRecommendation!.Cards.Select(c => c.ContextualValue), loading.ArchetypeRecommendation.Cards.Select(c => c.ContextualValue));
        Assert.Equal(1, loading.LaneRecommendation.TopRecommendedPackIndex);
        await client.Complete("BG"); var ready = await Read(reader, u => u.ArchetypeDataStatus.Source == LimitedStatisticsSource.Live);
        Assert.Equal(0, ready.ArchetypeRecommendation!.TopRecommendedPackIndex); Assert.Equal(1, ready.LaneRecommendation!.TopRecommendedPackIndex);
        coordinator.Observe(current); var replay = await Read(reader, u => !u.IsLoading && !u.ArchetypeDataStatus.IsLoading);
        Assert.Equal(ready.ArchetypeRecommendation.Cards.Select(c => c.ContextualValue), replay.ArchetypeRecommendation!.Cards.Select(c => c.ContextualValue)); Assert.Single(client.Requests);
    }

    [Fact]
    public async Task PairChangeNeverAppliesOldAffinityWhileNewDataLoads()
    {
        foreach (var oldResponseArrivesLate in new[] { false, true })
        {
        var data = Data(); var client = new Client(); await using var coordinator = new LimitedStatisticsCoordinator(new(client, data.Catalog));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)); await using var reader = coordinator.ReadUpdatesAsync(timeout.Token).GetAsyncEnumerator();
        coordinator.Observe(Update(data, BgPool)); await Read(reader, u => u.ArchetypeDataStatus.IsLoading);
        if (!oldResponseArrivesLate)
        {
            await client.Complete("BG");
            await Read(reader, u => u.ArchetypeDataStatus.Source == LimitedStatisticsSource.Live);
        }
        coordinator.Observe(Update(data, Enumerable.Repeat(104, 14).Concat(Enumerable.Repeat(101, 7)).ToArray()));
        var gu = await Read(reader, u => u.ArchetypeDataStatus.IsLoading);
        Assert.Equal("UG", gu.ArchetypeRecommendation!.Profile.Active!.Definition.Pair.Code); Assert.Null(gu.ArchetypeRecommendation.PairStatistics);
        Assert.All(gu.ArchetypeRecommendation.Cards, c => Assert.Equal(0, c.Affinity.Adjustment));
        if (oldResponseArrivesLate)
        {
            await client.Complete("BG");
            var afterOldResponse = await Read(reader, u => u.ArchetypeDataStatus.IsLoading);
            Assert.Equal("UG", afterOldResponse.ArchetypeRecommendation!.Profile.Active!.Definition.Pair.Code);
            Assert.Null(afterOldResponse.ArchetypeRecommendation.PairStatistics);
            Assert.All(afterOldResponse.ArchetypeRecommendation.Cards, c => Assert.Equal(0, c.Affinity.Adjustment));
        }
        await client.Complete("UG"); var ready = await Read(reader, u => u.ArchetypeDataStatus.Source == LimitedStatisticsSource.Live);
        Assert.Equal("UG", ready.ArchetypeRecommendation!.PairStatistics!.Pair.Code); Assert.Equal(["BG", "UG"], client.Requests.Select(p => p.Code));
        }
    }

    [Fact]
    public async Task RequestBudgetBoundsPairLoadsAndNewDraftClearsActiveContext()
    {
        var data = Data(); var client = new Client(); var configuration = new ArchetypeConfiguration(maxPairDatasetsPerDraft: 1);
        await using var coordinator = new LimitedStatisticsCoordinator(new(client, data.Catalog, archetypeConfiguration: configuration));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)); await using var reader = coordinator.ReadUpdatesAsync(timeout.Token).GetAsyncEnumerator();
        coordinator.Observe(Update(data, BgPool)); await Read(reader, u => u.ArchetypeDataStatus.IsLoading); await client.Complete("BG");
        await Read(reader, u => u.ArchetypeDataStatus.Source == LimitedStatisticsSource.Live);
        coordinator.Observe(Update(data, Enumerable.Repeat(104, 14).Concat(Enumerable.Repeat(101, 7)).ToArray()));
        var capped = await Read(reader, u => u.ArchetypeRecommendation?.Profile.Active?.Definition.Pair.Code == "UG");
        Assert.Contains("budget", capped.ArchetypeDataStatus.Diagnostic); Assert.All(capped.ArchetypeRecommendation!.Cards, c => Assert.Equal(0, c.Affinity.Adjustment)); Assert.Single(client.Requests);
        coordinator.Observe(Update(data, [], "new-draft")); var reset = await Read(reader, u => u.ArchetypeRecommendation is not null);
        Assert.Null(reset.ArchetypeRecommendation!.Profile.Active); Assert.Null(reset.ArchetypeRecommendation.PairStatistics);
    }

    [Fact]
    public async Task WrongExactFormatPairResponseDegradesToLaneWithoutRetryStorm()
    {
        var data = Data(); var client = new Client(); await using var coordinator = new LimitedStatisticsCoordinator(new(client, data.Catalog));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)); await using var reader = coordinator.ReadUpdatesAsync(timeout.Token).GetAsyncEnumerator();
        var current = Update(data, BgPool); coordinator.Observe(current); await Read(reader, u => u.ArchetypeDataStatus.IsLoading);
        await client.Complete("BG", SeventeenLandsFormat.PremierDraft);
        var failed = await Read(reader, u => !u.IsLoading && !u.ArchetypeDataStatus.IsLoading);
        Assert.Equal(LimitedStatisticsSource.Unavailable, failed.ArchetypeDataStatus.Source); Assert.Null(failed.ArchetypeRecommendation!.PairStatistics);
        Assert.All(failed.ArchetypeRecommendation.Cards, c => Assert.Equal(0, c.Affinity.Adjustment));
        coordinator.Observe(current); await Read(reader, u => !u.IsLoading); Assert.Single(client.Requests);
    }
}
