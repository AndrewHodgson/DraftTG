using System.Text.Json;
using DraftTG.ArenaIntegration;
using DraftTG.Data;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;

namespace DraftTG.Application.Tests;

public sealed class RecommendationOrchestrationTests
{
    private static readonly LimitedStatisticsContext Context = new("HOB", LimitedStatisticsFormat.QuickDraft);
    private static Card Card(string id, string name, string set = "HOB") => LimitedStatisticsTests.Card(id, name, set);
    private static DraftSnapshot Snapshot(params Card[] cards) => LimitedStatisticsTests.Snapshot(cards);
    private static SeventeenLandsRating Row(string name, double rate, int count) =>
        new(name, GameInHandWinRate: rate, GameInHandGameCount: count);
    private static SeventeenLandsRatingsResult Ratings(string expansion, SeventeenLandsFormat format,
        params SeventeenLandsRating[] rows) => new(expansion, format, rows, SeventeenLandsSource.Live);

    [Fact]
    public async Task BaselineUsesWholeEnvironmentOncePerNameAndNeverDraftHistoryOrPackCopies()
    {
        var a = Card("a", "Alpha");
        var anotherPrinting = Card("a-copy", "Alpha");
        var b = Card("b", "Beta");
        var offPack = Card("off-pack", "Off Pack", "BON");
        var client = new FakeClient((expansion, format) => Task.FromResult(Ratings(expansion, format,
            Row("Alpha", 0.50, 100), Row("Beta", 0.60, 300), Row("Off Pack", 0.40, 600))));
        var service = new LimitedStatisticsService(client, new([a, anotherPrinting, b, offPack]));
        var snapshot = Snapshot(anotherPrinting, b, anotherPrinting);
        var loaded = await service.LoadAsync(Context, snapshot);
        var first = new LimitedStatisticsUpdate(snapshot, false, loaded).Recommendation!;
        var expected = (0.50 * 100 + 0.60 * 300 + 0.40 * 600) / 1000;
        Assert.Equal(expected, first.EnvironmentBaseline!.Value, 12);
        Assert.Equal(3, first.EnvironmentBaselineCardCount);
        Assert.Equal(3, loaded.EnvironmentCatalog!.Count);
        // The representative identity is not used to resolve an actual pack slot.
        Assert.NotNull(loaded.Catalog.StatisticsFor(anotherPrinting.Identifier));
        var withHistory = snapshot with
        {
            History = new([new(new(PackNumber.Create(1), PickNumber.Create(1)), offPack.Identifier)])
        };
        var changed = new LimitedStatisticsUpdate(withHistory, false, await service.LoadAsync(Context, withHistory)).Recommendation!;
        Assert.Equal(first.EnvironmentBaseline, changed.EnvironmentBaseline);
        Assert.Equal(first.Cards, changed.Cards);
        var withoutDuplicate = Snapshot(anotherPrinting, b);
        Assert.Equal(first.EnvironmentBaseline,
            new LimitedStatisticsUpdate(withoutDuplicate, false, await service.LoadAsync(Context, withoutDuplicate))
                .Recommendation!.EnvironmentBaseline);
    }

    [Fact]
    public void EnvironmentMappingRejectsConflictingNamesAndDoesNotInventUnknownIdentities()
    {
        var catalog = new CardCatalog([Card("a", "Alpha"), Card("b", "Beta")]);
        var environment = LimitedStatisticsMapper.MapEnvironment(
            [Row("Alpha", 0.5, 100), Row("Alpha", 0.6, 100), Row("Beta", 0.6, 100), Row("Unknown", 0.9, 100)],
            catalog, Context);
        Assert.Equal(1, environment.Count);
        Assert.NotNull(environment.StatisticsFor(CardIdentifier.Create("b")));
        Assert.Null(environment.StatisticsFor(CardIdentifier.Create("a")));
    }

