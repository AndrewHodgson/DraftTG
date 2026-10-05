using DraftTG.Application;
using DraftTG.App.Platform;
using SkiaSharp;
using System.Text.Json;

namespace DraftTG.App.Tests;

public sealed partial class MainWindowViewModelTests
{
    private static (FakeArenaCapture Wgc, FakeArenaCapture Gdi) BackendFakes() =>
        (new() { Backend = "Windows.Graphics.Capture" }, new() { Backend = "GDI fallback / BitBlt SRCCOPY" });
    private static Task<ArenaRegionFrame> BackendAcquire(IArenaRegionCapture capture, FakeArenaCapture locator, CancellationToken token = default) =>
        capture.CaptureAsync(locator.Inspection.Window!, CaptureRegion, 7, _ => { }, token);

    [Fact]
    public async Task WgcIsPreferredWithoutRequestingGdi()
    {
        var (wgc, gdi) = BackendFakes(); var capture = WindowsCaptureFactory.Create(null, false, wgc, gdi);
        using var frame = await BackendAcquire(capture, wgc);
        Assert.Equal(wgc.Backend, frame.Backend); Assert.Equal(1, wgc.Captures); Assert.Equal(0, gdi.Captures);
    }

    [Fact]
    public async Task DeveloperGdiSelectorIsExplicitAndInvalidSelectorIsRejected()
    {
        var (wgc, gdi) = BackendFakes(); var capture = WindowsCaptureFactory.Create("gdi", false, wgc, gdi);
        using var frame = await BackendAcquire(capture, gdi);
        Assert.Equal(gdi.Backend, frame.Backend); Assert.Equal(0, wgc.Captures); Assert.Equal(1, gdi.Captures);
        Assert.Throws<ArgumentException>(() => WindowsCaptureFactory.Create("desktop", false, wgc, gdi));
    }

    [Fact]
    public async Task WgcInitializationFailureDefaultsToManualWithoutGdi()
    {
        var (wgc, gdi) = BackendFakes();
        wgc.Handler = (_, _, _, _, _) => throw new CaptureBackendInitializationException("Unsupported");
        var capture = WindowsCaptureFactory.Create(null, false, wgc, gdi);
        await Assert.ThrowsAsync<CaptureBackendInitializationException>(() => BackendAcquire(capture, wgc));
        Assert.Equal(0, gdi.Captures);
    }

    [Fact]
    public async Task OptInInitializationFallbackRecordsActualBackendAndReason()
    {
        var (wgc, gdi) = BackendFakes();
        wgc.Handler = (_, _, _, _, _) => throw new CaptureBackendInitializationException("Unsupported");
        var capture = WindowsCaptureFactory.Create("wgc", true, wgc, gdi);
        var stages = new List<string>();
        using var frame = await capture.CaptureAsync(wgc.Inspection.Window!, CaptureRegion, 7, stages.Add, default);
        Assert.StartsWith(gdi.Backend, frame.Backend); Assert.Contains("Unsupported", frame.Backend);
        Assert.Contains(stages, s => s.Contains("WGC initialization failed")); Assert.Equal(1, gdi.Captures);
    }

    [Fact]
    public async Task RuntimeDeviceLossAndTimeoutNeverSilentlyFallBack()
    {
        foreach (var error in new Exception[] { new IOException("device removed"), new TimeoutException("no frame") })
        {
            var (wgc, gdi) = BackendFakes(); wgc.Handler = (_, _, _, _, _) => throw error;
            var capture = WindowsCaptureFactory.Create(null, true, wgc, gdi);
            var observed = await Record.ExceptionAsync(() => BackendAcquire(capture, wgc));
            Assert.Same(error, observed); Assert.Equal(0, gdi.Captures);
        }
    }

    [Fact]
    public async Task CancellationDuringInitializationPreventsGdiFallback()
    {
        var (wgc, gdi) = BackendFakes(); using var cancellation = new CancellationTokenSource();
        wgc.Handler = (_, _, _, _, _) => { cancellation.Cancel(); throw new CaptureBackendInitializationException("init failed"); };
        var capture = WindowsCaptureFactory.Create(null, true, wgc, gdi);
        await Assert.ThrowsAsync<CaptureBackendInitializationException>(() => BackendAcquire(capture, wgc, cancellation.Token));
        Assert.Equal(0, gdi.Captures);
    }

