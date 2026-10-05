using DraftTG.Application;

namespace DraftTG.App.Platform;

internal interface ICaptureFailureLocation { string? Operation { get; } }
internal sealed class CaptureBackendInitializationException : IOException, ICaptureFailureLocation
{
    public CaptureBackendInitializationException(string message, Exception? inner = null, string? operation = null) : base(message, inner)
    { Operation = operation; if (inner is not null) HResult = inner.HResult; }
    public string? Operation { get; }
}
internal sealed class CaptureBackendRuntimeException : IOException, ICaptureFailureLocation
{
    public CaptureBackendRuntimeException(string operation, Exception inner) : base(operation + " failed: " + inner.Message, inner)
    { Operation = operation; HResult = inner.HResult; }
    public string Operation { get; }
}

/// <summary>Only initialization failures may opt into GDI. Timeouts, cancellation and runtime/device errors never silently switch backend.</summary>
internal sealed class PreferredArenaFrameCapture(IArenaRegionCapture primary, IArenaRegionCapture fallback,
    bool allowGdiFallback = false) : IArenaRegionCapture, ICaptureLifecycleSource, IDisposable
{
    public CaptureLifecycleSnapshot Lifecycle => primary is ICaptureLifecycleSource source ? source.Lifecycle
        : new(CaptureLifecycleState.Idle, "Backend does not expose lifecycle", 0, 0);
    private string? _backend;
    public string Backend => _backend ?? primary.Backend;
    public ArenaWindowInspection InspectWindow() => primary.InspectWindow();
    public void Dispose() { (primary as IDisposable)?.Dispose(); if (!ReferenceEquals(primary, fallback)) (fallback as IDisposable)?.Dispose(); }
    public async Task<ArenaRegionFrame> CaptureAsync(ArenaWindowGeometry window, NormalizedDraftRegion region,
        long generation, Action<string> stage, CancellationToken token)
    {
        _backend = primary.Backend;
        stage("Capture backend: " + Backend);
        try { return await primary.CaptureAsync(window, region, generation, stage, token); }
        catch (CaptureBackendInitializationException ex) when (allowGdiFallback && !token.IsCancellationRequested)
        {
            token.ThrowIfCancellationRequested();
            _backend = fallback.Backend;
            stage($"Capture backend: {Backend}; WGC initialization failed: {ex.Message}");
            var frame = await fallback.CaptureAsync(window, region, generation, stage, token);
            return frame with { Backend = fallback.Backend + "; WGC initialization failed: " + ex.Message };
        }
    }
}

internal static class WindowsCaptureFactory
{
    public static IArenaRegionCapture? CreateForCurrentPlatform() => OperatingSystem.IsWindows() ? Create(
        Environment.GetEnvironmentVariable("DRAFTTG_CAPTURE_BACKEND"),
        Environment.GetEnvironmentVariable("DRAFTTG_ALLOW_GDI_FALLBACK") == "1") : null;

    internal static IArenaRegionCapture Create(string? selector, bool allowGdiFallback,
        IArenaRegionCapture? primary = null, IArenaRegionCapture? fallback = null)
    {
        fallback ??= new GdiBitBltFrameCapture();
        if (string.Equals(selector, "gdi", StringComparison.OrdinalIgnoreCase)) return fallback;
        if (!string.IsNullOrEmpty(selector) && !string.Equals(selector, "wgc", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("DRAFTTG_CAPTURE_BACKEND must be wgc or gdi.");
        primary ??= new WindowsGraphicsCaptureFrameCapture();
        return new PreferredArenaFrameCapture(primary, fallback, allowGdiFallback);
    }
}
