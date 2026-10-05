using System.Text.Json;
using DraftTG.Application;
using DraftTG.App.Platform;
using DraftTG.Domain;
using SkiaSharp;

namespace DraftTG.App.Tests;

public sealed partial class MainWindowViewModelTests
{
    private static readonly PackFrameTiming NoSyncWait = new(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);
    private static PackFrameContext SyncContext(CardVisualLocalizationRequest request, long token) =>
        new(request with { PackGeneration = token }, DateTimeOffset.UtcNow.AddSeconds(-1));
    private static ArenaRegionFrame SyncFrame(PackFrameContext context, SKBitmap image, long capture = 1) =>
        new(image, 0, 0, 1) { Generation = 1, PackGeneration = context.Request.PackGeneration, CaptureRequestGeneration = capture };
    private static SKBitmap SolidFrame(SKColor color, int width = 800, int height = 480)
    { var image = new SKBitmap(width, height); image.Erase(color); return image; }
    private static CardVisualLocalizationResult ConfidentSyncResult(CardVisualLocalizationRequest request, ArenaRegionFrame frame)
    {
        var rectangles = request.CalibratedLayout.Slots.Take(request.Occurrences.Count)
            .Select(s => new NormalizedDraftRegion(s.X, s.Y, s.Width, s.Height)).ToArray();
        return new(request.Pack, request.Occurrences.Select((c, i) => new CardVisualMatch(c.Key, i, rectangles[i], .99,
            LocalizationConfidence.HighConfidence, "synchronization fake")).ToArray(), rectangles, frame.Image.Width, frame.Image.Height,
            TimeSpan.Zero, "synchronization fake") { PackGeneration = request.PackGeneration };
    }
    private static (PackFrameContext Context, SKBitmap Image) LiveSyncNine()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "arena-artwork-live");
        var manifest = JsonSerializer.Deserialize<OfflineLocalizationManifest>(File.ReadAllText(Path.Combine(directory, "manifest.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        return (SyncContext(OfflineArtworkCommand.CreateRequest(manifest), 17), SKBitmap.Decode(Path.Combine(directory, "arena-draft-capture.png")));
    }
    private static SKBitmap SyntheticEight(PackFrameContext context)
    {
        var references = SyntheticReferences(context.Request);
        try { return SyntheticPack(context.Request, references, Enumerable.Range(0, 8).ToArray(), width: 1325, height: 1131); }
        finally { DisposeReferences(references); }
    }

    [Fact]
    public async Task SyncLiveNinePixelsAreNeverMatchedWithNewEightCandidatesAndFreshEightIsReacquired()
    {
        var (a, imageA) = LiveSyncNine(); using var pixels = imageA;
        var next = AutoRequest(8);
        var b = SyncContext(next with { Pack = new(new(PackNumber.Create(1), PickNumber.Create(7)), next.Pack.AvailableCardIdentifiers) }, 18);
        using var sync = new PackFrameSynchronizer(NoSyncWait);
        sync.Activate(a);
        using (var first = await sync.AcquireAsync(a, _ => Task.FromResult(SyncFrame(a, pixels.Copy())), _ => { }, default)) { }
        sync.Activate(b);
        var captures = 0; var matcherCalls = 0; SKBitmap? rejected = null; var stages = new List<string>();
        using var current = await sync.AcquireAsync(b, _ =>
        {
            Assert.Equal(0, matcherCalls);
            captures++;
            return Task.FromResult(SyncFrame(b, captures == 1 ? rejected = pixels.Copy() : SyntheticEight(b), captures));
        }, stages.Add, default);
        matcherCalls++;
        Assert.NotEqual(PackFrameSynchronizer.Hash(pixels), current.ImageHash);
        Assert.Equal(2, captures); Assert.Equal(1, matcherCalls); Assert.Equal((nint)0, rejected!.Handle);
        Assert.Equal(b.Request.PackGeneration, current.PackGeneration);
        Assert.Contains(stages, s => s.Contains("identical to prior pack"));
        Assert.True(ConfidentSyncResult(b.Request, current).IsSafeFor(b.Request));
    }

    [Fact]
    public async Task SyncCountDefenseRecognizesNineUnchangedArtRegionsDespiteOtherPixelChanges()
    {
        var (a, imageA) = LiveSyncNine(); using var pixels = imageA;
        var b = SyncContext(AutoRequest(8), 18);
        using var sync = new PackFrameSynchronizer(NoSyncWait); sync.Activate(a);
        using (var first = await sync.AcquireAsync(a, _ => Task.FromResult(SyncFrame(a, pixels.Copy())), _ => { }, default))
        {
            // Actual known nine-card rectangles from the existing fixture's successful audit; no matcher redesign/test here.
            double[,] coords = { {26.29,42.78,245.08,340.36}, {287.33,38.51,246.74,351.76}, {548.28,41.30,246.51,343.86},
                {809.99,41.51,245.92,342.36}, {1071.67,41.93,244.10,341.92}, {26.40,398.68,244.87,342.34},
                {287.40,399.97,245.48,341.92}, {549.24,400.82,245.03,338.58}, {810.38,399.95,244.71,343.49} };
            var rectangles = Enumerable.Range(0, 9).Select(i => new NormalizedDraftRegion(coords[i,0]/1325, coords[i,1]/1131,
                coords[i,2]/1325, coords[i,3]/1131)).ToArray();
            string[] visualNames = ["Hatching Plans", "Stonesplitter Bolt", "Rat Out", "Grabby Giant", "Merry Bards", "Redcap Thief",
                "Return from the Wilds", "Gingerbrute", "Scarecrow Guide"];
            var matches = visualNames.Select((name, i) => new CardVisualMatch(a.Request.Occurrences.Single(c => c.CardName.StartsWith(name, StringComparison.Ordinal)).Key, i, rectangles[i], .99,
                LocalizationConfidence.HighConfidence, "prior confirmed occupancy")).ToArray();
            await sync.RecordConfirmedCardsAsync(a, first, new(a.Request.Pack, matches, rectangles, 1325, 1131, TimeSpan.Zero,
                "prior confirmed occupancy") { PackGeneration = 17 }, default);
        }
        sync.Activate(b); var attempts = 0; var stages = new List<string>();
        using var current = await sync.AcquireAsync(b, _ =>
        {
            var image = ++attempts == 1 ? pixels.Copy() : SyntheticEight(b);
            if (attempts == 1) image.SetPixel(0, 0, SKColors.Magenta);
            return Task.FromResult(SyncFrame(b, image, attempts));
        }, stages.Add, default);
        Assert.Equal(2, attempts);
        Assert.Contains(stages, s => s.Contains("expected 8 cards, detected 9 unchanged confirmed card rectangles"));
    }

    [Fact]
    public async Task SyncRepeatedStalePixelsStopAfterThreeAttemptsWithoutMatcher()
    {
        using var sync = new PackFrameSynchronizer(NoSyncWait);
        var a = SyncContext(AutoRequest(9), 1); var b = SyncContext(AutoRequest(8), 2);
        sync.Activate(a);
        using (var baseline = await sync.AcquireAsync(a, _ => Task.FromResult(SyncFrame(a, SolidFrame(SKColors.Red))), _ => { }, default)) { }
        sync.Activate(b); var attempts = 0; var rejected = new List<SKBitmap>();
        var error = await Assert.ThrowsAsync<StaleArenaFrameException>(() => sync.AcquireAsync(b, _ =>
        {
            attempts++; var image = SolidFrame(SKColors.Red); rejected.Add(image);
            return Task.FromResult(SyncFrame(b, image, attempts));
        }, _ => { }, default));
        Assert.Equal(3, attempts); Assert.Contains("Failed after 3 synchronized", error.Message);
        Assert.All(rejected, image => Assert.Equal((nint)0, image.Handle));
    }

    [Fact]
    public async Task SyncWrongPackGenerationIsRejectedBeforeMatchingAndReacquired()
    {
        using var sync = new PackFrameSynchronizer(NoSyncWait); var b = SyncContext(AutoRequest(8), 22); sync.Activate(b);
        var attempts = 0;
        using var current = await sync.AcquireAsync(b, _ => Task.FromResult(SyncFrame(b, SolidFrame(SKColors.Blue))
            with { PackGeneration = ++attempts == 1 ? 21 : 22 }), _ => { }, default);
        Assert.Equal(2, attempts); Assert.Equal(22, current.PackGeneration);
    }

    [Fact]
    public async Task SyncFrameBeforePackTransitionIsRejectedEvenWithCurrentToken()
    {
        using var sync = new PackFrameSynchronizer(NoSyncWait); var b = SyncContext(AutoRequest(8), 22); sync.Activate(b);
        var attempts = 0;
        using var current = await sync.AcquireAsync(b, _ => Task.FromResult(SyncFrame(b, SolidFrame(SKColors.Blue))
            with { CapturedAt = ++attempts == 1 ? b.ChangedAt.AddMilliseconds(-1) : DateTimeOffset.UtcNow }), _ => { }, default);
        Assert.Equal(2, attempts); Assert.True(current.CapturedAt >= b.ChangedAt);
    }

    [Fact]
    public async Task SyncRapidPackTransitionDiscardsLateFrameAndAllowsNewRequest()
    {
        using var sync = new PackFrameSynchronizer(NoSyncWait);
        var a = SyncContext(AutoRequest(9), 1); var b = SyncContext(AutoRequest(8), 2);
        var delivery = new TaskCompletionSource<ArenaRegionFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        sync.Activate(a); var work = sync.AcquireAsync(a, _ => delivery.Task, _ => { }, default);
        sync.Activate(b); var late = SyncFrame(a, SolidFrame(SKColors.Red)); delivery.SetResult(late);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work); Assert.Equal((nint)0, late.Image.Handle);
        using var latest = await sync.AcquireAsync(b, _ => Task.FromResult(SyncFrame(b, SolidFrame(SKColors.Blue))), _ => { }, default);
        Assert.Equal(2, latest.PackGeneration);
    }

    [Fact]
    public async Task SyncVisualSettleIsCancelledBeforeAnyCaptureWhenPackChanges()
    {
        var settling = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var sync = new PackFrameSynchronizer(new(TimeSpan.FromMilliseconds(250), TimeSpan.Zero, TimeSpan.Zero), async (_, token) =>
        { settling.SetResult(); await Task.Delay(Timeout.Infinite, token); });
        var a = SyncContext(AutoRequest(9), 1) with { ChangedAt = DateTimeOffset.UtcNow }; sync.Activate(a);
        var captures = 0;
        var work = sync.AcquireAsync(a, _ => { captures++; return Task.FromResult(SyncFrame(a, SolidFrame(SKColors.Red))); }, _ => { }, default);
        await settling.Task.WaitAsync(TimeSpan.FromSeconds(2)); sync.Activate(SyncContext(AutoRequest(8), 2));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work); Assert.Equal(0, captures);
    }

    [Fact]
    public async Task SyncSamePackRetryCapturesAgainWithoutChangingSemanticGeneration()
    {
        using var overlay = CaptureOverlay(); var fake = new FakeArenaCapture(); var requests = new List<(long Pack, long Capture)>();
        await using var session = CaptureSession(overlay, fake, (r, f, _) =>
        { requests.Add((r.PackGeneration, f.CaptureRequestGeneration)); return Task.FromResult(EmptyLocalization(r, f)); });
        var generation = overlay.PlacementContext!.Generation;
        session.RetryManually(); await session.CurrentWork; session.RetryManually(); await session.CurrentWork;
        Assert.Equal(2, fake.Captures); Assert.All(requests, r => Assert.Equal(generation, r.Pack));
        Assert.True(requests[1].Capture > requests[0].Capture); Assert.Equal(generation, overlay.PlacementContext.Generation);
    }

    [Fact]
    public async Task SyncDebugSaveRejectsPriorPackPixelsAndSavesFreshCurrentPairWithoutMatching()
    {
        using var overlay = CaptureOverlay(); var fake = new FakeArenaCapture(); var matcherCalls = 0;
        var directory = Path.Combine(Path.GetTempPath(), "DraftTG-sync-" + Guid.NewGuid().ToString("N"));
        try
        {
            fake.Handler = (_, _, g, _, _) => Task.FromResult(new ArenaRegionFrame(SolidFrame(SKColors.Red), 0, 0, 1) { Generation = g });
            await using var session = CaptureSession(overlay, fake, (r, f, _) =>
            { matcherCalls++; return Task.FromResult(EmptyLocalization(r, f)); }, Path.Combine(directory, "arena-draft.png"));
            await session.SaveCurrentCaptureForDebugging(); var previousPath = session.LastDebugCapturePath;
            var (data, _) = FourBadgeFixture(); overlay.Session.ApplySessionUpdate(AssociationPack(data, [601, 602, 603], 2));
            var attempts = 0;
            fake.Handler = (_, _, g, _, _) => Task.FromResult(new ArenaRegionFrame(SolidFrame(++attempts == 1 ? SKColors.Red : SKColors.Blue), 0, 0, 1) { Generation = g });
            await session.SaveCurrentCaptureForDebugging();
            Assert.Equal(2, attempts); Assert.Equal(0, matcherCalls); Assert.NotEqual(previousPath, session.LastDebugCapturePath);
            Assert.Contains("-P1P2-gen", session.LastDebugCapturePath!);
            using var saved = SKBitmap.Decode(session.LastDebugCapturePath!); Assert.Equal(SKColors.Blue, saved.GetPixel(0, 0));
            var text = await File.ReadAllTextAsync(session.LastDebugLogPath!);
            Assert.Contains("Pack: P1P2", text); Assert.Contains("Expected card count: 3", text);
            Assert.Contains($"Pack generation/token: {overlay.PlacementContext!.Generation}", text);
            Assert.Contains($"Frame requested pack generation: {overlay.PlacementContext.Generation}", text);
            Assert.Contains("Frame acquired after pack transition: yes", text); Assert.Contains("PNG SHA256:", text);
            Assert.All(overlay.Session.CurrentPackPresentations, c => Assert.Contains(c.CardName, text));
            Assert.Equal(2, Directory.GetFiles(directory, "*.png").Length);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task SyncDebugSaveFailureNeverWritesPriorImageUnderNewPackName()
    {
        using var overlay = CaptureOverlay(); var fake = new FakeArenaCapture();
        var directory = Path.Combine(Path.GetTempPath(), "DraftTG-sync-" + Guid.NewGuid().ToString("N"));
        try
        {
            fake.Handler = (_, _, g, _, _) => Task.FromResult(new ArenaRegionFrame(SolidFrame(SKColors.Red), 0, 0, 1) { Generation = g });
            await using var session = CaptureSession(overlay, fake, debugPath: Path.Combine(directory, "arena-draft.png"));
            await session.SaveCurrentCaptureForDebugging(); var (data, _) = FourBadgeFixture();
            overlay.Session.ApplySessionUpdate(AssociationPack(data, [601, 602, 603], 2));
            await session.SaveCurrentCaptureForDebugging();
            Assert.Null(session.LastDebugCapturePath); Assert.Single(Directory.GetFiles(directory, "*.png")); Assert.Equal(4, fake.Captures);
            var failure = await File.ReadAllTextAsync(session.LastDebugLogPath!);
            Assert.Contains("Failed after 3 synchronized capture attempts", failure);
            Assert.Contains("No PNG saved", failure); Assert.Contains("Pack: P1P2", failure);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task SyncOldMatcherResultCannotApplyAfterPackAdvanceAndNewPackCanApply()
    {
        using var overlay = CaptureOverlay(); var fake = new FakeArenaCapture();
        var recognitionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delivery = new TaskCompletionSource<CardVisualLocalizationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        CardVisualLocalizationResult? oldResult = null; var color = SKColors.Red;
        fake.Handler = (_, _, g, _, _) => Task.FromResult(new ArenaRegionFrame(SolidFrame(color), 0, 0, 1) { Generation = g });
        await using var session = CaptureSession(overlay, fake, (r, f, _) =>
        {
            if (r.Pack.Position.Pick.Value == 1) { oldResult = ConfidentSyncResult(r, f); recognitionStarted.SetResult(); return delivery.Task; }
            return Task.FromResult(ConfidentSyncResult(r, f));
        });
        session.RetryManually(); await recognitionStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var (data, _) = FourBadgeFixture(); overlay.Session.ApplySessionUpdate(AssociationPack(data, [601, 602, 603], 2));
        Assert.All(overlay.Badges, b => Assert.False(b.IsPlaced)); color = SKColors.Blue;
        session.RetryManually(); delivery.SetResult(oldResult!); await session.CurrentWork;
        Assert.True(overlay.HasAutomaticVisualPlacement); Assert.Equal(3, overlay.Badges.Count); Assert.Equal(2, fake.Captures);
        Assert.NotEqual(oldResult!.PackGeneration, overlay.PlacementContext!.Generation);
    }

    [Fact]
    public void SyncResultApplyRejectsWrongTokenEvenWhenPackAndCoordinatesMatch()
    {
        using var overlay = CaptureOverlay(); var context = overlay.PlacementContext!;
        var request = new CardVisualLocalizationRequest(context.Pack, overlay.Session.CurrentPackPresentations, overlay.Layout)
            { PackGeneration = context.Generation };
        using var frame = new ArenaRegionFrame(SolidFrame(SKColors.Blue), 0, 0, 1);
        var result = ConfidentSyncResult(request, frame);
        Assert.False(overlay.ApplyAutomaticLocalization(context, result with { PackGeneration = context.Generation - 1 }));
        Assert.All(overlay.Badges, b => Assert.False(b.IsPlaced)); Assert.True(overlay.ApplyAutomaticLocalization(context, result));
    }

    [Fact]
    public async Task SyncCoordinatorRejectsCachedFrameTimestampBeforeFreshRequest()
    {
        var fake = new FakeArenaCapture(); SKBitmap? discarded = null;
        fake.Handler = (_, _, g, _, _) => Task.FromResult(new ArenaRegionFrame(discarded = SolidFrame(SKColors.Red), 0, 0, 1)
            { Generation = g, CapturedAt = DateTimeOffset.UtcNow.AddMinutes(-1) });
        using var coordinator = CaptureCoordinator(fake);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => coordinator.AcquireAsync(CaptureRegion, packGeneration: 17));
        Assert.Contains("Stale frame rejected", error.Message); Assert.Equal((nint)0, discarded!.Handle);
        Assert.False(coordinator.Diagnostics.LocalizationInvoked);
    }

    [Fact]
    public async Task SyncDebugPairCommitRejectsGenerationChangeAndCleansTemporaryFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "DraftTG-sync-" + Guid.NewGuid().ToString("N"));
        try
        {
            var context = SyncContext(AutoRequest(8), 18);
            using var frame = SyncFrame(context, SolidFrame(SKColors.Blue));
            var path = DebugCaptureArtifacts.CreatePath(Path.Combine(directory, "arena-draft.png"), context, 2);
            await Assert.ThrowsAsync<OperationCanceledException>(() => DebugCaptureArtifacts.SaveAsync(path, frame, "test",
                () => throw new OperationCanceledException("Pack changed during encoding"), default));
            Assert.Empty(Directory.GetFiles(directory));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
