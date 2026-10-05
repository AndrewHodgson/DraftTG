using System.Diagnostics;
using DraftTG.Application;
#if WINDOWS
using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
#endif

namespace DraftTG.App.Platform;

/// <summary>One HWND session/pool per request; one device/item per validated geometry on the owning MTA. Only the calibrated ROI is read back.</summary>
internal sealed class WindowsGraphicsCaptureFrameCapture : IArenaRegionCapture, ICaptureLifecycleSource, IDisposable
{
    public string Backend => "Windows.Graphics.Capture";
    private readonly Func<ArenaWindowInspection> _inspect;
    private readonly WgcCaptureLifecycle _lifecycle;
#if WINDOWS
    private readonly WgcCaptureWorker? _worker;
    private WgcDirect3DInterop? _ownedDevice;
    private GraphicsCaptureItem? _ownedItem;
    private ArenaWindowGeometry? _itemGeometry;
#endif
    public CaptureLifecycleSnapshot Lifecycle => _lifecycle.Lifecycle
#if WINDOWS
        with { PersistentDevices = _ownedDevice is null ? 0 : 1, PersistentItems = _ownedItem is null ? 0 : 1 }
#endif
        ;
    public WindowsGraphicsCaptureFrameCapture(Func<ArenaWindowInspection>? inspect = null, Func<IWgcCaptureRequest>? factory = null)
    {
        _inspect = inspect ?? new GdiBitBltFrameCapture().InspectWindow;
#if WINDOWS
        if (factory is null) _worker = new(ResetRetainedResources);
        _lifecycle = new(factory ?? (() => new NativeRequest(this)), _worker is null ? null : _worker.RunAsync);
#else
        _lifecycle = new(factory ?? (() => new NativeRequest(this)));
#endif
    }
    public ArenaWindowInspection InspectWindow() => _inspect();
    public void Dispose()
    {
#if WINDOWS
        _worker?.Dispose();
#endif
    }
    private sealed class NativeRequest(WindowsGraphicsCaptureFrameCapture owner) : IWgcCaptureRequest
    {
        public ArenaRegionFrame Capture(ArenaWindowGeometry window, NormalizedDraftRegion region, long generation,
            Action<string> stage, CancellationToken token)
        {
#if WINDOWS
            return owner.CaptureCore(window, region, generation, stage, token);
#else
            _ = owner; // This target has no native backend state to own.
            throw new CaptureBackendInitializationException("Windows Graphics Capture is unavailable in this portable build.", operation: "WGC platform support");
#endif
        }
        // CaptureCore's nested finally releases native resources on the same owning MTA worker before returning.
        public void Dispose() { }
    }

