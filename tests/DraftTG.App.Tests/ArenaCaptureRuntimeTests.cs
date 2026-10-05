using System.ComponentModel;
using DraftTG.Application;
using DraftTG.App.Platform;
using SkiaSharp;

namespace DraftTG.App.Tests;

public sealed partial class MainWindowViewModelTests
{
    private static readonly NormalizedDraftRegion CaptureRegion = new(.1, .1, .8, .8);
    private sealed class FakeArenaCapture : IArenaRegionCapture
    {
        public string Backend { get; set; } = "Unspecified/test capture";
        public ArenaWindowInspection Inspection { get; set; } = new(true,
            new ArenaWindowGeometry(42, 0, 0, 1000, 600, 1, false)
            { ProcessId = 7, Title = "MTGA", WindowClass = "UnityWndClass", ArenaIntegrity = "Medium", DraftTGIntegrity = "Medium" });
        public int Inspections { get; private set; }
        public int Captures { get; private set; }
        public Exception? InspectionError { get; set; }
        public ArenaWindowGeometry? LastWindow { get; private set; }
        public Func<ArenaWindowGeometry, NormalizedDraftRegion, long, Action<string>, CancellationToken, Task<ArenaRegionFrame>>? Handler { get; set; }
        public ArenaWindowInspection InspectWindow()
        { Inspections++; if (InspectionError is not null) throw InspectionError; return Inspection; }
        public Task<ArenaRegionFrame> CaptureAsync(ArenaWindowGeometry window, NormalizedDraftRegion region, long generation,
            Action<string> stage, CancellationToken token)
        {
            Captures++; LastWindow = window;
            if (Handler is not null) return Handler(window, region, generation, stage, token);
            stage("Capture source initialized");
            var crop = ArenaDraftCrop.Calculate(window, region);
            return Task.FromResult(new ArenaRegionFrame(new SKBitmap(crop.Width, crop.Height), window.X + crop.X, window.Y + crop.Y, window.Scaling)
                { Generation = generation, WindowHandle = window.Handle, Backend = Backend });
        }
    }
    private static ArenaCaptureCoordinator CaptureCoordinator(FakeArenaCapture fake, double timeoutMs = 2000) =>
        new(fake, new(TimeSpan.FromMilliseconds(timeoutMs)));
    private static OverlayViewModel CaptureOverlay()
    {
        var (data, _) = FourBadgeFixture(); var runtime = AssociationSession(data.Catalog);
        var overlay = new OverlayViewModel(runtime);
        runtime.ApplySessionUpdate(AssociationPack(data, [601, 602, 603, 604]));
        overlay.SetLayout(DraftCardLayout.Grid(5));
        return overlay;
    }
    private static CardVisualLocalizationResult EmptyLocalization(CardVisualLocalizationRequest request, ArenaRegionFrame frame) =>
        new(request.Pack, [], request.CalibratedLayout.Slots.Take(request.Occurrences.Count)
            .Select(s => new NormalizedDraftRegion(s.X, s.Y, s.Width, s.Height)).ToArray(), frame.Image.Width, frame.Image.Height, TimeSpan.Zero, "fake processor");
    private static AutomaticCardLocalizationSession CaptureSession(OverlayViewModel overlay, IArenaRegionCapture fake,
        Func<CardVisualLocalizationRequest, ArenaRegionFrame, CancellationToken, Task<CardVisualLocalizationResult>>? recognize = null,
        string? debugPath = null)
    {
        var calibration = new OverlayCalibration(new(0, 0, 1000, 600, 1), CaptureRegion, DraftCardLayout.Grid(5));
        return new(overlay, () => calibration, (_, _, _, _) => { }, _ => { }, fake,
            new(TimeSpan.FromMilliseconds(100)), recognize ?? ((request, frame, _) => Task.FromResult(EmptyLocalization(request, frame))), debugPath, action => action(),
            new(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero));
    }

