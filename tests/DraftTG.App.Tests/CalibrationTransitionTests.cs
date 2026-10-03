using DraftTG.Application;
using DraftTG.App.Platform;
using DraftTG.ArenaIntegration;
using DraftTG.Data;

namespace DraftTG.App.Tests;

public sealed partial class MainWindowViewModelTests
{
    [Theory]
    [InlineData(1728, 1117, 1)] // macOS desktop points, even on a Retina display rendering at 2x
    [InlineData(3456, 2234, 2)] // Windows desktop pixels at 200%
    public async Task SaveExitsCalibrationClosesPanelAndRetainsBadges(double width, double height, double scale)
    {
        using var rig = new CalibrationRig();
        var screen = new OverlayScreen(0, 0, width, height, scale);
        var current = new OverlayWindowGeometry(100, 100, 1200, 500).Capture(screen, DraftCardLayout.Grid(6));
        rig.Editor.Begin(current);
        AssertEditing(rig);
        Assert.True(rig.Native.Modes[^1].AllowsActivation);
        Assert.False(rig.Native.Modes[^1].IgnoresMouseEvents);
        var badges = rig.Model.Badges.ToArray();

        await rig.Editor.SaveAsync(current);

        AssertPassive(rig, calibrated: true);
        Assert.Equal(current, rig.Editor.Saved);
        var persisted = (await new OverlayCalibrationService(rig.File).LoadAsync([screen])).Calibration!;
        Assert.Equal(current.Region, persisted.Region);
        Assert.Equal(current.Layout.Slots, persisted.Layout.Slots);
        Assert.Equal(badges, rig.Model.Badges);
        Assert.Equal(current, rig.Surface.Applied);
        Assert.Equal(["hide", "close", "geometry", "passive"], rig.Surface.Events.TakeLast(4));

        rig.Editor.Begin(current with { Region = new(0.2, 0.2, 0.6, 0.6) });
        AssertEditing(rig);
        Assert.Equal(current, rig.Surface.Applied); // Reopen uses saved geometry, not a new default.
    }

    [Fact]
    public void RetinaCaptureUsesDesktopScaleRatherThanBackingPixelScale()
    {
        var screen = new OverlayScreen(0, 0, 1728, 1117, 1);
        var actual = new OverlayWindowGeometry(180, 160, 1200, 600);
        // The previous save path doubled the region and returned before exiting calibration.
        Assert.False(actual.Capture(screen, 2, DraftCardLayout.Grid(7)).IsValidFor(screen));
        var corrected = actual.Capture(screen, DraftCardLayout.Grid(7));
        Assert.True(corrected.IsValidFor(screen));
        var restored = OverlayWindowGeometry.Restore(corrected);
        Assert.Equal(actual.X, restored.X);
        Assert.Equal(actual.Y, restored.Y);
        Assert.Equal(actual.Width, restored.Width, 8);
        Assert.Equal(actual.Height, restored.Height, 8);
    }

    [Theory]
    [InlineData(600)]
    [InlineData(600.5)]
    public void RestoredGeometryDoesNotGrowByAPixelFromNormalizationNoise(double height)
    {
        var screen = new OverlayScreen(0, 0, 1728, 1117, 1);
        var geometry = new OverlayWindowGeometry(180, 160, 1200, height);
        Assert.Equal(geometry, OverlayWindowGeometry.Restore(geometry.Capture(screen, DraftCardLayout.Grid(7))));
    }

    [Fact]
    public async Task CancelDiscardsUnsavedGeometryAndClosesCalibration()
    {
        using var rig = new CalibrationRig();
        rig.Editor.Begin(rig.Initial);
        await rig.Editor.SaveAsync(rig.Initial);
        var persisted = rig.File.Json;
        rig.Editor.Begin(rig.Initial);
        rig.Surface.ApplyGeometry(rig.Initial with { Region = new(0.2, 0.2, 0.7, 0.6) });
        rig.Model.SetLayout(DraftCardLayout.Grid(4));
        rig.Editor.Cancel();
        AssertPassive(rig, calibrated: true);
        Assert.Equal(rig.Initial, rig.Surface.Applied);
        Assert.Equal(rig.Initial.Layout.Slots, rig.Model.Layout.Slots);
        Assert.Equal(persisted, rig.File.Json);
    }

    [Fact]
    public void CancelFirstCalibrationReturnsToUncalibratedState()
    {
        using var rig = new CalibrationRig();
        rig.Editor.Begin(rig.Initial);
        rig.Editor.Cancel();
        AssertPassive(rig, calibrated: false);
        Assert.True(rig.Model.NeedsCalibration);
        Assert.Null(rig.Editor.Saved);
        Assert.Null(rig.File.Json);
    }

    [Fact]
    public async Task SaveWaitsForPersistenceBeforeExiting()
    {
        using var rig = new CalibrationRig();
        rig.File.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Editor.Begin(rig.Initial);
        var saving = rig.Editor.SaveAsync(rig.Initial);
        Assert.True(rig.Editor.IsSaving);
        AssertEditing(rig);
        Assert.Null(rig.Editor.Saved);
        rig.File.Gate.SetResult();
        await saving;
        AssertPassive(rig, calibrated: true);
        Assert.False(rig.Editor.IsSaving);
    }

