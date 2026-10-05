using System.Runtime.InteropServices;
using DraftTG.App.Platform;
using DraftTG.Application;
using SkiaSharp;

namespace DraftTG.App.Tests;

public sealed partial class MainWindowViewModelTests
{
    private sealed class ScopedWgc(Action<ScopedWgc, CancellationToken>? action = null) : IWgcCaptureRequest
    {
        public int Disposals;
        public Exception? DisposalError;
        public SKBitmap? Pixels;
        public ArenaRegionFrame Capture(ArenaWindowGeometry w, NormalizedDraftRegion r, long g, Action<string> stage, CancellationToken token)
        {
            action?.Invoke(this, token); stage("Capture source initialized");
            var crop = ArenaDraftCrop.Calculate(w, r);
            return new(Pixels = new SKBitmap(crop.Width, crop.Height), w.X + crop.X, w.Y + crop.Y, w.Scaling)
                { Backend = "Windows.Graphics.Capture", Generation = g, WindowHandle = w.Handle };
        }
        public void Dispose() { Disposals++; if (DisposalError is not null) throw DisposalError; }
    }
    private static WindowsGraphicsCaptureFrameCapture ScopedBackend(Func<IWgcCaptureRequest> create) =>
        new(() => new FakeArenaCapture().Inspection, create);
    private static Task<ArenaRegionFrame> ScopedAcquire(WindowsGraphicsCaptureFrameCapture backend, CancellationToken token = default) =>
        backend.CaptureAsync(backend.InspectWindow().Window!, CaptureRegion, 1, _ => { }, token);

    [Fact]
    public async Task HundredScopedCapturesDisposeEachRequestAndPassStressResourceChecks()
    {
        var scopes = new List<ScopedWgc>(); var backend = ScopedBackend(() => { var s = new ScopedWgc(); scopes.Add(s); return s; });
        var result = await WgcStressCommand.RunCyclesAsync(backend, CaptureRegion, 100);
        Assert.Equal(100, result.Succeeded); Assert.True(result.ResourcesBounded); Assert.Equal(100, backend.Lifecycle.CompletedRequests);
        Assert.Equal(0, backend.Lifecycle.OwnedRequests); Assert.Equal(CaptureLifecycleState.FrameReady, backend.Lifecycle.State);
        Assert.All(scopes, s => { Assert.Equal(1, s.Disposals); Assert.Equal((nint)0, s.Pixels!.Handle); });
    }

    [Fact]
    public async Task PartialInitializationFailuresDisposeAndPreserveExactStageHresultThenRecover()
    {
        foreach (var stage in new[] { "GraphicsCaptureItem creation", "D3D device creation", "Frame pool creation", "Capture session creation", "Session StartCapture", "Texture readback" })
        {
            var failed = new ScopedWgc((_, _) => throw new CaptureBackendInitializationException("failed", new COMException("native failure", unchecked((int)0x887A0005)), stage));
            var next = new ScopedWgc(); var calls = 0; var backend = ScopedBackend(() => calls++ == 0 ? failed : next);
            await Assert.ThrowsAsync<CaptureBackendInitializationException>(() => ScopedAcquire(backend));
            Assert.Equal(1, failed.Disposals); Assert.Equal(stage, backend.Lifecycle.LastFailure!.Operation);
            Assert.Equal(unchecked((int)0x887A0005), backend.Lifecycle.LastFailure.HResult);
            using var frame = await ScopedAcquire(backend); Assert.Equal(1, next.Disposals); Assert.Equal(0, backend.Lifecycle.OwnedRequests);
        }
    }

    [Fact]
    public async Task WorkerInitializationFailureHasExactDiagnosticsAndCanRetryBeforeScopeCreation()
    {
        var scopes = 0; var attempts = 0;
        var lifecycle = new WgcCaptureLifecycle(() => { scopes++; return new ScopedWgc(); }, work => ++attempts == 1
            ? Task.FromException<ArenaRegionFrame>(new CaptureBackendInitializationException("apartment failed",
                new COMException("native apartment failure", unchecked((int)0x80010106)), "WGC worker apartment initialization"))
            : Task.Run(work));
        var window = new FakeArenaCapture().Inspection.Window!;
        await Assert.ThrowsAsync<CaptureBackendInitializationException>(() => lifecycle.CaptureAsync(window, CaptureRegion, 1, _ => { }, default));
        Assert.Equal(CaptureLifecycleState.Faulted, lifecycle.Lifecycle.State); Assert.Equal(0, scopes);
        Assert.Equal("WGC worker apartment initialization", lifecycle.Lifecycle.LastFailure!.Operation);
        Assert.Equal(unchecked((int)0x80010106), lifecycle.Lifecycle.LastFailure.HResult);
        using var frame = await lifecycle.CaptureAsync(window, CaptureRegion, 1, _ => { }, default);
        Assert.Equal(1, scopes); Assert.Equal(0, lifecycle.Lifecycle.OwnedRequests);
    }