    [Fact]
    public async Task RailRetryStartsCaptureAndLocalizationWithoutForegroundFocus()
    {
        using var overlay = CaptureOverlay(); var fake = new FakeArenaCapture(); var invoked = 0;
        await using var session = CaptureSession(overlay, fake, (request, frame, _) =>
        { invoked++; return Task.FromResult(EmptyLocalization(request, frame)); });
        session.RetryManually(); await session.CurrentWork;
        Assert.False(fake.LastWindow!.IsForeground); Assert.Equal(1, fake.Captures); Assert.Equal(1, invoked);
        Assert.Contains("Last frame dimensions: 800 x 480", overlay.CaptureRuntimeDiagnosticsText);
        Assert.Contains("Localization invoked: yes; result: matched 0/4", overlay.CaptureRuntimeDiagnosticsText);
        Assert.DoesNotContain("Retry requested.", overlay.LocalizationDiagnostics);
    }

    [Fact]
    public async Task RailRetryRediscoveringReplacedHwndStartsAnotherFreshCapture()
    {
        using var overlay = CaptureOverlay(); var fake = new FakeArenaCapture();
        var delayed = new TaskCompletionSource<ArenaRegionFrame>(); long oldGeneration = 0;
        fake.Handler = (_, _, generation, _, _) => { oldGeneration = generation; return delayed.Task; };
        await using var session = CaptureSession(overlay, fake);
        session.RetryManually(); var originalWork = session.CurrentWork;
        Assert.Equal(1, fake.Captures); Assert.False(originalWork.IsCompleted);
        var inspections = fake.Inspections;
        fake.Inspection = fake.Inspection with { Window = fake.Inspection.Window! with { Handle = 77, ProcessId = 8 } };
        fake.Handler = null;
        session.RetryManually(); await session.CurrentWork;
        await originalWork;
        Assert.True(fake.Inspections > inspections); Assert.Equal(2, fake.Captures); Assert.Equal((nint)77, fake.LastWindow!.Handle);
        Assert.Contains("PID: 8", overlay.CaptureRuntimeDiagnosticsText);
        delayed.SetResult(new(new SKBitmap(800, 480), 100, 60, 1) { Generation = oldGeneration });
    }

    [Fact]
    public async Task FocusAndTinyGeometryJitterDoNotInvalidateCurrentFrame()
    {
        var fake = new FakeArenaCapture(); var delivered = new TaskCompletionSource<ArenaRegionFrame>(); long generation = 0;
        fake.Handler = (_, _, g, _, _) => { generation = g; return delivered.Task; };
        using var coordinator = CaptureCoordinator(fake);
        var work = coordinator.AcquireAsync(CaptureRegion);
        fake.Inspection = fake.Inspection with { Window = fake.Inspection.Window! with { IsForeground = true, X = 1, Width = 1001 } };
        coordinator.Observe(); Assert.Equal(generation, coordinator.Generation);
        delivered.SetResult(new(new SKBitmap(800, 480), 100, 60, 1) { Generation = generation });
        using var frame = await work;
        Assert.Equal(generation, frame.Generation); Assert.Equal("Draft crop produced", coordinator.Diagnostics.Stage);
    }

