using System.Runtime.CompilerServices;
using DraftTG.Application;
using DraftTG.ArenaIntegration;
using DraftTG.Data;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;

namespace DraftTG.App.Tests;

public sealed partial class MainWindowViewModelTests
{
    [Fact]
    public void InitialStateIsStarting()
    {
        var viewModel = CreateViewModel();

        Assert.Equal("Starting DraftTG…", viewModel.StatusText);
        Assert.Empty(viewModel.CurrentPackCards);
        Assert.Empty(viewModel.DraftedCards);
    }

    [Fact]
    public async Task StartShowsLoadingThenWaitingWhenRuntimeIsReady()
    {
        var source = new ControlledLogSource(waitForCancellation: true);
        var runtime = CreateRuntime(source);
        var completion = new TaskCompletionSource<DraftTGRuntime>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var viewModel = CreateViewModel(new ControlledRuntimeFactory(completion.Task));

        viewModel.Start();
        Assert.Equal("Loading card data…", viewModel.StatusText);

        completion.SetResult(runtime);
        await source.Started.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Waiting for MTG Arena…", viewModel.StatusText);

        await viewModel.DisposeAsync();
    }

    [Fact]
    public void ReadyPremierSnapshotMapsPackHistoryRarityColorsAndPosition()
    {
        var viewModel = ReadyViewModel();

        viewModel.ApplySessionUpdate(ReadyUpdate(
            ArenaDraftMode.Premier,
            [101, 102, 103],
            [(1, 1, 102), (1, 2, 101)],
            pack: 1,
            pick: 3));

        Assert.Equal("Draft active", viewModel.StatusText);
        Assert.Equal("Premier Draft · Best of One", viewModel.ModeText);
        Assert.Equal("P1P3", viewModel.PositionText);
        Assert.Equal(["Alpha", "Beta", "Gamma"],
            viewModel.CurrentPackCards.Select(card => card.Name));
        Assert.Equal(["Common", "Rare", "Uncommon"],
            viewModel.CurrentPackCards.Select(card => card.Rarity));
        Assert.Equal(["W", "UR", "—"],
            viewModel.CurrentPackCards.Select(card => card.Colors));
        Assert.Equal(["P1P1", "P1P2"],
            viewModel.DraftedCards.Select(card => card.Position));
        Assert.Equal(["Beta", "Alpha"],
            viewModel.DraftedCards.Select(card => card.Name));
    }

    [Fact]
    public void ReadyTraditionalSnapshotDisplaysBestOfThree()
    {
        var viewModel = ReadyViewModel();

        viewModel.ApplySessionUpdate(ReadyUpdate(ArenaDraftMode.Traditional, [101], []));

        Assert.Equal("Traditional Draft · Best of Three", viewModel.ModeText);
    }

    [Fact]
    public void RecoveredQuickDraftSnapshotDisplaysP1P3PackAndHistory()
    {
        var viewModel = ReadyViewModel();

        viewModel.ApplySessionUpdate(ReadyUpdate(
            ArenaDraftMode.Quick,
            [101, 102, 103, 101, 102, 103, 101, 102, 103, 101, 102, 103],
            [(1, 1, 102), (1, 2, 103)],
            pack: 1,
            pick: 3));

        Assert.Equal("Quick Draft · Best of One", viewModel.ModeText);
        Assert.Equal("P1P3", viewModel.PositionText);
        Assert.Equal(12, viewModel.CurrentPackCards.Count);
        Assert.Equal(2, viewModel.DraftedCards.Count);
        Assert.Equal(["P1P1", "P1P2"],
            viewModel.DraftedCards.Select(card => card.Position));
    }

    [Fact]
    public void PackOrderingAndDuplicatesArePreserved()
    {
        var viewModel = ReadyViewModel();

        viewModel.ApplySessionUpdate(ReadyUpdate(
            ArenaDraftMode.Premier,
            [103, 101, 103, 102],
            []));

        Assert.Equal(["Gamma", "Alpha", "Gamma", "Beta"],
            viewModel.CurrentPackCards.Select(card => card.Name));
    }

