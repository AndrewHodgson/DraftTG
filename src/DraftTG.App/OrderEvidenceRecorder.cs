using Avalonia.Threading;
using DraftTG.Application;
using DraftTG.Data;

namespace DraftTG.App;

/// <summary>
/// Phase 9E.1 observer. After a safe automatic placement is applied, it applies the strict evidence gate, predicts
/// the order from evidence recorded before this pack, appends the observation and updates one rail diagnostic.
/// It never changes placement, badge visibility, identity or matcher thresholds. Work is serialized off the UI thread.
/// </summary>
internal sealed class OrderEvidenceRecorder : IAsyncDisposable
{
    private const int MaximumDetailLines = 12;
    private readonly OverlayViewModel _presentation;
    private readonly Func<(int Width, int Height)?> _clientSize;
    private readonly ArenaDisplayOrderEvidenceService _service;
    private readonly Action<Action> _dispatch;
    private readonly ArenaDraftEvidenceScopeTracker _scopes = new(Guid.NewGuid().ToString("N"));
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<string> _details = [];
    private Task _tail = Task.CompletedTask;
    private string? _scope;
    private bool _disposed;

    public OrderEvidenceRecorder(OverlayViewModel presentation, Func<(int Width, int Height)?> clientSize,
        ArenaDisplayOrderEvidenceService? service = null, Action<Action>? dispatch = null)
    {
        _presentation = presentation; _clientSize = clientSize;
        _service = service ?? CreateDefaultService();
        _dispatch = dispatch ?? (action => { if (Dispatcher.UIThread.CheckAccess()) action(); else Dispatcher.UIThread.Post(action); });
        presentation.AutomaticLocalizationApplied += Applied;
        presentation.Session.PackPresentationChanged += PackChanged;
    }

    internal static ArenaDisplayOrderEvidenceService CreateDefaultService(string? ledgerPath = null, string? databasePath = null) =>
        new(new(ledgerPath is null ? JsonLinesLedgerFile.OrderEvidence(ApplicationDataPathProviderFactory.CreateDefault()) : new(ledgerPath)),
            () => ArenaCardDatabaseLocator.FindNewest(databasePath) is { } path ? new ArenaCardDatabaseReader(path) : null);

    internal Task Completion => _tail;
    /// <summary>Current evaluation (immutable snapshot). Read by the Phase 9E.2A shadow observer at pack arrival.</summary>
    internal ArenaDisplayOrderEvaluation Evaluation => _presentation.Session.CurrentArenaState?.CurrentPack is { } pack
        ? _service.BeforePack(pack.Coordinate.Pack, pack.Coordinate.Pick, pack.CardIdentifiers.Select(c => c.Value).ToArray())
        : _service.Evaluation;

    /// <summary>Called on the UI thread after frame/current-geometry validation. Persistence stays on the evidence queue.</summary>
    internal void RecordVerified(VisualPlacementContext context, RecognitionFrameObservation observation, FixedSlotOrderEvidenceResult verified)
    {
        if (_disposed) return;
        var state = _presentation.Session.CurrentArenaState;
        var pack = state?.CurrentPack;
        var log = pack?.CardIdentifiers.Select(c => c.Value).ToArray();
        if (pack is null || log is null || _scope is null || pack.Coordinate.Pack != context.Pack.Position.Pack.Value
            || pack.Coordinate.Pick != context.Pack.Position.Pick.Value) return;
        var gate = FixedSlotOrderEvidence.Gate(observation.Request, log, verified,
            ReferenceEquals(context, _presentation.PlacementContext) ? context.Generation : -1, _presentation.Session.CurrentPackIdentity,
            true, _presentation.HasConfirmedVisualPlacement || _presentation.HasManualVisualEdits || _presentation.IsCalibrating);
        if (!gate.Accepted) return;
        var winner = verified.Candidates.Single(c => c.Verification.FullyVerifies(log.Length));
        var candidate = new ArenaDisplayOrderCandidate(_scope, state!.EventName, pack.Coordinate.Pack, pack.Coordinate.Pick,
            log, gate, observation.Window.Width, observation.Window.Height, observation.CaptureWidth, observation.CaptureHeight,
            "deterministic-slot-full-verification", DateTimeOffset.UtcNow)
        {
            EvidenceSource = ArenaDisplayOrderObservation.FixedSlotEvidenceSource,
            PredictionBeforeVerification = verified.PredictionBefore, CandidateOrderCount = verified.Candidates.Count,
            MinimumAmbiguityMargin = winner.Verification.Slots.Min(s => s.ExpectedScore - s.CompetingScore)
        };
        Enqueue(() =>
        {
            var outcome = _service.Record(candidate);
            Publish(ArenaDisplayOrderEvidencePresentation.RailLine(outcome), ArenaDisplayOrderEvidencePresentation.DetailLine(outcome));
        });
    }