    public async Task<ArenaRegionFrame> CaptureAsync(ArenaWindowGeometry window, NormalizedDraftRegion region,
        long generation, Action<string> stage, CancellationToken token)
    {
        return await _lifecycle.CaptureAsync(window, region, generation, stage, token);
    }

#if WINDOWS
    private ArenaRegionFrame CaptureCore(ArenaWindowGeometry window, NormalizedDraftRegion region,
        long generation, Action<string> stage, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        var operation = "Arena HWND acquisition / validation";
        token.ThrowIfCancellationRequested();
        stage("Arena HWND acquisition / validation");
        try { GdiBitBltFrameCapture.ValidateWindow(window); }
        catch (Exception ex)
        {
            try { ResetRetainedResources(); }
            catch (Exception cleanup) { Trace.TraceError(CaptureFailure.From("Invalid HWND resource cleanup", cleanup).Text); }
            var current = InspectWindow();
            throw new InvalidOperationException($"Arena HWND validation failed; previous 0x{window.Handle.ToInt64():X}; rediscovered "
                + (current.Window is { } found ? $"0x{found.Handle.ToInt64():X}; retry uses fresh geometry." : "none: " + current.Diagnostic), ex);
        }
        var crop = ArenaDraftCrop.Calculate(window, region);
        stage("WGC worker apartment initialization");
        // WgcCaptureWorker owns one MTA apartment until the backend is disposed.
        try
        {
            stage("Initializing Windows.Graphics.Capture HWND session");
            WgcDirect3DInterop? device = null;
            Direct3D11CaptureFramePool? pool = null;
            GraphicsCaptureSession? session = null;
            GraphicsCaptureItem? item = null;
            ArenaRegionFrame? captured = null;
            Exception? requestFailure = null;
            try
            {
                WindowCaptureBounds bounds;
                Windows.Graphics.SizeInt32 surfaceSize;
                void Initializing(string next) { operation = next; stage(next); token.ThrowIfCancellationRequested(); }
                try
                {
                    Initializing("WGC platform support");
                    if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362) || !GraphicsCaptureSession.IsSupported())
                        throw new PlatformNotSupportedException("WGC requires Windows 10 build 18362+ and capture support.");
                    Initializing(_ownedItem is not null && ArenaWindowGeometry.SameCaptureGeometry(_itemGeometry, window)
                        ? "GraphicsCaptureItem reuse for validated HWND" : "GraphicsCaptureItem creation");
                    if (_ownedItem is null || !ArenaWindowGeometry.SameCaptureGeometry(_itemGeometry, window))
                    { _ownedItem = WgcDirect3DInterop.CreateItem(window.Handle); _itemGeometry = window; }
                    item = _ownedItem;
                    Initializing("GraphicsCaptureItem dimensions / surface bounds");
                    surfaceSize = item.Size;
                    bounds = SurfaceBounds(window, surfaceSize.Width, surfaceSize.Height);
                    Initializing(_ownedDevice is null ? "D3D device creation" : "D3D device reuse on owning MTA");
                    device = _ownedDevice ??= new WgcDirect3DInterop();
                    Initializing("Frame pool creation");
                    pool = Direct3D11CaptureFramePool.CreateFreeThreaded(device.WinrtDevice,
                        DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, surfaceSize);
                    Initializing("Capture session creation");
                    session = pool.CreateCaptureSession(item);
                    Initializing("Capture session configuration");
                    if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) session.IsCursorCaptureEnabled = false;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                { throw new CaptureBackendInitializationException(operation + " failed: " + ex.Message, ex, operation); }
                token.ThrowIfCancellationRequested();
                stage("Capture source initialized");
                var requestedPresentation = TimeSpan.FromSeconds(Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
                operation = "Session StartCapture"; stage(operation);
                session.StartCapture(); // A failure here is a runtime failure, never a silent GDI fallback.
                operation = "Waiting for Windows.Graphics.Capture frame"; stage(operation);
                // TryGetNextFrame polling is supported by WGC. Avoid managed event/COM callback lifetimes entirely.
                var cancellationSignal = token.WaitHandle;
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    // Also detect target closure when no frame arrives, without an extra item event subscription.
                    GdiBitBltFrameCapture.ValidateWindow(window);
                    using var frame = pool.TryGetNextFrame();
                    if (frame is null)
                    { cancellationSignal.WaitOne(10); continue; }
                    if (frame.ContentSize.Width != surfaceSize.Width || frame.ContentSize.Height != surfaceSize.Height)
                        throw new InvalidDataException("Arena WGC content size changed; discard frame and reinitialize on next request.");
                    if (frame.SystemRelativeTime < requestedPresentation) continue;
                    GdiBitBltFrameCapture.ValidateWindow(window);
                    if (SurfaceBounds(window, surfaceSize.Width, surfaceSize.Height) != bounds)
                        throw new InvalidDataException("Arena WGC surface geometry changed during acquisition.");
                    operation = "Texture readback / draft-region copy"; stage(operation);
                    var bitmap = device.CopyCrop(frame.Surface, bounds.MapCrop(window, crop));
                    try
                    {
                        token.ThrowIfCancellationRequested();
                        GdiBitBltFrameCapture.ValidateWindow(window);
                        return captured = new(bitmap, window.X + crop.X, window.Y + crop.Y, window.Scaling)
                        {
                            WindowHandle = window.Handle, Backend = Backend, Generation = generation,
                            CapturedAt = DateTimeOffset.UtcNow, CaptureDuration = watch.Elapsed,
                            PresentationTime = frame.SystemRelativeTime
                        };
                    }
                    catch { bitmap.Dispose(); throw; }
                }
            }
            catch (Exception ex) { requestFailure = ex; throw; }
            finally
            {
                Exception? cleanupFailure = null;
                void Cleanup(string cleanupOperation, Action release)
                {
                    try { release(); }
                    catch (Exception ex)
                    {
                        var failure = new CaptureBackendRuntimeException(cleanupOperation, ex);
                        if (cleanupFailure is null) cleanupFailure = failure;
                        else Trace.TraceError(CaptureFailure.From(cleanupOperation, failure).Text);
                    }
                }
                Cleanup("WGC disposal notification", () => stage("Disposing native WGC session/pool"));
                Cleanup("Capture session disposal", () => session?.Dispose());
                Cleanup("Frame pool disposal", () => pool?.Dispose());
                // The retained GraphicsCaptureItem has no Close/IClosable. Its projection owns/releases the COM reference.
                // A frame's own Close can throw while unwinding a successful return; its bitmap has not transferred yet.
                if (requestFailure is not null || cleanupFailure is not null) captured?.Dispose();
                if (cleanupFailure is not null)
                {
                    if (requestFailure is null) throw cleanupFailure;
                    Trace.TraceError(CaptureFailure.From("Secondary native WGC cleanup failure", cleanupFailure).Text);
                    // A failed cleanup also invalidates a retained device, even when the primary failure was cancellation.
                    try { ResetRetainedResources(); }
                    catch (Exception ex) { Trace.TraceError(CaptureFailure.From("Faulted D3D device cleanup", ex).Text); }
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            try { ResetRetainedResources(); }
            catch (Exception cleanup) { System.Diagnostics.Trace.TraceError(CaptureFailure.From("Faulted D3D device cleanup", cleanup).Text); }
            if (ex is ICaptureFailureLocation) throw;
            throw new CaptureBackendRuntimeException(operation, ex);
        }
    }

    private void ResetRetainedResources()
    {
        _ownedItem = null; _itemGeometry = null;
        var device = _ownedDevice; _ownedDevice = null; device?.Dispose();
    }

    private static WindowCaptureBounds SurfaceBounds(ArenaWindowGeometry client, int width, int height)
    {
        // WGC captures window bounds, while calibration uses client pixels. Do not infer an aspect/scale transform.
        if (DwmGetWindowAttribute(client.Handle, 9, out var extended, Marshal.SizeOf<NativeRect>()) >= 0
            && extended.Right - extended.Left == width && extended.Bottom - extended.Top == height)
            return new(extended.Left, extended.Top, width, height);
        if (GetWindowRect(client.Handle, out var window)
            && window.Right - window.Left == width && window.Bottom - window.Top == height)
            return new(window.Left, window.Top, width, height);
        throw new InvalidDataException($"WGC surface {width}x{height} does not match Arena window bounds; refusing guessed crop coordinates.");
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out NativeRect value, int size);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);
#endif
}