    [Fact]
    public void NoCurrentPackRemovesActionablePackAndKeepsHistory()
    {
        var viewModel = ReadyViewModel();
        viewModel.ApplySessionUpdate(ReadyUpdate(
            ArenaDraftMode.Premier,
            [101, 102],
            [(1, 1, 103)]));

        viewModel.ApplySessionUpdate(Update(State(
            ArenaDraftSessionStatus.Active,
            ArenaDraftMode.Premier,
            currentCards: null,
            picks: [(1, 1, 103)])));

        Assert.Equal("Draft detected — waiting for next pack", viewModel.StatusText);
        Assert.False(viewModel.IsCurrentPackVisible);
        Assert.True(viewModel.IsWaitingForPackVisible);
        Assert.Empty(viewModel.CurrentPackCards);
        Assert.Equal("Gamma", Assert.Single(viewModel.DraftedCards).Name);
    }

    [Fact]
    public void CompletionRemovesActionablePackAndPreservesHistory()
    {
        var viewModel = ReadyViewModel();
        viewModel.ApplySessionUpdate(ReadyUpdate(
            ArenaDraftMode.Premier,
            [101],
            [(1, 1, 102)]));

        viewModel.ApplySessionUpdate(Update(State(
            ArenaDraftSessionStatus.Completed,
            ArenaDraftMode.Premier,
            currentCards: null,
            picks: [(1, 1, 102)])));

        Assert.Equal("Draft complete", viewModel.StatusText);
        Assert.False(viewModel.IsCurrentPackVisible);
        Assert.Empty(viewModel.CurrentPackCards);
        Assert.Equal("Beta", Assert.Single(viewModel.DraftedCards).Name);
    }

    [Fact]
    public void UnsupportedModeHasUserFriendlyStatus()
    {
        var viewModel = ReadyViewModel();

        viewModel.ApplySessionUpdate(Update(State(
            ArenaDraftSessionStatus.Active,
            ArenaDraftMode.PickTwo,
            [101])));

        Assert.Contains("not supported", viewModel.StatusText, StringComparison.Ordinal);
        Assert.False(viewModel.IsCurrentPackVisible);
    }

    [Fact]
    public void UnresolvedArenaCardShowsMismatchAndNumericIdentifier()
    {
        var viewModel = ReadyViewModel();

        viewModel.ApplySessionUpdate(Update(State(
            ArenaDraftSessionStatus.Active,
            ArenaDraftMode.Premier,
            [999])));

        Assert.Equal("Card data mismatch", viewModel.StatusText);
        Assert.Contains("999", viewModel.DiagnosticText, StringComparison.Ordinal);
        Assert.False(viewModel.IsCurrentPackVisible);
    }

    [Fact]
    public void AmbiguousArenaCardShowsDiagnosticWithoutGuessAndLaterUpdatesContinue()
    {
        var viewModel = ReadyViewModel();
        var state = State(
            ArenaDraftSessionStatus.Active,
            ArenaDraftMode.Premier,
            [999]);
        var ambiguousAdapter = new ArenaDraftSnapshotAdapter(
            new ArenaCardResolver(AmbiguousCatalogData()));

        viewModel.ApplySessionUpdate(Update(state, ambiguousAdapter));

        Assert.Equal("Card data mismatch", viewModel.StatusText);
        Assert.Equal("Ambiguous card mapping: Arena ID 999", viewModel.DiagnosticText);
        Assert.Empty(viewModel.CurrentPackCards);
        Assert.False(viewModel.IsCurrentPackVisible);

        viewModel.ApplySessionUpdate(ReadyUpdate(ArenaDraftMode.Premier, [101], []));
        Assert.Equal("Draft active", viewModel.StatusText);
        Assert.Equal("Alpha", Assert.Single(viewModel.CurrentPackCards).Name);
    }

