using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using DraftTG.Application;
using DraftTG.ArenaIntegration;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;

namespace DraftTG.App;

public sealed record CurrentPackCardViewModel(string Name, string Rarity, string Colors)
{
    public CurrentPackCardPresentation? Presentation { get; init; }
    public CardOccurrenceKey? OccurrenceKey => Presentation?.Key;
    public static CurrentPackCardViewModel From(CurrentPackCardPresentation occurrence, string colors) => new(
        occurrence.CardName, occurrence.Card?.Rarity.ToString() ?? "Unknown", colors)
    {
        Presentation = occurrence
    };
    // Identity-bearing rows read every value from their occurrence. The init setters support
    // standalone formatter callers that have no active pack; they cannot override an occurrence.
    private LimitedCardStatisticsPresentation _statistics = LimitedCardStatisticsPresentation.Missing;
    private CardRecommendation? _recommendation;
    private ContextualCardRecommendation? _contextualRecommendation;
    private LaneCardRecommendation? _laneRecommendation;
    public LimitedCardStatisticsPresentation Statistics
    { get => Presentation is { } p ? p.Statistics : _statistics; init => _statistics = value; }
    public CardRecommendation? Recommendation
    { get => Presentation is { } p ? p.Statistical : _recommendation; init => _recommendation = value; }
    public ContextualCardRecommendation? ContextualRecommendation
    { get => Presentation is { } p ? p.Pool : _contextualRecommendation; init => _contextualRecommendation = value; }
    public LaneCardRecommendation? LaneRecommendation
    { get => Presentation is { } p ? p.Lane : _laneRecommendation; init => _laneRecommendation = value; }
}

public sealed record DraftedCardViewModel(string Position, string Name);

