using System.Net;
using System.Text;
using System.Text.Json;
using DraftTG.ArenaIntegration;
using DraftTG.Data;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;

namespace DraftTG.Application.Tests;

public sealed class LimitedStatisticsTests
{
    private static readonly LimitedStatisticsContext Context = new("HOB", LimitedStatisticsFormat.QuickDraft);

    [Theory]
    [InlineData("QuickDraft_HOB_20260915", "HOB")]
    [InlineData("PremierDraft_HOB", "HOB")]
    [InlineData("TradDraft_HOB_20260915", "HOB")]
    [InlineData("TraditionalDraft_HOB", "HOB")]
    [InlineData("Unknown_HOB", null)]
    [InlineData("QuickDraft_HOB_Extra", null)]
    [InlineData("QuickDraft_HOB_20260915_extra", null)]
    public void ResolvesOnlyKnownEventShapes(string eventName, string? expected) =>
        Assert.Equal(expected, DraftExpansionResolver.Resolve(eventName, null, new()));

    [Fact]
    public void PackFallbackRequiresAllResolvedCardsAndUnanimousSet()
    {
        var a = Card("a", "Alpha");
        var b = Card("b", "Beta");
        Assert.Equal("HOB", DraftExpansionResolver.Resolve("unknown", Snapshot(a, b), new([a, b])));
        Assert.Null(DraftExpansionResolver.Resolve("unknown", Snapshot(a, b), new([a])));
        b = b with { SetCode = CardSetCode.Create("BON") };
        Assert.Null(DraftExpansionResolver.Resolve("unknown", Snapshot(a, b), new([a, b])));
        Assert.Null(DraftExpansionResolver.Resolve("unknown", Snapshot(), new()));
    }

    [Theory]
    [InlineData(ArenaDraftModeKind.Quick, LimitedStatisticsFormat.QuickDraft, SeventeenLandsFormat.QuickDraft)]
    [InlineData(ArenaDraftModeKind.Premier, LimitedStatisticsFormat.PremierDraft, SeventeenLandsFormat.PremierDraft)]
    [InlineData(ArenaDraftModeKind.Traditional, LimitedStatisticsFormat.TraditionalDraft, SeventeenLandsFormat.TradDraft)]
    public void FormatMappingIsExplicit(ArenaDraftModeKind arena, LimitedStatisticsFormat expected, SeventeenLandsFormat provider)
    {
        Assert.Equal(expected, DraftExpansionResolver.FormatFor(arena));
        Assert.Equal(provider, LimitedStatisticsService.ProviderFormat(expected));
    }

    [Fact]
    public void UnsupportedModesHaveNoStatisticalFormat()
    {
        Assert.Null(DraftExpansionResolver.FormatFor(ArenaDraftModeKind.PickTwo));
        Assert.Null(DraftExpansionResolver.FormatFor(ArenaDraftModeKind.Unknown));
    }

    [Fact]
    public async Task ExactFormatPreferredAndRawFractionsPreserved()
    {
        var card = Card("a", "Alpha");
        var client = new FakeClient((expansion, format, _) => Task.FromResult(Ratings(expansion, format, [new("Alpha", GameInHandWinRate: 0.5874)])));
        var result = await new LimitedStatisticsService(client, new([card])).LoadAsync(Context, Snapshot(card));
        Assert.False(result.IsFallback);
        Assert.Equal(Context, result.ActualSourceContext);
        Assert.Equal(0.5874, result.Catalog.StatisticsFor(card.Identifier)!.GameInHandWinRate);
        Assert.Equal([SeventeenLandsFormat.QuickDraft], client.Requests.Select(request => request.Format));
    }

