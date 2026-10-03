using DraftTG.Data;

namespace DraftTG.Application.Tests;

public sealed class OverlayCalibrationTests
{
    private static readonly OverlayScreen Screen = new(0, 0, 3456, 2234, 2);
    private static OverlayCalibration Valid() => new(Screen, new(0.12, 0.18, 0.76, 0.62), DraftCardLayout.Grid(7));

    [Theory]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void GridProfilesHaveFourteenNormalizedOrderedSlots(int columns)
    {
        var layout = DraftCardLayout.Grid(columns);
        Assert.Equal(14, layout.Slots.Count);
        Assert.Equal(Enumerable.Range(0, 14), layout.Slots.Select(slot => slot.Index));
        Assert.All(layout.Slots, slot => Assert.True(slot.IsValid));
        Assert.True(layout.Slots[columns].Y > layout.Slots[0].Y);
    }
    [Fact]
    public void SlotsScaleToRegionWithoutKnowingScreenResolution()
    {
        var slot = new CardSlot(0, 0.25, 0.5, 0.1, 0.2);
        Assert.Equal(new SlotBounds(250, 250, 100, 100), slot.Scale(1000, 500));
        Assert.Equal(new SlotBounds(500, 500, 200, 200), slot.Scale(2000, 1000));
    }
    [Fact]
    public void LayoutCopiesInputAndRejectsMissingOrMisorderedSlots()
    {
        var slots = DraftCardLayout.Grid(7).Slots.ToArray();
        var layout = new DraftCardLayout(slots);
        slots[0] = slots[0] with { X = 0.4 };
        Assert.NotEqual(slots[0], layout.Slots[0]);
        Assert.Throws<ArgumentException>(() => new DraftCardLayout(slots.Take(13)));
        Assert.Throws<ArgumentException>(() => new DraftCardLayout(slots.Reverse()));
    }
    [Theory]
    [InlineData(-0.1, 0, 0.5, 0.5)]
    [InlineData(0.8, 0, 0.5, 0.5)]
    [InlineData(0, 0, 0, 0.5)]
    [InlineData(double.NaN, 0, 0.5, 0.5)]
    [InlineData(0, 0, double.PositiveInfinity, 0.5)]
    public void InvalidRegionCannotBeUsed(double x, double y, double width, double height) =>
        Assert.False((Valid() with { Region = new(x, y, width, height) }).IsValidFor(Screen));

