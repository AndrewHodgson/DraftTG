using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Threading;
using DraftTG.Application;
using DraftTG.App.Platform;
using DraftTG.Data;
using DraftTG.Domain;
using SkiaSharp;

namespace DraftTG.App;

/// <summary>The synchronized frame the artwork matcher just used, plus the client geometry it was captured from.</summary>
internal sealed record RecognitionFrameObservation(CardVisualLocalizationRequest Request, SKBitmap Frame,
    IReadOnlyDictionary<CardIdentifier, SKBitmap> References, ArenaWindowGeometry Window, NormalizedDraftRegion Anchor)
{
    public int CaptureWidth { get; init; } = Frame.Width;
    public int CaptureHeight { get; init; } = Frame.Height;
    public long CaptureGeneration { get; init; }
    public long CaptureRequestGeneration { get; init; }
    public long FramePackGeneration { get; init; }
    public long Revision { get; init; }
    public DateTimeOffset CapturedAt { get; init; }
    public int FrameX { get; init; }
    public int FrameY { get; init; }
    public nint FrameWindowHandle { get; init; }
    public CardVisualLocalizationResult? Visual { get; init; }
}

/// <summary>Arena's window mode from its native style: a captioned window is "Windowed"; borderless or exclusive is not.</summary>
internal static class ArenaWindowModeProbe
{
    public static ArenaWindowMode Detect(ArenaWindowGeometry window)
    {
        if (!OperatingSystem.IsWindows() || window.Handle == 0) return ArenaWindowMode.Unknown;
        const long caption = 0x00C00000; // WS_CAPTION
        var style = GetWindowLongPtr(window.Handle, -16).ToInt64();
        return style == 0 ? ArenaWindowMode.Unknown : (style & caption) == caption ? ArenaWindowMode.Windowed : ArenaWindowMode.Fullscreen;
    }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
}

/// <summary>
/// Phase 9E.2A shadow observer. At pack arrival it predicts the visual order from Arena-DB evidence recorded BEFORE this
/// pack; after a safe automatic placement it compares the deterministic slot candidate with that placement, runs the
/// experimental fixed-slot verifier on a copy of the matcher's frame, writes one structured record per pack/geometry
/// generation and updates one rail line. It never changes placement, badges, identity, recommendations or thresholds.
/// All work is serialized off the UI thread and exception-isolated.
/// </summary>
internal sealed class DeterministicSlotShadowObserver : IAsyncDisposable
{
    private const int MaximumDetailLines = 12, RetainedPacks = 8;
    private sealed record PredictionEntry(VisualPlacementContext Context, IReadOnlyList<int> LogOrder, ArenaDisplayOrderPrediction Prediction,
        int Classes, ArenaCardDatabaseInfo? Database, string DraftScope);
    private sealed record VerificationEntry(int ClientWidth, int ClientHeight, string Summary, int Accepted, int Count, double Milliseconds, string? Error,
        int VerifiedCandidateOrders = 0, string? FallbackReason = null, long CaptureGeneration = 0, long CaptureRequestGeneration = 0,
        IReadOnlyList<int>? VerifiedOrder = null, bool GeometryAvailable = false, bool EvidenceEligible = false);

    private readonly OverlayViewModel _presentation;
    private readonly Func<ArenaWindowGeometry?> _window;
    private readonly Func<NormalizedDraftRegion?> _anchor;
    private readonly Func<ArenaWindowGeometry, ArenaWindowMode> _mode;
    private readonly Func<ArenaDisplayOrderEvaluation> _evaluation;
    private readonly Func<ArenaCardDatabaseReader?> _database;
    private readonly JsonLinesLedgerFile? _log;
    private readonly Action<Action> _dispatch;
    private readonly Func<RecognitionFrameObservation, bool> _isCurrentFrame;
    private readonly Action<VisualPlacementContext, RecognitionFrameObservation, FixedSlotOrderEvidenceResult>? _recordEvidence;
    private readonly HashSet<long> _framed = [];
    private readonly ArenaDraftEvidenceScopeTracker _scopes = new(Guid.NewGuid().ToString("N"));
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _gate = new();
    private readonly Dictionary<long, PredictionEntry> _predictions = [];
    private readonly Dictionary<long, VerificationEntry> _verifications = [];
    private readonly HashSet<(long Generation, int Width, int Height, long CaptureGeneration, long CaptureRequest)> _recorded = [];
    private readonly List<string> _details = [];
    private Task _tail = Task.CompletedTask;
    private bool _disposed;