    [Theory]
    [InlineData(LimitedStatisticsFormat.QuickDraft)]
    [InlineData(LimitedStatisticsFormat.TraditionalDraft)]
    public async Task EmptyExactFormatFallsBackToExplicitPremierSource(LimitedStatisticsFormat requested)
    {
        var card = Card("a", "Alpha");
        var client = new FakeClient((expansion, format, _) => Task.FromResult(Ratings(expansion, format,
            format == SeventeenLandsFormat.PremierDraft ? [new("Alpha", GameInHandWinRate: 0.6)] : [])));
        var result = await new LimitedStatisticsService(client, new([card]))
            .LoadAsync(Context with { Format = requested }, Snapshot(card));
        Assert.True(result.IsFallback);
        Assert.Equal(LimitedStatisticsFormat.PremierDraft, result.ActualSourceContext!.Format);
        Assert.Equal(requested, result.RequestedContext.Format);
        Assert.Equal([LimitedStatisticsService.ProviderFormat(requested), SeventeenLandsFormat.PremierDraft],
            client.Requests.Select(request => request.Format));
    }

    [Fact]
    public async Task NetworkFailureDoesNotTriggerFormatFallback()
    {
        var client = new FakeClient((expansion, format, _) => Task.FromResult(
            new SeventeenLandsRatingsResult(expansion, format, [], SeventeenLandsSource.Unavailable)));
        var result = await new LimitedStatisticsService(client, new()).LoadAsync(Context, null);
        Assert.Equal(LimitedStatisticsSource.Unavailable, result.Source);
        Assert.Null(result.ActualSourceContext);
        Assert.Single(client.Requests);
    }

    [Fact]
    public void CurrentPrintingIsMappedWithoutChoosingUnrelatedPrinting()
    {
        var current = Card("current", "Alpha");
        var unrelated = Card("other", "Alpha", "OLD");
        var result = LimitedStatisticsMapper.Map([new("Alpha", GameInHandWinRate: 0.6)],
            new([unrelated, current]), Context, Snapshot(current));
        Assert.NotNull(result.Catalog.StatisticsFor(current.Identifier));
        Assert.Null(result.Catalog.StatisticsFor(unrelated.Identifier));
    }

    [Fact]
    public void SupplementalCurrentCardMapsEvenWhenMainSetPrintingExists()
    {
        var current = Card("bonus", "Alpha", "BON");
        var main = Card("main", "Alpha");
        var result = LimitedStatisticsMapper.Map([new("Alpha")], new([main, current]), Context, Snapshot(current));
        Assert.NotNull(result.Catalog.StatisticsFor(current.Identifier));
        Assert.Null(result.Catalog.StatisticsFor(main.Identifier));
    }

