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
    public string Backend { get; init; } = "Unspecified/test capture";
    public double CaptureMilliseconds { get; init; }
    public double? PresentationMilliseconds { get; init; }
    public DateTimeOffset? ReferenceStartedAt { get; init; }
    public DateTimeOffset? ReferenceCompletedAt { get; init; }
    public DateTimeOffset? MatcherStartedAt { get; init; }
    public DateTimeOffset? MatcherCompletedAt { get; init; }
    public CardRecognitionAudit? MatcherAudit { get; init; }
    public string? PixelHash { get; init; }
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
        int Classes, ArenaCardDatabaseInfo? Database, string DraftScope, string? DraftIdentifierDigest, DateTimeOffset PredictedAt);
    private sealed record VerificationEntry(int ClientWidth, int ClientHeight, string Summary, int Accepted, int Count, double Milliseconds, string? Error,
        int VerifiedCandidateOrders = 0, string? FallbackReason = null, long CaptureGeneration = 0, long CaptureRequestGeneration = 0,
        IReadOnlyList<int>? VerifiedOrder = null, bool GeometryAvailable = false, bool EvidenceEligible = false, RecognitionFrameObservation? Frame = null,
        FixedSlotOrderEvidenceResult? Result = null, bool Current = false, bool Manual = false, long CurrentGeneration = 0, DateTimeOffset? VerificationStartedAt = null, DateTimeOffset? AdmittedAt = null, double QueueMilliseconds = 0, double CopyMilliseconds = 0);

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
    private readonly HashSet<long> _manualGenerations = [];
    private readonly ArenaDraftEvidenceScopeTracker _scopes = new(Guid.NewGuid().ToString("N"));
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _gate = new();
    private readonly Dictionary<long, PredictionEntry> _predictions = [];
    private readonly Dictionary<long, VerificationEntry> _verifications = [];
    private readonly HashSet<(long Generation, int Width, int Height, long CaptureGeneration, long CaptureRequest)> _recorded = [];
    private readonly List<string> _details = [];
    private Task _tail = Task.CompletedTask;
    private bool _disposed;
    private VisualPlacementContext? _lastContext;

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
        presentation.Session.CurrentPackPlacementInvalidated += AcceptedPickRetired;
    }

    private void AcceptedPickRetired() => RecordRuntimeEvent("AcceptedPickBadgeRetirement", new
    { PlacedBadges = _presentation.Badges.Count(b => b.IsPlaced), PlacementContextCleared = _presentation.PlacementContext is null });

    internal static JsonLinesLedgerFile DefaultLog() => new(Path.Combine(
        ApplicationDataPathProviderFactory.CreateDefault().GetApplicationDataDirectory(), "localization", "deterministic-shadow.jsonl"));

    internal Task Completion => _tail;

    internal void RecordRuntimeEvent(string kind, object details)
    {
        if (!LiveLocalizationValidation.Enabled) return;
        var line = LiveLocalizationValidation.Event(kind, details);
        Enqueue(() => _log?.AppendLine(line));
    }

    private void PresentationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_disposed) return;
        if (e.PropertyName is nameof(OverlayViewModel.HasManualVisualEdits) or nameof(OverlayViewModel.HasConfirmedVisualPlacement)
            or nameof(OverlayViewModel.IsCalibrating))
        {
            if (_presentation.PlacementContext is { } edited && (_presentation.HasManualVisualEdits
                || _presentation.HasConfirmedVisualPlacement || _presentation.IsCalibrating))
                lock (_gate) _manualGenerations.Add(edited.Generation);
        }
        if (LiveLocalizationValidation.Enabled && e.PropertyName == nameof(OverlayViewModel.Badges) && _presentation.PlacementContext is { } scoringContext)
            RecordRuntimeEvent("BadgeScoringSnapshot", ProductionBadgeDiagnostics.Capture(_presentation, scoringContext, null));
        if (e.PropertyName != nameof(OverlayViewModel.PlacementContext)) return;
        var context = _presentation.PlacementContext;
        if (context is null)
        {
            RecordRuntimeEvent("PackRetired", new { Generation = _lastContext?.Generation,
                Pack = _lastContext?.Pack.Position.Pack.Value, Pick = _lastContext?.Pack.Position.Pick.Value,
                PlacedBadges = _presentation.Badges.Count(b => b.IsPlaced), Reason = "placement context retired (accepted pick or session clear)" });
            _lastContext = null;
            return;
        }
        _lastContext = context;
        RecordRuntimeEvent("PackAccepted", new { context.Generation, Pack = context.Pack.Position.Pack.Value,
            Pick = context.Pack.Position.Pick.Value, CardCount = context.Pack.AvailableCardIdentifiers.Count,
            context.ChangedAt, DraftIdentifierDigest = _presentation.Session.CurrentArenaState?.DraftIdentifier is { } id
                ? ArenaDisplayOrderObservation.ScopeDigest("draft:" + id.Value) : null,
            Client = _window() is { } w ? new { w.X, w.Y, w.Width, w.Height, w.Scaling } : null });
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
                _predictions[context.Generation] = new(context, log ?? [], prediction, evaluation.Classes.Count, database, scope,
                    state?.DraftIdentifier is { } id ? ArenaDisplayOrderObservation.ScopeDigest("draft:" + id.Value) : null, DateTimeOffset.UtcNow);
                foreach (var stale in _predictions.Keys.Where(g => g <= context.Generation - RetainedPacks).ToArray())
                { _predictions.Remove(stale); _verifications.Remove(stale); _framed.Remove(stale); _manualGenerations.Remove(stale); }
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
        var copiedAt = DateTimeOffset.UtcNow;
        SKBitmap? frame = null;
        var references = new Dictionary<CardIdentifier, SKBitmap>();
        try
        {
            frame = observation.Frame.Copy();
            foreach (var pair in observation.References) references.Add(pair.Key, pair.Value.Copy());
            lock (_gate) _framed.Add(observation.Request.PackGeneration);
            var owned = frame;
            var queuedAt = DateTimeOffset.UtcNow;
            Enqueue(() => Verify(observation with { References = references }, owned, references, queuedAt, (queuedAt - copiedAt).TotalMilliseconds), () =>
            { owned.Dispose(); foreach (var image in references.Values) image.Dispose(); });
        }
        catch { frame?.Dispose(); foreach (var image in references.Values) image.Dispose(); throw; }
    }

    private void Verify(RecognitionFrameObservation observation, SKBitmap frame, Dictionary<CardIdentifier, SKBitmap> references, DateTimeOffset queuedAt, double copyMilliseconds)
    {
        var queueMilliseconds = (DateTimeOffset.UtcNow - queuedAt).TotalMilliseconds;
        var request = observation.Request; var window = observation.Window;
        PredictionEntry? entry;
        lock (_gate) _predictions.TryGetValue(request.PackGeneration, out entry);
        VerificationEntry Store(string summary, int accepted = 0, double ms = 0, string? error = null)
        {
            var value = new VerificationEntry(window.Width, window.Height, summary, accepted, request.Occurrences.Count, ms, error);
            lock (_gate) _verifications[request.PackGeneration] = value;
            return value;
        }
        if (entry is null || !entry.Context.Pack.Equals(request.Pack))
        {
            Store("not run (order prediction not ready)", error: "prediction");
            RecordRuntimeEvent("StaleResultRejected", new { RequestedGeneration = request.PackGeneration,
                observation.CaptureGeneration, observation.CaptureRequestGeneration, Reason = "prediction missing or pack no longer retained" });
            return;
        }
        var mapping = VisualCaptureMapping.FromAnchor(observation.Anchor, window.Width, window.Height);
        var geometry = ArenaDraftSlotGeometry.TryCreateLayout(window.Width, window.Height, request.Occurrences.Count,
            new(ArenaDraftView.DraftPackGrid, _mode(window)));
        var verificationStartedAt = DateTimeOffset.UtcNow;
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
            bool everManual;
            lock (_gate) everManual = _manualGenerations.Contains(request.PackGeneration);
            var fallback = manual || everManual ? "manual intervention during this pack generation"
                : evidenceGate.Accepted ? null : evidenceGate.Reason;
            var value = new VerificationEntry(window.Width, window.Height,
                $"{verified}/{result.Candidates.Count} candidate orders fully verified · {accepted}/{count} slots · {ms:F0} ms",
                accepted, count, ms, result.DeclineReason, verified, fallback, observation.CaptureGeneration, observation.CaptureRequestGeneration,
                verified == 1 ? result.Candidates.Single(c => c.Verification.FullyVerifies(count)).Order : null, geometry.IsAvailable, evidenceGate.Accepted,
                observation, result, current, manual || everManual, _presentation.PlacementContext?.Generation ?? -1, verificationStartedAt, DateTimeOffset.UtcNow, queueMilliseconds, copyMilliseconds);
            lock (_gate) _verifications[request.PackGeneration] = value;
            try { if (evidenceGate.Accepted) _recordEvidence?.Invoke(entry.Context, observation, result); }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine("Fast evidence admission failed: " + ex.Message); }
            if (observation.Visual is { } visual)
                Enqueue(() => Compare(entry.Context, request, visual, window, observation.Anchor, value));
        });
    }

    private void Applied(VisualPlacementContext context, CardVisualLocalizationResult result)
    {
        if (_disposed) return;
        if (LiveLocalizationValidation.Enabled)
            RecordRuntimeEvent("ProductionBadgePipeline", ProductionBadgeDiagnostics.Capture(_presentation, context, result));
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
        ArenaWindowGeometry? window, NormalizedDraftRegion? anchor, VerificationEntry? capturedVerification = null)
    {
        var coordinate = Coordinate(context.Pack);
        PredictionEntry? entry; VerificationEntry? verification;
        lock (_gate) { _predictions.TryGetValue(context.Generation, out entry); _verifications.TryGetValue(context.Generation, out verification); }
        verification = capturedVerification ?? verification;
        if (entry is null || window is null || anchor is null)
        { Publish($"Deterministic slots: {coordinate} not compared ({(entry is null ? "order prediction not ready" : "Arena client geometry unknown")})", null, context.Generation); return; }
        lock (_gate) if (!_recorded.Add((context.Generation, window.Width, window.Height,
            verification?.CaptureGeneration ?? 0, verification?.CaptureRequestGeneration ?? 0))) return;
        var candidate = Locate(context, entry.LogOrder, entry.Prediction, window);
        if (verification is not null && (verification.ClientWidth != window.Width || verification.ClientHeight != window.Height)) verification = null;
        var comparisonCandidate = !candidate.IsAvailable && verification?.VerifiedOrder is { } order
            ? Locate(context, entry.LogOrder, new(ArenaDisplayOrderPredictionStatus.Unanimous, [new(order, 0)], entry.Prediction.SurvivingRules), window)
            : candidate;
        var comparison = verification?.FallbackReason == "stale pack, geometry or frame"
            ? DeterministicShadowComparison.Unavailable(DeterministicShadowStatus.Stale, verification.FallbackReason)
            : DeterministicSlotShadowComparer.Compare(comparisonCandidate, request, result, VisualCaptureMapping.FromAnchor(anchor, window.Width, window.Height));
        var detail = DeterministicSlotPresentation.Detail(coordinate, candidate, comparison) + " · verifier " + (verification?.Summary ?? "not run");
        System.Diagnostics.Trace.WriteLine("Deterministic slots: " + detail);
        try { _log?.AppendLine(Record(context, entry, candidate, comparison, verification, result, request, window, anchor)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { detail += " · log write failed: " + ex.Message; }
        Publish(DeterministicSlotPresentation.ComparisonLine(coordinate, comparison), detail, context.Generation);
    }

    private DeterministicSlotResult Locate(VisualPlacementContext context, IReadOnlyList<int> log, ArenaDisplayOrderPrediction prediction, ArenaWindowGeometry? window) =>
        DeterministicDraftCardLocator.Locate(new(context.Pack, context.Generation, log, prediction, window?.Width ?? 0, window?.Height ?? 0,
            new(ArenaDraftView.DraftPackGrid, window is null ? ArenaWindowMode.Unknown : _mode(window))));

    /// <summary>One concise structured record: coordinates, sizes, statuses and metrics. No pixels, names, paths or account data.</summary>
    private string Record(VisualPlacementContext context, PredictionEntry entry, DeterministicSlotResult candidate,
        DeterministicShadowComparison comparison, VerificationEntry? verification, CardVisualLocalizationResult visual, CardVisualLocalizationRequest request, ArenaWindowGeometry window, NormalizedDraftRegion anchor)
    {
        static double? RawFinite(double value) => double.IsFinite(value) ? value : null;
        static double? Finite(double value) => double.IsFinite(value) ? value : null;
        return JsonSerializer.Serialize(new
        {
            Schema = 3, Source = "DraftTG/9E.2B live deterministic validation", Kind = "PackObservation", RecordedAt = DateTimeOffset.UtcNow,
            LiveValidationMode = LiveLocalizationValidation.Enabled, LiveLocalizationValidation.SessionId,
            LiveLocalizationValidation.Build, LiveLocalizationValidation.BuildId,
            PackGeneration = context.Generation, CurrentPackGeneration = verification?.CurrentGeneration ?? 0,
            CurrentGenerationFrame = verification?.Current ?? false, ManualPlacementInvolved = verification?.Manual ?? true,
            FramePackGeneration = verification?.Frame?.FramePackGeneration ?? 0,
            PredictionExistedBeforeVerification = verification?.VerificationStartedAt is { } started && entry.PredictedAt <= started,
            entry.PredictedAt, entry.DraftIdentifierDigest, verification?.VerificationStartedAt, verification?.AdmittedAt,
            WindowMode = _mode(window).ToString(),
            FrameRevision = verification?.Frame?.Revision ?? 0, CapturedAt = verification?.Frame?.CapturedAt,
            PackChangedAt = context.ChangedAt, Backend = verification?.Frame?.Backend,
            FrameWidth = verification?.Frame?.CaptureWidth, FrameHeight = verification?.Frame?.CaptureHeight,
            CaptureMapping = VisualCaptureMapping.FromAnchor(anchor, window.Width, window.Height), CaptureAnchor = anchor,
            PixelHash = verification?.Frame?.PixelHash,
            ReferenceIdentities = verification?.Frame?.References.Keys.Select(k => k.Value).ToArray(),
            FullMatcherAudit = verification?.Frame?.MatcherAudit,
            ReferenceStartedAt = verification?.Frame?.ReferenceStartedAt, ReferenceCompletedAt = verification?.Frame?.ReferenceCompletedAt,
            MatcherStartedAt = verification?.Frame?.MatcherStartedAt, MatcherCompletedAt = verification?.Frame?.MatcherCompletedAt,
            ObserverQueueMilliseconds = verification?.QueueMilliseconds, FrameCopyMilliseconds = verification?.CopyMilliseconds,
            ClientRectangle = new { window.X, window.Y, window.Width, window.Height }, DpiScale = window.Scaling,
            Dpi = window.Scaling * 96, WindowHandle = window.Handle.ToInt64(),
            PredictedOrderIdentities = entry.Prediction.Orders.Select(o => o.Order),
            DeterministicSlots = candidate.Assignments.Select(a => new { a.Slot, a.ArenaGrpId, PackIndex = a.Key.PackIndex,
                CardIdentity = a.Key.CardIdentifier.Value, a.Rectangle }),
            CandidateVerifications = verification?.Result?.Candidates.Select(c => new { c.Order,
                FullyVerified = c.Verification.FullyVerifies(candidate.CardCount), c.Verification.Error,
                Milliseconds = c.Verification.Duration.TotalMilliseconds,
                Slots = c.Verification.Slots.Select(s => new { s.Slot, s.ExpectedOccurrence,
                    ArenaId = entry.LogOrder[s.ExpectedOccurrence], CardIdentity = request.Occurrences[s.ExpectedOccurrence].CardIdentifier.Value,
                    PredictedClientRectangle = ArenaDraftSlotGeometry.TryCreateLayout(window.Width, window.Height, candidate.CardCount,
                        new(ArenaDraftView.DraftPackGrid, _mode(window))).Layout?.Slots[s.Slot],
                    Confidence = RawFinite(s.ExpectedScore), AmbiguityMargin = RawFinite(s.ExpectedScore - s.CompetingScore), s.Accepted, s.Reason }) }),
            WgcMatches = visual.Matches.Select(m => new { PackIndex = m.Key.PackIndex, CardIdentity = m.Key.CardIdentifier.Value,
                ArenaId = entry.LogOrder[m.Key.PackIndex], m.VisualSlot, Confidence = RawFinite(m.Confidence), State = m.State.ToString(),
                ClientRectangle = VisualCaptureMapping.FromAnchor(anchor, window.Width, window.Height).ToClient(m.Rectangle) }),
            WgcFullyMatched = visual.IsSafeFor(request) && visual.Matches.Count == candidate.CardCount,
            FullPackVerified = verification?.EvidenceEligible == true,
            PlacementAgreedWithWgc = comparison.Status is DeterministicShadowStatus.ExactAgreement or DeterministicShadowStatus.GeometryAgreement,
            ProductionAuthority = "WGC full matcher", WgcFullMatcherInvoked = verification?.Frame?.Backend == "Windows.Graphics.Capture",
            FallbackInvoked = verification?.EvidenceEligible != true || verification.Manual || !candidate.IsAvailable
                || comparison.Status is not (DeterministicShadowStatus.ExactAgreement or DeterministicShadowStatus.GeometryAgreement),
            ComparisonSlots = comparison.Slots,
            WgcPackGeneration = visual.PackGeneration, WgcCaptureRequestGeneration = visual.CaptureRequestGeneration,
            CaptureMilliseconds = verification?.Frame?.CaptureMilliseconds, PresentationMilliseconds = verification?.Frame?.PresentationMilliseconds, FullMatcherMilliseconds = visual.Duration.TotalMilliseconds,
            VerificationMilliseconds = verification?.Milliseconds,
            AdmissionAgeMilliseconds = verification?.Frame is { } f ? (verification.AdmittedAt!.Value - f.CapturedAt).TotalMilliseconds : (double?)null,
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
            FallbackReason = verification?.FallbackReason ?? (!candidate.IsAvailable ? candidate.Reason
                : comparison.Status is not (DeterministicShadowStatus.ExactAgreement or DeterministicShadowStatus.GeometryAgreement) ? comparison.Reason : null),
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
                catch (Exception ex)
                {
                    Publish("Deterministic slots: shadow failed (" + ex.GetType().Name + ")", ex.Message);
                    if (LiveLocalizationValidation.Enabled)
                        try { _log?.AppendLine(LiveLocalizationValidation.Event("ShadowFailure", new { Reason = ex.Message })); }
                        catch (Exception logError) { System.Diagnostics.Trace.WriteLine("Shadow failure logging failed: " + logError.Message); }
                }
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
        _presentation.Session.CurrentPackPlacementInvalidated -= AcceptedPickRetired;
        await _lifetime.CancelAsync();
        await _tail;
        _lifetime.Dispose();
    }
}
