using DraftTG.ArenaIntegration;
using DraftTG.Data;
using DraftTG.Domain;

namespace DraftTG.Application.Tests;

public sealed class LaneOrchestrationTests
{
    private static ScryfallCardCatalogData Data() => new ScryfallCardCatalogDecoder().DecodeCatalogData("""
        [
          {"id":"green","arena_id":101,"name":"Green","colors":["G"],"rarity":"common","set":"woe","collector_number":"1"},
          {"id":"blue","arena_id":102,"name":"Blue","colors":["U"],"rarity":"common","set":"woe","collector_number":"2"},
          {"id":"signal","arena_id":103,"name":"Signal","colors":["G"],"rarity":"common","set":"woe","collector_number":"3"},
          {"id":"environment","arena_id":104,"name":"Environment","colors":[],"rarity":"common","set":"woe","collector_number":"4"}
        ]
        """);
    private static SeventeenLandsRatingsResult Ratings(string expansion, SeventeenLandsFormat format) =>
        new(expansion, format, [new("Green", GameInHandWinRate: 0.64, GameInHandGameCount: 5000, AverageLastSeenAt: 14),
            new("Blue", GameInHandWinRate: 0.65, GameInHandGameCount: 5000, AverageLastSeenAt: 14),
            new("Signal", GameInHandWinRate: 0.75, GameInHandGameCount: 5000, AverageLastSeenAt: 3),
            new("Environment", GameInHandWinRate: 0.50, GameInHandGameCount: 100000, AverageLastSeenAt: 10)], SeventeenLandsSource.Live);
    private sealed class Client(Func<string, SeventeenLandsFormat, Task<SeventeenLandsRatingsResult>>? load = null)
        : ISeventeenLandsCardRatingsClient
    {
        public Task<SeventeenLandsRatingsResult> LoadAsync(string expansion, SeventeenLandsFormat format,
            bool forceRefresh = false, CancellationToken cancellationToken = default) =>
            load?.Invoke(expansion, format) ?? Task.FromResult(Ratings(expansion, format));
    }
    private static DraftSessionUpdate Update(ScryfallCardCatalogData data, int pick, int[]? cards,
        string draft = "draft", ArenaDraftPickRecordList? completed = null)
    {
        var id = ArenaDraftIdentifier.Create(draft);
        var state = new ArenaDraftStateSnapshot(ArenaDraftSessionStatus.Active, id, ArenaDraftMode.Quick, "QuickDraft_WOE",
            cards is null ? null : new(id, ArenaDraftCoordinate.Create(1, pick), new(cards.Select(ArenaCardIdentifier.Create))),
            completed ?? new([]));
        return new(state, new ArenaDraftSnapshotAdapter(new(data)).Convert(state));
    }
    private static async Task<LimitedStatisticsUpdate> ReadReady(IAsyncEnumerator<LimitedStatisticsUpdate> reader)
    {
        do { Assert.True(await reader.MoveNextAsync()); } while (reader.Current.IsLoading);
        return reader.Current;
    }

    [Fact]
    public async Task ObservationsRecomputeLaneStateReplayIsSafeAndNewDraftResetsIt()
    {
        var data = Data();
        await using var coordinator = new LimitedStatisticsCoordinator(new(new Client(), data.Catalog));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var reader = coordinator.ReadUpdatesAsync(timeout.Token).GetAsyncEnumerator();
        for (var pick = 8; pick <= 10; pick++)
        {
            var observed = Update(data, pick, [103]);
            coordinator.Observe(observed);
            var ready = await ReadReady(reader);
            Assert.Equal(pick - 7, ready.LaneRecommendation!.Profile.For(MagicColor.Green).Evidence);
            Assert.Equal((pick - 7) / 4.0, ready.LaneRecommendation.Profile.ObservationConfidence);
            coordinator.Observe(observed);
            var replay = await ReadReady(reader);
            Assert.Equal(ready.LaneRecommendation.Profile.Colors, replay.LaneRecommendation!.Profile.Colors);
        }
        var current = Update(data, 11, [101, 102]);
        coordinator.Observe(current);
        var final = await ReadReady(reader);
        Assert.Equal(0, final.LaneRecommendation!.TopRecommendedPackIndex);
        Assert.Equal(1, final.ContextualRecommendation!.TopRecommendedPackIndex);
        Assert.Equal(1, final.Recommendation!.TopRecommendedPackIndex);
        Assert.Same(final.ContextualRecommendation, final.LaneRecommendation.PoolRecommendation);
        Assert.Contains("Quick Draft / bot", LaneRecommendationPresentation.Summary(final.LaneRecommendation, "Green"));
        Assert.Contains("ALSA 3", LaneRecommendationPresentation.Diagnostics(final.LaneRecommendation));
        coordinator.Observe(Update(data, 11, null, completed: new([new(ArenaDraftIdentifier.Create("draft"),
            ArenaDraftCoordinate.Create(1, 11), new([ArenaCardIdentifier.Create(101)]))])));
        var gap = await ReadReady(reader);
        Assert.Null(gap.LaneRecommendation);
        Assert.Equal(CardIdentifier.Create("green"), gap.ObservationHistory.Observations.Last().SelectedCardIdentifier);
        coordinator.Observe(Update(data, 1, [101, 102], draft: "new-draft"));
        var fresh = await ReadReady(reader);
        Assert.Single(fresh.ObservationHistory.Observations);
        Assert.Empty(fresh.LaneRecommendation!.Profile.Signals);
        Assert.Equal(1, fresh.LaneRecommendation.TopRecommendedPackIndex);
    }

    [Fact]
    public async Task DelayedStatisticsEnrichPastAlsaAndBaselineWithoutPublishingAnOldRecommendation()
    {
        var response = new TaskCompletionSource<SeventeenLandsRatingsResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var data = Data();
        await using var coordinator = new LimitedStatisticsCoordinator(new(new Client((_, _) => response.Task), data.Catalog));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var reader = coordinator.ReadUpdatesAsync(timeout.Token).GetAsyncEnumerator();
        coordinator.Observe(Update(data, 8, [103]));
        Assert.True(await reader.MoveNextAsync());
        Assert.True(reader.Current.IsLoading);
        Assert.Null(reader.Current.LaneRecommendation);
        var next = Update(data, 9, [101, 102]);
        coordinator.Observe(next);
        response.SetResult(Ratings("WOE", SeventeenLandsFormat.QuickDraft));
        var ready = await ReadReady(reader);
        Assert.Same(next.SnapshotResult.Snapshot, ready.Snapshot);
        Assert.Equal(2, ready.ObservationHistory.Observations.Count);
        Assert.Equal(3, ready.ObservationHistory.Observations[0].Cards[0].AverageLastSeenAt);
        Assert.Equal(ready.Recommendation!.EnvironmentBaseline, ready.ObservationHistory.Observations[0].EnvironmentBaseline);
        Assert.Single(ready.LaneRecommendation!.Profile.Signals);
        Assert.Equal(0.25, ready.LaneRecommendation.Profile.ObservationConfidence);
        Assert.Contains("Observed since P1P8", LaneRecommendationPresentation.Summary(ready.LaneRecommendation, "Blue"));
    }
}
