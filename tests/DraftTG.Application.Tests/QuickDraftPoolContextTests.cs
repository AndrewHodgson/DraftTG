using System.Runtime.CompilerServices;
using System.Text.Json;
using DraftTG.ArenaIntegration;
using DraftTG.Data;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;

namespace DraftTG.Application.Tests;

public sealed class QuickDraftPoolContextTests
{
    [Fact]
    public async Task MidDraftUnorderedPoolFeedsColorAndArchetypeContextWithoutFakeHistory()
    {
        var data = new ScryfallCardCatalogDecoder().DecodeCatalogData("""
            [
              {"id":"black","arena_id":1,"name":"Black","colors":["B"],"rarity":"common","set":"woe","collector_number":"1"},
              {"id":"green","arena_id":2,"name":"Green","colors":["G"],"rarity":"common","set":"woe","collector_number":"2"},
              {"id":"blue","arena_id":3,"name":"Blue","colors":["U"],"rarity":"common","set":"woe","collector_number":"3"}
            ]
            """);
        var pool = Enumerable.Repeat(1, 7).Concat(Enumerable.Repeat(2, 7)).ToArray();
        string Status(int pick, int[] cards) => JsonSerializer.Serialize(new
        {
            CurrentModule = "BotDraft", Payload = JsonSerializer.Serialize(new
            {
                EventName = "QuickDraft_WOE", DraftStatus = "PickNext", PackNumber = 1, PickNumber = pick,
                NumCardsToPick = 1, DraftPack = new[] { 3, 2 }, PickedCards = cards
            })
        });
        var adapter = new ArenaDraftSnapshotAdapter(new ArenaCardResolver(data));
        var coordinator = new DraftSessionCoordinator(new LinesSource([Status(0, pool), Status(1, pool.Prepend(1).Reverse().ToArray())]),
            new(), new(), adapter);
        var updates = new List<DraftSessionUpdate>();
        await foreach (var update in coordinator.RunAsync()) updates.Add(update);
        var initial = updates.First(u => u.SnapshotResult.Snapshot is not null).SnapshotResult.Snapshot!;
        Assert.Equal(0, initial.History.Count);
        Assert.Equal(14, initial.DraftedPool.Count);
        var final = updates[^1].SnapshotResult.Snapshot!;
        Assert.Equal(15, final.DraftedPool.Count);
        Assert.Equal(14, updates[^1].ArenaState.UnqualifiedHistoryCardCount);
        var known = Assert.Single(final.History.Picks);
        Assert.Equal(new DraftPosition(PackNumber.Create(2), PickNumber.Create(1)), known.Position);
        Assert.Equal("black", known.SelectedCardIdentifier.Value);
        Assert.All(updates, u => Assert.Null(u.Diagnostic));

        var statistical = new StatisticalRecommendationEngine().Recommend(final.CurrentPack, new([]));
        var contextual = new ContextualRecommendationEngine().Recommend(final, data.Catalog, statistical);
        Assert.Same(statistical, contextual.StatisticalRecommendation);
        Assert.Equal(8, contextual.Profile.For(MagicColor.Black).Evidence);
        Assert.Equal(7, contextual.Profile.For(MagicColor.Green).Evidence);
        Assert.Equal(0, contextual.Profile.For(MagicColor.Blue).Evidence);
        Assert.Equal(15, contextual.Profile.CompletedPickCount);
        Assert.Equal(1, contextual.Profile.ProgressFactor);
        var set = new SetArchetypeProfile("WOE", "https://example.test/woe",
            [new("WOE", ArchetypeColorPair.Create("BG"), "BG", "Test", "Test")]);
        var lane = new LaneContextualRecommendationEngine().Recommend(final, contextual,
            DraftPackObservationHistory.Empty, LimitedStatisticsFormat.QuickDraft);
        var archetype = new ArchetypeRecommendationEngine().Recommend(lane, set, new("WOE", LimitedStatisticsFormat.QuickDraft));
        Assert.Equal("BG", archetype.Profile.Active!.Definition.Pair.Code);
        Assert.Equal(1, archetype.Profile.Active.Coverage);

        // Both pool and exact history remain separately available while waiting for a pack and at completion.
        var engine = new ArenaDraftStateEngine();
        var parser = new ArenaDraftLogParser();
        foreach (var line in new[] { Status(0, pool), Status(1, pool.Prepend(1).ToArray()) })
            foreach (var fact in parser.Parse(new ArenaLogSourceEvent.Line(line))) engine.Apply(fact);
        engine.Apply(new ArenaDraftLogEvent.DraftCompleted(new("QuickDraft_WOE")));
        var completed = adapter.Convert(engine.Current);
        Assert.Equal(DraftSnapshotAvailability.Completed, completed.Availability);
        Assert.Equal(15, completed.ResolvedDraftedPool!.Count);
        Assert.Equal(1, completed.ResolvedHistory!.Count);
    }

    private sealed class LinesSource(string[] lines) : IArenaLogSource
    {
        public async IAsyncEnumerable<ArenaLogSourceEvent> ReadEventsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var line in lines) { cancellationToken.ThrowIfCancellationRequested(); yield return new ArenaLogSourceEvent.Line(line); }
            await Task.CompletedTask;
        }
    }
}
