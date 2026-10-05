using DraftTG.ArenaIntegration;
using DraftTG.Data;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;

namespace DraftTG.Application.Tests;

public sealed class SuggestedDeckConstructionTests
{
    private static ScryfallCardCatalogData Data() => new ScryfallCardCatalogDecoder().DecodeCatalogData("""
        [{"id":"green","arena_id":101,"name":"Green","colors":["G"],"rarity":"common","set":"woe","collector_number":"1","cmc":2,"mana_cost":"{1}{G}","type_line":"Creature"},
         {"id":"red","arena_id":102,"name":"Red","colors":["R"],"rarity":"common","set":"woe","collector_number":"2","cmc":2,"mana_cost":"{1}{R}","type_line":"Creature"},
         {"id":"black","arena_id":103,"name":"Black","colors":["B"],"rarity":"common","set":"woe","collector_number":"3","cmc":2,"mana_cost":"{1}{B}","type_line":"Creature"}]
        """);
    private static readonly SetArchetypeProfileCatalog Profiles = new([new("WOE", "https://example.test/woe",
        [new("WOE", ArchetypeColorPair.Create("RG"), "Beatdown", "Test")])]);
    private static DraftSessionUpdate Update(ScryfallCardCatalogData data, bool completed, string id = "draft-A")
    {
        var state = new ArenaDraftStateSnapshot(completed ? ArenaDraftSessionStatus.Completed : ArenaDraftSessionStatus.Active,
            ArenaDraftIdentifier.Create(id), ArenaDraftMode.Quick, "QuickDraft_WOE", null, new([]))
        { RecoveredPool = completed ? new(null, new(Enumerable.Repeat(ArenaCardIdentifier.Create(101), 20)
            .Concat(Enumerable.Repeat(ArenaCardIdentifier.Create(102), 11)).Concat(Enumerable.Repeat(ArenaCardIdentifier.Create(103), 11)))) : null };
        return new(state, new ArenaDraftSnapshotAdapter(new(data)).Convert(state));
    }
    private sealed class Client(bool delayedPair = false) : ISeventeenLandsCardRatingsClient, ISeventeenLandsPairRatingsClient
    {
        public int OverallRequests; public List<ArchetypeColorPair> PairRequests { get; } = [];
        public TaskCompletionSource PairStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<SeventeenLandsRatingsResult> PairResponse { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public static SeventeenLandsRatingsResult Result(ArchetypeColorPair? pair = null) => new("WOE", SeventeenLandsFormat.QuickDraft,
            new[] { "Green", "Red", "Black" }.Select(name => new SeventeenLandsRating(name, 10000, GameInHandWinRate: .55)),
            SeventeenLandsSource.Cache, colorPair: pair);
        public Task<SeventeenLandsRatingsResult> LoadAsync(string expansion, SeventeenLandsFormat format, bool forceRefresh = false, CancellationToken cancellationToken = default)
        { OverallRequests++; return Task.FromResult(Result()); }
        public Task<SeventeenLandsRatingsResult> LoadPairAsync(string expansion, SeventeenLandsFormat format, ArchetypeColorPair colorPair, CancellationToken cancellationToken = default)
        { PairRequests.Add(colorPair); PairStarted.TrySetResult(); return delayedPair ? PairResponse.Task : Task.FromResult(Result(colorPair)); }
    }
    private static DeckConstructionService Service(Client client, ScryfallCardCatalogData data, int cap = 2) =>
        new(new LimitedStatisticsService(client, data.Catalog, Profiles), Profiles, suggestedConfiguration: new(maximumAdditionalPairDataRequests: cap));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task MultipleBuildsReuseOneEnvironmentAndOnlyTheExistingFinalActivePairRequest(int cap)
    {
        var data = Data(); var client = new Client(); var service = Service(client, data, cap);
        Assert.Null(await service.BuildSuggestionsAsync(Update(data, false), "session-A"));
        Assert.Equal(0, client.OverallRequests); Assert.Empty(client.PairRequests);
        var set = (await service.BuildSuggestionsAsync(Update(data, true), "session-A"))!;
        Assert.Equal(2, set.Builds.Count); Assert.Equal(1, client.OverallRequests);
        Assert.Equal("RG", Assert.Single(client.PairRequests).Code);
        Assert.Equal("session-A", set.SessionIdentity);
        Assert.Equal(DeckPlanSource.AlternativeColorPair, set.Builds[1].Deck.Plan.Source);
    }

    [Fact]
    public async Task LatePairDataCannotPublishIntoANewSessionEvenWhenProviderIgnoresCancellation()
    {
        var data = Data(); var client = new Client(delayedPair: true); var coordinator = new DeckConstructionCoordinator(Service(client, data));
        coordinator.Observe(Update(data, true)); await client.PairStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var generation = coordinator.Observe(Update(data, false, "draft-B"));
        client.PairResponse.SetResult(Client.Result(ArchetypeColorPair.Create("RG")));
        await coordinator.DisposeAsync();
        var records = new List<DeckBuildUpdate>(); await foreach (var update in coordinator.ReadUpdatesAsync()) records.Add(update);
        var latest = Assert.Single(records); Assert.Equal(generation, latest.Generation);
        Assert.Null(latest.Suggestions); Assert.Null(latest.Result!.Deck); Assert.Equal(DeckBuildAvailability.DraftInProgress, latest.Result.Availability);
    }

    [Fact]
    public async Task CompletedRefreshesAreIdempotentAndNewAnonymousSessionCannotReuseOldSelectionIdentity()
    {
        var data = Data(); var client = new Client(); await using var coordinator = new DeckConstructionCoordinator(Service(client, data));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var reader = coordinator.ReadUpdatesAsync(timeout.Token).GetAsyncEnumerator();
        async Task<SuggestedDeckSet> NextSet()
        { while (await reader.MoveNextAsync()) if (reader.Current.Suggestions is { } set) return set; throw new InvalidOperationException(); }
        var completed = Update(data, true); var generation = coordinator.Observe(completed); var first = await NextSet();
        Assert.Equal(generation, coordinator.Observe(completed)); Assert.Equal(1, client.OverallRequests);
        coordinator.Observe(Update(data, false)); coordinator.Observe(completed); var next = await NextSet();
        Assert.NotEqual(first.SessionIdentity, next.SessionIdentity); Assert.NotEqual(first.Recommended!.Id, next.Recommended!.Id);
    }
}