    [Fact]
    public void AmbiguousPrintingsAreReportedAndNotArbitrarilySelected()
    {
        var result = LimitedStatisticsMapper.Map([new("Alpha")], new([Card("a", "Alpha"), Card("b", "Alpha")]), Context, null);
        Assert.Equal(0, result.Catalog.Count);
        Assert.Contains("ambiguity", result.Diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public void ActiveExpansionResolvesSinglePrintingOutsideCurrentPack()
    {
        var correct = Card("a", "Alpha");
        var result = LimitedStatisticsMapper.Map([new("Alpha")], new([Card("b", "Alpha", "OLD"), correct]), Context, null);
        Assert.Equal(1, result.Catalog.Count);
        Assert.NotNull(result.Catalog.StatisticsFor(correct.Identifier));
    }

    [Fact]
    public void MissingOrDifferentCaseNamesNeverFabricateStatistics()
    {
        var card = Card("a", "Alpha");
        var result = LimitedStatisticsMapper.Map([new("alpha"), new("Alph")], new([card]), Context, Snapshot(card));
        Assert.Null(result.Catalog.StatisticsFor(card.Identifier));
    }

    [Fact]
    public void MultipleDirectlyObservedPrintingsAndDuplicatePackCopiesAreSafe()
    {
        var a = Card("a", "Alpha");
        var b = Card("b", "Alpha", "BON");
        var result = LimitedStatisticsMapper.Map([new("Alpha")], new([a, b]), Context, Snapshot(a, a, b));
        Assert.Equal(2, result.Catalog.Count);
    }

    [Fact]
    public async Task HobQuickDraftFixtureTraversesParserIdentityRatingsAndMappingInArenaOrder()
    {
        string[] names = ["Mirkwood Mediator", "Vow to Erebor", "Enchanted River's Grasp", "Ordinary Bear",
            "Iron Hills Stalwart", "Reverent Howl", "Eagle's Rescue", "Bombur, Gentle Dreamer", "Dwarven Mattock", "Mountain"];
        var data = CatalogData(names);
        var source = new FakeArenaLogSource();
        source.EnqueueRun(FakeArenaLogSource.Line("""{"method":"BotDraftDraftStatus","response":{"EventName":"QuickDraft_HOB_20260915","DraftId":"sanitized-hob","DraftPack":[101,102,103,104,105,106,107,108,109,110],"PackNumber":0,"PickNumber":2,"DraftStatus":"Drafting"}}"""));
        var coordinator = new DraftSessionCoordinator(source, new(), new(), new(new(data)));
        DraftSessionUpdate? final = null;
        await foreach (var update in coordinator.RunAsync()) final = update;
        var snapshot = final!.SnapshotResult.Snapshot!;
        using var handler = new RatingsHandler(JsonSerializer.Serialize(names.Select((name, index) =>
            new { name, ever_drawn_win_rate = 0.5 + index / 100.0, avg_seen = 6.24, ever_drawn_game_count = 4820 })));
        using var http = new HttpClient(handler);
        var directory = Path.Combine(Path.GetTempPath(), "DraftTG-hob-" + Guid.NewGuid());
        try
        {
            var service = new LimitedStatisticsService(new SeventeenLandsCardRatingsClient(http, new Paths(directory)), data.Catalog);
            var context = service.Resolve(final);
            Assert.Equal(Context, context);
            var result = await service.LoadAsync(context!, snapshot);
            Assert.Equal(SeventeenLandsFormat.QuickDraft.ToString(), handler.Requests.Single().Query.Split("format=")[1]);
            Assert.Equal(10, result.Catalog.Count);
            Assert.Equal(names, snapshot.CurrentPack.AvailableCardIdentifiers.Select(id => data.Catalog.Find(id)!.Name));
            Assert.Equal(names, new LimitedStatisticsUpdate(snapshot, false, result).Cards.Keys.Select(id => data.Catalog.Find(id)!.Name));
            Assert.Equal(0.5, result.Catalog.StatisticsFor(snapshot.CurrentPack.AvailableCardIdentifiers[0])!.GameInHandWinRate);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task PendingStatisticsDoNotBlockArenaConsumptionAndMapLatestPackWithoutRefetch()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<SeventeenLandsRatingsResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeClient((_, _, _) => { started.SetResult(); return release.Task; });
        var data = CatalogData(["Alpha", "Beta"]);
        await using var stats = new LimitedStatisticsCoordinator(new(client, data.Catalog));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var reader = stats.ReadUpdatesAsync(cancellation.Token).GetAsyncEnumerator();
        var first = Update(data, [101]);
        stats.Observe(first);
        await started.Task.WaitAsync(cancellation.Token);
        Assert.True(await reader.MoveNextAsync());
        Assert.True(reader.Current.IsLoading);
        var source = new FakeArenaLogSource();
        source.EnqueueRun(FakeArenaLogSource.Line("""{"method":"BotDraftDraftStatus","response":{"EventName":"QuickDraft_HOB_20260915","DraftId":"draft","DraftPack":[102],"PackNumber":0,"PickNumber":3,"DraftStatus":"Drafting"}}"""));
        var arena = new DraftSessionCoordinator(source, new(), new(), new(new(data)));
        DraftSessionUpdate? latest = null;
        await foreach (var update in arena.RunAsync(cancellation.Token)) { latest = update; stats.Observe(update); }
        Assert.NotNull(latest!.SnapshotResult.Snapshot);
        Assert.Single(client.Requests);
        release.SetResult(Ratings("HOB", SeventeenLandsFormat.QuickDraft, [new("Beta", GameInHandWinRate: 0.6)]));
        do { Assert.True(await reader.MoveNextAsync()); } while (reader.Current.IsLoading);
        Assert.Same(latest.SnapshotResult.Snapshot, reader.Current.Snapshot);
        Assert.Equal("60.0%", reader.Current.Cards.Values.Single().GameInHand);
        stats.Observe(Update(data, [101, 102]));
        Assert.True(await reader.MoveNextAsync());
        Assert.False(reader.Current.IsLoading);
        Assert.Single(client.Requests);
    }

    [Fact]
    public async Task ChangedEnvironmentCancelsAndDisregardsLateOldResults()
    {
        var old = new TaskCompletionSource<SeventeenLandsRatingsResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeClient((expansion, format, token) =>
        {
            if (expansion == "HOB") { started.SetResult(token); return old.Task; }
            return Task.FromResult(Ratings(expansion, format, [new("Alpha", GameInHandWinRate: 0.7)]));
        });
        var data = CatalogData(["Alpha"]);
        await using var coordinator = new LimitedStatisticsCoordinator(new(client, data.Catalog));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var reader = coordinator.ReadUpdatesAsync(timeout.Token).GetAsyncEnumerator();
        coordinator.Observe(Update(data, [101]));
        var token = await started.Task.WaitAsync(timeout.Token);
        coordinator.Observe(Update(data, [101], "QuickDraft_OTHER_20260915"));
        Assert.True(token.IsCancellationRequested);
        old.SetResult(Ratings("HOB", SeventeenLandsFormat.QuickDraft, [new("Alpha", GameInHandWinRate: 0.1)]));
        do { Assert.True(await reader.MoveNextAsync()); } while (reader.Current.IsLoading);
        Assert.Equal("OTHER", reader.Current.Result!.ActualSourceContext!.Expansion);
        Assert.Equal(0.7, reader.Current.Result.Catalog.StatisticsFor(CardIdentifier.Create("card-0"))!.GameInHandWinRate);
    }

    [Fact]
    public async Task DisposalCancelsRequestAndDisposesOwnedResource()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = false;
        var client = new FakeClient(async (_, _, token) =>
        {
            started.SetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException) { canceled = true; throw; }
            return Ratings("HOB", SeventeenLandsFormat.QuickDraft, []);
        });
        var resource = new Resource();
        var data = CatalogData(["Alpha"]);
        var coordinator = new LimitedStatisticsCoordinator(new(client, data.Catalog), resource);
        coordinator.Observe(Update(data, [101]));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(canceled);
        Assert.True(resource.Disposed);
    }

    [Fact]
    public async Task UnknownContextDoesNotFetchAndReportsUnavailable()
    {
        var client = new FakeClient((_, _, _) => throw new InvalidOperationException("Must not fetch"));
        var data = CatalogData(["Alpha", "Beta"]);
        var update = Update(data, [], "UnknownEvent") with { ArenaState = new(ArenaDraftSessionStatus.Active, null, ArenaDraftMode.Unknown("UnknownEvent"), "UnknownEvent", null, new([])) };
        await using var coordinator = new LimitedStatisticsCoordinator(new(client, data.Catalog));
        coordinator.Observe(update);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var reader = coordinator.ReadUpdatesAsync(timeout.Token).GetAsyncEnumerator();
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal("Stats: unavailable", reader.Current.StatusText);
        Assert.Empty(client.Requests);
    }

    [Fact]
    public void ActiveMappingIgnoresUnrelatedPrintingsAndMapsHistoryByIdentity()
    {
        var current = Card("current", "Alpha");
        var history = Card("history", "Alpha", "BON");
        var unrelated = Enumerable.Range(0, 50).SelectMany(i => new[]
        {
            Card($"other-{i}-a", $"Other {i}"), Card($"other-{i}-b", $"Other {i}")
        });
        var catalog = new CardCatalog(unrelated.Concat([current, history, Card("old", "Alpha", "OLD")]));
        var snapshot = Snapshot(current, current) with
        {
            History = new([new(new(PackNumber.Create(1), PickNumber.Create(1)), history.Identifier)])
        };
        var rows = Enumerable.Range(0, 50).Select(i => new SeventeenLandsRating($"Other {i}"))
            .Append(new("Alpha", GameInHandWinRate: 0.5874));
        var result = LimitedStatisticsMapper.Map(rows, catalog, Context, snapshot);
        Assert.Equal(2, result.Catalog.Count);
        Assert.Null(result.Diagnostic);
        Assert.Equal(0.5874, result.Catalog.StatisticsFor(current.Identifier)!.GameInHandWinRate);
        Assert.Equal(0.5874, result.Catalog.StatisticsFor(history.Identifier)!.GameInHandWinRate);
        Assert.Null(result.Catalog.StatisticsFor(CardIdentifier.Create("old")));
    }

    [Fact]
    public void SameNamedPrintingsMapIndependentlyAcrossSnapshots()
    {
        var first = Card("first", "Alpha");
        var second = Card("second", "Alpha", "BON");
        var catalog = new CardCatalog([first, second]);
        foreach (var card in new[] { first, second })
        {
            var result = LimitedStatisticsMapper.Map([new("Alpha", GameInHandGameCount: 4820)], catalog, Context, Snapshot(card));
            Assert.Equal(4820, result.Catalog.StatisticsFor(card.Identifier)!.GameInHandGameCount);
            Assert.Equal(1, result.Catalog.Count);
            Assert.Null(result.Diagnostic);
        }
    }

    [Fact]
    public void ConflictingProviderRowsLeaveOnlyAffectedActiveCardUnavailable()
    {
        var a = Card("a", "Alpha");
        var b = Card("b", "Beta");
        var result = LimitedStatisticsMapper.Map(
            [new("Alpha", GameInHandWinRate: 0.5), new("Alpha", GameInHandWinRate: 0.7), new("Beta", GameInHandWinRate: 0.6)],
            new([a, b]), Context, Snapshot(a, b, a));
        Assert.Null(result.Catalog.StatisticsFor(a.Identifier));
        Assert.Equal(0.6, result.Catalog.StatisticsFor(b.Identifier)!.GameInHandWinRate);
        Assert.Equal("1 current-pack card has no statistics.", result.Diagnostic);
    }

    [Fact]
    public void IdenticalProviderEvidenceIsReconciledWithoutDuplicateCatalogKeys()
    {
        var card = Card("a", "Alpha");
        var row = new SeventeenLandsRating("Alpha", GameInHandGameCount: 500);
        var result = LimitedStatisticsMapper.Map([row, row with { }], new([card]), Context, Snapshot(card, card));
        Assert.Equal(1, result.Catalog.Count);
        Assert.Null(result.Diagnostic);
    }

    [Fact]
    public void MissingHistoryAndUnrelatedProviderConflictsDoNotWarnAboutCurrentPack()
    {
        var card = Card("a", "Alpha");
        var history = Card("b", "Beta");
        var snapshot = Snapshot(card) with
        {
            History = new([new(new(PackNumber.Create(1), PickNumber.Create(1)), history.Identifier)])
        };
        var result = LimitedStatisticsMapper.Map(
            [new("Alpha"), new("Other", GameInHandGameCount: 1), new("Other", GameInHandGameCount: 2)],
            new([card, history]), Context, snapshot);
        Assert.Null(result.Diagnostic);
        Assert.Null(result.Catalog.StatisticsFor(history.Identifier));
    }

    [Fact]
    public async Task LiveServiceWithoutSnapshotDoesNotRunStandalonePrintingAudit()
    {
        var client = new FakeClient((expansion, format, _) => Task.FromResult(Ratings(expansion, format, [new("Alpha")])));
        var result = await new LimitedStatisticsService(client, new([Card("a", "Alpha"), Card("b", "Alpha")]))
            .LoadAsync(Context, null);
        Assert.Equal(0, result.Catalog.Count);
        Assert.Null(result.Diagnostic);
        Assert.Equal(Context, result.ActualSourceContext);
    }

    [Fact]
    public async Task LiveHobPickFourFixtureKeepsQuickSourceAndOrderWithoutGlobalBanner()
    {
        string[] names = ["Stony-Voiced Goblins", "Old Thrush", "Long Lake Nuisance", "Mirkwood Nurturer"];
        var active = names.Select((name, i) => Card($"active-{i}", name, i == 2 ? "BON" : "HOB")).ToArray();
        var catalog = new CardCatalog(active.Concat(names.SelectMany((name, i) => new[]
            { Card($"alt-{i}", name), Card($"old-{i}", name, "OLD") }))
            .Concat([Card("other-a", "Unrelated"), Card("other-b", "Unrelated")]));
        using var handler = new RatingsHandler(JsonSerializer.Serialize(names.Append("Unrelated").Select((name, i) =>
            new { name, ever_drawn_win_rate = 0.58 - i / 100.0, avg_seen = 6.24, ever_drawn_game_count = 4820 })));
        using var http = new HttpClient(handler);
        var directory = Path.Combine(Path.GetTempPath(), "DraftTG-hob-71-" + Guid.NewGuid());
        try
        {
            var snapshot = Snapshot(active) with
            {
                CurrentPack = new(new(PackNumber.Create(1), PickNumber.Create(4)), active.Select(card => card.Identifier))
            };
            var result = await new LimitedStatisticsService(new SeventeenLandsCardRatingsClient(http, new Paths(directory)), catalog)
                .LoadAsync(Context, snapshot);
            Assert.Equal(Context, result.ActualSourceContext);
            Assert.False(result.IsFallback);
            Assert.Null(result.Diagnostic);
            Assert.Equal(4, result.Catalog.Count);
            Assert.All(active, card => Assert.NotNull(result.Catalog.StatisticsFor(card.Identifier)));
            var presentation = new LimitedStatisticsUpdate(snapshot, false, result);
            Assert.Equal("Stats: Quick Draft", presentation.StatusText);
            Assert.Equal(names, snapshot.CurrentPack.AvailableCardIdentifiers.Select(id => catalog.Find(id)!.Name));
            Assert.All(presentation.Cards.Values, row =>
            {
                Assert.Equal("6.24", row.AverageLastSeen);
                Assert.Equal("n=4,820", row.SampleCount);
                Assert.NotEqual("—", row.GameInHand);
            });
            Assert.Single(handler.Requests);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Phase75FixtureMapsTrueGihAndReportsSlotCoverageWithoutHistory()
    {
        var names = new[] { "Alpha", "Missing GIH", "Unknown sample", "Low GIH", "ALSA only" };
        var cards = names.Select((name, i) => Card($"fixture-{i}", name)).ToArray();
        var history = Card("history-fixture", "Alpha");
        var snapshot = Snapshot(cards.Concat([cards[0]]).ToArray()) with
        {
            History = new([new(new(PackNumber.Create(1), PickNumber.Create(1)), history.Identifier)])
        };
        using var handler = new RatingsHandler(await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "17lands-phase7-5.json")));
        using var http = new HttpClient(handler);
        var directory = Path.Combine(Path.GetTempPath(), "DraftTG-phase75-" + Guid.NewGuid());
        try
        {
            var service = new LimitedStatisticsService(new SeventeenLandsCardRatingsClient(http, new Paths(directory)),
                new(cards.Concat([history])));
            var result = await service.LoadAsync(Context, snapshot);
            var complete = result.Catalog.StatisticsFor(cards[0].Identifier)!;
            Assert.Equal(0.575, complete.GameInHandWinRate);
            Assert.Equal(900, complete.GameInHandGameCount);
            Assert.NotEqual(0.510, complete.GameInHandWinRate);
            Assert.NotEqual(5000, complete.GameInHandGameCount);
            Assert.Equal(0.59, complete.OpeningHandWinRate);
            Assert.Equal(0.56, complete.DrawnWinRate);
            Assert.Equal(0.025, complete.DrawnImprovementWinRate);
            Assert.Equal(4.2, complete.AverageTakenAt);
            Assert.Equal(0.7, complete.PlayRate);
            var update = new LimitedStatisticsUpdate(snapshot, false, result);
            Assert.Equal("57.5%", update.Cards[cards[0].Identifier].GameInHand);
            Assert.Equal("n=900", update.Cards[cards[0].Identifier].SampleCount);
            Assert.False(update.Cards[cards[0].Identifier].IsLowSample);
            Assert.Equal("\u2014", update.Cards[cards[1].Identifier].GameInHand);
            Assert.True(update.Cards[cards[1].Identifier].IsLowSample);
            Assert.Equal("7.10", update.Cards[cards[1].Identifier].AverageLastSeen);
            Assert.Equal("", update.Cards[cards[2].Identifier].SampleCount);
            Assert.False(update.Cards[cards[2].Identifier].IsLowSample);
            Assert.Equal("57.5%", update.Cards[cards[2].Identifier].GameInHand);
            Assert.True(update.Cards[cards[3].Identifier].IsLowSample);
            Assert.Equal("\u2014", update.Cards[cards[4].Identifier].GameInHand);
            Assert.Equal("8.20", update.Cards[cards[4].Identifier].AverageLastSeen);
            Assert.False(update.Cards[cards[4].Identifier].IsLowSample);
            Assert.Equal("GIH available: 4 / 6\nGIH low sample: 2\nGIH unavailable: 2\nALSA available: 5 / 6\nSource: 17Lands QuickDraft / HOB", update.CoverageText);
            Assert.Empty(new LimitedStatisticsUpdate(snapshot, true, null).CoverageText);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    internal static Card Card(string id, string name, string set = "HOB") => new(CardIdentifier.Create(id), name,
        ColorSet.Colorless, CardRarity.Common, CardSetCode.Create(set), CollectorNumber.Create("1"));
    internal static DraftSnapshot Snapshot(params Card[] cards) => new(
        new(new(PackNumber.Create(1), PickNumber.Create(1)), cards.Select(card => card.Identifier)), new(), DraftFormat.BestOfOne);
    private static SeventeenLandsRatingsResult Ratings(string expansion, SeventeenLandsFormat format, IEnumerable<SeventeenLandsRating> rows) =>
        new(expansion, format, rows, SeventeenLandsSource.Live, DateTimeOffset.UtcNow);
    private sealed class FakeClient(Func<string, SeventeenLandsFormat, CancellationToken, Task<SeventeenLandsRatingsResult>> load) : ISeventeenLandsCardRatingsClient
    {
        public List<(string Expansion, SeventeenLandsFormat Format)> Requests { get; } = [];
        public Task<SeventeenLandsRatingsResult> LoadAsync(string expansion, SeventeenLandsFormat format, bool forceRefresh = false, CancellationToken cancellationToken = default)
        { Requests.Add((expansion, format)); return load(expansion, format, cancellationToken); }
    }
    private static ScryfallCardCatalogData CatalogData(string[] names) => new ScryfallCardCatalogDecoder().DecodeCatalogData(
        JsonSerializer.Serialize(names.Select((name, index) => new { id = $"card-{index}", arena_id = 101 + index,
            name, colors = Array.Empty<string>(), rarity = "common", set = "hob", collector_number = $"{index + 1}" })));
    private static DraftSessionUpdate Update(ScryfallCardCatalogData data, int[] cards, string eventName = "QuickDraft_HOB_20260915")
    {
        var state = new ArenaDraftStateSnapshot(ArenaDraftSessionStatus.Active, ArenaDraftIdentifier.Create("draft"), ArenaDraftMode.Quick,
            eventName, cards.Length == 0 ? null : new(ArenaDraftIdentifier.Create("draft"), ArenaDraftCoordinate.Create(1, 1),
                new(cards.Select(ArenaCardIdentifier.Create))), new([]));
        return new(state, new ArenaDraftSnapshotAdapter(new(data)).Convert(state));
    }
    private sealed class RatingsHandler(string json) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
    private sealed class Paths(string directory) : IApplicationDataPathProvider
    { public string GetApplicationDataDirectory() => directory; }
    private sealed class Resource : IDisposable
    { public bool Disposed { get; private set; } public void Dispose() => Disposed = true; }
}
