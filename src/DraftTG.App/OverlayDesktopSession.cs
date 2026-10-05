using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using DraftTG.Application;
using DraftTG.App.Platform;

namespace DraftTG.App;

/// <summary>One runtime and lifecycle for both windows. All window mutations stay on the UI thread.</summary>
public sealed class OverlayDesktopSession : ICalibrationSurface
{
    private readonly MainWindowViewModel _runtime;
    private readonly OverlayViewModel _presentation;
    private readonly CardOverlayWindow _cards;
    private readonly OverlayInteractionController _interaction;
    private readonly OverlayCalibrationService _settings;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly OverlayCalibrationEditor _calibration;
    private readonly AutomaticCardLocalizationSession _localization;
    private bool _localizationSurfaceVisible = true;
    private OverlayCalibration? Saved => _calibration.Saved;
    private bool _closing;
    private bool _allowClose;
    private bool _opened;
    private bool _fatalShown;
    private Task<OverlayCalibrationLoadResult>? _loadTask;
    private Task? _saveTask;
    private int _calibrationRevision;
    public OverlayDesktopSession(MainWindowViewModel runtime, OverlayCalibrationService settings)
    {
        _runtime = runtime;
        _settings = settings;
        _presentation = new(runtime);
        Rail = new(_presentation);
        _cards = new(_presentation);
        _interaction = new(ClickThroughWindowControllerFactory.Create(_cards));
        _calibration = new(_presentation, settings, _interaction, this);
        _localization = new(_presentation, () => Saved, (x, y, width, height) =>
        {
            _cards.Position = new(x, y); _cards.Width = width; _cards.Height = height;
            _presentation.SetViewport(width, height);
        }, visible => { _localizationSurfaceVisible = visible; RefreshVisibility(); });
        Rail.Opened += OnOpened;
        Rail.Closing += OnClosing;
        Rail.ActionRequested += OnAction;
        _cards.Closing += (_, e) => { if (!_allowClose) { e.Cancel = true; Rail.Close(); } };
        _presentation.PropertyChanged += PresentationChanged;
        Rail.Screens.Changed += ScreensChanged;
    }
    public ControlRailWindow Rail { get; }

