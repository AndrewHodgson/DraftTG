using System.Globalization;
using DraftTG.Application;
using DraftTG.App.Platform;

namespace DraftTG.App;

internal sealed record ArenaCaptureOptions(TimeSpan FreshFrameTimeout)
{
    public static ArenaCaptureOptions FromEnvironment() => new(
        double.TryParse(Environment.GetEnvironmentVariable("DRAFTTG_FRESH_FRAME_TIMEOUT_SECONDS"),
            NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds is >= .1 and <= 30
            ? TimeSpan.FromSeconds(seconds) : TimeSpan.FromSeconds(2));
}

internal sealed record ArenaCaptureDiagnostics
{
    public ArenaWindowInspection Inspection { get; init; } = new(false, null, "Not inspected yet.");
    public long Generation { get; init; }
    public long RequestGeneration { get; init; }
    public long RequestedPackGeneration { get; init; }
    public string Stage { get; init; } = "Not started";
    public string Backend { get; init; } = "Not selected";
    public CaptureLifecycleState State { get; init; } = CaptureLifecycleState.Idle;
    public CaptureLifecycleSnapshot? NativeLifecycle { get; init; }
    public CaptureFailure? LastFailure { get; init; }
    public long LastFailureRequest { get; init; }
    public DateTimeOffset? LastSuccessfulFrameAt { get; init; }
    public string WindowRediscovery { get; init; } = "No previous HWND";
    public string? FrameHash { get; init; }
    public TimeSpan CaptureDuration { get; init; }
    public TimeSpan LocalizationDuration { get; init; }
    public bool Initialized { get; init; }
    public bool Running { get; init; }
    public DateTimeOffset? LastFrameAt { get; init; }
    public long LastFrameGeneration { get; init; }
    public int FrameWidth { get; init; }
    public int FrameHeight { get; init; }
    public int CropWidth { get; init; }
    public int CropHeight { get; init; }
    public bool LocalizationInvoked { get; init; }
    public string LocalizationResult { get; init; } = "not started";
    public string? Error { get; init; }
    public string? SavedPath { get; init; }
    public string? DiagnosticLogPath { get; init; }
    public string Format(TimeSpan timeout)
    {
        var w = Inspection.Window;
        static string Yes(bool value) => value ? "yes" : "no";
        return $"Stage: {Stage}\nCapture backend: {Backend}\n"
            + $"State: {State}; backend lifecycle: {NativeLifecycle?.State.ToString() ?? "not exposed"}; native request owners: {NativeLifecycle?.OwnedRequests.ToString() ?? "unknown"}; persistent D3D devices: {NativeLifecycle?.PersistentDevices.ToString() ?? "unknown"}; persistent capture items: {NativeLifecycle?.PersistentItems.ToString() ?? "unknown"}\n"
            + $"HWND rediscovery: {WindowRediscovery}\n"
            + $"Automatic placement: {(Error is not null ? "unavailable" : Backend.StartsWith("Windows.Graphics.Capture", StringComparison.Ordinal) ? "Windows capture" : Backend.StartsWith("GDI", StringComparison.Ordinal) ? "GDI fallback" : "waiting")}\n"
            + $"Arena process found: {Yes(Inspection.ProcessFound)}; PID: {w?.ProcessId.ToString() ?? "unresolved"}\n"
            + $"Arena HWND found: {Yes(w is not null)}; HWND: {(w is null ? "none" : $"0x{w.Handle.ToInt64():X}")}\n"
            + $"Title: {w?.Title ?? "unknown"}; Class: {w?.WindowClass ?? "unknown"}\n"
            + $"Visible: {Yes(w?.IsVisible == true)}; Minimized: {Yes(w?.IsMinimized == true)}; Foreground: {Yes(w?.IsForeground == true)} (not required)\n"
            + $"Client bounds (screen px): {(w is null ? "unknown" : $"{w.X}/{w.Y}/{w.Width}/{w.Height}")}; DPI scale: {w?.Scaling.ToString("F3", CultureInfo.InvariantCulture) ?? "unknown"}\n"
            + $"Integrity: Arena {w?.ArenaIntegrity ?? "unknown"}; DraftTG {w?.DraftTGIntegrity ?? "unknown"}\n"
            + $"Capture generation: {Generation}; source initialized: {Yes(Initialized)}; session running: {Yes(Running)}\n"
            + $"Capture request generation: {RequestGeneration}; requested pack generation: {RequestedPackGeneration}\n"
            + $"Fresh-frame timeout: {timeout.TotalSeconds:F1} s\nLast frame received: {LastFrameAt?.ToString("O") ?? "NEVER"}; frame generation: {LastFrameGeneration}\n"
            + $"Last successful frame across resets: {LastSuccessfulFrameAt?.ToString("O") ?? "NEVER"}\n"
            + $"Last frame dimensions: {FrameWidth} x {FrameHeight} (region-only); draft crop dimensions: {CropWidth} x {CropHeight}\n"
            + $"Frame hash (raw pixels): {FrameHash ?? "not computed"}; capture processing: {CaptureDuration.TotalMilliseconds:F1} ms; localization processing: {LocalizationDuration.TotalMilliseconds:F1} ms\n"
            + $"Localization invoked: {Yes(LocalizationInvoked)}; result: {LocalizationResult}\n"
            + (Inspection.Diagnostic is null ? "" : $"Window diagnostic: {Inspection.Diagnostic}\n")
            + (Error is null ? "" : $"Failure: {Error}\n") + (SavedPath is null ? "" : $"Debug capture: {SavedPath}\n")
            + (LastFailure is null ? "" : $"Last failure (capture request {LastFailureRequest}):\nFailure stage: {LastFailure.Operation}\n"
                + $"Exception: {LastFailure.ExceptionType}\nHRESULT: 0x{LastFailure.HResult:X8}\nMessage: {LastFailure.Message}\nRetry available\n")
            + (DiagnosticLogPath is null ? "" : $"Diagnostic log: {DiagnosticLogPath}\n");
    }
}

/// <summary>Acquisition/generation/timeout policy shared by production and fake-capture tests.</summary>
internal sealed class ArenaCaptureCoordinator(IArenaRegionCapture capture, ArenaCaptureOptions options) : IDisposable
{
    private readonly object _gate = new();
    private ArenaCaptureDiagnostics _diagnostics = new();
    private CancellationTokenSource? _active;
    private bool _disposed;
    public event Action? Changed;
    public ArenaCaptureDiagnostics Diagnostics { get { lock (_gate) return _diagnostics; } }
    public ArenaWindowGeometry? Window => Diagnostics.Inspection.Window;
    public long Generation => Diagnostics.Generation;
    public string DiagnosticText => Diagnostics.Format(options.FreshFrameTimeout);
    // Read-only evidence freshness check; does not restart capture or alter window tracking.
    internal ArenaWindowGeometry? InspectCurrentWindow() => capture.InspectWindow().Window;