    [Fact]
    public void ParseWarningDoesNotReplaceValidDraftDisplay()
    {
        var viewModel = ReadyViewModel();
        var ready = ReadyUpdate(ArenaDraftMode.Premier, [101, 102], []);
        viewModel.ApplySessionUpdate(ready);

        viewModel.ApplySessionUpdate(ready with
        {
            Diagnostic = new DraftSessionDiagnostic(
                DraftSessionDiagnosticKind.ParseError,
                "Malformed draft record.")
        });

        Assert.Equal("Draft active", viewModel.StatusText);
        Assert.Equal(["Alpha", "Beta"], viewModel.CurrentPackCards.Select(card => card.Name));
        Assert.Contains("Arena log warning", viewModel.DiagnosticText, StringComparison.Ordinal);
    }

    [Fact]
    public void CachedRefreshFailureIsNonfatal()
    {
        var viewModel = CreateViewModel();

        viewModel.ApplyRuntime(CreateRuntime(
            new ControlledLogSource(),
            DraftTGCardDataStatus.CacheAfterRefreshFailure,
            "Network unavailable."));

        Assert.Equal("Waiting for MTG Arena…", viewModel.StatusText);
        Assert.True(viewModel.HasCardDataWarning);
        Assert.Contains("Using cached card data", viewModel.CardDataWarningText,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task FatalSourceErrorHasReadableStatus()
    {
        var source = new ControlledLogSource(
            error: new ArenaLogSourceException("Player.log cannot be read."));
        var viewModel = CreateViewModel(new ImmediateRuntimeFactory(CreateRuntime(source)));

        viewModel.Start();
        await WaitUntilAsync(() => viewModel.StatusText == "Unable to monitor MTG Arena log");

        Assert.Equal("Player.log cannot be read.", viewModel.DiagnosticText);
        await viewModel.DisposeAsync();
    }

    [Fact]
    public void MissingCatalogCardUsesPlaceholderAndDiagnostic()
    {
        var viewModel = CreateViewModel();
        viewModel.ApplyRuntime(CreateRuntime(new ControlledLogSource(), catalog: new CardCatalog()));

        viewModel.ApplySessionUpdate(ReadyUpdate(ArenaDraftMode.Premier, [101], []));

        Assert.Equal("Unknown card", Assert.Single(viewModel.CurrentPackCards).Name);
        Assert.Contains("domain-a", viewModel.DiagnosticText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisposeCancelsRuntimeConsumptionAndWaitsForSource()
    {
        var source = new ControlledLogSource(waitForCancellation: true);
        var viewModel = CreateViewModel(new ImmediateRuntimeFactory(CreateRuntime(source)));
        viewModel.Start();
        await source.Started.WaitAsync(TimeSpan.FromSeconds(5));

        await viewModel.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(source.CancellationObserved);
    }

    [Fact]
    public async Task BootstrapFailureDoesNotEscapeBackgroundTask()
    {
        var viewModel = CreateViewModel(new FailingRuntimeFactory(
            new InvalidOperationException("No usable catalog.")));

        viewModel.Start();
        await WaitUntilAsync(() => viewModel.StatusText == "Unable to load card data");

        Assert.Equal("No usable catalog.", viewModel.DiagnosticText);
        await viewModel.DisposeAsync();
    }

    [Fact]
    public void StatisticsFormatFractionsAlsaAndSampleCount()
    {
        var presentation = LimitedCardStatisticsPresentation.From(new(CardIdentifier.Create("a"),
            GameInHandGameCount: 4820, GameInHandWinRate: 0.5874, AverageLastSeenAt: 6.24));
        Assert.Equal("58.7%", presentation.GameInHand);
        Assert.Equal("6.24", presentation.AverageLastSeen);
        Assert.Equal("n=4,820", presentation.SampleCount);
        Assert.False(presentation.IsLowSample);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(20, true)]
    [InlineData(499, true)]
    [InlineData(500, false)]
    [InlineData(20000, false)]
    [InlineData(null, false)]
    public void LowSampleIsPresentationOnly(int? count, bool expected)
    {
        var statistics = new LimitedCardStatistics(CardIdentifier.Create("a"), GameInHandGameCount: count, GameInHandWinRate: 0.6);
        var presentation = LimitedCardStatisticsPresentation.From(statistics);
        Assert.Equal(expected, presentation.IsLowSample);
        Assert.Equal("60.0%", presentation.GameInHand);
        Assert.Equal(count, statistics.GameInHandGameCount);
    }

    [Fact]
    public void MissingMetricsShowDashAndUnknownSample()
    {
        var presentation = LimitedCardStatisticsPresentation.From(null);
        Assert.Equal("—", presentation.GameInHand);
        Assert.Equal("—", presentation.AverageLastSeen);
        Assert.Empty(presentation.SampleCount);
        Assert.False(presentation.IsLowSample);
    }

    [Fact]
    public void StatisticsArriveAsynchronouslyWithoutReorderingOrLosingDuplicateCards()
    {
        var viewModel = ReadyViewModel();
        var update = ReadyUpdate(ArenaDraftMode.Quick, [103, 101, 103, 102], [(1, 1, 102)]);
        viewModel.ApplySessionUpdate(update);
        var snapshot = update.SnapshotResult.Snapshot!;
        Assert.All(viewModel.CurrentPackCards, row => Assert.Equal("—", row.Statistics.GameInHand));
        viewModel.ApplyStatisticsUpdate(new(snapshot, true, null));
        Assert.All(viewModel.CurrentPackCards, row => Assert.Equal("…", row.Statistics.GameInHand));
        var context = new LimitedStatisticsContext("TST", LimitedStatisticsFormat.QuickDraft);
        var result = new LimitedStatisticsLoadResult(context, context,
            new([new(CardIdentifier.Create("domain-a"), GameInHandGameCount: 20, GameInHandWinRate: 0.9),
                 new(CardIdentifier.Create("domain-c"), GameInHandGameCount: 20000, GameInHandWinRate: 0.5)]),
            LimitedStatisticsSource.Live, DateTimeOffset.UtcNow, null);
        viewModel.ApplyStatisticsUpdate(new(snapshot, false, result));
        Assert.Equal(["Gamma", "Alpha", "Gamma", "Beta"], viewModel.CurrentPackCards.Select(row => row.Name));
        Assert.Equal(["50.0%", "90.0%", "50.0%", "—"], viewModel.CurrentPackCards.Select(row => row.Statistics.GameInHand));
        Assert.True(viewModel.CurrentPackCards[1].Statistics.IsLowSample);
        Assert.Equal("Beta", Assert.Single(viewModel.DraftedCards).Name);
    }

    [Fact]
    public void PremierFallbackShowsActualSourceAndStaleCache()
    {
        var viewModel = ReadyViewModel();
        var update = ReadyUpdate(ArenaDraftMode.Quick, [101], []);
        viewModel.ApplySessionUpdate(update);
        var context = new LimitedStatisticsContext("TST", LimitedStatisticsFormat.QuickDraft);
        viewModel.ApplyStatisticsUpdate(new(update.SnapshotResult.Snapshot, false,
            new(context, context with { Format = LimitedStatisticsFormat.PremierDraft }, new(),
                LimitedStatisticsSource.StaleCache, DateTimeOffset.UtcNow, "Refresh unavailable.")));
        Assert.Equal("Stats: Premier Draft (fallback) · stale cache", viewModel.StatisticsStatusText);
        Assert.Equal("Refresh unavailable.", viewModel.StatisticsDiagnosticText);
        Assert.Equal("Draft active", viewModel.StatusText);
    }

    [Fact]
    public void StatisticsFailurePreservesNamesHistoryAndTracking()
    {
        var viewModel = ReadyViewModel();
        var update = ReadyUpdate(ArenaDraftMode.Quick, [101], [(1, 1, 102)]);
        viewModel.ApplySessionUpdate(update);
        viewModel.ApplyStatisticsUpdate(new(update.SnapshotResult.Snapshot, false, null, "Provider unavailable."));
        Assert.Equal("Stats: unavailable", viewModel.StatisticsStatusText);
        Assert.Equal("Alpha", Assert.Single(viewModel.CurrentPackCards).Name);
        Assert.Equal("Beta", Assert.Single(viewModel.DraftedCards).Name);
        Assert.Equal("Draft active", viewModel.StatusText);
        viewModel.ApplySessionUpdate(ReadyUpdate(ArenaDraftMode.Quick, [103], [(1, 1, 102)]));
        Assert.Equal("Gamma", Assert.Single(viewModel.CurrentPackCards).Name);
    }

    [Fact]
    public void QueuedStatisticsForAnOldPackAreIgnored()
    {
        var viewModel = ReadyViewModel();
        var old = ReadyUpdate(ArenaDraftMode.Quick, [101], []);
        viewModel.ApplySessionUpdate(old);
        viewModel.ApplySessionUpdate(ReadyUpdate(ArenaDraftMode.Quick, [102], []));
        viewModel.ApplyStatisticsUpdate(new(old.SnapshotResult.Snapshot, true, null));
        Assert.Equal("Beta", Assert.Single(viewModel.CurrentPackCards).Name);
        Assert.Equal("—", viewModel.CurrentPackCards[0].Statistics.GameInHand);
    }

    [Fact]
    public async Task RuntimeStreamsStatsAfterPackAndDisposesPendingHttpWork()
    {
        var client = new ControlledRatingsClient();
        var source = new StatisticsLogSource();
        var runtime = CreateRuntime(source) with
        {
            Statistics = new LimitedStatisticsCoordinator(new LimitedStatisticsService(client, CatalogData().Catalog))
        };
        var viewModel = CreateViewModel(new ImmediateRuntimeFactory(runtime));
        viewModel.Start();
        await client.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitUntilAsync(() => viewModel.CurrentPackCards.Count == 2);
        Assert.Equal("Draft active", viewModel.StatusText);
        Assert.Equal(["Alpha", "Beta"], viewModel.CurrentPackCards.Select(row => row.Name));
        client.Release.SetResult(new("TST", SeventeenLandsFormat.QuickDraft,
            [new("Alpha", GameInHandGameCount: 4820, GameInHandWinRate: 0.5874, AverageLastSeenAt: 6.24)],
            SeventeenLandsSource.Live, DateTimeOffset.UtcNow));
        await WaitUntilAsync(() => viewModel.CurrentPackCards[0].Statistics.GameInHand == "58.7%");
        Assert.Equal("—", viewModel.CurrentPackCards[1].Statistics.GameInHand);
        Assert.Equal("Stats: Quick Draft", viewModel.StatisticsStatusText);
        await viewModel.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, client.Calls);
    }

    [Fact]
    public async Task ClosingOverlayCancelsPendingStatisticsNormally()
    {
        var client = new ControlledRatingsClient();
        var runtime = CreateRuntime(new StatisticsLogSource()) with
        {
            Statistics = new LimitedStatisticsCoordinator(new LimitedStatisticsService(client, CatalogData().Catalog))
        };
        var viewModel = CreateViewModel(new ImmediateRuntimeFactory(runtime));
        viewModel.Start();
        await client.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitUntilAsync(() => viewModel.CurrentPackCards.Count == 2);
        await viewModel.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(client.CancellationObserved);
        Assert.Equal("Draft active", viewModel.StatusText);
        Assert.Empty(viewModel.DiagnosticText);
    }

    private sealed class ControlledRatingsClient : ISeventeenLandsCardRatingsClient
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<SeventeenLandsRatingsResult> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool CancellationObserved { get; private set; }
        public int Calls { get; private set; }
        public async Task<SeventeenLandsRatingsResult> LoadAsync(string expansion, SeventeenLandsFormat format,
            bool forceRefresh = false, CancellationToken cancellationToken = default)
        {
            Calls++;
            Started.TrySetResult();
            try { return await Release.Task.WaitAsync(cancellationToken); }
            catch (OperationCanceledException) { CancellationObserved = true; throw; }
        }
    }

    private sealed class StatisticsLogSource : IArenaLogSource
    {
        public async IAsyncEnumerable<ArenaLogSourceEvent> ReadEventsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ArenaLogSourceEvent.Line(
                """{"method":"BotDraftDraftStatus","response":{"EventName":"QuickDraft_TST","DraftId":"stats-test","DraftPack":[101,102],"PackNumber":0,"PickNumber":0,"DraftStatus":"Drafting"}}""");
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LiveMappingShowsOnlyConciseCurrentCardDiagnostics(bool missingCurrentRow)
    {
        var baseCatalog = CatalogData().Catalog;
        var unrelated = Enumerable.Range(0, 50).SelectMany(i => new[]
        {
            new Card(CardIdentifier.Create($"unrelated-{i}-a"), $"Unrelated {i}", ColorSet.Colorless,
                CardRarity.Common, CardSetCode.Create("TST"), CollectorNumber.Create("1")),
            new Card(CardIdentifier.Create($"unrelated-{i}-b"), $"Unrelated {i}", ColorSet.Colorless,
                CardRarity.Common, CardSetCode.Create("TST"), CollectorNumber.Create("2"))
        });
        var catalog = new CardCatalog(baseCatalog.Cards.Concat(unrelated).Concat(baseCatalog.Cards.Select(card =>
            card with { Identifier = CardIdentifier.Create(card.Identifier.Value + "-alternate") })));
        var viewModel = CreateViewModel();
        viewModel.ApplyRuntime(CreateRuntime(new ControlledLogSource(), catalog: catalog));
        var update = ReadyUpdate(ArenaDraftMode.Quick, [103, 101, 103, 102], []);
        viewModel.ApplySessionUpdate(update);
        var context = new LimitedStatisticsContext("TST", LimitedStatisticsFormat.QuickDraft);
        var rows = Enumerable.Range(0, 50).Select(i => new SeventeenLandsRating($"Unrelated {i}"))
            .Concat(baseCatalog.Cards.Where(card => !missingCurrentRow || card.Name != "Beta")
                .Select(card => new SeventeenLandsRating(card.Name, GameInHandGameCount: 4820,
                    GameInHandWinRate: 0.5874, AverageLastSeenAt: 6.24)));
        var mapping = LimitedStatisticsMapper.Map(rows, catalog, context, update.SnapshotResult.Snapshot);
        viewModel.ApplyStatisticsUpdate(new(update.SnapshotResult.Snapshot, false,
            new(context, context, mapping.Catalog, LimitedStatisticsSource.Cache, DateTimeOffset.UtcNow, mapping.Diagnostic)));
        Assert.Equal(["Gamma", "Alpha", "Gamma", "Beta"], viewModel.CurrentPackCards.Select(card => card.Name));
        Assert.Equal("Stats: Quick Draft", viewModel.StatisticsStatusText);
        Assert.Equal(missingCurrentRow ? "1 current-pack card has no statistics." : "", viewModel.StatisticsDiagnosticText);
        Assert.All(viewModel.CurrentPackCards.Take(3), card =>
        {
            Assert.Equal("58.7%", card.Statistics.GameInHand);
            Assert.Equal("6.24", card.Statistics.AverageLastSeen);
            Assert.Equal("n=4,820", card.Statistics.SampleCount);
        });
        Assert.Equal(missingCurrentRow ? "—" : "58.7%", viewModel.CurrentPackCards[3].Statistics.GameInHand);
        Assert.Equal(missingCurrentRow ? "—" : "6.24", viewModel.CurrentPackCards[3].Statistics.AverageLastSeen);
    }

    private static MainWindowViewModel ReadyViewModel()
    {
        var viewModel = CreateViewModel();
        viewModel.ApplyRuntime(CreateRuntime(new ControlledLogSource()));
        return viewModel;
    }

    private static MainWindowViewModel CreateViewModel(IDraftTGRuntimeFactory? factory = null) =>
        new(factory ?? new ImmediateRuntimeFactory(CreateRuntime(new ControlledLogSource())),
            new InlineDispatcher());

    private static DraftTGRuntime CreateRuntime(
        IArenaLogSource source,
        DraftTGCardDataStatus status = DraftTGCardDataStatus.Cache,
        string? diagnostic = null,
        CardCatalog? catalog = null) =>
        new(
            new DraftSessionCoordinator(
                source,
                new ArenaDraftLogParser(),
                new ArenaDraftStateEngine(),
                Adapter()),
            catalog ?? CatalogData().Catalog,
            status,
            DateTimeOffset.Parse("2026-09-01T00:00:00Z"),
            diagnostic);

    private static DraftSessionUpdate ReadyUpdate(
        ArenaDraftMode mode,
        int[] currentCards,
        (int Pack, int Pick, int Card)[] picks,
        int pack = 1,
        int pick = 1) =>
        Update(State(
            ArenaDraftSessionStatus.Active,
            mode,
            currentCards,
            picks,
            pack,
            pick));

    private static DraftSessionUpdate Update(
        ArenaDraftStateSnapshot state,
        ArenaDraftSnapshotAdapter? adapter = null) =>
        new(state, (adapter ?? Adapter()).Convert(state));

    private static ArenaDraftStateSnapshot State(
        ArenaDraftSessionStatus status,
        ArenaDraftMode mode,
        int[]? currentCards,
        (int Pack, int Pick, int Card)[]? picks = null,
        int pack = 1,
        int pick = 1) =>
        new(
            status,
            ArenaDraftIdentifier.Create("draft-1"),
            mode,
            "Draft_TST",
            currentCards is null
                ? null
                : new ArenaDraftPackState(
                    ArenaDraftIdentifier.Create("draft-1"),
                    ArenaDraftCoordinate.Create(pack, pick),
                    new ArenaCardIdentifierList(currentCards.Select(ArenaCardIdentifier.Create))),
            new ArenaDraftPickRecordList((picks ?? []).Select(item =>
                new ArenaDraftPickRecord(
                    ArenaDraftIdentifier.Create("draft-1"),
                    ArenaDraftCoordinate.Create(item.Pack, item.Pick),
                    new ArenaCardIdentifierList([ArenaCardIdentifier.Create(item.Card)])))));

    private static ArenaDraftSnapshotAdapter Adapter() =>
        new(new ArenaCardResolver(CatalogData()));

    private static ScryfallCardCatalogData CatalogData() =>
        new ScryfallCardCatalogDecoder().DecodeCatalogData("""
            [
              {"id":"domain-a","arena_id":101,"name":"Alpha","colors":["W"],"rarity":"common","set":"tst","collector_number":"1"},
              {"id":"domain-b","arena_id":102,"name":"Beta","colors":["U","R"],"rarity":"rare","set":"tst","collector_number":"2"},
              {"id":"domain-c","arena_id":103,"name":"Gamma","colors":[],"rarity":"uncommon","set":"tst","collector_number":"3"}
            ]
            """);

    private static ScryfallCardCatalogData AmbiguousCatalogData() =>
        new ScryfallCardCatalogDecoder().DecodeCatalogData("""
            [
              {"id":"ambiguous-a","arena_id":999,"name":"Candidate A","colors":["W"],"rarity":"common","set":"one","collector_number":"1"},
              {"id":"ambiguous-b","arena_id":999,"name":"Candidate B","colors":["U"],"rarity":"common","set":"two","collector_number":"2"}
            ]
            """);

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!predicate())
        {
            if (DateTime.UtcNow >= timeout) throw new TimeoutException();
            await Task.Yield();
        }
    }

    private sealed class InlineDispatcher : IUiDispatcher
    {
        public Task InvokeAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }
    }

    private sealed class ImmediateRuntimeFactory(DraftTGRuntime runtime) : IDraftTGRuntimeFactory
    {
        public Task<DraftTGRuntime> CreateAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(runtime);
    }

    private sealed class ControlledRuntimeFactory(Task<DraftTGRuntime> runtimeTask)
        : IDraftTGRuntimeFactory
    {
        public Task<DraftTGRuntime> CreateAsync(CancellationToken cancellationToken = default) =>
            runtimeTask.WaitAsync(cancellationToken);
    }

    private sealed class FailingRuntimeFactory(Exception error) : IDraftTGRuntimeFactory
    {
        public Task<DraftTGRuntime> CreateAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<DraftTGRuntime>(error);
    }

    private sealed class ControlledLogSource(
        bool waitForCancellation = false,
        Exception? error = null) : IArenaLogSource
    {
        private readonly TaskCompletionSource _started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;
        public bool CancellationObserved { get; private set; }

        public async IAsyncEnumerable<ArenaLogSourceEvent> ReadEventsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _started.TrySetResult();
            if (error is not null) throw error;
            if (!waitForCancellation) yield break;

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CancellationObserved = true;
                throw;
            }
            yield break;
        }
    }
}