    [Fact]
    public async Task CleanupFailureDisposesReturnedPixelsAndReleasesOwnershipForNextRequest()
    {
        var first = new ScopedWgc { DisposalError = new IOException("dispose failed") }; var calls = 0;
        var backend = ScopedBackend(() => calls++ == 0 ? first : new ScopedWgc());
        await Assert.ThrowsAsync<IOException>(() => ScopedAcquire(backend)); Assert.Equal((nint)0, first.Pixels!.Handle);
        Assert.Equal("WGC resource disposal", backend.Lifecycle.LastFailure!.Operation);
        using var next = await ScopedAcquire(backend); Assert.Equal(0, backend.Lifecycle.OwnedRequests);
    }

    [Fact]
    public async Task CaptureFailureRemainsActionableWhenTeardownAlsoFails()
    {
        var primary = new IOException("original acquisition failure");
        var failed = new ScopedWgc((_, _) => throw primary) { DisposalError = new IOException("secondary cleanup failure") };
        var calls = 0; var backend = ScopedBackend(() => calls++ == 0 ? failed : new());
        Assert.Same(primary, await Assert.ThrowsAsync<IOException>(() => ScopedAcquire(backend)));
        Assert.Equal("WGC request creation", backend.Lifecycle.LastFailure!.Operation);
        Assert.Equal(primary.Message, backend.Lifecycle.LastFailure.Message); Assert.Equal(1, failed.Disposals);
        using var next = await ScopedAcquire(backend); Assert.Equal(0, backend.Lifecycle.OwnedRequests);
    }

