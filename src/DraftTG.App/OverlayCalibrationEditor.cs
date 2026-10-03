using DraftTG.Application;
using DraftTG.App.Platform;

namespace DraftTG.App;

internal interface ICalibrationSurface
{
    void HideCards();
    void ApplyGeometry(OverlayCalibration calibration);
    void ClosePanel();
    void ShowCalibration();
    void RefreshPassive();
}

/// <summary>The existing calibration UI flow, separated from native windows for transition tests.</summary>
internal sealed class OverlayCalibrationEditor(OverlayViewModel presentation, OverlayCalibrationService settings,
    OverlayInteractionController interaction, ICalibrationSurface surface)
{
    public OverlayCalibration? Saved { get; private set; }
    public bool IsSaving { get; private set; }
    // Prevent property notifications from showing the window halfway through a mode change.
    public bool IsTransitioning { get; private set; }
    private int _revision;

    public void SetSaved(OverlayCalibration? calibration) { Saved = calibration; _revision++; }

    public void Begin(OverlayCalibration initial)
    {
        if (IsSaving) return;
        if (presentation.IsCalibrating) { surface.ShowCalibration(); return; }
        IsTransitioning = true;
        try
        {
            if (!interaction.TryApply(true, out var diagnostic))
            { surface.HideCards(); presentation.SetDiagnostic(diagnostic); return; }
            _revision++;
            surface.ApplyGeometry(Saved ?? initial);
            presentation.SetDiagnostic(null);
            presentation.SetCalibrationState(true, Saved is not null);
            presentation.SetPanel(RailPanel.Calibration);
        }
        finally { IsTransitioning = false; }
        surface.ShowCalibration();
    }

    public async Task SaveAsync(OverlayCalibration current, CancellationToken cancellationToken = default)
    {
        if (!presentation.IsCalibrating || IsSaving) return;
        if (!current.IsValidFor(current.Screen))
        {
            presentation.SetDiagnostic("Keep the draft region fully on one screen, at least 400 × 220 logical pixels in size.");
            return;
        }
        IsSaving = true;
        var revision = _revision;
        try
        {
            var diagnostic = await settings.SaveAsync(current, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (revision != _revision) return; // Display invalidation supersedes an in-flight save.
            presentation.SetDiagnostic(diagnostic);
            if (diagnostic is not null) return; // Do not claim a failed write was saved.
            Saved = current;
            Exit();
        }
        finally { IsSaving = false; }
    }

    public void Cancel() { _revision++; Exit(); }

    private void Exit()
    {
        IsTransitioning = true;
        try
        {
            surface.HideCards();
            // One authoritative state hides every calibration visual before any native work.
            presentation.SetCalibrationState(false, Saved is not null);
            surface.ClosePanel();
            if (!interaction.TryApply(false, out var diagnostic))
            { presentation.SetDiagnostic(diagnostic); return; }
            // Removing native calibration chrome can affect geometry; restore it afterward.
            if (Saved is not null) surface.ApplyGeometry(Saved);
        }
        finally { IsTransitioning = false; }
        surface.RefreshPassive();
    }
}