    [Fact]
    public async Task WgcFrameMetadataSurvivesCoordinatorAndSynchronizationDiagnostics()
    {
        var (wgc, gdi) = BackendFakes();
        wgc.Handler = (w, _, g, stage, _) =>
        {
            stage("Capture source initialized");
            return Task.FromResult(new ArenaRegionFrame(SolidFrame(SKColors.Blue), 100, 60, 1)
            { Backend = wgc.Backend, WindowHandle = w.Handle, Generation = g, CaptureDuration = TimeSpan.FromMilliseconds(42), PresentationTime = TimeSpan.FromSeconds(123) });
        };
        using var coordinator = new ArenaCaptureCoordinator(WindowsCaptureFactory.Create(null, false, wgc, gdi), new(TimeSpan.FromSeconds(2)));
        using var frame = await coordinator.AcquireAsync(CaptureRegion, packGeneration: 12);
        var hashed = frame with { ImageHash = PackFrameSynchronizer.Hash(frame.Image) }; coordinator.SynchronizedFrame(hashed);
        Assert.Equal((nint)42, frame.WindowHandle); Assert.Equal(12, frame.PackGeneration); Assert.Equal(1, frame.CaptureRequestGeneration);
        Assert.Equal(TimeSpan.FromSeconds(123), frame.PresentationTime); Assert.True(coordinator.Diagnostics.Initialized);
        Assert.Contains("Capture backend: Windows.Graphics.Capture", coordinator.DiagnosticText);
        Assert.Contains(hashed.ImageHash!, coordinator.DiagnosticText); Assert.Equal(frame.CaptureDuration, coordinator.Diagnostics.CaptureDuration);
    }

    [Fact]
    public async Task WgcWrongPackTokenIsRejectedAndBufferDisposed()
    {
        var (wgc, gdi) = BackendFakes(); SKBitmap? pixels = null;
        wgc.Handler = (_, _, g, _, _) => Task.FromResult(new ArenaRegionFrame(pixels = SolidFrame(SKColors.Blue), 100, 60, 1)
        { Backend = wgc.Backend, Generation = g, PackGeneration = 11 });
        using var coordinator = new ArenaCaptureCoordinator(WindowsCaptureFactory.Create(null, false, wgc, gdi), new(TimeSpan.FromSeconds(2)));
        await Assert.ThrowsAsync<InvalidDataException>(() => coordinator.AcquireAsync(CaptureRegion, packGeneration: 12));
        Assert.Equal((nint)0, pixels!.Handle); Assert.False(coordinator.Diagnostics.LocalizationInvoked); Assert.Equal(0, gdi.Captures);
    }

    [Fact]
    public async Task IdenticalPixelsAfterCountChangeAreRejectedForBothBackends()
    {
        foreach (var label in new[] { "Windows.Graphics.Capture", "GDI fallback" })
        {
            using var sync = new PackFrameSynchronizer(NoSyncWait);
            var a = SyncContext(AutoRequest(9), 1); var b = SyncContext(AutoRequest(8), 2);
            sync.Activate(a);
            using (var baseline = await sync.AcquireAsync(a, _ => Task.FromResult(SyncFrame(a, SolidFrame(SKColors.Red)) with { Backend = label }), _ => { }, default)) { }
            sync.Activate(b); var attempts = 0;
            await Assert.ThrowsAsync<StaleArenaFrameException>(() => sync.AcquireAsync(b, _ =>
            { attempts++; return Task.FromResult(SyncFrame(b, SolidFrame(SKColors.Red)) with { Backend = label }); }, _ => { }, default));
            Assert.Equal(3, attempts);
        }
    }