    [Fact]
    public async Task PackAndLoadingPresentationExistBeforeRecommendationThenArrivalRanksOccurrences()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<SeventeenLandsRatingsResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeClient((_, _) => { started.SetResult(); return response.Task; });
        var data = CatalogData();
        await using var coordinator = new LimitedStatisticsCoordinator(new(client, data.Catalog));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var reader = coordinator.ReadUpdatesAsync(timeout.Token).GetAsyncEnumerator();
        var session = Update(data, [101, 102, 101, 103]);
        coordinator.Observe(session);
        await started.Task.WaitAsync(timeout.Token);
        Assert.True(await reader.MoveNextAsync());
        Assert.Same(session.SnapshotResult.Snapshot, reader.Current.Snapshot);
        Assert.True(reader.Current.IsLoading);
        Assert.Null(reader.Current.Recommendation);
        response.SetResult(Ratings("HOB", SeventeenLandsFormat.QuickDraft,
            Row("Alpha", 0.575, 900), Row("Beta", 0.58, 5000)));
        Assert.True(await reader.MoveNextAsync());
        var recommendation = reader.Current.Recommendation!;
        Assert.Equal(RecommendationAvailability.ReadyPartialCoverage, recommendation.Availability);
        Assert.Equal(3, recommendation.ScoredCardCount);
        Assert.Equal(4, recommendation.TotalPackCardCount);
        Assert.Equal(new int?[] { 2, 1, 3, null }, recommendation.Cards.Select(card => card.StatisticalRank));
        Assert.Equal([0, 1, 2, 3], recommendation.Cards.Select(card => card.PackIndex));