    private void Update(Func<ArenaCaptureDiagnostics, ArenaCaptureDiagnostics> change, long? generation = null, long? request = null)
    {
        lock (_gate)
        {
            if (_disposed || (generation is not null && generation != _diagnostics.Generation)
                || (request is not null && request != _diagnostics.RequestGeneration)) return;
            _diagnostics = change(_diagnostics);
            if (capture is ICaptureLifecycleSource source) _diagnostics = _diagnostics with { NativeLifecycle = source.Lifecycle };
        }
        Changed?.Invoke();
    }

    public ArenaWindowGeometry? Observe(bool retainCompletedPlacementOnTranslation = false)
    {
        try
        {
            var observation = capture.InspectWindow();
            var previous = Window;
            var same = retainCompletedPlacementOnTranslation
                ? ArenaWindowGeometry.SamePlacementGeometry(previous, observation.Window)
                : ArenaWindowGeometry.SameCaptureGeometry(previous, observation.Window);
            if (!same) RestartCore("Window geometry discovered/changed");
            Update(d => d with { Inspection = observation, WindowRediscovery = previous?.Handle == observation.Window?.Handle ? d.WindowRediscovery
                : $"previous {(previous is null ? "none" : $"0x{previous.Handle.ToInt64():X}")}; current "
                    + (observation.Window is { } current ? $"0x{current.Handle.ToInt64():X}" : "none; " + observation.Diagnostic) });
            return observation.Window;
        }
        catch (Exception ex)
        {
            Fail("Window discovery", ex);
            throw;
        }
    }