    [Fact]
    public async Task RealSizeAndDpiChangeCancelOldGenerationAndAcceptFreshFrame()
    {
        var fake = new FakeArenaCapture(); var delivered = new TaskCompletionSource<ArenaRegionFrame>(); long old = 0;
        fake.Handler = (_, _, g, _, _) => { old = g; return delivered.Task; };
        using var coordinator = CaptureCoordinator(fake); var stale = coordinator.AcquireAsync(CaptureRegion);
        fake.Inspection = fake.Inspection with { Window = fake.Inspection.Window! with { Width = 1200, Scaling = 1.25 } };
        coordinator.Observe(); Assert.True(coordinator.Generation > old);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stale);
        var late = new ArenaRegionFrame(new SKBitmap(800, 480), 100, 60, 1) { Generation = old };
        delivered.SetResult(late);
        fake.Handler = null;
        using var latest = await coordinator.AcquireAsync(CaptureRegion);
        Assert.Equal(coordinator.Generation, latest.Generation); Assert.Equal(960, latest.Image.Width);
        Assert.Equal("Draft crop produced", coordinator.Diagnostics.Stage);
    }

    [Fact]
    public async Task DeliveredFrameFromOlderGenerationIsRejected()
    {
        var fake = new FakeArenaCapture(); SKBitmap? pixels = null;
        fake.Handler = (_, _, generation, _, _) => Task.FromResult(new ArenaRegionFrame(pixels = new SKBitmap(800, 480), 100, 60, 1)
            { Generation = generation - 1 });
        using var coordinator = CaptureCoordinator(fake);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.AcquireAsync(CaptureRegion));
        Assert.Equal((nint)0, pixels!.Handle);
        Assert.False(coordinator.Diagnostics.LocalizationInvoked);
        Assert.Contains("stale generation rejected", coordinator.Diagnostics.Stage);
    }

    [Fact]
    public async Task MissingFrameHasBoundedTimeoutAndLateFrameIsDisposed()
    {
        var fake = new FakeArenaCapture(); var delivered = new TaskCompletionSource<ArenaRegionFrame>(); long generation = 0;
        fake.Handler = (_, _, g, stage, _) => { generation = g; stage("Capture source initialized"); return delivered.Task; };
        using var coordinator = CaptureCoordinator(fake, 30);
        var work = coordinator.AcquireAsync(CaptureRegion);
        await Assert.ThrowsAsync<TimeoutException>(async () => await work.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Contains("no frame was received within", coordinator.DiagnosticText);
        Assert.Contains("Last frame received: NEVER", coordinator.DiagnosticText); Assert.False(coordinator.Diagnostics.Running);
        var late = new ArenaRegionFrame(new SKBitmap(800, 480), 100, 60, 1) { Generation = generation };
        delivered.SetResult(late); Assert.Equal((nint)0, late.Image.Handle);
        Assert.False(coordinator.Diagnostics.LocalizationInvoked);
    }

    [Fact]
    public async Task MissingProcessHwndAndMinimizedWindowExposeDistinctReasons()
    {
        var fake = new FakeArenaCapture(); var window = fake.Inspection.Window!;
        foreach (var (inspection, reason) in new[]
        {
            (new ArenaWindowInspection(false, null), "Arena process not found"),
            (new ArenaWindowInspection(true, null, "helper window rejected"), "no eligible rendering HWND"),
            (new ArenaWindowInspection(true, window with { IsMinimized = true }), "hidden or minimized"),
            (new ArenaWindowInspection(true, window with { IsVisible = false }), "hidden or minimized")
        })
        {
            fake.Inspection = inspection; using var coordinator = CaptureCoordinator(fake);
            await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.AcquireAsync(CaptureRegion));
            Assert.Contains(reason, coordinator.DiagnosticText); Assert.Equal(0, fake.Captures);
        }
    }

    [Fact]
    public async Task InvalidDraftCropIsReportedBeforeRequestingRegionalCapture()
    {
        var fake = new FakeArenaCapture(); using var coordinator = CaptureCoordinator(fake);
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.AcquireAsync(new(.9, .1, .5, .8)));
        Assert.Equal(0, fake.Captures); Assert.Contains("crop is outside Arena client bounds", coordinator.DiagnosticText);
        Assert.Contains("Last frame received: NEVER", coordinator.DiagnosticText);
    }

    [Fact]
    public async Task FreshFrameWithWrongCropDimensionsIsDistinctFromNoFrame()
    {
        var fake = new FakeArenaCapture();
        fake.Handler = (_, _, generation, _, _) => Task.FromResult(new ArenaRegionFrame(new SKBitmap(80, 80), 0, 0, 1) { Generation = generation });
        using var coordinator = CaptureCoordinator(fake);
        await Assert.ThrowsAsync<InvalidDataException>(() => coordinator.AcquireAsync(CaptureRegion));
        Assert.NotNull(coordinator.Diagnostics.LastFrameAt); Assert.Equal(80, coordinator.Diagnostics.FrameWidth);
        Assert.Contains("Fresh frame received, but draft-region crop", coordinator.DiagnosticText);
        Assert.False(coordinator.Diagnostics.LocalizationInvoked);
        SKBitmap? discarded = null;
        fake.Handler = (_, _, generation, _, _) =>
        {
            fake.InspectionError = new Win32Exception(5, "Window revalidation failed.");
            return Task.FromResult(new ArenaRegionFrame(discarded = new SKBitmap(800, 480), 100, 60, 1) { Generation = generation });
        };
        await Assert.ThrowsAsync<Win32Exception>(() => coordinator.AcquireAsync(CaptureRegion));
        Assert.Equal((nint)0, discarded!.Handle);
        Assert.Contains("Window revalidation failed", coordinator.DiagnosticText);
    }

    [Fact]
    public async Task InitializationWin32FailureRetainsStageTypeHresultAndNativeCode()
    {
        var fake = new FakeArenaCapture();
        fake.Handler = (_, _, _, stage, _) => { stage("Initializing GDI capture source"); throw new Win32Exception(5, "GetDC failed."); };
        using var coordinator = CaptureCoordinator(fake);
        await Assert.ThrowsAsync<Win32Exception>(() => coordinator.AcquireAsync(CaptureRegion));
        Assert.Contains("Initializing GDI capture source failed", coordinator.DiagnosticText);
        Assert.Contains("Win32Exception; HRESULT 0x", coordinator.DiagnosticText); Assert.Contains("Win32 5; GetDC failed.", coordinator.DiagnosticText);
        Assert.False(coordinator.Diagnostics.Initialized);
    }

    [Fact]
    public async Task BitmapConversionFailureIsDiagnosedAfterSourceInitialization()
    {
        var fake = new FakeArenaCapture();
        fake.Handler = (_, _, _, stage, _) =>
        { stage("Capture source initialized"); stage("Converting capture bitmap"); throw new InvalidOperationException("Pixel conversion failed."); };
        using var coordinator = CaptureCoordinator(fake);
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.AcquireAsync(CaptureRegion));
        Assert.True(coordinator.Diagnostics.Initialized); Assert.Contains("Converting capture bitmap failed", coordinator.DiagnosticText);
        Assert.Contains("Pixel conversion failed", coordinator.DiagnosticText); Assert.Null(coordinator.Diagnostics.LastFrameAt);
    }

    [Fact]
    public async Task CaptureFailureLeavesExplicitManualConfirmationAvailable()
    {
        using var overlay = CaptureOverlay(); var fake = new FakeArenaCapture(); var invoked = false;
        fake.Handler = (_, _, _, _, _) => throw new Win32Exception(5, "BitBlt failed.");
        await using var session = CaptureSession(overlay, fake, (request, frame, _) =>
        { invoked = true; return Task.FromResult(EmptyLocalization(request, frame)); });
        session.RetryManually(); await session.CurrentWork;
        Assert.False(invoked); Assert.Contains("BitBlt failed", overlay.CaptureRuntimeDiagnosticsText);
        Assert.Equal("Card placement: Manual fallback", overlay.LocalizationStatus);
        foreach (var row in overlay.VisualSlots) row.SelectedCard = row.Choices[3 - row.VisualSlot];
        Assert.True(overlay.ConfirmVisualSelections()); Assert.True(overlay.HasConfirmedVisualPlacement);
        Assert.All(overlay.Badges, b => Assert.True(b.IsPlaced));
    }

    [Fact]
    public async Task SuccessfulCropRunsExistingArtworkMatcherAndPublishesAutomaticPlacement()
    {
        using var overlay = CaptureOverlay(); var fake = new FakeArenaCapture();
        var request = new CardVisualLocalizationRequest(overlay.PlacementContext!.Pack, overlay.Session.CurrentPackPresentations, overlay.Layout);
        var references = SyntheticReferences(request);
        try
        {
            fake.Handler = (_, _, generation, stage, _) =>
            {
                stage("Capture source initialized");
                return Task.FromResult(new ArenaRegionFrame(SyntheticPack(request, references, [3, 1, 0, 2], width: 800, height: 480), 100, 60, 1)
                    { Generation = generation });
            };
            await using var session = CaptureSession(overlay, fake, (r, frame, token) =>
                Task.FromResult(new CardTemplateRecognizer().Recognize(r, frame.Image, references, token)));
            session.RetryManually(); await session.CurrentWork;
            Assert.True(overlay.HasAutomaticVisualPlacement); Assert.False(overlay.HasConfirmedVisualPlacement);
            Assert.Equal([2, 1, 3, 0], overlay.Badges.Select(b => b.VisualSlotIndex!.Value));
            Assert.Contains("Localization invoked: yes; result: matched 4/4", overlay.CaptureRuntimeDiagnosticsText);
        }
        finally { DisposeReferences(references); }
    }

    [Fact]
    public async Task ExplicitDebugActionSavesOneRegionalPngWithoutInvokingMatcher()
    {
        using var overlay = CaptureOverlay(); var fake = new FakeArenaCapture(); var invoked = false;
        var directory = Path.Combine(Path.GetTempPath(), "DraftTG-capture-test-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "crop.png");
        try
        {
            await using var session = CaptureSession(overlay, fake, (request, frame, _) =>
            { invoked = true; return Task.FromResult(EmptyLocalization(request, frame)); }, path);
            Assert.False(File.Exists(path));
            await session.SaveCurrentCaptureForDebugging();
            var savedPath = session.LastDebugCapturePath!;
            using var saved = SKBitmap.Decode(savedPath);
            Assert.Equal((800, 480), (saved.Width, saved.Height)); Assert.Equal(1, fake.Captures); Assert.False(invoked);
            Assert.Contains(savedPath, overlay.CaptureRuntimeDiagnosticsText); Assert.Contains("Localization invoked: no", overlay.CaptureRuntimeDiagnosticsText);
            Assert.Contains("Last frame dimensions: 800 x 480", await File.ReadAllTextAsync(session.LastDebugLogPath!));
            fake.Handler = (_, _, _, _, _) => throw new Win32Exception(5, "BitBlt failed.");
            await session.SaveCurrentCaptureForDebugging();
            Assert.Null(session.LastDebugCapturePath); Assert.Equal(2, fake.Captures); Assert.False(invoked);
            Assert.Contains("Win32 5; BitBlt failed.", await File.ReadAllTextAsync(session.LastDebugLogPath!));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            var log = Path.ChangeExtension(path, ".txt"); if (File.Exists(log)) File.Delete(log);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task ExplicitRetryKeepsConfirmedManualMappingWhenFreshCaptureFails()
    {
        using var overlay = CaptureOverlay(); var fake = new FakeArenaCapture();
        Assert.True(overlay.ConfirmVisualOrder(overlay.PlacementContext!, VisualKeys(overlay, 3, 1, 0, 2)));
        var slots = overlay.Badges.Select(b => b.VisualSlotIndex).ToArray();
        fake.Handler = (_, _, _, _, _) => throw new InvalidOperationException("Capture unavailable.");
        await using var session = CaptureSession(overlay, fake);
        session.RetryManually(); await session.CurrentWork;
        Assert.Equal(1, fake.Captures); Assert.True(overlay.HasConfirmedVisualPlacement);
        Assert.Equal(slots, overlay.Badges.Select(b => b.VisualSlotIndex));
    }
}
