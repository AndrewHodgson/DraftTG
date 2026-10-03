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
    public LimitedCardStatisticsPresentation Statistics { get; init; } = LimitedCardStatisticsPresentation.Missing;
    public CardRecommendation? Recommendation { get; init; }
}

public sealed record DraftedCardViewModel(string Position, string Name);

public sealed class MainWindowViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly IDraftTGRuntimeFactory _runtimeFactory;
    private readonly IUiDispatcher _dispatcher;
    private readonly CancellationTokenSource _lifecycleCancellation = new();
    private CardCatalog? _catalog;
    private DraftSnapshot? _currentSnapshot;
    private string _statisticsStatusText = string.Empty;
    private string _statisticsCoverageText = string.Empty;
    private string _recommendationStatusText = string.Empty;
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
    public ObservableCollection<DraftedCardViewModel> DraftedCards { get; } = [];

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
        try
        {
            await foreach (var update in runtime.Coordinator
                .RunAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                await _dispatcher.InvokeAsync(() =>
                {
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
        if (!ReferenceEquals(update.Snapshot, _currentSnapshot)) return;
        StatisticsStatusText = update.StatusText;
        StatisticsDiagnosticText = update.Diagnostic ?? string.Empty;
        StatisticsCoverageText = update.CoverageText;
        if (_currentSnapshot is null) return;
        var topName = update.Recommendation?.TopRecommendedPackIndex is { } top
            ? CurrentPackCards[top].Name : null;
        RecommendationStatusText = RecommendationPresentation.Summary(update.Recommendation, topName, update.IsLoading);
        for (var index = 0; index < CurrentPackCards.Count; index++)
        {
            var id = _currentSnapshot.CurrentPack.AvailableCardIdentifiers[index];
            CurrentPackCards[index] = CurrentPackCards[index] with
            {
                Statistics = update.Cards.GetValueOrDefault(id) ?? LimitedCardStatisticsPresentation.Missing,
                Recommendation = update.Recommendation?.Cards[index]
            };
        }
        PackPresentationChanged?.Invoke(false);
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
        if (update.Diagnostic is not null)
        {
            DiagnosticText = $"Arena log warning: {update.Diagnostic.Message}";
            return;
        }

        DiagnosticText = string.Empty;
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
        StatisticsStatusText = _statisticsEnabled ? "Stats: loading…" : "Stats: unavailable";
        StatisticsDiagnosticText = string.Empty;
        StatisticsCoverageText = string.Empty;
        RecommendationStatusText = RecommendationPresentation.Summary(null, null, _statisticsEnabled);
        CurrentPackCards.Clear();
        DraftedCards.Clear();
        var missingIdentifiers = new List<string>();

        foreach (var identifier in snapshot.CurrentPack.AvailableCardIdentifiers)
        {
            var card = _catalog?.Find(identifier);
            if (card is null)
            {
                CurrentPackCards.Add(new CurrentPackCardViewModel("Unknown card", "Unknown", "—"));
                missingIdentifiers.Add(identifier.Value);
                continue;
            }
            CurrentPackCards.Add(new CurrentPackCardViewModel(
                card.Name,
                card.Rarity.ToString(),
                FormatColors(card.Colors))
            {
                Statistics = _statisticsEnabled ? LimitedCardStatisticsPresentation.Loading
                    : LimitedCardStatisticsPresentation.Missing
            });
        }

        foreach (var pick in snapshot.History.Picks)
        {
            var card = _catalog?.Find(pick.SelectedCardIdentifier);
            if (card is null) missingIdentifiers.Add(pick.SelectedCardIdentifier.Value);
            DraftedCards.Add(new DraftedCardViewModel(
                $"P{pick.Position.Pack.Value}P{pick.Position.Pick.Value}",
                card?.Name ?? "Unknown card"));
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
        StatisticsStatusText = string.Empty;
        StatisticsDiagnosticText = string.Empty;
        StatisticsCoverageText = string.Empty;
        RecommendationStatusText = string.Empty;
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