    [Fact]
    public void ChangedDisplaySizePositionOrScalingRequiresRecalibration()
    {
        var calibration = Valid();
        Assert.True(calibration.IsValidFor(Screen));
        Assert.False(calibration.IsValidFor(Screen with { Width = 1920 }));
        Assert.False(calibration.IsValidFor(Screen with { X = -3456 }));
        Assert.False(calibration.IsValidFor(Screen with { Scaling = 1 }));
    }
    [Fact]
    public async Task CalibrationRoundTripsRegionScreenAndCustomSlots()
    {
        var file = new MemoryFile();
        var service = new OverlayCalibrationService(file);
        var slots = DraftCardLayout.Grid(6).Slots.ToArray();
        slots[0] = slots[0] with { X = 0.01 };
        var original = Valid() with { Layout = new(slots) };
        Assert.Null(await service.SaveAsync(original));
        var result = await new OverlayCalibrationService(file).LoadAsync([Screen]);
        Assert.Null(result.Diagnostic);
        Assert.Equal(original.Region, result.Calibration!.Region);
        Assert.Equal(original.Layout.Slots, result.Calibration.Layout.Slots);
    }
    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("<html/>")]
    [InlineData("{\"Version\":999}")]
    public async Task InvalidSavedDocumentReturnsCalibrationRequired(string json)
    {
        var result = await new OverlayCalibrationService(new MemoryFile { Json = json }).LoadAsync([Screen]);
        Assert.Null(result.Calibration);
        Assert.NotNull(result.Diagnostic);
    }
    [Fact]
    public async Task MissingCalibrationIsNormalFirstRun()
    {
        var result = await new OverlayCalibrationService(new MemoryFile()).LoadAsync([Screen]);
        Assert.Null(result.Calibration);
        Assert.Null(result.Diagnostic);
    }
    [Fact]
    public async Task SavedCalibrationForDisconnectedDisplayIsNotApplied()
    {
        var file = new MemoryFile();
        var service = new OverlayCalibrationService(file);
        await service.SaveAsync(Valid());
        Assert.Null((await service.LoadAsync([Screen with { Width = 1920 }])).Calibration);
    }
    [Fact]
    public async Task SaveFailureIsNonfatalAndCancellationPropagates()
    {
        var service = new OverlayCalibrationService(new FailingFile());
        Assert.NotNull(await service.SaveAsync(Valid()));
        Assert.Null((await service.LoadAsync([Screen])).Calibration);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new OverlayCalibrationService(new MemoryFile()).LoadAsync([Screen], cts.Token));
    }
    [Theory]
    [InlineData(0, 0, 2)]
    [InlineData(-3456, 120, 2)]
    [InlineData(3456, -200, 1)]
    public async Task SaveCapturesResizedMovedWindowAndReopeningRestoresIt(int screenX, int screenY, double scaling)
    {
        var screen = Screen with { X = screenX, Y = screenY, Scaling = scaling };
        var file = new MemoryFile();
        var service = new OverlayCalibrationService(file);
        var original = new OverlayWindowGeometry(screenX + 100, screenY + 100, 600, 300);
        await service.SaveAsync(original.Capture(screen, scaling, DraftCardLayout.Grid(7)));
        var movedAndResized = new OverlayWindowGeometry(screenX + 240, screenY + 220, 900, 440);
        await service.SaveAsync(movedAndResized.Capture(screen, scaling, DraftCardLayout.Grid(6)));
        var loaded = (await new OverlayCalibrationService(file).LoadAsync([screen])).Calibration!;
        AssertGeometryEqual(movedAndResized, OverlayWindowGeometry.Restore(loaded));
        Assert.Equal(DraftCardLayout.Grid(6).Slots, loaded.Layout.Slots);
    }
    [Fact]
    public async Task UnsavedGeometryDoesNotChangeSavedCalibration()
    {
        var file = new MemoryFile();
        var service = new OverlayCalibrationService(file);
        var saved = new OverlayWindowGeometry(240, 220, 900, 440);
        await service.SaveAsync(saved.Capture(Screen, 2, DraftCardLayout.Grid(7)));
        var unsaved = (saved with { X = 320, Width = 1000 }).Capture(Screen, 2, DraftCardLayout.Grid(7));
        Assert.True(unsaved.IsValidFor(Screen));
        AssertGeometryEqual(saved, OverlayWindowGeometry.Restore((await service.LoadAsync([Screen])).Calibration!));
    }
    [Theory]
    [InlineData(-1, 400, 220)]
    [InlineData(100, 399, 220)]
    [InlineData(100, 400, 219)]
    [InlineData(100, double.NaN, 220)]
    [InlineData(100, 2000, 220)]
    public void InvalidNativeGeometryRemainsRejected(int x, double width, double height)
    {
        var calibration = new OverlayWindowGeometry(x, 100, width, height).Capture(Screen, 2, DraftCardLayout.Grid(7));
        Assert.False(calibration.IsValidFor(Screen));
    }
    private static void AssertGeometryEqual(OverlayWindowGeometry expected, OverlayWindowGeometry actual)
    {
        Assert.Equal(expected.X, actual.X);
        Assert.Equal(expected.Y, actual.Y);
        // Normalization and JSON round trips have floating-point error far below a pixel.
        Assert.Equal(expected.Width, actual.Width, precision: 8);
        Assert.Equal(expected.Height, actual.Height, precision: 8);
    }
    private sealed class MemoryFile : IOverlaySettingsFile
    {
        public string? Json { get; set; }
        public Task<string?> ReadAsync(CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(Json); }
        public Task WriteAsync(string json, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); Json = json; return Task.CompletedTask; }
    }
    private sealed class FailingFile : IOverlaySettingsFile
    {
        public Task<string?> ReadAsync(CancellationToken cancellationToken = default) => throw new IOException();
        public Task WriteAsync(string json, CancellationToken cancellationToken = default) => throw new IOException();
    }
}