        var next = Update(data, [101], pick: 2);
        coordinator.Observe(next);
        Assert.True(await reader.MoveNextAsync());
        Assert.Same(next.SnapshotResult.Snapshot, reader.Current.Snapshot);
        Assert.Null(reader.Current.Recommendation!.TopRecommendedPackIndex);
        Assert.Equal(RecommendationAvailability.InsufficientComparableStatistics, reader.Current.Recommendation.Availability);
        Assert.Equal(1, client.Calls);
    }

    [Fact]
    public async Task ContextChangeDiscardsLateRecommendationsAndUsesNewEnvironment()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var old = new TaskCompletionSource<SeventeenLandsRatingsResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeClient((expansion, format) =>
        {
            if (expansion == "HOB") { started.SetResult(); return old.Task; }
            return Task.FromResult(Ratings(expansion, format, Row("Alpha", 0.50, 500), Row("Beta", 0.65, 500)));
        });
        var data = CatalogData();
        await using var coordinator = new LimitedStatisticsCoordinator(new(client, data.Catalog));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var reader = coordinator.ReadUpdatesAsync(timeout.Token).GetAsyncEnumerator();
        coordinator.Observe(Update(data, [101, 102]));
        await started.Task.WaitAsync(timeout.Token);
        var next = Update(data, [102, 101], "QuickDraft_OTHER_20260915");
        coordinator.Observe(next);
        old.SetResult(Ratings("HOB", SeventeenLandsFormat.QuickDraft, Row("Alpha", 0.9, 500), Row("Beta", 0.1, 500)));
        do { Assert.True(await reader.MoveNextAsync()); } while (reader.Current.IsLoading);
        Assert.Same(next.SnapshotResult.Snapshot, reader.Current.Snapshot);
        Assert.Equal("OTHER", reader.Current.Result!.ActualSourceContext!.Expansion);
        Assert.Equal(0, reader.Current.Recommendation!.TopRecommendedPackIndex);
        Assert.Equal(0.575, reader.Current.Recommendation.EnvironmentBaseline!.Value, 12);
        Assert.Equal(0.65, reader.Current.Recommendation.Cards[0].RawGamesInHandWinRate);
    }

    [Fact]
    public async Task NewStatisticsResponseRecomputesRanksAndBaselineForSameSnapshot()
    {
        var a = Card("a", "Alpha");
        var b = Card("b", "Beta");
        var strongerAlpha = true;
        var client = new FakeClient((expansion, format) => Task.FromResult(Ratings(expansion, format,
            Row("Alpha", strongerAlpha ? 0.65 : 0.50, 900), Row("Beta", 0.575, 900))));
        var service = new LimitedStatisticsService(client, new([a, b]));
        var snapshot = Snapshot(a, b);
        var first = new LimitedStatisticsUpdate(snapshot, false, await service.LoadAsync(Context, snapshot)).Recommendation!;
        strongerAlpha = false;
        var second = new LimitedStatisticsUpdate(snapshot, false, await service.LoadAsync(Context, snapshot)).Recommendation!;
        Assert.Equal(0, first.TopRecommendedPackIndex);
        Assert.Equal(1, second.TopRecommendedPackIndex);
        Assert.NotEqual(first.EnvironmentBaseline, second.EnvironmentBaseline);
        Assert.Equal(0.65, first.Cards[0].RawGamesInHandWinRate);
    }

    [Fact]
    public void NoPackOrLoadingStatisticsHaveNoRecommendation()
    {
        var snapshot = Snapshot(Card("a", "Alpha"), Card("b", "Beta"));
        Assert.Null(new LimitedStatisticsUpdate(snapshot, true, null).Recommendation);
        Assert.Null(new LimitedStatisticsUpdate(snapshot, false, null).Recommendation);
        Assert.Null(new LimitedStatisticsUpdate(null, false, null).Recommendation);
        Assert.Equal("Statistical recommendation unavailable", RecommendationPresentation.Summary(null, null));
        Assert.Equal("Stats Pick: loading\u2026", RecommendationPresentation.Summary(null, null, true));
    }

    [Fact]
    public void ParticipantOnlyCatalogCannotSilentlySupplyAnEnvironmentBaseline()
    {
        var a = Card("a", "Alpha");
        var snapshot = Snapshot(a, a);
        var result = new LimitedStatisticsLoadResult(Context, Context,
            new([new(a.Identifier, GameInHandWinRate: 0.6, GameInHandGameCount: 1000)]),
            LimitedStatisticsSource.Cache, null, null);
        var recommendation = new LimitedStatisticsUpdate(snapshot, false, result).Recommendation!;
        Assert.Equal(RecommendationAvailability.NoEnvironmentBaseline, recommendation.Availability);
        Assert.Null(recommendation.TopRecommendedPackIndex);
    }

    private sealed class FakeClient(Func<string, SeventeenLandsFormat, Task<SeventeenLandsRatingsResult>> load)
        : ISeventeenLandsCardRatingsClient
    {
        public int Calls { get; private set; }
        public Task<SeventeenLandsRatingsResult> LoadAsync(string expansion, SeventeenLandsFormat format,
            bool forceRefresh = false, CancellationToken cancellationToken = default)
        { Calls++; return load(expansion, format); }
    }

    private static ScryfallCardCatalogData CatalogData() => new ScryfallCardCatalogDecoder().DecodeCatalogData(
        JsonSerializer.Serialize(new[] { "Alpha", "Beta", "Gamma" }.Select((name, i) => new
        {
            id = $"card-{i}", arena_id = 101 + i, name, colors = Array.Empty<string>(),
            rarity = "common", set = "hob", collector_number = $"{i + 1}"
        })));

    private static DraftSessionUpdate Update(ScryfallCardCatalogData data, int[] cards,
        string eventName = "QuickDraft_HOB_20260915", int pick = 1)
    {
        var state = new ArenaDraftStateSnapshot(ArenaDraftSessionStatus.Active, ArenaDraftIdentifier.Create("draft"),
            ArenaDraftMode.Quick, eventName, new(ArenaDraftIdentifier.Create("draft"), ArenaDraftCoordinate.Create(1, pick),
                new(cards.Select(ArenaCardIdentifier.Create))), new([]));
        return new(state, new ArenaDraftSnapshotAdapter(new(data)).Convert(state));
    }
}
