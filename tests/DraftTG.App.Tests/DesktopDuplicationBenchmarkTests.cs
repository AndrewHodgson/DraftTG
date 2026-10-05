using System.Text.Json;
using DraftTG.Application;
using DraftTG.App.Platform;
using SkiaSharp;

namespace DraftTG.App.Tests;

public sealed class DesktopDuplicationBenchmarkTests
{
    private static readonly ArenaWindowGeometry Window = new(42, -1000, 50, 800, 480, 1.5, false) { ProcessId = 24 };
    private static readonly NormalizedDraftRegion Region = new(0, 0, 1, 1);
    private static readonly PackFrameTiming NoWait = new(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);
    private sealed class Fake(string name) : IArenaRegionCapture
    {
        public string Backend => name;
        public int Calls { get; private set; }
        public Func<long, CancellationToken, Task<ArenaRegionFrame>>? Acquire { get; set; }
        public ArenaWindowInspection InspectWindow() => new(true, Window);
        public Task<ArenaRegionFrame> CaptureAsync(ArenaWindowGeometry window, NormalizedDraftRegion region, long generation,
            Action<string> stage, CancellationToken token)
        { Calls++; return Acquire?.Invoke(generation, token) ?? Task.FromResult(Frame(generation)); }
    }
    private static ArenaRegionFrame Frame(long generation)
    {
        var image = new SKBitmap(Window.Width, Window.Height); image.Erase(SKColors.Red);
        return new(image, Window.X, Window.Y, Window.Scaling) { Generation = generation, Backend = "fake", WindowHandle = Window.Handle };
    }
    private static PackFrameContext Context(long generation, int count = 9)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "arena-artwork-live", "manifest.json");
        var manifest = JsonSerializer.Deserialize<OfflineLocalizationManifest>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        return new(OfflineArtworkCommand.CreateRequest(manifest with { Cards = manifest.Cards.Take(count).ToArray() }) with { PackGeneration = generation }, DateTimeOffset.UtcNow.AddSeconds(-2));
    }
    private static CaptureBackendComparison Compare(Fake a, Fake b) => new(a, b, new(TimeSpan.FromSeconds(1)), NoWait);
    private static Fake Failed(string name) => new(name) { Acquire = (_, _) => Task.FromException<ArenaRegionFrame>(new CaptureBackendInitializationException("fake init failure", operation: "test initialization")) };

    [Fact] public async Task ShadowSelectionKeepsPrimaryWhenBothFramesAcceptedAndDoesNotClaimRecognition()
    {
        using var comparison = Compare(new("primary"), new("secondary"));
        var pair = await comparison.CompareAsync(null, Window, Region, default);
        Assert.Equal("primary", pair.SelectedBackend); Assert.StartsWith("A:", pair.Outcome);
        Assert.False(pair.ManualCaptureFallbackRequired);
        Assert.All(pair.Records, r => { Assert.Null(r.Matched); Assert.Null(r.ExpectedCards); Assert.Null(r.DebugImage); Assert.Equal(16, r.PerceptualHash!.Length); });
    }
    [Fact] public async Task SecondaryStillCapturesAfterPrimaryInitializationFailure()
    {
        using var comparison = Compare(Failed("primary"), new("secondary"));
        var pair = await comparison.CompareAsync(null, Window, Region, default);
        Assert.Equal("secondary", pair.SelectedBackend); Assert.StartsWith("C:", pair.Outcome);
        Assert.Contains("test initialization", pair.Records[0].Diagnostic);
        Assert.True(pair.Records[1].FreshFrameAccepted);
    }
    [Fact] public async Task SecondaryFailureDoesNotResetAcceptedPrimary()
    {
        using var comparison = Compare(new("primary"), Failed("secondary"));
        var pair = await comparison.CompareAsync(null, Window, Region, default);
        Assert.Equal("primary", pair.SelectedBackend); Assert.StartsWith("D:", pair.Outcome);
    }
    [Fact] public async Task BothFailuresRequireManualCaptureFallbackAndNoSafePlacement()
    {
        using var comparison = Compare(Failed("primary"), Failed("secondary"));
        var pair = await comparison.CompareAsync(null, Window, Region, default);
        Assert.Null(pair.SelectedBackend); Assert.True(pair.ManualCaptureFallbackRequired);
        Assert.All(pair.Records, r => Assert.False(r.SafePlacement));
    }
    [Fact] public async Task PackAndRequestGenerationsPropagateIndependently()
    {
        using var comparison = Compare(new("primary"), new("secondary")); var context = Context(44);
        comparison.Activate(context);
        var pair = await comparison.CompareAsync(context, Window, Region, default);
        Assert.All(pair.Records, r => { Assert.True(r.FreshFrameAccepted); Assert.Equal(44, r.PackGeneration); Assert.Equal(1, r.CaptureRequestGeneration); Assert.True(r.CaptureGeneration > 0); });
    }
    [Fact] public async Task WrongCaptureGenerationIsRejectedAndItsBitmapReleased()
    {
        ArenaRegionFrame? stale = null;
        var primary = new Fake("primary") { Acquire = (g, _) => Task.FromResult(stale = Frame(g - 1)) };
        using var comparison = Compare(primary, new("secondary"));
        var pair = await comparison.CompareAsync(null, Window, Region, default);
        Assert.False(pair.Records[0].FreshFrameAccepted); Assert.True(pair.Records[1].FreshFrameAccepted);
        Assert.Equal((nint)0, stale!.Image.Handle);
    }
    [Fact] public async Task CompletedOldPackCaptureCannotApplyAfterPackActivation()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        async Task<ArenaRegionFrame> Block(long g, CancellationToken ignored)
        { if (Interlocked.Increment(ref calls) == 2) entered.SetResult(); await release.Task; return Frame(g); }
        using var comparison = Compare(new("primary") { Acquire = Block }, new("secondary") { Acquire = Block });
        var old = Context(10); comparison.Activate(old);
        var work = comparison.CompareAsync(old, Window, Region, default); await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        comparison.Activate(Context(11, 8)); release.SetResult();
        var result = await work;
        Assert.All(result.Records, r => Assert.False(r.FreshFrameAccepted)); Assert.True(result.ManualCaptureFallbackRequired);
    }
    [Fact] public async Task IdenticalPixelsOnCardCountChangeAreRetriedAndNeverAccepted()
    {
        var primary = new Fake("primary"); var secondary = new Fake("secondary"); using var comparison = Compare(primary, secondary);
        var a = Context(20); comparison.Activate(a); await comparison.CompareAsync(a, Window, Region, default);
        var b = Context(21, 8); comparison.Activate(b); var pair = await comparison.CompareAsync(b, Window, Region, default);
        Assert.All(pair.Records, r => { Assert.False(r.FreshFrameAccepted); Assert.Equal(3, r.CaptureAttempts); Assert.Contains("identical to prior pack", r.Diagnostic); });
        Assert.Equal(4, primary.Calls); Assert.Equal(4, secondary.Calls);
    }
    [Fact] public async Task PrimaryCanRecoverWithoutRecreatingOrResettingSecondary()
    {
        var primary = new Fake("primary"); primary.Acquire = (g, _) => primary.Calls == 1 ? Task.FromException<ArenaRegionFrame>(new IOException("first failure")) : Task.FromResult(Frame(g));
        var secondary = new Fake("secondary"); using var comparison = Compare(primary, secondary);
        await comparison.CompareAsync(null, Window, Region, default);
        var recovered = await comparison.CompareAsync(null, Window, Region, default);
        Assert.Equal("primary", recovered.SelectedBackend); Assert.Equal(2, secondary.Calls);
    }
    [Fact] public void PointerOnlyOrZeroImageMetadataCannotCountAsPixelUpdate()
    {
        Assert.False(DesktopDuplicationGeometry.IsPixelUpdate(0, 0)); Assert.False(DesktopDuplicationGeometry.IsPixelUpdate(123, 0));
        Assert.False(DesktopDuplicationGeometry.IsPixelUpdate(0, 1)); Assert.True(DesktopDuplicationGeometry.IsPixelUpdate(123, 1));
    }
    [Fact] public void PrimaryAndNegativeOriginSecondaryOutputsMapPhysicalPixelsWithoutDpiRescaling()
    {
        var outputs = new[] { new DesktopOutput(0, 0, "primary", new(0, 0, 1920, 1080), true, 1), new DesktopOutput(1, 0, "secondary", new(-1920, 0, 1920, 1080), true, 1) };
        var secondary = Window with { X = -1800, Y = 80 };
        var selected = DesktopDuplicationGeometry.Select(secondary, outputs);
        Assert.Equal("secondary", selected.Name);
        Assert.Equal(new ArenaDraftCrop(140, 110, 300, 200), DesktopDuplicationGeometry.Map(selected, secondary, new(20, 30, 300, 200)));
        Assert.Equal("primary", DesktopDuplicationGeometry.Select(secondary with { X = 100 }, outputs).Name);
    }
    [Fact] public void SpanningClientIsRejectedEvenWhenDraftRoiCouldFitOneMonitor()
    {
        var outputs = new[] { new DesktopOutput(0, 0, "a", new(0, 0, 1920, 1080), true, 1), new DesktopOutput(0, 1, "b", new(1920, 0, 1920, 1080), true, 1) };
        Assert.Throws<InvalidDataException>(() => DesktopDuplicationGeometry.Select(Window with { X = 1800 }, outputs));
    }
    [Fact] public async Task OldTimestampWrongPackOrRequestTokensAreRejectedBeforeRecognition()
    {
        var primary = new Fake("primary");
        primary.Acquire = (g, _) => Task.FromResult(primary.Calls switch
        {
            1 => Frame(g) with { CapturedAt = DateTimeOffset.UtcNow.AddMinutes(-1) },
            2 => Frame(g) with { PackGeneration = 999 },
            _ => Frame(g) with { CaptureRequestGeneration = 999 }
        });
        using var comparison = Compare(primary, new("secondary")); var context = Context(50); comparison.Activate(context);
        var pair = await comparison.CompareAsync(context, Window, Region, default);
        Assert.False(pair.Records[0].FreshFrameAccepted); Assert.Equal(3, pair.Records[0].CaptureAttempts);
        Assert.True(pair.Records[1].FreshFrameAccepted); Assert.Equal("secondary", pair.SelectedBackend);
    }
    [Fact] public async Task DebugSaveFailureDoesNotPoisonIndependentCaptureResults()
    {
        using var comparison = new CaptureBackendComparison(new Fake("primary"), new Fake("secondary"), new(TimeSpan.FromSeconds(1)), NoWait,
            save: (_, _) => throw new IOException("disk failure"));
        var pair = await comparison.CompareAsync(null, Window, Region, default);
        Assert.All(pair.Records, r => { Assert.True(r.FreshFrameAccepted); Assert.Contains("Debug save failed", r.Diagnostic); Assert.Null(r.DebugImage); });
        Assert.False(pair.ManualCaptureFallbackRequired);
    }
}