    public DeterministicSlotShadowObserver(OverlayViewModel presentation, Func<ArenaWindowGeometry?> window, Func<NormalizedDraftRegion?> anchor,
        Func<ArenaDisplayOrderEvaluation> evaluation, Func<ArenaCardDatabaseReader?>? database = null,
        Func<ArenaWindowGeometry, ArenaWindowMode>? mode = null, JsonLinesLedgerFile? log = null, Action<Action>? dispatch = null,
        Func<RecognitionFrameObservation, bool>? isCurrentFrame = null,
        Action<VisualPlacementContext, RecognitionFrameObservation, FixedSlotOrderEvidenceResult>? recordEvidence = null)
    {
        _presentation = presentation; _window = window; _anchor = anchor; _evaluation = evaluation;
        _database = database ?? (() => ArenaCardDatabaseLocator.FindNewest(null) is { } path ? new ArenaCardDatabaseReader(path) : null);
        _mode = mode ?? ArenaWindowModeProbe.Detect;
        _log = log;
        _isCurrentFrame = isCurrentFrame ?? (_ => false); // No live freshness contract means diagnostics only.
        _recordEvidence = recordEvidence;
        _dispatch = dispatch ?? (action => { if (Dispatcher.UIThread.CheckAccess()) action(); else Dispatcher.UIThread.Post(action); });
        presentation.PropertyChanged += PresentationChanged;
        presentation.AutomaticLocalizationApplied += Applied;
    }

    internal static JsonLinesLedgerFile DefaultLog() => new(Path.Combine(
        ApplicationDataPathProviderFactory.CreateDefault().GetApplicationDataDirectory(), "localization", "deterministic-shadow.jsonl"));

    internal Task Completion => _tail;