    [Fact]
    public async Task DebugSaveUsesSamePreferredBackendAndLogsMetadataWithoutMatching()
    {
        using var overlay = CaptureOverlay(); var (wgc, gdi) = BackendFakes(); var matched = 0;
        var directory = Path.Combine(Path.GetTempPath(), "DraftTG-wgc-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var session = CaptureSession(overlay, WindowsCaptureFactory.Create(null, false, wgc, gdi), (r, f, _) =>
            { matched++; return Task.FromResult(EmptyLocalization(r, f)); }, Path.Combine(directory, "capture.png"));
            session.RetryManually(); await session.CurrentWork;
            await session.SaveCurrentCaptureForDebugging();
            Assert.Equal(2, wgc.Captures); Assert.Equal(0, gdi.Captures); Assert.Equal(1, matched);
            var log = await File.ReadAllTextAsync(session.LastDebugLogPath!);
            Assert.Contains("Capture backend: Windows.Graphics.Capture", log); Assert.Contains("Frame HWND: 0x2A", log);
            Assert.Contains("Frame capture request generation: 2", log); Assert.Contains("Image hash (decoded pixels SHA256):", log);
            Assert.Contains("PNG SHA256:", log);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task FailedWgcRequestRemovesPriorAutomaticBadgesAndKeepsManualFallback()
    {
        using var overlay = CaptureOverlay(); var (wgc, gdi) = BackendFakes();
        await using var session = CaptureSession(overlay, WindowsCaptureFactory.Create(null, false, wgc, gdi),
            (r, f, _) => Task.FromResult(ConfidentSyncResult(r, f)));
        session.RetryManually(); await session.CurrentWork; Assert.True(overlay.HasAutomaticVisualPlacement);
        wgc.Handler = (_, _, _, _, _) => throw new IOException("WGC device lost");
        session.RetryManually(); await session.CurrentWork;
        Assert.False(overlay.HasAutomaticVisualPlacement); Assert.All(overlay.Badges, b => Assert.False(b.IsPlaced));
        Assert.Equal("Card placement: Manual fallback", overlay.LocalizationStatus); Assert.Equal(0, gdi.Captures);
        Assert.True(overlay.ConfirmVisualOrder(overlay.PlacementContext!, VisualKeys(overlay, 3, 1, 0, 2)));
    }

    [Fact]
    public async Task LateWgcFrameAfterPackAdvanceNeverInvokesMatcherAndIsDisposed()
    {
        using var overlay = CaptureOverlay(); var (wgc, gdi) = BackendFakes();
        var delivery = new TaskCompletionSource<ArenaRegionFrame>(); long generation = 0; var matched = 0;
        wgc.Handler = (_, _, g, _, _) => { generation = g; return delivery.Task; };
        await using var session = CaptureSession(overlay, WindowsCaptureFactory.Create(null, false, wgc, gdi), (r, f, _) =>
        { matched++; return Task.FromResult(ConfidentSyncResult(r, f)); });
        session.RetryManually(); var work = session.CurrentWork;
        var (data, _) = FourBadgeFixture(); overlay.Session.ApplySessionUpdate(AssociationPack(data, [601, 602, 603], 2));
        var late = new ArenaRegionFrame(SolidFrame(SKColors.Red), 100, 60, 1) { Backend = wgc.Backend, Generation = generation };
        delivery.SetResult(late); await work;
        Assert.Equal((nint)0, late.Image.Handle); Assert.Equal(0, matched); Assert.Equal(0, gdi.Captures);
        Assert.All(overlay.Badges, b => Assert.False(b.IsPlaced));
    }

    [Fact]
    public void WgcWindowToClientCropIncludesNonClientOffsetWithoutScaling()
    {
        var client = new ArenaWindowGeometry(42, -1912, 38, 1904, 1042, 1.5, false);
        var bounds = new WindowCaptureBounds(-1920, 7, 1920, 1080);
        Assert.Equal(new ArenaDraftCrop(108, 91, 800, 480), bounds.MapCrop(client, new(100, 60, 800, 480)));
    }

    [Fact]
    public void WgcSurfaceBoundsMismatchRejectsInsteadOfClippingOrGuessing()
    {
        var client = new ArenaWindowGeometry(42, 30, 40, 1000, 600, 1, false);
        Assert.Throws<InvalidDataException>(() => new WindowCaptureBounds(0, 0, 800, 480).MapCrop(client, new(100, 60, 800, 480)));
    }

    [Fact]
    public void LiveWgcEightCardFixtureMatchesCorrectVisualOrderAtOriginalThresholds()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "arena-wgc-p1p7");
        var manifest = JsonSerializer.Deserialize<OfflineLocalizationManifest>(File.ReadAllText(Path.Combine(directory, "manifest.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        using var frame = SKBitmap.Decode(Path.Combine(directory, "arena-draft-capture.png"));
        var request = OfflineArtworkCommand.CreateRequest(manifest);
        var references = OfflineArtworkCommand.LoadReferences(manifest, directory);
        try
        {
            CardRecognitionAudit? audit = null;
            var result = new CardTemplateRecognizer().Recognize(request, frame, references, diagnostic: value => audit = value);
            Assert.Equal(8, result.Matches.Count); Assert.True(result.IsSafeFor(request));
            Assert.Equal(manifest.ExpectedVisualOrder!, result.Matches.OrderBy(m => m.VisualSlot).Select(m => m.Key.CardIdentifier.Value));
            Assert.All(audit!.Slots, s => { Assert.True(s.Accepted); Assert.Equal(.94, s.Threshold); Assert.Equal(.07, s.RequiredMargin); Assert.InRange(s.Assigned.Score, .96, 1); });
        }
        finally { foreach (var reference in references.Values) reference.Dispose(); }
    }
}