    public void Restart()
    {
        Observe(); // Explicit Retry always rediscovers; there is no indefinitely cached HWND.
        RestartCore("Capture restart requested");
    }
    public void PackChanged() => RestartCore("Pack changed; previous frame/result invalidated");
    private void RestartCore(string stage)
    {
        lock (_gate) _active?.Cancel();
        Update(d => d with { Generation = d.Generation + 1, Stage = stage, State = CaptureLifecycleState.Idle, Initialized = false, Running = false,
            Backend = capture.Backend, FrameHash = null, CaptureDuration = TimeSpan.Zero, LocalizationDuration = TimeSpan.Zero,
            LastFrameAt = null, LastFrameGeneration = 0, FrameWidth = 0, FrameHeight = 0,
            CropWidth = 0, CropHeight = 0, LocalizationInvoked = false, LocalizationResult = "not started", Error = null, SavedPath = null, DiagnosticLogPath = null });
    }
    public void Note(string stage, long? generation = null) => Update(d => d with { Stage = stage }, generation);
    public void Fail(string stage, Exception exception, long? generation = null, long? request = null) => Update(d =>
    {
        var failure = CaptureFailure.From(stage, exception);
        if (capture is ICaptureLifecycleSource { Lifecycle.State: CaptureLifecycleState.Faulted } source
            && source.Lifecycle.LastFailure is { } exact && exact.ExceptionType == failure.ExceptionType
            && exact.HResult == failure.HResult && exact.Message == failure.Message) failure = exact;
        return d with { State = CaptureLifecycleState.Faulted,
        Stage = failure.Operation.EndsWith(" failed", StringComparison.Ordinal) ? failure.Operation : failure.Operation + " failed", Running = false,
        LastFailure = failure, LastFailureRequest = d.RequestGeneration,
        Error = $"{exception.GetType().Name}; HRESULT 0x{exception.HResult:X8}; "
            + (exception is System.ComponentModel.Win32Exception native ? $"Win32 {native.NativeErrorCode}; " : "") + exception.Message
        };
    }, generation, request);

