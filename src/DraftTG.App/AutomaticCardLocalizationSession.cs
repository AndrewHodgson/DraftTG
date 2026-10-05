using System.ComponentModel;
using Avalonia.Threading;
using DraftTG.Application;
using DraftTG.App.Platform;
using DraftTG.Data;
using SkiaSharp;

namespace DraftTG.App;

internal sealed class AutomaticCardLocalizationSession : ICardVisualLocator, IAsyncDisposable
{
    private readonly OverlayViewModel _presentation;
    private readonly Func<OverlayCalibration?> _calibration;
    private readonly Action<int, int, double, double> _moveOverlay;
    private readonly Action<bool> _captureVisibility;
    private readonly ArenaCaptureCoordinator? _coordinator;
    private readonly Func<CardVisualLocalizationRequest, ArenaRegionFrame, CancellationToken, Task<CardVisualLocalizationResult>> _recognize;
    private readonly Action<Action> _dispatch;
    private readonly string _debugPath;
    private readonly PackFrameSynchronizer _synchronizer;
    private readonly string _sessionScope = Guid.NewGuid().ToString("N");
    private PackFrameContext? _packContext;
    internal string? LastDebugCapturePath { get; private set; }
    internal string? LastDebugLogPath { get; private set; }
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
    private readonly ScryfallVisualReferenceCache _references;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _attempt;
    private Task? _work;
    private Task? _restartWork;
    private ArenaWindowGeometry? _window;
    private OverlayCalibration? _saved;
    private NormalizedDraftRegion? _anchor;
    private bool _pending, _disposed, _force, _saveRequested, _capturing;
    private int _retries;
    private long _revision, _observedGeneration;
    private DateTimeOffset _notBefore;
    private (int X, int Y, double Width, double Height)? _overlayGeometry;

    public AutomaticCardLocalizationSession(OverlayViewModel presentation, Func<OverlayCalibration?> calibration,
        Action<int, int, double, double> moveOverlay, Action<bool> captureVisibility,
        IArenaRegionCapture? capture = null, ArenaCaptureOptions? options = null,
        Func<CardVisualLocalizationRequest, ArenaRegionFrame, CancellationToken, Task<CardVisualLocalizationResult>>? recognize = null,
        string? debugPath = null, Action<Action>? dispatch = null, PackFrameTiming? timing = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _presentation = presentation; _calibration = calibration; _moveOverlay = moveOverlay; _captureVisibility = captureVisibility;
        capture ??= WindowsCaptureFactory.CreateForCurrentPlatform();
        _coordinator = capture is null ? null : new(capture, options ?? ArenaCaptureOptions.FromEnvironment());
        _references = new(ApplicationDataPathProviderFactory.CreateDefault().GetApplicationDataDirectory(), _http);
        _recognize = recognize is null ? RecognizeAsync : (request, frame, token) =>
        {
            _coordinator!.LocalizationStarted(frame.Generation);
            return recognize(request, frame, token);
        };
        _debugPath = debugPath ?? Path.Combine(Path.GetTempPath(), "DraftTG", "arena-draft.png");
        _synchronizer = new(timing ?? PackFrameTiming.FromEnvironment(), delay);
        BindPack();
        _dispatch = dispatch ?? (action => { if (Dispatcher.UIThread.CheckAccess()) action(); else Dispatcher.UIThread.Post(action); });
        if (_coordinator is not null) _coordinator.Changed += DiagnosticsChanged;
        _presentation.PropertyChanged += Changed;
        _timer.Tick += Tick;
    }

