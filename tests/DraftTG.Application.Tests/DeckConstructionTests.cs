using System.Text.Json;
using DraftTG.ArenaIntegration;
using DraftTG.Data;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;

namespace DraftTG.Application.Tests;

public sealed class DeckConstructionTests
{
    private static ScryfallCardCatalogData Data() => new ScryfallCardCatalogDecoder().DecodeCatalogData("""
        [{"id":"green","arena_id":101,"name":"Green","colors":["G"],"rarity":"common","set":"woe","collector_number":"1","cmc":2,"mana_cost":"{1}{G}","type_line":"Creature"},
         {"id":"black","arena_id":102,"name":"Black","colors":["B"],"rarity":"common","set":"woe","collector_number":"2","cmc":3,"mana_cost":"{2}{B}","type_line":"Creature"}]
        """);
    private static DraftSessionUpdate Update(ScryfallCardCatalogData data, bool completed, string id = "draft-1")
    {
        var draft = ArenaDraftIdentifier.Create(id);
        var state = new ArenaDraftStateSnapshot(completed ? ArenaDraftSessionStatus.Completed : ArenaDraftSessionStatus.Active,
            draft, ArenaDraftMode.Quick, "QuickDraft_WOE", null, new([]))
        { RecoveredPool = completed ? new(null, new(Enumerable.Repeat(ArenaCardIdentifier.Create(101), 21)
            .Concat(Enumerable.Repeat(ArenaCardIdentifier.Create(102), 21)))) : null };
        return new(state, new ArenaDraftSnapshotAdapter(new(data)).Convert(state));
    }
    private static SeventeenLandsRatingsResult Ratings() => new("WOE", SeventeenLandsFormat.QuickDraft,
        [new("Green", GameInHandWinRate: .58, GameInHandGameCount: 10000), new("Black", GameInHandWinRate: .56, GameInHandGameCount: 10000)], SeventeenLandsSource.Cache);
    private sealed class Client(bool controlled = false) : ISeventeenLandsCardRatingsClient
    {
        public int Requests;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<SeventeenLandsRatingsResult> Response { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<SeventeenLandsRatingsResult> LoadAsync(string expansion, SeventeenLandsFormat format, bool forceRefresh = false, CancellationToken cancellationToken = default)
        { Interlocked.Increment(ref Requests); Started.TrySetResult(); return controlled ? Response.Task : Task.FromResult(Ratings()); }
    }

    [Fact]
    public async Task CompletedPoolBuildsAutomaticallyWithoutCurrentPackAndNewSessionClearsResult()
    {
        var data = Data(); var client = new Client(); var service = new DeckConstructionService(new LimitedStatisticsService(client, data.Catalog));
        Assert.Equal(DeckBuildAvailability.DraftInProgress, (await service.BuildAsync(Update(data, false))).Availability);
        Assert.Equal(0, client.Requests);
        await using var coordinator = new DeckConstructionCoordinator(service);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var reader = coordinator.ReadUpdatesAsync(timeout.Token).GetAsyncEnumerator();
        var completed = Update(data, true); var generation = coordinator.Observe(completed);
        DeckBuildUpdate built;
        do { Assert.True(await reader.MoveNextAsync()); built = reader.Current; } while (built.Result?.Deck is null);
        Assert.Null(completed.SnapshotResult.Snapshot);
        Assert.Equal(42, built.Pool!.TotalCardCount); Assert.Equal(40, built.Result!.Deck!.TotalCardCount);
        Assert.Equal("BG", built.Result.Deck.Plan.Pair.Code); Assert.Equal(DeckPlanSource.ActiveArchetype, built.Result.Deck.Plan.Source);
        Assert.Equal(generation, coordinator.Observe(completed)); Assert.Equal(1, client.Requests);
        var next = coordinator.Observe(Update(data, false, "draft-2")); Assert.True(next > generation);
        Assert.True(await reader.MoveNextAsync()); Assert.Equal(DeckBuildAvailability.DraftInProgress, reader.Current.Result!.Availability);
        Assert.Null(reader.Current.Result.Deck);
    }

    [Fact]
    public async Task LateCompletedWorkerCannotPublishIntoNewDraftEvenIfProviderIgnoresCancellation()
    {
        var data = Data(); var client = new Client(controlled: true);
        var coordinator = new DeckConstructionCoordinator(new(new LimitedStatisticsService(client, data.Catalog)));
        coordinator.Observe(Update(data, true)); await client.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var generation = coordinator.Observe(Update(data, false, "draft-2")); client.Response.SetResult(Ratings());
        await coordinator.DisposeAsync();
        var records = new List<DeckBuildUpdate>(); await foreach (var update in coordinator.ReadUpdatesAsync()) records.Add(update);
        var last = Assert.Single(records); Assert.Equal(generation, last.Generation);
        Assert.Equal(DeckBuildAvailability.DraftInProgress, last.Result!.Availability); Assert.Null(last.Result.Deck);
    }
}
