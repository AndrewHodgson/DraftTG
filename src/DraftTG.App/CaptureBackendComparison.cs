using System.Diagnostics;
using DraftTG.Application;
using DraftTG.App.Platform;
using SkiaSharp;

namespace DraftTG.App;

internal sealed record CaptureBenchmarkRecord(string Backend, DateTimeOffset RequestedAt, DateTimeOffset FinishedAt,
    long PackGeneration, long CaptureRequestGeneration, long CaptureGeneration, int? ExpectedCards,
    bool FreshFrameAccepted, int Width, int Height, string? Sha256, string? PerceptualHash,
    double TotalMs, double CaptureMs, double InitializationMs, double AcquisitionMs, double CropMs, double LocalizationMs, int? Matched, int? DetectedRectangles,
    bool? SafePlacement, string BadgePlacement, string Diagnostic, DesktopDuplicationMetrics? DesktopDuplication,
    int CaptureAttempts, IReadOnlyList<DesktopDuplicationMetrics> DesktopDuplicationAttempts, string? DebugImage = null);
internal sealed record CaptureBenchmarkPair(IReadOnlyList<CaptureBenchmarkRecord> Records, string Outcome,
    string? SelectedBackend, bool ManualCaptureFallbackRequired);

/// <summary>Developer shadow comparison, not a production fallback policy. Owners and synchronization are independent.</summary>
internal sealed class CaptureBackendComparison : IDisposable
{
    private sealed record Branch(IArenaRegionCapture Backend, ArenaCaptureCoordinator Coordinator, PackFrameSynchronizer Synchronizer);
    private readonly Branch[] _branches;
    private readonly Func<CardVisualLocalizationRequest, ArenaRegionFrame, CancellationToken, Task<CardVisualLocalizationResult>>? _recognize;
    private readonly Func<CaptureBenchmarkRecord, ArenaRegionFrame, string?>? _save;
    private readonly SemaphoreSlim _pairGate = new(1, 1);
    public CaptureBackendComparison(IArenaRegionCapture primary, IArenaRegionCapture secondary, ArenaCaptureOptions options,
        PackFrameTiming timing,
        Func<CardVisualLocalizationRequest, ArenaRegionFrame, CancellationToken, Task<CardVisualLocalizationResult>>? recognize = null,
        Func<CaptureBenchmarkRecord, ArenaRegionFrame, string?>? save = null)
    {
        if (ReferenceEquals(primary, secondary)) throw new ArgumentException("Comparison requires independent backend instances.");
        _branches = [new(primary, new(primary, options), new(timing)), new(secondary, new(secondary, options), new(timing))];
        _recognize = recognize; _save = save;
    }
    public void Activate(PackFrameContext? context)
    {
        foreach (var b in _branches) { b.Synchronizer.Activate(context); b.Coordinator.PackChanged(); }
    }
    public async Task<CaptureBenchmarkPair> CompareAsync(PackFrameContext? context, ArenaWindowGeometry window,
        NormalizedDraftRegion region, CancellationToken token)
    {
        await _pairGate.WaitAsync(token);
        try
        {
        var records = await Task.WhenAll(_branches.Select(b => AcquireAsync(b, context, window, region, token)));
        if (context is not null && _branches.Any(b => !b.Synchronizer.IsCurrent(context)))
            records = records.Select(r => r with { FreshFrameAccepted = false, SafePlacement = false,
                Diagnostic = "Stale comparison rejected after pack change.\n" + r.Diagnostic }).ToArray();
        var a = records[0].FreshFrameAccepted; var b = records[1].FreshFrameAccepted;
        var outcome = a && b ? records[0].Sha256 == records[1].Sha256 ? "A: both accepted; identical pixels" : "B: both accepted; different pixels"
            : !a && b ? "C: primary failed; secondary accepted" : a ? "D: primary accepted; secondary failed" : "E: both failed";
        return new(records, outcome, records.FirstOrDefault(r => r.FreshFrameAccepted)?.Backend, !a && !b);
        }
        finally { _pairGate.Release(); }
    }
    private async Task<CaptureBenchmarkRecord> AcquireAsync(Branch branch, PackFrameContext? context,
        ArenaWindowGeometry window, NormalizedDraftRegion region, CancellationToken token)
    {
        var requested = DateTimeOffset.UtcNow; var watch = Stopwatch.StartNew(); var localization = TimeSpan.Zero;
        ArenaRegionFrame? frame = null; CardVisualLocalizationResult? result = null;
        var accepted = false; string? hash = null, perceptual = null; var diagnostic = "Not started";
        var progress = new List<string>(); var attempts = new List<DesktopDuplicationMetrics>(); var captureAttempts = 0;
        var initializedMs = 0d; var copyStartedMs = 0d; var captureStartedMs = 0d; var captureFinishedMs = 0d;
        void Changed()
        {
            var stage = branch.Coordinator.Diagnostics.Stage;
            if (stage == "Capture source initialized" && initializedMs == 0) initializedMs = watch.Elapsed.TotalMilliseconds;
            if (stage.StartsWith("Texture readback", StringComparison.Ordinal) || stage.StartsWith("Copying calibrated", StringComparison.Ordinal))
                copyStartedMs = watch.Elapsed.TotalMilliseconds;
        }
        branch.Coordinator.Changed += Changed;
        async Task<ArenaRegionFrame> Capture(CancellationToken ct)
        {
            captureAttempts++;
            captureStartedMs = watch.Elapsed.TotalMilliseconds; initializedMs = 0; copyStartedMs = 0;
            try { return await branch.Coordinator.AcquireAsync(region, ct, context?.Request.PackGeneration ?? 0); }
            finally { captureFinishedMs = watch.Elapsed.TotalMilliseconds; if (branch.Backend is DesktopDuplicationFrameCapture dd) attempts.Add(dd.Metrics); }
        }
        try
        {
            if (!ArenaWindowGeometry.SameCaptureGeometry(window, branch.Coordinator.Observe()))
                throw new InvalidDataException("Arena geometry differs from comparison snapshot.");
            frame = context is null
                ? await Capture(token)
                : await branch.Synchronizer.AcquireAsync(context,
                    Capture, progress.Add, token);
            hash = frame.ImageHash ?? PackFrameSynchronizer.Hash(frame.Image);
            frame = frame with { ImageHash = hash }; perceptual = DifferenceHash(frame.Image);
            branch.Coordinator.SynchronizedFrame(frame);
            if (context is not null && _recognize is not null)
            {
                var matchWatch = Stopwatch.StartNew();
                result = await _recognize(context.Request, frame, token);
                localization = matchWatch.Elapsed;
                if (!branch.Synchronizer.IsCurrent(context)) throw new OperationCanceledException("Pack changed during recognition.", token);
                result = result with { PackGeneration = frame.PackGeneration, CaptureRequestGeneration = frame.CaptureRequestGeneration };
                await branch.Synchronizer.RecordConfirmedCardsAsync(context, frame, result, token);
                branch.Coordinator.LocalizationCompleted(result.Matches.Count, context.Request.Occurrences.Count, frame.Generation, result.Duration);
            }
            token.ThrowIfCancellationRequested();
            if (context is not null && !branch.Synchronizer.IsCurrent(context)) throw new OperationCanceledException("Pack changed before comparison result.", token);
            accepted = true;
            diagnostic = context is null ? "Capture-only: no semantic pack identity/currentness or recognition claim." : "Freshness policy accepted; physical card count/placement still requires validation.";
        }
        catch (Exception ex)
        {
            diagnostic = $"{ex.GetType().Name}; 0x{ex.HResult:X8}; {ex.Message}";
        }
        try
        {
            var safe = context is not null && result is not null ? result.IsSafeFor(context.Request) : (bool?)null;
            branch.Coordinator.Changed -= Changed;
            var ddMetrics = branch.Backend is DesktopDuplicationFrameCapture desktop ? desktop.Metrics : null;
            var nativeMs = frame?.CaptureDuration.TotalMilliseconds ?? 0;
            var record = new CaptureBenchmarkRecord(branch.Backend.Backend, requested, DateTimeOffset.UtcNow,
                context?.Request.PackGeneration ?? 0, frame?.CaptureRequestGeneration ?? branch.Coordinator.Diagnostics.RequestGeneration,
                frame?.Generation ?? branch.Coordinator.Generation, context?.Request.Occurrences.Count,
                accepted, frame?.Image.Width ?? 0, frame?.Image.Height ?? 0, hash, perceptual,
                watch.Elapsed.TotalMilliseconds, nativeMs, ddMetrics?.InitializationMs ?? Math.Max(0, initializedMs - captureStartedMs),
                ddMetrics?.AcquisitionMs ?? (copyStartedMs > initializedMs ? copyStartedMs - initializedMs : 0),
                ddMetrics?.CropMs ?? (copyStartedMs > 0 ? Math.Max(0, captureFinishedMs - copyStartedMs) : 0), localization.TotalMilliseconds,
                result?.Matches.Count, result?.Rectangles.Count, accepted ? safe : false,
                "Not displayed: developer shadow benchmark; no production badge application.",
                diagnostic + "\n" + string.Join('\n', progress) + "\n" + branch.Coordinator.DiagnosticText,
                ddMetrics, captureAttempts, attempts);
            if (accepted && frame is not null && _save is not null)
            {
                token.ThrowIfCancellationRequested();
                try { record = record with { DebugImage = _save(record, frame) }; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { record = record with { Diagnostic = record.Diagnostic + $"\nDebug save failed: {ex.GetType().Name}; 0x{ex.HResult:X8}" }; }
            }
            return record;
        }
        finally { frame?.Dispose(); }
    }
    internal static string DifferenceHash(SKBitmap image)
    {
        using var small = new SKBitmap(9, 8);
        using (var canvas = new SKCanvas(small)) canvas.DrawBitmap(image, new SKRect(0, 0, 9, 8));
        static int Luma(SKColor c) => c.Red * 299 + c.Green * 587 + c.Blue * 114;
        ulong hash = 0;
        for (var y = 0; y < 8; y++) for (var x = 0; x < 8; x++)
            if (Luma(small.GetPixel(x, y)) > Luma(small.GetPixel(x + 1, y))) hash |= 1UL << (y * 8 + x);
        return hash.ToString("X16", System.Globalization.CultureInfo.InvariantCulture);
    }
    public void Dispose()
    {
        foreach (var b in _branches) { b.Synchronizer.Dispose(); b.Coordinator.Dispose(); }
    }
}