    public async Task<ArenaRegionFrame> AcquireAsync(NormalizedDraftRegion region, CancellationToken cancellationToken = default,
        long packGeneration = 0)
    {
        var window = Observe();
        var generation = Generation;
        Task<ArenaRegionFrame>? task = null;
        ArenaRegionFrame? acquiredFrame = null;
        var frameReceived = false;
        CancellationTokenSource attempt;
        long requestGeneration;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _active?.Cancel();
            _active = attempt;
            _diagnostics = _diagnostics with { RequestGeneration = _diagnostics.RequestGeneration + 1,
                RequestedPackGeneration = packGeneration, LastFrameAt = null, LastFrameGeneration = 0 };
            requestGeneration = _diagnostics.RequestGeneration;
        }
        var requestedAt = DateTimeOffset.UtcNow;
        try
        {
            if (window is null) throw new InvalidOperationException(Diagnostics.Inspection.ProcessFound
                ? "Arena process found, but no eligible rendering HWND was found." : "Arena process not found.");
            if (!window.IsVisible || window.IsMinimized) throw new InvalidOperationException("Arena rendering window is hidden or minimized.");
            Update(d => d with { Stage = "Validating draft crop" }, generation, requestGeneration);
            var crop = ArenaDraftCrop.Calculate(window, region);
            Update(d => d with { Stage = "Starting fresh-frame acquisition", State = CaptureLifecycleState.Initializing, Initialized = false, Running = true, Error = null,
                CropWidth = crop.Width, CropHeight = crop.Height, LocalizationInvoked = false, LocalizationResult = "not started" }, generation, requestGeneration);
            task = capture.CaptureAsync(window, region, generation, stage => Update(d => d with
            {
                Stage = stage,
                Backend = capture.Backend,
                State = stage.StartsWith("Waiting for previous WGC request", StringComparison.Ordinal) ? CaptureLifecycleState.Queued
                    : stage == "Capture source initialized" ? CaptureLifecycleState.Capturing
                    : stage.StartsWith("Disposing", StringComparison.Ordinal) ? CaptureLifecycleState.Disposing : d.State,
                Initialized = d.Initialized || stage == "Capture source initialized"
            }, generation, requestGeneration), attempt.Token);
            var frame = await task.WaitAsync(options.FreshFrameTimeout, attempt.Token);
            acquiredFrame = frame;
            frameReceived = true;
            Update(d => d with { Stage = "Fresh frame received", State = CaptureLifecycleState.FrameReady, Running = false, LastFrameAt = frame.CapturedAt,
                Backend = frame.Backend, CaptureDuration = frame.CaptureDuration,
                LastFrameGeneration = frame.Generation, FrameWidth = frame.Image.Width, FrameHeight = frame.Image.Height }, generation, requestGeneration);
            if (frame.Generation != Generation || frame.Generation != generation)
            { throw new OperationCanceledException("Frame belongs to an older capture generation.", attempt.Token); }
            attempt.Token.ThrowIfCancellationRequested();
            if (frame.CapturedAt < requestedAt || (frame.PackGeneration != 0 && frame.PackGeneration != packGeneration)
                || (frame.CaptureRequestGeneration != 0 && frame.CaptureRequestGeneration != requestGeneration))
                throw new InvalidDataException("Stale frame rejected: timestamp or request/pack generation differs from this acquisition.");
            var current = Observe();
            if (generation != Generation || !ArenaWindowGeometry.SameCaptureGeometry(window, current))
            { throw new OperationCanceledException("Arena geometry changed before frame delivery.", attempt.Token); }
            if (frame.Image.Width != crop.Width || frame.Image.Height != crop.Height)
            {
                throw new InvalidDataException("Fresh frame received, but draft-region crop dimensions are outside the delivered capture bounds.");
            }
            Update(d => d with { Stage = "Draft crop produced", LastSuccessfulFrameAt = frame.CapturedAt,
                CropWidth = frame.Image.Width, CropHeight = frame.Image.Height }, generation, requestGeneration);
            acquiredFrame = null; // Ownership transfers to the localization/debug caller only on success.
            return frame with { CaptureRequestGeneration = requestGeneration, PackGeneration = packGeneration };
        }
        catch (TimeoutException)
        {
            attempt.Cancel();
            var error = new TimeoutException($"Capture started but no frame was received within {options.FreshFrameTimeout.TotalSeconds:F1} s.");
            Fail("Fresh-frame acquisition (last operation: " + Diagnostics.Stage + ")", error, generation, requestGeneration);
            if (task is not null && !frameReceived) DisposeLateFrame(task);
            throw error;
        }
        catch (OperationCanceledException)
        {
            attempt.Cancel();
            if (task is not null && !frameReceived) DisposeLateFrame(task);
            Update(d => d with { Stage = "Capture canceled / stale generation rejected", State = CaptureLifecycleState.Idle, Running = false }, generation, requestGeneration);
            throw;
        }
        catch (Exception ex)
        {
            Fail(Diagnostics.Stage, ex, generation, requestGeneration);
            throw;
        }
        finally
        {
            acquiredFrame?.Dispose();
            lock (_gate) { if (ReferenceEquals(_active, attempt)) _active = null; }
            // A timed-out native worker may still access token.WaitHandle during cleanup.
            // Disposing the source here used to invalidate that handle underneath it.
            if (task is { IsCompleted: false })
                _ = task.ContinueWith(_ => attempt.Dispose(), CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            else attempt.Dispose();
        }
    }
    private static void DisposeLateFrame(Task<ArenaRegionFrame> task) => _ = task.ContinueWith(t =>
    {
        if (t.Status == TaskStatus.RanToCompletion) t.Result.Dispose();
        else if (t.IsFaulted) _ = t.Exception;
    }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    public void LocalizationStarted(long? generation = null) => Update(d => d with { Stage = "Artwork localization invoked", LocalizationInvoked = true }, generation);
    public void SynchronizedFrame(ArenaRegionFrame frame) => Update(d => d with
    { Backend = frame.Backend, FrameHash = frame.ImageHash, CaptureDuration = frame.CaptureDuration }, frame.Generation);
    public void LocalizationCompleted(int matched, int expected, long? generation = null, TimeSpan? duration = null) => Update(d => d with
    { Stage = matched == expected ? $"Matched {matched}/{expected}" : $"Ambiguous {matched}/{expected}; manual confirmation available",
        LocalizationResult = $"matched {matched}/{expected}", Running = false, LocalizationDuration = duration ?? TimeSpan.Zero }, generation);
    public void Saved(string path) => Update(d => d with { Stage = "Debug draft capture saved", SavedPath = path });
    public void DiagnosticLogSaved(string path) => Update(d => d with { DiagnosticLogPath = path });
    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; _active?.Cancel(); }
        (capture as IDisposable)?.Dispose();
    }
}