    private void DiagnosticsChanged() => _dispatch(() =>
    { if (!_disposed) _presentation.SetCaptureRuntimeDiagnostics(_coordinator!.DiagnosticText); });
    public void Start() { Retry(); Tick(null, EventArgs.Empty); _timer.Start(); }
    public bool CanFollowWindow => _coordinator is not null && _anchor is not null;
    internal Task CurrentWork => _restartWork ?? _work ?? Task.CompletedTask;
    public void RetryManually()
    {
        _presentation.InvalidateAutomaticLocalization("Rediscovering Arena and requesting a fresh frame.");
        RequestImmediate(false);
    }
    public async Task SaveCurrentCaptureForDebugging()
    {
        LastDebugCapturePath = null; LastDebugLogPath = null;
        RequestImmediate(true);
        await CurrentWork;
    }
    private void RequestImmediate(bool save)
    {
        Retry(); _force = true; _saveRequested = save; _notBefore = DateTimeOffset.MinValue;
        try { _coordinator?.Restart(); Tick(null, EventArgs.Empty); _restartWork = FinishRestartAsync(); }
        catch (Exception ex) { ReportFailure(ex); }
    }
    private async Task FinishRestartAsync()
    {
        if (_work is not null) await _work;
        // Retry during an active attempt starts immediately after that attempt observes cancellation.
        if (!_disposed && _pending && _force) Tick(null, EventArgs.Empty);
        if (_work is not null) await _work;
    }
    public void Retry()
    {
        _revision++; _attempt?.Cancel(); _retries = 0; _pending = true;
        _notBefore = DateTimeOffset.MinValue; // The request-scoped synchronizer owns the cancellable visual settle delay.
    }
    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OverlayViewModel.IsCalibrating)) _overlayGeometry = null;
        if (e.PropertyName == nameof(OverlayViewModel.PlacementContext)) BindPack();
        if (e.PropertyName is nameof(OverlayViewModel.PlacementContext) or nameof(OverlayViewModel.IsCalibrating)) Retry();
    }
    private void BindPack()
    {
        var context = _presentation.PlacementContext;
        _packContext = context is null ? null : new(new(context.Pack,
            Array.AsReadOnly(_presentation.Session.CurrentPackPresentations.ToArray()), _presentation.Layout)
            { PackGeneration = context.Generation }, context.ChangedAt);
        _synchronizer.Activate(_packContext);
        _coordinator?.PackChanged();
    }

    private void Tick(object? sender, EventArgs e)
    {
        if (_disposed) return;
        if (_coordinator is null)
        {
            _presentation.SetCaptureRuntimeDiagnostics("Stage: Platform unavailable\nmacOS capture is deferred; manual positions are available.");
            _presentation.SetLocalizationStatus("Card placement: Manual fallback", "macOS capture is not implemented yet. Use Match card positions.");
            return;
        }
        try
        {
            var window = _coordinator.Observe();
            if (_presentation.IsCalibrating) { _coordinator.Note("Waiting for calibration editing to finish"); return; }
            if (_calibration() is not { } saved) { _coordinator.Note("Waiting for saved draft-region calibration"); return; }
            if (_observedGeneration != _coordinator.Generation || !ReferenceEquals(saved, _saved))
            {
                var changedLayout = window is not null && _window is not null &&
                    (Math.Abs(window.Width - _window.Width) > 1 || Math.Abs(window.Height - _window.Height) > 1 || Math.Abs(window.Scaling - _window.Scaling) >= .001);
                if (!ReferenceEquals(saved, _saved) || window?.Handle != _window?.Handle) _anchor = null;
                _observedGeneration = _coordinator.Generation; _saved = saved;
                var immediate = _force;
                Retry(); if (immediate) _notBefore = DateTimeOffset.MinValue;
                _presentation.InvalidateAutomaticLocalization("Arena window/geometry changed; requesting a fresh frame.", changedLayout);
            }
            _window = window;
            if (window is null || !window.IsVisible || window.IsMinimized)
            {
                _captureVisibility(false);
                var reason = window is null ? (_coordinator.Diagnostics.Inspection.ProcessFound
                    ? "Arena process found but no eligible rendering HWND was found." : "Arena process not found.")
                    : "Arena window is hidden or minimized.";
                _coordinator.Note("Waiting: " + reason);
                _presentation.SetLocalizationStatus("Card placement: Manual fallback", reason);
                return;
            }
            _anchor ??= Anchor(saved, window);
            if (_anchor is null) throw new InvalidOperationException("Draft-region crop is outside Arena client bounds; calibrate it fully inside Arena.");
            var geometry = (X: window.X + (int)Math.Round(_anchor.X * window.Width), Y: window.Y + (int)Math.Round(_anchor.Y * window.Height),
                Width: _anchor.Width * window.Width / window.Scaling, Height: _anchor.Height * window.Height / window.Scaling);
            if (_overlayGeometry != geometry)
            {
                _overlayGeometry = geometry;
                _moveOverlay(geometry.X, geometry.Y, geometry.Width, geometry.Height);
            }
            _captureVisibility(!_capturing);
            if (_presentation.PlacementContext is null || _packContext is null) { _coordinator.Note("Waiting for a current draft pack; no current frame can be saved"); return; }
            if (!_pending || DateTimeOffset.UtcNow < _notBefore || _work is { IsCompleted: false }
                || (!_force && (_presentation.HasConfirmedVisualPlacement || _presentation.HasManualVisualEdits)))
            {
                if (!_coordinator.Diagnostics.Initialized && !_coordinator.Diagnostics.Running)
                {
                    if (_work is { IsCompleted: false }) _coordinator.Note("Waiting for preceding capture/localization cleanup");
                    else if (_presentation.HasConfirmedVisualPlacement || _presentation.HasManualVisualEdits)
                        _coordinator.Note("Automatic capture paused: confirmed placement or manual edits retained");
                    else if (!_pending) _coordinator.Note("Automatic capture attempts exhausted; Retry automatic placement is available");
                    else _coordinator.Note("Waiting for bounded automatic retry delay");
                }
                return;
            }
            var save = _saveRequested; _saveRequested = false; _force = false; _pending = false;
            var snapshot = _packContext with { Request = _packContext.Request with { CalibratedLayout = _presentation.Layout } };
            _work = RunAsync(window, _presentation.PlacementContext, snapshot, _revision, save);
        }
        catch (Exception ex) { ReportFailure(ex); }
    }

    internal static NormalizedDraftRegion? Anchor(OverlayCalibration calibration, ArenaWindowGeometry window)
    {
        var r = calibration.Region; var screen = calibration.Screen;
        var region = new NormalizedDraftRegion((screen.X + r.X * screen.Width - window.X) / window.Width,
            (screen.Y + r.Y * screen.Height - window.Y) / window.Height, r.Width * screen.Width / window.Width,
            r.Height * screen.Height / window.Height);
        return region.IsValid ? region : null;
    }

    private async Task RunAsync(ArenaWindowGeometry window, VisualPlacementContext context, PackFrameContext snapshot, long revision, bool save)
    {
        _attempt?.Dispose();
        _attempt = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var token = _attempt.Token;
        if (!save) _presentation.SetLocalizationStatus("Card placement: Recognizing");
        try
        {
            if (save)
            {
                using var frame = await AcquireSynchronizedAsync(snapshot, token);
                VerifyCurrent(context, snapshot, revision, token);
                _coordinator!.Note("Saving debug draft crop");
                var path = DebugCaptureArtifacts.CreatePath(_debugPath, snapshot, frame.CaptureRequestGeneration);
                await DebugCaptureArtifacts.SaveAsync(path, frame, DebugText(snapshot, frame),
                    () => VerifyCurrent(context, snapshot, revision, token), token);
                LastDebugCapturePath = path; LastDebugLogPath = Path.ChangeExtension(path, ".txt");
                _coordinator.Saved(path); _coordinator.DiagnosticLogSaved(LastDebugLogPath);
                return;
            }
            var request = snapshot.Request;
            var generation = _coordinator!.Generation;
            var result = await LocateForContextAsync(snapshot, token);
            VerifyCurrent(context, snapshot, revision, token);
            if (_disposed || token.IsCancellationRequested || revision != _revision || generation != _coordinator.Generation
                || !ArenaWindowGeometry.SameCaptureGeometry(_coordinator.Observe(), window)) return;
            if (result.PackGeneration != snapshot.Request.PackGeneration) return;
            _presentation.ApplyAutomaticLocalization(context, result);
            if (result.Matches.Count < request.Occurrences.Count && ++_retries < 3)
            { _pending = true; _notBefore = DateTimeOffset.UtcNow.AddSeconds(2); }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested || revision != _revision || !_synchronizer.IsCurrent(snapshot)) { }
        catch (Exception ex)
        {
            if (!_disposed && revision == _revision)
            {
                ReportFailure(ex);
                if (save)
                {
                    LastDebugCapturePath = null;
                    var path = Path.ChangeExtension(DebugCaptureArtifacts.CreatePath(_debugPath, snapshot,
                        _coordinator!.Diagnostics.RequestGeneration), ".txt");
                    try
                    {
                        await DebugCaptureArtifacts.SaveFailureAsync(path, DebugText(snapshot, null) + "No PNG saved: fresh synchronized capture failed.\n", _lifetime.Token);
                        LastDebugLogPath = path; _coordinator.DiagnosticLogSaved(path);
                    }
                    catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
                    catch (Exception logError) { _coordinator.Fail("Saving capture failure diagnostic", logError); }
                }
                if (!save && ex is not StaleArenaFrameException && ++_retries < 3) { _pending = true; _notBefore = DateTimeOffset.UtcNow.AddSeconds(2); }
            }
        }
        finally
        {
            _capturing = false;
            if (!_disposed) _captureVisibility(_coordinator?.Window is { IsVisible: true, IsMinimized: false });
        }
    }

    private void ReportFailure(Exception ex)
    {
        if (_coordinator?.Diagnostics.Error is null) _coordinator?.Fail(_coordinator.Diagnostics.Stage, ex);
        _presentation.InvalidateAutomaticLocalization(ex.Message);
        _presentation.SetLocalizationStatus("Card placement: Manual fallback", ex.Message);
    }
    private void VerifyCurrent(VisualPlacementContext context, PackFrameContext snapshot, long revision, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_disposed || revision != _revision || !ReferenceEquals(context, _presentation.PlacementContext) || !_synchronizer.IsCurrent(snapshot))
            throw new OperationCanceledException("Pack changed; stale frame/result discarded.", token);
    }
    private async Task<ArenaRegionFrame> AcquireSynchronizedAsync(PackFrameContext snapshot, CancellationToken token)
    {
        var frame = await _synchronizer.AcquireAsync(snapshot, t => AcquireAsync(snapshot.Request.PackGeneration, t), stage =>
        {
            _coordinator!.Note(stage);
            _presentation.SetLocalizationStatus("Card placement: " + stage);
        }, token);
        _coordinator!.SynchronizedFrame(frame);
        return frame;
    }

    private async Task<ArenaRegionFrame> AcquireAsync(long packGeneration, CancellationToken token)
    {
        if (_coordinator is null || _window is null || _anchor is null) throw new InvalidOperationException("Arena region capture unavailable.");
        _capturing = true; _captureVisibility(false);
        try { return await _coordinator.AcquireAsync(_anchor, token, packGeneration); }
        finally { _capturing = false; if (!_disposed) _captureVisibility(_coordinator.Window is { IsVisible: true, IsMinimized: false }); }
    }
    public async Task<CardVisualLocalizationResult> LocateAsync(CardVisualLocalizationRequest request, CancellationToken cancellationToken = default)
    {
        var snapshot = _packContext;
        if (snapshot is null || request.PackGeneration != snapshot.Request.PackGeneration || !request.Pack.Equals(snapshot.Request.Pack))
            throw new OperationCanceledException("Localization request does not belong to the current pack generation.", cancellationToken);
        return await LocateForContextAsync(snapshot with { Request = request }, cancellationToken);
    }
    private async Task<CardVisualLocalizationResult> LocateForContextAsync(PackFrameContext snapshot, CancellationToken cancellationToken)
    {
        using var frame = await AcquireSynchronizedAsync(snapshot, cancellationToken);
        var request = snapshot.Request;
        _presentation.SetLocalizationStatus("Card placement: Localization running");
        var recognized = await _recognize(request, frame, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_synchronizer.IsCurrent(snapshot)) throw new OperationCanceledException("Pack changed during localization.", cancellationToken);
        if (!recognized.Pack.Equals(request.Pack)) throw new InvalidDataException("Localization returned a different pack identity.");
        if (recognized.PackGeneration != 0 && recognized.PackGeneration != request.PackGeneration)
            throw new InvalidDataException("Localization returned a stale pack generation.");
        var result = recognized with { PackGeneration = request.PackGeneration, CaptureRequestGeneration = frame.CaptureRequestGeneration };
        await _synchronizer.RecordConfirmedCardsAsync(snapshot, frame, result, cancellationToken);
        _coordinator!.LocalizationCompleted(result.Matches.Count, request.Occurrences.Count, frame.Generation, result.Duration);
        var debugDirectory = Environment.GetEnvironmentVariable("DRAFTTG_LOCALIZATION_DEBUG_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(debugDirectory)) LocalizationDebugCapture.Save(debugDirectory, frame.Image, request, result);
        return result;
    }
    private string DebugText(PackFrameContext snapshot, ArenaRegionFrame? frame)
    {
        var request = snapshot.Request; var position = request.Pack.Position;
        return _coordinator!.DiagnosticText
            + $"Draft session ID: unavailable in presentation; runtime scope: {_sessionScope}\n"
            + $"Pack: P{position.Pack.Value}P{position.Pick.Value}\nExpected card count: {request.Occurrences.Count}\n"
            + $"Pack generation/token: {request.PackGeneration}\nPack-change timestamp: {snapshot.ChangedAt:O}\n"
            + $"Frame requested pack generation: {frame?.PackGeneration.ToString() ?? "none"}\n"
            + $"Frame capture request generation: {frame?.CaptureRequestGeneration.ToString() ?? "none"}\n"
            + $"Capture timestamp: {frame?.CapturedAt.ToString("O") ?? "none"}\n"
            + $"Frame acquired after pack transition: {(frame is null ? "no frame" : frame.CapturedAt >= snapshot.ChangedAt ? "yes" : "no")}\n"
            + $"Image hash (decoded pixels SHA256): {frame?.ImageHash ?? "none"}\n"
            + $"Capture backend: {frame?.Backend ?? _coordinator.Diagnostics.Backend}\n"
            + $"Frame HWND: {(frame is null ? "none" : $"0x{frame.WindowHandle.ToInt64():X}")}\n"
            + $"Backend processing duration: {frame?.CaptureDuration.TotalMilliseconds:F1} ms\n"
            + $"WGC presentation time (QPC): {frame?.PresentationTime?.ToString() ?? "unavailable"}\n"
            + "Candidate cards (payload order):\n" + string.Join("\n", request.Occurrences.Select(c => $"  {c.CardName}; {c.CardIdentifier.Value}")) + "\n";
    }
    private async Task<CardVisualLocalizationResult> RecognizeAsync(CardVisualLocalizationRequest request, ArenaRegionFrame frame, CancellationToken token)
    {
        _coordinator!.Note("Preparing artwork references after fresh draft crop", frame.Generation);
        var references = await Task.Run(() => _references.GetAsync(request.Occurrences.Select(c => c.CardIdentifier), token), token);
        token.ThrowIfCancellationRequested();
        _coordinator.LocalizationStarted(frame.Generation);
        return await Task.Run(() => new CardTemplateRecognizer().Recognize(request, frame.Image, references, token), token);
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true; _timer.Stop(); _timer.Tick -= Tick; _presentation.PropertyChanged -= Changed;
        if (_coordinator is not null) { _coordinator.Changed -= DiagnosticsChanged; _coordinator.Dispose(); }
        await _lifetime.CancelAsync();
        _synchronizer.Dispose();
        if (_work is not null) await _work;
        if (_restartWork is not null) await _restartWork;
        _attempt?.Dispose(); _lifetime.Dispose(); _references.Dispose(); _http.Dispose();
    }
}