public sealed class MainWindowViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly IDraftTGRuntimeFactory _runtimeFactory;
    private readonly IUiDispatcher _dispatcher;
    private readonly CancellationTokenSource _lifecycleCancellation = new();
    private CardCatalog? _catalog;
    private DraftSnapshot? _currentSnapshot;
    private bool _draftCompleted;
    private ArenaDraftIdentifier? _deckDraftId;
    private string? _deckEventName;
    private long _deckBuildGeneration;
    private bool _deckLoading;
    private SuggestedDeckId? _retainedSuggestedDeckId;
    private string? _retainedSuggestedSessionIdentity;
    private string _statisticsStatusText = string.Empty;
    private string _statisticsCoverageText = string.Empty;
    private string _recommendationStatusText = string.Empty;
    private string _contextualRecommendationStatusText = string.Empty;
    private string _contextualRecommendationDiagnosticsText = string.Empty;
    private string _statisticalPickStatusText = string.Empty;
    private string _laneRecommendationStatusText = string.Empty;
    private string _finalRecommendationStatusText = string.Empty;
    private string _archetypeStatusText = string.Empty;
    private string _statisticsDiagnosticText = string.Empty;
    private bool _statisticsEnabled;
    private Task? _runTask;
    private string _statusText = "Starting DraftTG…";
    private string _modeText = string.Empty;
    private string _positionText = string.Empty;
    private string _packAreaText = string.Empty;
    private string _diagnosticText = string.Empty;
    private string _cardDataWarningText = string.Empty;
    private bool _isCurrentPackVisible;
    private bool _isWaitingForPackVisible;
    private bool _isDraftContentVisible;
    private bool _isDisposed;

    public MainWindowViewModel(
        IDraftTGRuntimeFactory runtimeFactory,
        IUiDispatcher dispatcher)
    {
        _runtimeFactory = runtimeFactory ?? throw new ArgumentNullException(nameof(runtimeFactory));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    // Emitted after a whole pack presentation is ready; statistics updates retain slot objects.
    public event Action<bool>? PackPresentationChanged;

    public ObservableCollection<CurrentPackCardViewModel> CurrentPackCards { get; } = [];
    /// <summary>The immutable ordered snapshot shared by rail names and passive badge bindings.</summary>
    public IReadOnlyList<CurrentPackCardPresentation> CurrentPackPresentations { get; private set; } = [];
    public DraftPack? CurrentPackIdentity => _currentSnapshot?.CurrentPack;
    /// <summary>Arena-native state behind the current pack (GrpIds in log order); Phase 9E.1 order evidence only.</summary>
    internal ArenaDraftStateSnapshot? CurrentArenaState { get; private set; }
    public ObservableCollection<DraftedCardViewModel> DraftedCards { get; } = [];
    public DraftPoolSnapshot? DraftPool { get; private set; }
    public DraftPoolAnalysis? PoolAnalysis { get; private set; }
    public bool HasDraftPool => DraftPool is not null;
    public string PoolSummaryText { get; private set; } = string.Empty;
    public string PoolEntryDiagnosticsText => DraftPool is { } pool && _catalog is { } catalog
        ? (PoolIdentity is { Total: > 0 } identity ? identity.Text + "\n\n" : "") + DraftPoolPresentation.Entries(pool, catalog) : string.Empty;
    public ArenaCardIdentitySummary? PoolIdentity { get; private set; }
    public DeckBuildResult? BaselineDeckResult { get; private set; }
    public SuggestedDeckSet? SuggestedDeckSet { get; private set; }
    public IReadOnlyList<SuggestedDeck> SuggestedDecks => SuggestedDeckSet?.Builds ?? [];
    public SuggestedDeck? SelectedSuggestedDeck
    {
        get => SuggestedDeckSet?.Selected;
        set
        {
            if (value is null || SuggestedDeckSet is not { } set || value.Id == set.Selected?.Id || !set.Builds.Any(b => b.Id == value.Id)) return;
            SuggestedDeckSet = set.Select(value.Id);
            NotifyDeckPresentation();
        }
    }
    private DeckBuildResult? SelectedDeckResult => SelectedSuggestedDeck is { } selected
        ? new(selected.Availability, selected.Deck, "Selected suggested build") : BaselineDeckResult;
    public bool HasBaselineDeckPanel => _draftCompleted;
    public string BaselineDeckSummaryText => BaselineDeckPresentation.Summary(SelectedDeckResult, _deckLoading)
        + SuggestedDeckPresentation.Coverage(SelectedSuggestedDeck);
    public string BaselineDeckCardsText => BaselineDeckPresentation.Cards(SelectedDeckResult?.Deck, _catalog);
    public string BaselineDeckSideboardText => BaselineDeckPresentation.Sideboard(SelectedDeckResult?.Deck, _catalog);
    public string BaselineDeckDiagnosticsText => BaselineDeckPresentation.Diagnostics(SelectedDeckResult?.Deck, _catalog)
        + SuggestedDeckPresentation.Diagnostics(SuggestedDeckSet);
    public string SuggestedDeckDifferenceText => SuggestedDeckPresentation.Difference(SelectedSuggestedDeck, _catalog);
    public bool HasSuggestedDeckDifference => SelectedSuggestedDeck is { IsRecommended: false };

    public string StatisticsStatusText
    {
        get => _statisticsStatusText;
        private set => SetField(ref _statisticsStatusText, value);
    }

    public string StatisticsCoverageText
    {
        get => _statisticsCoverageText;
        private set => SetField(ref _statisticsCoverageText, value);
    }

    public string RecommendationStatusText
    {
        get => _recommendationStatusText;
        private set => SetField(ref _recommendationStatusText, value);
    }

    public string ContextualRecommendationStatusText
    {
        get => _contextualRecommendationStatusText;
        private set => SetField(ref _contextualRecommendationStatusText, value);
    }

    public string StatisticalPickStatusText
    {
        get => _statisticalPickStatusText;
        private set => SetField(ref _statisticalPickStatusText, value);
    }

    public string LaneRecommendationStatusText
    {
        get => _laneRecommendationStatusText;
        private set => SetField(ref _laneRecommendationStatusText, value);
    }

    public string FinalRecommendationStatusText
    { get => _finalRecommendationStatusText; private set => SetField(ref _finalRecommendationStatusText, value); }
    public string ArchetypeStatusText
    { get => _archetypeStatusText; private set => SetField(ref _archetypeStatusText, value); }

    public string ContextualRecommendationDiagnosticsText
    {
        get => _contextualRecommendationDiagnosticsText;
        private set => SetField(ref _contextualRecommendationDiagnosticsText, value);
    }

    public string StatisticsDiagnosticText
    {
        get => _statisticsDiagnosticText;
        private set => SetField(ref _statisticsDiagnosticText, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public string ModeText
    {
        get => _modeText;
        private set => SetField(ref _modeText, value);
    }

    public string PositionText
    {
        get => _positionText;
        private set => SetField(ref _positionText, value);
    }

    public string PackAreaText
    {
        get => _packAreaText;
        private set => SetField(ref _packAreaText, value);
    }

    public string DiagnosticText
    {
        get => _diagnosticText;
        private set
        {
            if (SetField(ref _diagnosticText, value))
            {
                OnPropertyChanged(nameof(HasDiagnostic));
            }
        }
    }

    public string CardDataWarningText
    {
        get => _cardDataWarningText;
        private set
        {
            if (SetField(ref _cardDataWarningText, value))
            {
                OnPropertyChanged(nameof(HasCardDataWarning));
            }
        }
    }

    public bool IsCurrentPackVisible
    {
        get => _isCurrentPackVisible;
        private set => SetField(ref _isCurrentPackVisible, value);
    }

    public bool IsWaitingForPackVisible
    {
        get => _isWaitingForPackVisible;
        private set => SetField(ref _isWaitingForPackVisible, value);
    }

    public bool IsDraftContentVisible
    {
        get => _isDraftContentVisible;
        private set => SetField(ref _isDraftContentVisible, value);
    }

    public bool HasDiagnostic => !string.IsNullOrEmpty(DiagnosticText);
    public bool HasCardDataWarning => !string.IsNullOrEmpty(CardDataWarningText);
    public bool HasDraftedCards => DraftedCards.Count > 0;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _runTask ??= RunAsync(_lifecycleCancellation.Token);
    }

    public async Task StopAsync()
    {
        if (_isDisposed) return;
        await _lifecycleCancellation.CancelAsync().ConfigureAwait(false);
        if (_runTask is not null)
        {
            await _runTask.ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        await StopAsync().ConfigureAwait(false);
        _lifecycleCancellation.Dispose();
        _isDisposed = true;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        await _dispatcher.InvokeAsync(() => StatusText = "Loading card data…")
            .ConfigureAwait(false);

        DraftTGRuntime runtime;
        try
        {
            runtime = await _runtimeFactory.CreateAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception error)
        {
            await _dispatcher.InvokeAsync(() =>
            {
                StatusText = "Unable to load card data";
                DiagnosticText = error.Message;
            }).ConfigureAwait(false);
            return;
        }

        await _dispatcher.InvokeAsync(() => ApplyRuntime(runtime)).ConfigureAwait(false);

        using var statisticsCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var statisticsTask = runtime.Statistics is { } statistics
            ? ReadStatisticsAsync(statistics, statisticsCancellation.Token) : Task.CompletedTask;
        var decksTask = runtime.Decks is { } decks ? ReadDecksAsync(decks, statisticsCancellation.Token) : Task.CompletedTask;
        try
        {
            await foreach (var update in runtime.Coordinator
                .RunAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                await _dispatcher.InvokeAsync(() =>
                {
                    if (runtime.Decks is { } deckCoordinator) _deckBuildGeneration = deckCoordinator.Observe(update);
                    ApplySessionUpdate(update);
                    runtime.Statistics?.Observe(update);
                })
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            await _dispatcher.InvokeAsync(() =>
            {
                ClearCurrentPack();
                StatusText = "Unable to monitor MTG Arena log";
                DiagnosticText = error.Message;
            }).ConfigureAwait(false);
        }
        finally
        {
            await statisticsCancellation.CancelAsync().ConfigureAwait(false);
            if (runtime.Decks is not null) await runtime.Decks.DisposeAsync().ConfigureAwait(false);
            await decksTask.ConfigureAwait(false);
            if (runtime.Statistics is not null)
                await runtime.Statistics.DisposeAsync().ConfigureAwait(false);
            await statisticsTask.ConfigureAwait(false);
        }
    }

    private async Task ReadStatisticsAsync(LimitedStatisticsCoordinator statistics, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var update in statistics.ReadUpdatesAsync(cancellationToken).ConfigureAwait(false))
                await _dispatcher.InvokeAsync(() => ApplyStatisticsUpdate(update)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    internal void ApplyStatisticsUpdate(LimitedStatisticsUpdate update)
    {
        // A queued UI callback for a previous pack/environment cannot overwrite current rows.
        var snapshot = _currentSnapshot;
        if (!ReferenceEquals(update.Snapshot, snapshot)
            || !Equals(update.PackIdentity, snapshot?.CurrentPack)) return;
        StatisticsStatusText = update.StatusText;
        StatisticsDiagnosticText = update.Diagnostic ?? string.Empty;
        StatisticsCoverageText = update.CoverageText;
        if (snapshot is null || !ReferenceEquals(snapshot, _currentSnapshot)) return;
        var occurrences = snapshot.CurrentPack.AvailableCardIdentifiers
            .Select((id, index) => update.Occurrences[new CardOccurrenceKey(index, id)].WithCard(_catalog?.Find(id)))
            .ToArray();
        if (!ReferenceEquals(snapshot, _currentSnapshot)) return;
        CurrentPackPresentations = Array.AsReadOnly(occurrences);
        var topName = occurrences.FirstOrDefault(c => c.Statistical?.IsTopStatisticalCandidate == true)?.CardName;
        RecommendationStatusText = RecommendationPresentation.Summary(update.Recommendation, topName, update.IsLoading);
        var contextName = occurrences.FirstOrDefault(c => c.Pool?.IsTopContextualCandidate == true)?.CardName;
        ContextualRecommendationStatusText = ContextualRecommendationPresentation.Summary(
            update.ContextualRecommendation, contextName, update.IsLoading);
        var finalName = occurrences.FirstOrDefault(c => c.Lane?.IsTopContextualCandidate == true)?.CardName;
        LaneRecommendationStatusText = LaneRecommendationPresentation.Summary(update.LaneRecommendation, finalName, update.IsLoading);
        var archetypeName = occurrences.FirstOrDefault(c => c.IsContextPick)?.CardName;
        FinalRecommendationStatusText = TrophyRecommendationPresentation.Summary(update.TrophyRecommendation,
            update.TrophyDataStatus, archetypeName, update.IsLoading);
        ArchetypeStatusText = ArchetypeRecommendationPresentation.Archetype(update.ArchetypeRecommendation, update.ArchetypeDataStatus);
        StatisticalPickStatusText = update.Recommendation?.TopRecommendedPackIndex is { } statsTop
            && (update.ArchetypeRecommendation?.TopRecommendedPackIndex ?? update.LaneRecommendation?.TopRecommendedPackIndex) != statsTop ? $"Stats Pick: {topName}" : string.Empty;
        ContextualRecommendationDiagnosticsText = ContextualRecommendationPresentation.Diagnostics(
            update.ContextualRecommendation, update.ObservationHistory)
            + "\n\n" + LaneRecommendationPresentation.Diagnostics(update.LaneRecommendation);
        ContextualRecommendationDiagnosticsText += "\n\n" + ArchetypeRecommendationPresentation.Diagnostics(update.ArchetypeRecommendation, update.ArchetypeDataStatus);
        ContextualRecommendationDiagnosticsText += "\n\n" + TrophyRecommendationPresentation.Diagnostics(update.TrophyRecommendation, update.TrophyDataStatus);
        ContextualRecommendationDiagnosticsText += "\n\nCurrent-pack identity:\n"
            + string.Join("\n\n", occurrences.Select(c => c.DiagnosticText));
        if (occurrences.Any(c => !c.IsIdentityConsistent))
            StatisticsDiagnosticText = "Current-pack statistics identity mismatch; affected occurrence hidden.";
        // Rebuild complete rows in Arena order from immutable occurrences, never merge lists by position.
        var rows = occurrences.Select(c => CurrentPackCardViewModel.From(c,
            c.Card is { } card ? FormatColors(card.Colors) : "—")).ToArray();
        for (var index = 0; index < rows.Length; index++)
            if (index < CurrentPackCards.Count) CurrentPackCards[index] = rows[index];
            else CurrentPackCards.Add(rows[index]);
        while (CurrentPackCards.Count > rows.Length) CurrentPackCards.RemoveAt(CurrentPackCards.Count - 1);
        PackPresentationChanged?.Invoke(false);
    }

    private async Task ReadDecksAsync(DeckConstructionCoordinator decks, CancellationToken token)
    {
        try
        {
            await foreach (var update in decks.ReadUpdatesAsync(token).ConfigureAwait(false))
                await _dispatcher.InvokeAsync(() => ApplyDeckBuildUpdate(update)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    internal void ApplyDeckBuildUpdate(DeckBuildUpdate update)
    {
        if (!_draftCompleted || update.Generation != _deckBuildGeneration || update.DraftIdentifier != _deckDraftId
            || update.EventName != _deckEventName || !Equals(update.Pool, DraftPool)) return;
        BaselineDeckResult = update.Result; _deckLoading = update.IsLoading;
        if (update.Suggestions is { } next)
        {
            var selected = SuggestedDeckSet?.SessionIdentity == next.SessionIdentity ? SelectedSuggestedDeck?.Id
                : _retainedSuggestedSessionIdentity == next.SessionIdentity ? _retainedSuggestedDeckId : null;
            SuggestedDeckSet = next.Select(selected);
            _retainedSuggestedDeckId = null; _retainedSuggestedSessionIdentity = null;
        }
        else if (!update.IsLoading) SuggestedDeckSet = null;
        NotifyDeckPresentation();
    }

    private void NotifyDeckPresentation()
    {
        OnPropertyChanged(nameof(BaselineDeckResult)); OnPropertyChanged(nameof(HasBaselineDeckPanel));
        OnPropertyChanged(nameof(BaselineDeckSummaryText)); OnPropertyChanged(nameof(BaselineDeckCardsText));
        OnPropertyChanged(nameof(BaselineDeckSideboardText)); OnPropertyChanged(nameof(BaselineDeckDiagnosticsText));
        OnPropertyChanged(nameof(SuggestedDeckSet)); OnPropertyChanged(nameof(SuggestedDecks));
        OnPropertyChanged(nameof(SelectedSuggestedDeck)); OnPropertyChanged(nameof(SuggestedDeckDifferenceText));
        OnPropertyChanged(nameof(HasSuggestedDeckDifference));
    }

    internal void ApplyRuntime(DraftTGRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        _catalog = runtime.Catalog;
        _statisticsEnabled = runtime.Statistics is not null;
        StatusText = "Waiting for MTG Arena…";
        CardDataWarningText = runtime.CardDataStatus == DraftTGCardDataStatus.CacheAfterRefreshFailure
            ? "Using cached card data — the latest refresh failed."
            : string.Empty;
        if (runtime.CardDataStatus == DraftTGCardDataStatus.CacheAfterRefreshFailure
            && !string.IsNullOrWhiteSpace(runtime.CardDataDiagnostic))
        {
            CardDataWarningText += $" {runtime.CardDataDiagnostic}";
        }
    }

    internal void ApplySessionUpdate(DraftSessionUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (!update.ArenaState.IsCompleted || _deckDraftId != update.ArenaState.DraftIdentifier || _deckEventName != update.ArenaState.EventName)
        { SuggestedDeckSet = null; _retainedSuggestedDeckId = null; _retainedSuggestedSessionIdentity = null; }
        else if (!Equals(DraftPool, update.SnapshotResult.DraftPool) && SuggestedDeckSet is { } previous)
        {
            // Retain the pair choice, but hide the old inventory proposal while the corrected pool is rebuilt.
            _retainedSuggestedDeckId = previous.Selected?.Id; _retainedSuggestedSessionIdentity = previous.SessionIdentity;
            SuggestedDeckSet = null;
        }
        if (!update.ArenaState.IsCompleted || !Equals(DraftPool, update.SnapshotResult.DraftPool)
            || _deckDraftId != update.ArenaState.DraftIdentifier || _deckEventName != update.ArenaState.EventName)
        { BaselineDeckResult = null; _deckLoading = update.ArenaState.IsCompleted; }
        _draftCompleted = update.ArenaState.IsCompleted;
        _deckDraftId = update.ArenaState.DraftIdentifier; _deckEventName = update.ArenaState.EventName;
        DraftPool = update.SnapshotResult.DraftPool;
        PoolIdentity = update.SnapshotResult.PoolIdentity;
        PoolAnalysis = DraftPool is { } pool ? new DraftPoolAnalyzer().Analyze(pool, _catalog ?? new CardCatalog()) : null;
        PoolSummaryText = PoolAnalysis is { } analysis
            ? DraftPoolPresentation.Summary(analysis, update.ArenaState.IsCompleted) : string.Empty;
        OnPropertyChanged(nameof(DraftPool));
        OnPropertyChanged(nameof(PoolAnalysis));
        OnPropertyChanged(nameof(HasDraftPool));
        OnPropertyChanged(nameof(PoolSummaryText));
        OnPropertyChanged(nameof(PoolEntryDiagnosticsText));
        NotifyDeckPresentation();
        if (update.Diagnostic is not null)
        {
            DiagnosticText = $"Arena log warning: {update.Diagnostic.Message}";
            if (!update.ArenaState.IsCompleted) return;
        }
        else DiagnosticText = string.Empty;
        switch (update.SnapshotResult.Availability)
        {
            case DraftSnapshotAvailability.Ready:
                ApplyReady(update.ArenaState, update.SnapshotResult.Snapshot!);
                break;
            case DraftSnapshotAvailability.Idle:
                ResetDraftPresentation();
                StatusText = "Waiting for MTG Arena…";
                break;
            case DraftSnapshotAvailability.NoCurrentPack:
                ClearCurrentPack();
                IsDraftContentVisible = true;
                IsWaitingForPackVisible = true;
                PackAreaText = "Waiting for next pack…";
                StatusText = "Draft detected — waiting for next pack";
                ModeText = FormatMode(update.ArenaState.Mode, null);
                break;
            case DraftSnapshotAvailability.Completed:
                ClearCurrentPack();
                IsDraftContentVisible = true;
                StatusText = "Draft complete";
                PackAreaText = string.Empty;
                break;
            case DraftSnapshotAvailability.UnsupportedDraftMode:
            case DraftSnapshotAvailability.UnsupportedMultiCardPick:
                ClearCurrentPack();
                IsDraftContentVisible = true;
                StatusText = "Draft detected — recommendations not supported for this format yet";
                ModeText = FormatMode(update.ArenaState.Mode, null);
                break;
            case DraftSnapshotAvailability.UnresolvedArenaCard:
                ClearCurrentPack();
                IsDraftContentVisible = true;
                StatusText = "Card data mismatch";
                DiagnosticText = "Unresolved Arena card IDs: " + string.Join(
                    ", ",
                    update.SnapshotResult.UnresolvedArenaCards.Select(card => card.Value));
                break;
            case DraftSnapshotAvailability.AmbiguousArenaCard:
                ClearCurrentPack();
                IsDraftContentVisible = true;
                StatusText = "Card data mismatch";
                var ambiguousIdentifiers = update.SnapshotResult.AmbiguousArenaCards
                    .Select(card => card.Value)
                    .ToArray();
                DiagnosticText = ambiguousIdentifiers.Length == 1
                    ? $"Ambiguous card mapping: Arena ID {ambiguousIdentifiers[0]}"
                    : "Ambiguous card mappings: Arena IDs "
                        + string.Join(", ", ambiguousIdentifiers);
                if (update.SnapshotResult.UnresolvedArenaCards.Count > 0)
                {
                    DiagnosticText += ". Missing Arena card IDs: " + string.Join(
                        ", ",
                        update.SnapshotResult.UnresolvedArenaCards.Select(card => card.Value));
                }
                break;
            case DraftSnapshotAvailability.UnsupportedCoordinate:
                ClearCurrentPack();
                IsDraftContentVisible = true;
                StatusText = "Draft data uses an unsupported pack or pick position";
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    internal void ApplyFatalSourceError(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        ClearCurrentPack();
        StatusText = "Unable to monitor MTG Arena log";
        DiagnosticText = error.Message;
    }

    private void ApplyReady(ArenaDraftStateSnapshot arenaState, DraftSnapshot snapshot)
    {
        _currentSnapshot = snapshot;
        CurrentArenaState = arenaState;
        StatisticsStatusText = _statisticsEnabled ? "Stats: loading…" : "Stats: unavailable";
        StatisticsDiagnosticText = string.Empty;
        StatisticsCoverageText = string.Empty;
        RecommendationStatusText = RecommendationPresentation.Summary(null, null, _statisticsEnabled);
        ContextualRecommendationStatusText = _statisticsEnabled ? "Context Pick: loading…" : string.Empty;
        LaneRecommendationStatusText = _statisticsEnabled ? "Context Pick: loading…" : string.Empty;
        FinalRecommendationStatusText = LaneRecommendationStatusText;
        ArchetypeStatusText = string.Empty;
        ContextualRecommendationDiagnosticsText = string.Empty;
        StatisticalPickStatusText = string.Empty;
        CurrentPackCards.Clear();
        DraftedCards.Clear();
        var missingIdentifiers = new List<string>();
        var occurrences = new List<CurrentPackCardPresentation>();

        foreach (var (identifier, index) in snapshot.CurrentPack.AvailableCardIdentifiers.Select((id, index) => (id, index)))
        {
            var card = _catalog?.Find(identifier);
            if (card is null) missingIdentifiers.Add(identifier.Value);
            var occurrence = new CurrentPackCardPresentation(new(index, identifier), card, isLoading: _statisticsEnabled);
            occurrences.Add(occurrence);
            CurrentPackCards.Add(CurrentPackCardViewModel.From(occurrence,
                card is null ? "—" : FormatColors(card.Colors)));
        }

        CurrentPackPresentations = Array.AsReadOnly(occurrences.ToArray());

        foreach (var pick in snapshot.History.Picks)
        {
            var card = _catalog?.Find(pick.SelectedCardIdentifier);
            if (card is null) missingIdentifiers.Add(pick.SelectedCardIdentifier.Value);
            DraftedCards.Add(new DraftedCardViewModel(
                $"P{pick.Position.Pack.Value}P{pick.Position.Pick.Value}",
                card?.Name ?? "Unknown card"));
        }

        var exactCounts = snapshot.History.SelectedCardIdentifiers.GroupBy(id => id).ToDictionary(g => g.Key, g => g.Count());
        foreach (var identifier in snapshot.DraftedPool.CardIdentifiers)
        {
            if (exactCounts.GetValueOrDefault(identifier) > 0) { exactCounts[identifier]--; continue; }
            var card = _catalog?.Find(identifier);
            if (card is null) missingIdentifiers.Add(identifier.Value);
            DraftedCards.Add(new DraftedCardViewModel("Position unknown", card?.Name ?? "Unknown card"));
        }

        OnPropertyChanged(nameof(HasDraftedCards));
        StatusText = "Draft active";
        ModeText = FormatMode(arenaState.Mode, snapshot.Format);
        PositionText = $"P{snapshot.CurrentPack.Position.Pack.Value}P{snapshot.CurrentPack.Position.Pick.Value}";
        PackAreaText = "Current pack";
        IsDraftContentVisible = true;
        IsCurrentPackVisible = true;
        IsWaitingForPackVisible = false;

        if (missingIdentifiers.Count > 0)
        {
            DiagnosticText = "Catalog lookup failed for card IDs: "
                + string.Join(", ", missingIdentifiers.Distinct(StringComparer.Ordinal));
        }
        if (arenaState.RecoveredPool is not null && arenaState.UnqualifiedHistoryCardCount > 0)
            DiagnosticText += (DiagnosticText.Length > 0 ? "\n" : "")
                + $"Exact pick history: {arenaState.ExactHistoryCardCount}/{arenaState.DraftedPool.Count} drafted card occurrences; other positions unknown.";
        if (arenaState.PickedCardsDiagnostic is { } warning)
            DiagnosticText += (DiagnosticText.Length > 0 ? "\n" : "") + warning.Message;
        PackPresentationChanged?.Invoke(true);
    }

    private void ResetDraftPresentation()
    {
        ClearCurrentPack();
        DraftedCards.Clear();
        OnPropertyChanged(nameof(HasDraftedCards));
        IsDraftContentVisible = false;
        ModeText = string.Empty;
        PositionText = string.Empty;
        PackAreaText = string.Empty;
    }

    private void ClearCurrentPack()
    {
        _currentSnapshot = null;
        CurrentArenaState = null;
        CurrentPackPresentations = [];
        StatisticsStatusText = string.Empty;
        StatisticsDiagnosticText = string.Empty;
        StatisticsCoverageText = string.Empty;
        RecommendationStatusText = string.Empty;
        ContextualRecommendationStatusText = string.Empty;
        LaneRecommendationStatusText = string.Empty;
        FinalRecommendationStatusText = string.Empty;
        ArchetypeStatusText = string.Empty;
        ContextualRecommendationDiagnosticsText = string.Empty;
        StatisticalPickStatusText = string.Empty;
        CurrentPackCards.Clear();
        IsCurrentPackVisible = false;
        IsWaitingForPackVisible = false;
        PositionText = string.Empty;
        PackPresentationChanged?.Invoke(true);
    }

    private static string FormatMode(ArenaDraftMode? mode, DraftFormat? format)
    {
        var modeName = mode?.Kind switch
        {
            ArenaDraftModeKind.Premier => "Premier Draft",
            ArenaDraftModeKind.Traditional => "Traditional Draft",
            ArenaDraftModeKind.Quick => "Quick Draft",
            ArenaDraftModeKind.PickTwo => "Pick-Two Draft",
            ArenaDraftModeKind.Unknown => "Unknown Draft",
            _ => "Draft"
        };
        var formatName = format switch
        {
            DraftFormat.BestOfOne => "Best of One",
            DraftFormat.BestOfThree => "Best of Three",
            _ => null
        };
        return formatName is null ? modeName : $"{modeName} · {formatName}";
    }

    private static string FormatColors(ColorSet colors)
    {
        if (colors.IsColorless) return "—";
        return string.Concat(colors.Colors.Select(color => color switch
        {
            MagicColor.White => "W",
            MagicColor.Blue => "U",
            MagicColor.Black => "B",
            MagicColor.Red => "R",
            MagicColor.Green => "G",
            _ => string.Empty
        }));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