    [Fact]
    public async Task ConcurrentRequestsCannotInitializeUntilPreviousScopeIsDisposed()
    {
        using var release = new ManualResetEventSlim(); var entered = new TaskCompletionSource(); var creations = 0; ScopedWgc? first = null;
        var backend = ScopedBackend(() => Interlocked.Increment(ref creations) == 1
            ? first = new((_, _) => { entered.SetResult(); release.Wait(TimeSpan.FromSeconds(5)); }) : new());
        var a = ScopedAcquire(backend); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); var b = ScopedAcquire(backend);
        Assert.Equal(1, creations); Assert.False(b.IsCompleted); release.Set();
        using var fa = await a; using var fb = await b; Assert.Equal(1, first!.Disposals); Assert.Equal(2, creations);
    }

    [Fact]
    public async Task CancelingQueuedRequestDoesNotDisposeTheCurrentOwnerOrPoisonNextCapture()
    {
        using var release = new ManualResetEventSlim(); var entered = new TaskCompletionSource(); var first = new ScopedWgc((_, _) => { entered.SetResult(); release.Wait(TimeSpan.FromSeconds(5)); });
        var calls = 0; var backend = ScopedBackend(() => calls++ == 0 ? first : new());
        var active = ScopedAcquire(backend); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); using var cancel = new CancellationTokenSource();
        var queued = ScopedAcquire(backend, cancel.Token); cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        Assert.Equal(0, first.Disposals); release.Set(); using var a = await active; using var b = await ScopedAcquire(backend);
        Assert.Equal(1, first.Disposals); Assert.Equal(2, calls);
    }

    [Fact]
    public async Task TimedOutNativeWorkerCanStillUseTokenHandleUntilCleanupCompletes()
    {
        using var release = new ManualResetEventSlim(); var entered = new TaskCompletionSource(); var probed = new TaskCompletionSource<Exception?>(); var calls = 0;
        var backend = ScopedBackend(() => calls++ == 0 ? new ScopedWgc((_, token) =>
        {
            entered.SetResult(); release.Wait(TimeSpan.FromSeconds(5));
            try { token.WaitHandle.WaitOne(0); probed.SetResult(null); } catch (Exception ex) { probed.SetResult(ex); }
            token.ThrowIfCancellationRequested();
        }) : new());
        using var coordinator = new ArenaCaptureCoordinator(backend, new(TimeSpan.FromMilliseconds(50)));
        var attempt = coordinator.AcquireAsync(CaptureRegion); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<TimeoutException>(() => attempt); release.Set();
        Assert.Null(await probed.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        using var next = await coordinator.AcquireAsync(CaptureRegion); Assert.Equal(0, backend.Lifecycle.OwnedRequests);
    }

    [Fact]
    public async Task OldRequestCallbacksCannotOverwriteNewRequestInSameWindowGeneration()
    {
        var fake = new FakeArenaCapture(); var delivery = new TaskCompletionSource<ArenaRegionFrame>(); Action<string>? oldStage = null; long oldGeneration = 0;
        fake.Handler = (_, _, g, stage, _) => { oldStage = stage; oldGeneration = g; return delivery.Task; };
        using var coordinator = CaptureCoordinator(fake); var old = coordinator.AcquireAsync(CaptureRegion);
        fake.Handler = null; using var current = await coordinator.AcquireAsync(CaptureRegion);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => old); var before = coordinator.DiagnosticText;
        oldStage!("Old source initialized / disposing stale session");
        Assert.Equal(before, coordinator.DiagnosticText); var pixels = new SKBitmap(800, 480);
        delivery.SetResult(new(pixels, 100, 60, 1) { Generation = oldGeneration }); Assert.Equal((nint)0, pixels.Handle);
    }

    [Fact]
    public async Task PackAndGeometryResetsRetainNativeFailureAndLastSuccessfulFrameHistory()
    {
        var fake = new FakeArenaCapture(); using var coordinator = CaptureCoordinator(fake);
        using (var frame = await coordinator.AcquireAsync(CaptureRegion)) { }
        var success = coordinator.Diagnostics.LastSuccessfulFrameAt;
        fake.Handler = (_, _, _, _, _) => throw new CaptureBackendInitializationException("setup failed", new COMException("denied", unchecked((int)0x80070005)), "GraphicsCaptureItem creation");
        await Assert.ThrowsAsync<CaptureBackendInitializationException>(() => coordinator.AcquireAsync(CaptureRegion));
        coordinator.PackChanged(); Assert.False(coordinator.Diagnostics.Initialized); Assert.Null(coordinator.Diagnostics.LastFrameAt);
        Assert.Equal(success, coordinator.Diagnostics.LastSuccessfulFrameAt); Assert.Equal("GraphicsCaptureItem creation", coordinator.Diagnostics.LastFailure!.Operation);
        Assert.Contains("HRESULT: 0x80070005", coordinator.DiagnosticText); Assert.Contains("Retry available", coordinator.DiagnosticText);
    }

    [Fact]
    public async Task RetryRediscoversChangedHwndAndReportsPreviousAndCurrentHandles()
    {
        var fake = new FakeArenaCapture(); using var coordinator = CaptureCoordinator(fake); coordinator.Observe();
        fake.Inspection = fake.Inspection with { Window = fake.Inspection.Window! with { Handle = 77 } }; coordinator.Restart();
        using var frame = await coordinator.AcquireAsync(CaptureRegion);
        Assert.Equal((nint)77, frame.WindowHandle); Assert.Contains("previous 0x2A; current 0x4D", coordinator.DiagnosticText);
    }

    [Fact]
    public async Task CurrentFramesInvokeLocalizationAcrossAllFourteenCardCounts()
    {
        using var overlay = CaptureOverlay(); var fake = new FakeArenaCapture(); var count = 14; var invoked = new List<int>();
        fake.Handler = (w, r, g, stage, _) => { stage("Capture source initialized"); var pixels = SolidFrame(new SKColor((byte)(count * 15), 80, 120));
            return Task.FromResult(new ArenaRegionFrame(pixels, 100, 60, 1) { Generation = g }); };
        await using var session = CaptureSession(overlay, fake, (request, frame, _) => { invoked.Add(request.Occurrences.Count); return Task.FromResult(EmptyLocalization(request, frame)); });
        var (data, _) = FourBadgeFixture();
        for (count = 14; count >= 1; count--)
        {
            overlay.Session.ApplySessionUpdate(AssociationPack(data, Enumerable.Range(0, count).Select(i => 601 + i % 4).ToArray(), 15 - count));
            session.RetryManually(); await session.CurrentWork;
        }
        Assert.Equal(Enumerable.Range(1, 14).Reverse(), invoked); Assert.Equal(14, fake.Captures);
    }

    [Fact]
    public async Task DebugSaveReplacesInFlightAutomaticRequestAfterScopedCleanup()
    {
        using var overlay = CaptureOverlay(); var entered = new TaskCompletionSource(); var scopes = new List<ScopedWgc>();
        var backend = ScopedBackend(() => { var s = new ScopedWgc(scopes.Count == 0 ? (_, token) => { entered.SetResult(); token.WaitHandle.WaitOne(); token.ThrowIfCancellationRequested(); } : null); scopes.Add(s); return s; });
        var directory = Path.Combine(Path.GetTempPath(), "DraftTG-wgc-life-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var session = CaptureSession(overlay, backend, debugPath: Path.Combine(directory, "frame.png"));
            session.RetryManually(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await session.SaveCurrentCaptureForDebugging();
            Assert.Equal(2, scopes.Count); Assert.All(scopes, s => Assert.Equal(1, s.Disposals)); Assert.NotNull(session.LastDebugCapturePath);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task ExplicitRetryAfterScopedFaultRestoresAutomaticPlacementAndManualSafety()
    {
        using var overlay = CaptureOverlay(); var calls = 0;
        var backend = ScopedBackend(() => calls++ == 0 ? new ScopedWgc((_, _) => throw new CaptureBackendInitializationException("pool failed", operation: "Frame pool creation")) : new());
        await using var session = CaptureSession(overlay, backend, (r, f, _) => Task.FromResult(ConfidentSyncResult(r, f)));
        session.RetryManually(); await session.CurrentWork; Assert.False(overlay.HasAutomaticVisualPlacement);
        Assert.All(overlay.Badges, b => Assert.False(b.IsPlaced)); session.RetryManually(); await session.CurrentWork;
        Assert.True(overlay.HasAutomaticVisualPlacement); Assert.Equal(2, calls); Assert.Equal(0, backend.Lifecycle.OwnedRequests);
    }
}