    private async void OnOpened(object? sender, EventArgs e)
    {
        _opened = true;
        var screen = Rail.Screens.ScreenFromWindow(Rail) ?? Rail.Screens.Primary;
        if (screen is not null)
            Rail.Position = new(screen.WorkingArea.X + 12, screen.WorkingArea.Y + 60);
        _runtime.Start(); // Calibration I/O never gates Scryfall bootstrap or Arena tracking.
        _localization.Start(); // Timer reports prerequisites even if calibration loading is superseded.
        try
        {
            var revision = _calibrationRevision;
            var result = await (_loadTask = _settings.LoadAsync(CurrentScreens(), _lifetime.Token));
            if (_closing || revision != _calibrationRevision) return;
            _calibration.SetSaved(result.Calibration);
            _presentation.SetDiagnostic(result.Diagnostic);
            if (Saved is not null) ApplyGeometry(Saved);
            _presentation.SetCalibrationState(false, Saved is not null);
            RefreshVisibility();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
    }

    private void PresentationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OverlayViewModel.ShowBadges) && !_calibration.IsTransitioning) RefreshVisibility();
        if (e.PropertyName == nameof(OverlayViewModel.CompactStatus) && _presentation.CompactStatus == "Error" && !_fatalShown)
        {
            _fatalShown = true;
            Rail.OpenPanel(RailPanel.Status);
        }
    }
    private void RefreshVisibility()
    {
        if (!_opened || _closing) return;
        if (!_interaction.TryApply(_presentation.IsCalibrating, out var diagnostic))
        {
            _cards.Hide();
            _presentation.SetDiagnostic(diagnostic);
            return;
        }
        if (!_presentation.IsCalibrating && (!_presentation.ShowBadges || Saved is null || !_localizationSurfaceVisible))
        { _cards.Hide(); return; }
        if (!_cards.IsVisible) _cards.Show(); // No activation during normal gameplay.
        // Showing a native window can reset its style; verify the complete policy afterward.
        if (!_interaction.TryApply(_presentation.IsCalibrating, out diagnostic))
        {
            _cards.Hide();
            _presentation.SetDiagnostic(diagnostic);
            return;
        }
        if (_cards.ActualTransparencyLevel != WindowTransparencyLevel.Transparent)
        {
            _cards.Hide();
            _presentation.SetDiagnostic("Card overlay hidden: transparent windows are unavailable on this display.");
        }
    }

    private async void OnAction(string action)
    {
        if (_closing || _calibration.IsSaving) return;
        switch (action)
        {
            case "close-panel": Rail.ClosePanel(); break;
            case "edit": BeginCalibration(); break;
            case "cancel": CancelCalibration(); break;
            case "save": await (_saveTask = SaveCalibrationAsync()); break;
            case "confirm-card-positions": _presentation.ConfirmVisualSelections(); break;
            case "retry-card-localization": _localization.RetryManually(); break;
            case "save-card-capture": await _localization.SaveCurrentCaptureForDebugging(); break;
            default:
                if (_presentation.IsCalibrating && action.StartsWith("columns-", StringComparison.Ordinal)
                    && int.TryParse(action.AsSpan(8), out var columns))
                    _presentation.SetLayout(DraftCardLayout.Grid(columns));
                break;
        }
    }

    private void BeginCalibration()
    {
        _calibrationRevision++;
        var screen = Rail.Screens.ScreenFromWindow(Rail) ?? Rail.Screens.Primary;
        if (screen is null) { _presentation.SetDiagnostic("No display is available for calibration."); return; }
        var draft = Saved ?? new OverlayCalibration(ToScreen(screen), new(0.12, 0.18, 0.76, 0.62), DraftCardLayout.Grid(7));
        _calibration.Begin(draft);
    }
    private void CancelCalibration() => _calibration.Cancel();
    private Task SaveCalibrationAsync()
    {
        if (!_presentation.IsCalibrating) return Task.CompletedTask;
        var screen = _cards.Screens.ScreenFromWindow(_cards);
        if (screen is null)
        {
            _presentation.SetDiagnostic("No display is available to save the overlay position.");
            return Task.CompletedTask;
        }
        var display = ToScreen(screen);
        var geometry = new OverlayWindowGeometry(_cards.Position.X, _cards.Position.Y,
            _cards.ClientSize.Width, _cards.ClientSize.Height);
        // Screen coordinates and render pixels differ on macOS Retina. Use the screen's
        // coordinate scale, also used by Restore, not the backing-store RenderScaling.
        return SaveCalibrationCoreAsync(geometry.Capture(display, _presentation.Layout));
    }
    private async Task SaveCalibrationCoreAsync(OverlayCalibration calibration)
    {
        try { await _calibration.SaveAsync(calibration, _lifetime.Token); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
    }
    void ICalibrationSurface.HideCards() => _cards.Hide();
    void ICalibrationSurface.ApplyGeometry(OverlayCalibration calibration) => ApplyGeometry(calibration);
    void ICalibrationSurface.ClosePanel() => Rail.ClosePanel();
    void ICalibrationSurface.RefreshPassive() => RefreshVisibility();
    void ICalibrationSurface.ShowCalibration()
    {
        RefreshVisibility();
        if (_cards.IsVisible) _cards.Activate();
        Rail.OpenPanel(RailPanel.Calibration);
    }
    private void ApplyGeometry(OverlayCalibration calibration)
    {
        var geometry = OverlayWindowGeometry.Restore(calibration);
        _cards.Position = new(geometry.X, geometry.Y);
        _cards.Width = geometry.Width;
        _cards.Height = geometry.Height;
        _presentation.SetLayout(calibration.Layout);
        _presentation.SetViewport(_cards.Width, _cards.Height);
    }
    private void ScreensChanged(object? sender, EventArgs e)
    {
        if (_closing) return;
        if (_presentation.IsCalibrating || (Saved is not null && !CurrentScreens().Any(Saved.IsValidFor) && !_localization.CanFollowWindow))
        {
            _calibrationRevision++;
            _cards.Hide();
            _calibration.SetSaved(null);
            _calibration.Cancel();
            _presentation.SetDiagnostic("Display geometry changed. Set overlay position again.");
        }
        else if (_localization.CanFollowWindow) _localization.Retry();
        // Keep the interactive escape hatch reachable after a display is disconnected.
        if (!Rail.Screens.All.Any(screen => screen.WorkingArea.Contains(Rail.Position)) && Rail.Screens.Primary is { } primary)
            Rail.Position = new(primary.WorkingArea.X + 12, primary.WorkingArea.Y + 60);
    }
    private IReadOnlyList<OverlayScreen> CurrentScreens() => Rail.Screens.All.Select(ToScreen).ToArray();
    private static OverlayScreen ToScreen(Screen screen) => new(screen.Bounds.X, screen.Bounds.Y,
        screen.Bounds.Width, screen.Bounds.Height, screen.Scaling);

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        Rail.ClosePanel();
        _cards.Hide();
        await _lifetime.CancelAsync();
        try
        {
            try { await Task.WhenAll((Task?)_loadTask ?? Task.CompletedTask, _saveTask ?? Task.CompletedTask); }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
            await _localization.DisposeAsync();
            await _runtime.DisposeAsync();
        }
        finally
        {
            _presentation.PropertyChanged -= PresentationChanged;
            Rail.Screens.Changed -= ScreensChanged;
            _presentation.Dispose();
            _allowClose = true;
            _interaction.Dispose();
            _cards.Close();
            _lifetime.Dispose();
            Rail.Close();
        }
    }
}
