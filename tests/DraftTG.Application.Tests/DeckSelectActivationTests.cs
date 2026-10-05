using DraftTG.ArenaIntegration;
using DraftTG.Data;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;

namespace DraftTG.Application.Tests;

public sealed class DeckSelectActivationTests
{
    private static string[] Lines() => File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "deckselect-woe-completion-live.jsonl"));
    private static ScryfallCardCatalogData Data() => new ScryfallCardCatalogDecoder().DecodeCatalogData(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "deckselect-woe-cards.json")));
    private sealed class Ratings(CardCatalog catalog) : ISeventeenLandsCardRatingsClient
    {
        public int Requests;
        public Task<SeventeenLandsRatingsResult> LoadAsync(string expansion, SeventeenLandsFormat format,
            bool forceRefresh = false, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Requests);
            return Task.FromResult(new SeventeenLandsRatingsResult(expansion, format,
                catalog.Cards.Select(c => new SeventeenLandsRating(c.Name, GameInHandWinRate: .55, GameInHandGameCount: 10000)), SeventeenLandsSource.Cache));
        }
    }
    private static DraftSessionCoordinator Session(ScryfallCardCatalogData data, IEnumerable<string> lines)
    {
        var source = new FakeArenaLogSource(); source.EnqueueRun(lines.Select(FakeArenaLogSource.Line).ToArray());
        return new(source, new(), new(), new(new(data)));
    }
    private static async Task<DeckBuildUpdate> Built(IAsyncEnumerator<DeckBuildUpdate> reader)
    {
        while (await reader.MoveNextAsync()) if (reader.Current.Result?.Deck is not null) return reader.Current;
        throw new InvalidOperationException("No automatically published build.");
    }

    [Fact]
    public async Task RealRawSequenceAutomaticallyBuildsReadyDeckOnceAndRepeatedCompletionIsIdempotent()
    {
        var data = Data(); var ratings = new Ratings(data.Catalog);
        await using var decks = new DeckConstructionCoordinator(new(new LimitedStatisticsService(ratings, data.Catalog)));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var reader = decks.ReadUpdatesAsync(timeout.Token).GetAsyncEnumerator();
        var completedUpdates = new List<DraftSessionUpdate>();
        var lines = Lines().Concat(Enumerable.Repeat(Lines()[3], 3));
        await foreach (var update in Session(data, lines).RunAsync(timeout.Token))
        {
            Assert.Null(update.Diagnostic); decks.Observe(update);
            if (update.ArenaState.IsCompleted) completedUpdates.Add(update);
        }
        var completed = Assert.Single(completedUpdates);
        Assert.Null(completed.SnapshotResult.Snapshot); Assert.Equal(DraftSnapshotAvailability.Completed, completed.SnapshotResult.Availability);
        Assert.Equal(42, completed.SnapshotResult.DraftPool!.TotalCardCount);
        Assert.Equal(DraftPoolCompleteness.Complete, completed.SnapshotResult.DraftPool.Completeness);
        var built = await Built(reader); Assert.Equal(DeckBuildAvailability.Ready, built.Result!.Availability);
        Assert.Equal(40, built.Result.Deck!.TotalCardCount); Assert.Equal(23, built.Result.Deck.NonlandCount); Assert.Equal(17, built.Result.Deck.LandCount);
        Assert.Equal(1, ratings.Requests); Assert.Equal(built.Generation, decks.Observe(completed));
        Assert.All(completed.SnapshotResult.DraftPool.Entries, p => Assert.Equal(p.Count,
            built.Result.Deck.Nonlands.Concat(built.Result.Deck.NonbasicLands).Concat(built.Result.Deck.Sideboard)
                .Where(e => e.CardIdentifier == p.CardIdentifier).Sum(e => e.Count)));
    }

    [Fact]
    public async Task NewRawDraftStartReplacesCompletedPoolAndActiveSuggestedDeckWithoutAnotherBuild()
    {
        var data = Data(); var ratings = new Ratings(data.Catalog);
        await using var decks = new DeckConstructionCoordinator(new(new LimitedStatisticsService(ratings, data.Catalog)));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var reader = decks.ReadUpdatesAsync(timeout.Token).GetAsyncEnumerator();
        var lines = Lines().Append("""[UnityCrossThreadLogger]==> EventJoin {"id":"entry-2","request":{"EventName":"QuickDraft_WOE_20260929","EntryCurrencyType":"Gold","EntryCurrencyPaid":10000}}""")
            .Append("""{"CurrentModule":"BotDraft","Payload":{"EventName":"QuickDraft_WOE_20260929","DraftStatus":"PickNext","PackNumber":0,"PickNumber":0,"NumCardsToPick":1,"DraftPack":[86978],"PickedCards":[]}}""");
        DeckBuildUpdate? oldBuild = null; DraftSessionUpdate? last = null;
        await foreach (var update in Session(data, lines).RunAsync(timeout.Token))
        {
            decks.Observe(update); last = update;
            if (update.ArenaState.IsCompleted) oldBuild = await Built(reader);
        }
        Assert.NotNull(oldBuild); Assert.Equal(DeckBuildAvailability.Ready, oldBuild.Result!.Availability);
        Assert.Equal(ArenaDraftSessionStatus.Active, last!.ArenaState.Status);
        Assert.Equal(0, last.SnapshotResult.DraftPool!.TotalCardCount); Assert.Empty(last.ArenaState.CompletedPicks);
        Assert.True(await reader.MoveNextAsync()); var next = reader.Current;
        Assert.True(next.Generation > oldBuild.Generation); Assert.Null(next.DraftIdentifier);
        Assert.Equal(oldBuild.EventName, next.EventName);
        Assert.Equal(DeckBuildAvailability.DraftInProgress, next.Result!.Availability); Assert.Null(next.Result.Deck);
        Assert.Equal(1, ratings.Requests);
    }
}