    private void PresentationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_disposed || e.PropertyName != nameof(OverlayViewModel.PlacementContext) || _presentation.PlacementContext is not { } context) return;
        try { PackArrived(context); }
        catch (Exception ex) { _presentation.SetDeterministicSlots("Deterministic slots: unavailable (" + ex.GetType().Name + ")", ex.Message); }
    }

    private void PackArrived(VisualPlacementContext context)
    {
        var arena = _presentation.Session.CurrentArenaState?.CurrentPack;
        var count = context.Pack.AvailableCardIdentifiers.Count;
        var log = arena?.CardIdentifiers.Select(card => card.Value).ToArray();
        var matches = arena is not null && log!.Length == count && arena.Coordinate.Pack == context.Pack.Position.Pack.Value
            && arena.Coordinate.Pick == context.Pack.Position.Pick.Value;
        // Snapshot the evidence now: the 9E.1 recorder may learn this very pack after its placement is applied.
        var evaluation = _evaluation();
        var state = _presentation.Session.CurrentArenaState;
        var scope = ArenaDisplayOrderObservation.ScopeDigest(_scopes.Observe(state?.DraftIdentifier?.Value, state?.EventName,
            context.Pack.Position.Pack.Value, context.Pack.Position.Pick.Value));
        Enqueue(() =>
        {
            ArenaDisplayOrderPrediction prediction; ArenaCardDatabaseInfo? database = null;
            if (!matches) prediction = ArenaDisplayOrderPrediction.Unavailable("Arena log pack does not match the current pack.", evaluation.Survivors.Count);
            else if (evaluation.IsFamilyRefuted) prediction = ArenaDisplayOrderPrediction.Unavailable("hypothesis family refuted");
            else if (_database() is not { } reader) prediction = ArenaDisplayOrderPrediction.Unavailable("Arena card database not found.", evaluation.Survivors.Count);
            else
            {
                var lookup = reader.ReadSortKeys(log!);
                database = lookup.Database;
                prediction = lookup.Status != ArenaCardDatabaseStatus.Available || lookup.MissingGrpIds.Count > 0
                    ? ArenaDisplayOrderPrediction.Unavailable(lookup.Diagnostic ?? "Arena card database unavailable.", evaluation.Survivors.Count)
                    : ArenaDisplayOrderModel.Predict(log!, lookup.Keys, evaluation.Survivors);
            }
            lock (_gate)
            {
                _predictions[context.Generation] = new(context, log ?? [], prediction, evaluation.Classes.Count, database, scope);
                foreach (var stale in _predictions.Keys.Where(g => g <= context.Generation - RetainedPacks).ToArray())
                { _predictions.Remove(stale); _verifications.Remove(stale); _framed.Remove(stale); }
                _recorded.RemoveWhere(r => r.Generation <= context.Generation - RetainedPacks);
            }
            var window = _window();
            var candidate = Locate(context, log ?? [], prediction, window);
            Publish(DeterministicSlotPresentation.PredictionLine(Coordinate(context.Pack), candidate), null, context.Generation);
        });
    }

    /// <summary>Called after synchronized matching. Copies the frame; verification runs on the serialized queue.</summary>
    public void ObserveFrame(RecognitionFrameObservation observation)
    {
        if (_disposed) return;
        SKBitmap? frame = null;
        var references = new Dictionary<CardIdentifier, SKBitmap>();
        try
        {
            frame = observation.Frame.Copy();
            foreach (var pair in observation.References) references.Add(pair.Key, pair.Value.Copy());
            lock (_gate) _framed.Add(observation.Request.PackGeneration);
            var owned = frame;
            Enqueue(() => Verify(observation, owned, references), () =>
            { owned.Dispose(); foreach (var image in references.Values) image.Dispose(); });
        }
        catch { frame?.Dispose(); foreach (var image in references.Values) image.Dispose(); throw; }
    }

    private void Verify(RecognitionFrameObservation observation, SKBitmap frame, Dictionary<CardIdentifier, SKBitmap> references)
    {
        var request = observation.Request; var window = observation.Window;
        PredictionEntry? entry;
        lock (_gate) _predictions.TryGetValue(request.PackGeneration, out entry);
        VerificationEntry Store(string summary, int accepted = 0, double ms = 0, string? error = null)
        {
            var value = new VerificationEntry(window.Width, window.Height, summary, accepted, request.Occurrences.Count, ms, error);
            lock (_gate) _verifications[request.PackGeneration] = value;
            return value;
        }
        if (entry is null || !entry.Context.Pack.Equals(request.Pack)) { Store("not run (order prediction not ready)", error: "prediction"); return; }
        var mapping = VisualCaptureMapping.FromAnchor(observation.Anchor, window.Width, window.Height);
        var geometry = ArenaDraftSlotGeometry.TryCreateLayout(window.Width, window.Height, request.Occurrences.Count,
            new(ArenaDraftView.DraftPackGrid, _mode(window)));
        var result = geometry.IsAvailable
            ? FixedSlotOrderEvidence.Verify(request, entry.LogOrder, entry.Prediction, window.Width, window.Height,
                new(ArenaDraftView.DraftPackGrid, _mode(window)), mapping, frame, references, _lifetime.Token)
            : new FixedSlotOrderEvidenceResult(entry.Prediction, [], geometry.Reason);
        var count = request.Occurrences.Count;
        var verified = result.VerifiedCount(count);
        var accepted = result.Candidates.Select(c => c.Verification.Accepted).DefaultIfEmpty(0).Max();
        var ms = result.Candidates.Sum(c => c.Verification.Duration.TotalMilliseconds);
        _dispatch(() =>
        {
            if (_disposed) return;
            // Admission is serialized with pack/manual/geometry changes on the UI thread. A native read catches changes between polls.
            var current = false;
            try { current = ReferenceEquals(entry.Context, _presentation.PlacementContext) && _isCurrentFrame(observation); }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine("Shadow freshness check failed: " + ex.Message); }
            var manual = _presentation.HasConfirmedVisualPlacement || _presentation.HasManualVisualEdits || _presentation.IsCalibrating;
            var evidenceGate = FixedSlotOrderEvidence.Gate(request, entry.LogOrder, result,
                _presentation.PlacementContext?.Generation ?? -1, _presentation.Session.CurrentPackIdentity, current, manual);
            var fallback = evidenceGate.Accepted ? null : evidenceGate.Reason;
            var value = new VerificationEntry(window.Width, window.Height,
                $"{verified}/{result.Candidates.Count} candidate orders fully verified · {accepted}/{count} slots · {ms:F0} ms",
                accepted, count, ms, result.DeclineReason, verified, fallback, observation.CaptureGeneration, observation.CaptureRequestGeneration,
                verified == 1 ? result.Candidates.Single(c => c.Verification.FullyVerifies(count)).Order : null, geometry.IsAvailable, evidenceGate.Accepted);
            lock (_gate) _verifications[request.PackGeneration] = value;
            try { if (evidenceGate.Accepted) _recordEvidence?.Invoke(entry.Context, observation, result); }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine("Fast evidence admission failed: " + ex.Message); }
            if (observation.Visual is { } visual)
                Enqueue(() => Compare(entry.Context, request, visual, window, observation.Anchor));
        });
    }

    private void Applied(VisualPlacementContext context, CardVisualLocalizationResult result)
    {
        if (_disposed) return;
        lock (_gate) if (_framed.Contains(context.Generation)) return; // The frame path logs after strict verification and freshness validation.
        try
        {
            // Snapshots only; the comparison and its I/O run on the queue.
            var request = new CardVisualLocalizationRequest(context.Pack, _presentation.Session.CurrentPackPresentations, _presentation.Layout)
            { PackGeneration = context.Generation };
            var window = _window(); var anchor = _anchor();
            Enqueue(() => Compare(context, request, result, window, anchor));
        }
        catch (Exception ex) { _presentation.SetDeterministicSlots("Deterministic slots: shadow failed (" + ex.GetType().Name + ")", ex.Message); }
    }

    private void Compare(VisualPlacementContext context, CardVisualLocalizationRequest request, CardVisualLocalizationResult result,
        ArenaWindowGeometry? window, NormalizedDraftRegion? anchor)
    {
        var coordinate = Coordinate(context.Pack);
        PredictionEntry? entry; VerificationEntry? verification;
        lock (_gate) { _predictions.TryGetValue(context.Generation, out entry); _verifications.TryGetValue(context.Generation, out verification); }
        if (entry is null || window is null || anchor is null)
        { Publish($"Deterministic slots: {coordinate} not compared ({(entry is null ? "order prediction not ready" : "Arena client geometry unknown")})", null, context.Generation); return; }
        lock (_gate) if (!_recorded.Add((context.Generation, window.Width, window.Height,
            verification?.CaptureGeneration ?? 0, verification?.CaptureRequestGeneration ?? 0))) return;
        var candidate = Locate(context, entry.LogOrder, entry.Prediction, window);
        if (verification is not null && (verification.ClientWidth != window.Width || verification.ClientHeight != window.Height)) verification = null;
        var comparisonCandidate = verification?.VerifiedOrder is { } order
            ? Locate(context, entry.LogOrder, new(ArenaDisplayOrderPredictionStatus.Unanimous, [new(order, 0)], entry.Prediction.SurvivingRules), window)
            : candidate;
        var comparison = verification?.FallbackReason == "stale pack, geometry or frame"
            ? DeterministicShadowComparison.Unavailable(DeterministicShadowStatus.Stale, verification.FallbackReason)
            : DeterministicSlotShadowComparer.Compare(comparisonCandidate, request, result, VisualCaptureMapping.FromAnchor(anchor, window.Width, window.Height));
        var detail = DeterministicSlotPresentation.Detail(coordinate, candidate, comparison) + " · verifier " + (verification?.Summary ?? "not run");
        System.Diagnostics.Trace.WriteLine("Deterministic slots: " + detail);
        try { _log?.AppendLine(Record(context, entry, candidate, comparison, verification, result)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { detail += " · log write failed: " + ex.Message; }
        Publish(DeterministicSlotPresentation.ComparisonLine(coordinate, comparison), detail, context.Generation);
    }

    private DeterministicSlotResult Locate(VisualPlacementContext context, IReadOnlyList<int> log, ArenaDisplayOrderPrediction prediction, ArenaWindowGeometry? window) =>
        DeterministicDraftCardLocator.Locate(new(context.Pack, context.Generation, log, prediction, window?.Width ?? 0, window?.Height ?? 0,
            new(ArenaDraftView.DraftPackGrid, window is null ? ArenaWindowMode.Unknown : _mode(window))));

    /// <summary>One concise structured record: coordinates, sizes, statuses and metrics. No pixels, names, paths or account data.</summary>
    private static string Record(VisualPlacementContext context, PredictionEntry entry, DeterministicSlotResult candidate,
        DeterministicShadowComparison comparison, VerificationEntry? verification, CardVisualLocalizationResult visual)
    {
        static double? Finite(double value) => double.IsFinite(value) ? Math.Round(value, 2) : null;
        return JsonSerializer.Serialize(new
        {
            Schema = 2, Source = "DraftTG/9E.2A.1 deterministic-slot shadow", RecordedAt = DateTimeOffset.UtcNow,
            entry.DraftScope, LiveSynchronizedFrame = verification?.CaptureRequestGeneration > 0,
            Pack = context.Pack.Position.Pack.Value, Pick = context.Pack.Position.Pick.Value, CardCount = candidate.CardCount,
            ClientWidth = candidate.ClientWidth, ClientHeight = candidate.ClientHeight,
            Aspect = candidate.ClientHeight > 0 ? Math.Round(candidate.ClientWidth / (double)candidate.ClientHeight, 4) : 0,
            Scale = Math.Round(candidate.ClientHeight / ArenaDraftSlotGeometry.ReferenceClientHeight, 5),
            OrderStatus = entry.Prediction.Status.ToString(), PredictedOrders = entry.Prediction.Orders.Count,
            SurvivingRules = entry.Prediction.SurvivingRules, RuleClasses = entry.Classes,
            Deterministic = candidate.Status.ToString(), DeterministicReason = candidate.Reason,
            GeometryAvailable = verification?.GeometryAvailable ?? candidate.Geometry?.IsAvailable ?? false,
            Visual = $"{visual.Matches.Count}/{candidate.CardCount} {visual.Method}", Shadow = comparison.Status.ToString(),
            comparison.Compared, OrderAgreements = comparison.SlotAgreements, comparison.ReadingOrderAgrees,
            MaxCenterDelta = Finite(comparison.MaxCenterDelta), RmsCenterDelta = Finite(comparison.RmsCenterDelta),
            OffsetX = Finite(comparison.BiasX), OffsetY = Finite(comparison.BiasY),
            MaxScatter = Finite(comparison.MaxResidual), RmsScatter = Finite(comparison.RmsResidual),
            Verifier = verification is null ? null : new { verification.Accepted, verification.Count, Milliseconds = Math.Round(verification.Milliseconds), verification.Error },
            FastVerifierResult = verification?.Summary ?? "not run",
            FastEvidenceEligible = verification?.EvidenceEligible ?? false,
            VerifiedCandidateOrderCount = verification?.VerifiedCandidateOrders ?? 0,
            FallbackReason = verification?.FallbackReason ?? (candidate.IsAvailable ? "shadow only; production uses full visual localization" : candidate.Reason),
            CaptureGeneration = verification?.CaptureGeneration ?? 0, CaptureRequestGeneration = verification?.CaptureRequestGeneration ?? 0,
            ArenaData = entry.Database?.DataVersion, ArenaGrp = entry.Database?.GrpVersion, ArenaDatabaseFile = entry.Database?.FileName
        });
    }

    private static string Coordinate(DraftPack pack) => $"P{pack.Position.Pack.Value}P{pack.Position.Pick.Value}";

    private void Publish(string status, string? detail, long? generation = null) => _dispatch(() =>
    {
        if (_disposed || (generation is not null && generation != _presentation.PlacementContext?.Generation)) return;
        if (detail is not null) { _details.Add(detail); if (_details.Count > MaximumDetailLines) _details.RemoveAt(0); }
        _presentation.SetDeterministicSlots(status, detail is null ? null : string.Join("\n", _details));
    });

    private void Enqueue(Action work, Action? cleanup = null)
    {
        lock (_gate) // Fed from the UI thread and from the matcher's worker thread.
        {
            if (_disposed) { cleanup?.Invoke(); return; }
            _tail = _tail.ContinueWith(_ =>
            {
                try { if (!_lifetime.IsCancellationRequested) work(); }
                catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
                catch (Exception ex) { Publish("Deterministic slots: shadow failed (" + ex.GetType().Name + ")", ex.Message); }
                finally { cleanup?.Invoke(); }
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _presentation.PropertyChanged -= PresentationChanged;
        _presentation.AutomaticLocalizationApplied -= Applied;
        await _lifetime.CancelAsync();
        await _tail;
        _lifetime.Dispose();
    }
}