    public void Start() => Enqueue(() =>
    {
        _service.Initialize();
        var line = ArenaDisplayOrderEvidencePresentation.IdleLine(_service.Evaluation, _service.DatabaseDiagnostic);
        var progress = _service.Progress;
        var skipped = _service.LastLoad?.SkippedLines.Count ?? 0;
        Publish(line, $"Gate 1: {progress.CompleteDrafts}/2 complete drafts · {progress.Observations}/80 observations"
            + (skipped > 0 ? $" · {skipped} invalid ledger line(s) skipped" : ""));
    });

    private void PackChanged(bool newPack)
    {
        if (!newPack || _presentation.Session.CurrentArenaState is not { CurrentPack: { } pack } state) return;
        _scope = _scopes.Observe(state.DraftIdentifier?.Value, state.EventName, pack.Coordinate.Pack, pack.Coordinate.Pick);
    }

    private void Applied(VisualPlacementContext context, CardVisualLocalizationResult result)
    {
        if (_disposed) return;
        // Runs inside placement publication: an evidence failure must never reach the localization path.
        try { Observe(context, result); }
        catch (Exception ex) { _presentation.SetOrderEvidence("Order evidence: not recorded (" + ex.GetType().Name + ")", ex.Message); }
    }

    private void Observe(VisualPlacementContext context, CardVisualLocalizationResult result)
    {
        var session = _presentation.Session;
        var state = session.CurrentArenaState; var pack = state?.CurrentPack;
        var request = new CardVisualLocalizationRequest(context.Pack, session.CurrentPackPresentations, _presentation.Layout)
        { PackGeneration = context.Generation };
        var current = ReferenceEquals(context, _presentation.PlacementContext) ? context.Generation : -1;
        var logOrder = pack?.CardIdentifiers.Select(card => card.Value).ToArray();
        var gate = ArenaDisplayOrderEvidenceGate.Evaluate(request, result, current, session.CurrentPackIdentity, logOrder,
            pack?.Coordinate.Pack ?? 0, pack?.Coordinate.Pick ?? 0,
            _presentation.HasConfirmedVisualPlacement || _presentation.HasManualVisualEdits);
        var coordinate = $"P{context.Pack.Position.Pack.Value}P{context.Pack.Position.Pick.Value}";
        if (!gate.Accepted) { _presentation.SetOrderEvidence($"{coordinate} order evidence: not recorded ({gate.Reason})"); return; }
        if (_clientSize() is not { } size || _scope is null)
        { _presentation.SetOrderEvidence($"{coordinate} order evidence: not recorded (Arena client size or draft scope unknown)"); return; }
        var candidate = new ArenaDisplayOrderCandidate(_scope, state!.EventName, pack!.Coordinate.Pack, pack.Coordinate.Pick, logOrder!,
            gate, size.Width, size.Height, result.CaptureWidth, result.CaptureHeight, result.Method, DateTimeOffset.UtcNow);
        Enqueue(() =>
        {
            var outcome = _service.Record(candidate);
            var detail = ArenaDisplayOrderEvidencePresentation.DetailLine(outcome);
            System.Diagnostics.Trace.WriteLine("Order evidence: " + detail);
            Publish(ArenaDisplayOrderEvidencePresentation.RailLine(outcome), detail);
        });
    }

    private void Publish(string status, string detail) => _dispatch(() =>
    {
        if (_disposed) return;
        _details.Add(detail);
        if (_details.Count > MaximumDetailLines) _details.RemoveAt(0);
        _presentation.SetOrderEvidence(status, string.Join("\n", _details));
    });

    private void Enqueue(Action work)
    {
        if (_disposed) return;
        _tail = _tail.ContinueWith(_ =>
        {
            if (_lifetime.IsCancellationRequested) return;
            try { work(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            { Publish("Order evidence: unavailable (" + ex.GetType().Name + ")", ex.Message); }
        }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _presentation.AutomaticLocalizationApplied -= Applied;
        _presentation.Session.PackPresentationChanged -= PackChanged;
        await _lifetime.CancelAsync();
        await _tail;
        _lifetime.Dispose();
    }
}