    [Fact]
    public async Task InvalidGeometryDoesNotPretendSaveSucceeded()
    {
        using var rig = new CalibrationRig();
        rig.Editor.Begin(rig.Initial);
        await rig.Editor.SaveAsync(rig.Initial with { Region = new(0.8, 0.1, 0.7, 0.6) });
        AssertEditing(rig);
        Assert.Null(rig.File.Json);
        Assert.Null(rig.Editor.Saved);
        Assert.Contains("fully on one screen", rig.Model.Diagnostics, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteFailureRetainsEditingAndPreviousSavedGeometry()
    {
        using var rig = new CalibrationRig();
        rig.Editor.SetSaved(rig.Initial);
        rig.Editor.Begin(rig.Initial);
        rig.File.Fail = true;
        await rig.Editor.SaveAsync(rig.Initial with { Region = new(0.2, 0.2, 0.7, 0.6) });
        AssertEditing(rig);
        Assert.Equal(rig.Initial, rig.Editor.Saved);
        Assert.Contains("could not be saved", rig.Model.Diagnostics, StringComparison.Ordinal);
        Assert.False(rig.Editor.IsSaving);
    }

    [Fact]
    public async Task DisplayInvalidationDuringSaveDoesNotRestoreStaleCalibration()
    {
        using var rig = new CalibrationRig();
        rig.File.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Editor.Begin(rig.Initial);
        var saving = rig.Editor.SaveAsync(rig.Initial);
        rig.Editor.SetSaved(null);
        rig.Editor.Cancel();
        rig.File.Gate.SetResult();
        await saving;
        AssertPassive(rig, calibrated: false);
        Assert.Null(rig.Editor.Saved);
    }

    [Fact]
    public async Task PassiveAdapterFailureStillRemovesGuidesAndClosesPanelButHidesCards()
    {
        using var rig = new CalibrationRig();
        rig.Editor.Begin(rig.Initial);
        rig.Native.FailPassive = true;
        await rig.Editor.SaveAsync(rig.Initial);
        Assert.False(rig.Model.IsCalibrating);
        Assert.Empty(rig.Model.Guides);
        Assert.False(rig.Surface.PanelOpen);
        Assert.False(rig.Surface.CardsVisible);
        Assert.Contains("hidden", rig.Model.Diagnostics, StringComparison.Ordinal);
    }

    private static void AssertEditing(CalibrationRig rig)
    {
        Assert.True(rig.Model.IsCalibrating); // Shared visibility binding for border, strip, handles, tint.
        Assert.True(rig.Model.IsCalibrationPanel);
        Assert.Equal(14, rig.Model.Guides.Count);
        Assert.True(rig.Surface.PanelOpen);
        Assert.True(rig.Surface.CardsVisible);
    }
    private static void AssertPassive(CalibrationRig rig, bool calibrated)
    {
        Assert.False(rig.Model.IsCalibrating);
        Assert.False(rig.Model.IsCalibrationPanel);
        Assert.Empty(rig.Model.Guides);
        Assert.Equal(RailPanel.None, rig.Model.Panel);
        Assert.False(rig.Surface.PanelOpen);
        Assert.Equal(calibrated, rig.Model.ShowBadges);
        Assert.Equal(calibrated, rig.Surface.CardsVisible);
        Assert.True(rig.Native.Modes[^1].IgnoresMouseEvents);
        Assert.False(rig.Native.Modes[^1].AllowsActivation);
    }
    private sealed class CalibrationRig : IDisposable
    {
        public OverlayViewModel Model { get; }
        public TransitionFile File { get; } = new();
        public TransitionNative Native { get; } = new();
        public TransitionSurface Surface { get; }
        public OverlayCalibrationEditor Editor { get; }
        public OverlayCalibration Initial { get; } = new(new(0, 0, 1728, 1117, 1), new(0.1, 0.1, 0.75, 0.65), DraftCardLayout.Grid(7));
        public CalibrationRig()
        {
            var runtime = ReadyViewModel();
            runtime.ApplySessionUpdate(ReadyUpdate(ArenaDraftMode.Quick, [101, 102], []));
            Model = new(runtime);
            Surface = new(Model);
            Editor = new(Model, new(File), new(Native), Surface);
        }
        public void Dispose() => Model.Dispose();
    }
    private sealed class TransitionNative : IClickThroughWindowController
    {
        public List<OverlayInteractionMode> Modes { get; } = [];
        public bool FailPassive { get; set; }
        public void SetMode(OverlayInteractionMode mode)
        {
            Modes.Add(mode);
            if (FailPassive && mode == OverlayInteractionMode.Passive) throw new InvalidOperationException("Test failure");
        }
    }
    private sealed class TransitionSurface(OverlayViewModel model) : ICalibrationSurface
    {
        public bool CardsVisible { get; private set; }
        public bool PanelOpen { get; private set; }
        public OverlayCalibration? Applied { get; private set; }
        public List<string> Events { get; } = [];
        public void HideCards() { Events.Add("hide"); CardsVisible = false; }
        public void ClosePanel() { Events.Add("close"); PanelOpen = false; model.SetPanel(RailPanel.None); }
        public void ApplyGeometry(OverlayCalibration calibration)
        {
            Events.Add("geometry"); Applied = calibration; model.SetLayout(calibration.Layout);
            var bounds = OverlayWindowGeometry.Restore(calibration);
            model.SetViewport(bounds.Width, bounds.Height);
        }
        public void ShowCalibration() { CardsVisible = true; PanelOpen = true; model.SetPanel(RailPanel.Calibration); }
        public void RefreshPassive() { Events.Add("passive"); CardsVisible = model.ShowBadges; }
    }
    private sealed class TransitionFile : IOverlaySettingsFile
    {
        public string? Json { get; private set; }
        public bool Fail { get; set; }
        public TaskCompletionSource? Gate { get; set; }
        public Task<string?> ReadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Json);
        public async Task WriteAsync(string json, CancellationToken cancellationToken = default)
        {
            if (Gate is not null) await Gate.Task.WaitAsync(cancellationToken);
            if (Fail) throw new IOException("Test failure");
            Json = json;
        }
    }
}
