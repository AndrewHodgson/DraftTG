using System.Diagnostics;
using DraftTG.Application;

namespace DraftTG.App.Platform;

/// <summary>Opt-in benchmark backend. Owns only its own native session; the production factory is unchanged.</summary>
internal sealed class DesktopDuplicationFrameCapture : IArenaRegionCapture, IDisposable
{
    public string Backend => "DXGI Desktop Duplication";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private DesktopDuplicationMetrics _metrics = new(0, 0, 0, 0, null, null, 0, 0, 0, "Idle");
    private int _disposed;
#if WINDOWS
    private DesktopDuplicationNativeSession? _session;
#endif
    public DesktopDuplicationMetrics Metrics => Volatile.Read(ref _metrics);
    public ArenaWindowInspection InspectWindow()
    {
#if WINDOWS
        using var dpi = PhysicalPixelContext.Enter();
#endif
        return new GdiBitBltFrameCapture().InspectWindow();
    }
    public async Task<ArenaRegionFrame> CaptureAsync(ArenaWindowGeometry window, NormalizedDraftRegion region,
        long generation, Action<string> stage, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => CaptureCore(window, region, generation, stage, linked.Token), linked.Token).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }
    private ArenaRegionFrame CaptureCore(ArenaWindowGeometry window, NormalizedDraftRegion region, long generation,
        Action<string> stage, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        Volatile.Write(ref _metrics, new(generation, 0, 0, 0, null, null, 0, 0, 0, "Validating physical Arena bounds"));
        void Progress(string text) { Volatile.Write(ref _metrics, Metrics with { Stage = text }); stage(text); }
        try
        {
#if WINDOWS
            using var dpi = PhysicalPixelContext.Enter();
            Progress("Validating physical Arena bounds");
            GdiBitBltFrameCapture.ValidateWindow(window);
            var crop = ArenaDraftCrop.Calculate(window, region);
            GdiBitBltFrameCapture.EnsureUnobstructed(window, crop);
            token.ThrowIfCancellationRequested();
            Progress("Selecting output and initializing Desktop Duplication");
            var init = Stopwatch.StartNew();
            if (_session is null || !_session.Matches(window))
            { _session?.Dispose(); _session = null; _session = new(window); }
            Volatile.Write(ref _metrics, Metrics with { Output = _session.Output.Name, InitializationMs = init.Elapsed.TotalMilliseconds });
            Progress("Capture source initialized");
            var bitmap = _session.CopyCurrentCrop(window, crop, Progress,
                update => Volatile.Write(ref _metrics, update(Metrics)), token);
            try
            {
                token.ThrowIfCancellationRequested(); GdiBitBltFrameCapture.ValidateWindow(window);
                GdiBitBltFrameCapture.EnsureUnobstructed(window, crop);
                return new(bitmap, window.X + crop.X, window.Y + crop.Y, window.Scaling)
                {
                    Backend = Backend, WindowHandle = window.Handle, Generation = generation,
                    CapturedAt = DateTimeOffset.UtcNow, CaptureDuration = watch.Elapsed,
                    PresentationTime = _session.PresentationTime
                };
            }
            catch { bitmap.Dispose(); throw; }
#else
            _ = window; _ = region; _ = token; Progress("Desktop Duplication platform support");
            throw new CaptureBackendInitializationException("Desktop Duplication is unavailable in this portable build.", operation: "DD platform support");
#endif
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _metrics, Metrics with { Failure = $"{ex.GetType().Name}; 0x{ex.HResult:X8}; {ex.Message}" });
#if WINDOWS
            _session?.Dispose(); _session = null; // Faults invalidate only this DD owner, never WGC.
#endif
            throw;
        }
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetime.Cancel(); _gate.Wait();
        try
        {
#if WINDOWS
            _session?.Dispose(); _session = null;
#endif
        }
        finally { _gate.Release(); }
        // Do not dispose the lifetime CTS while a canceled queued request may still link to its token.
    }
}
