using System.Text.Json;
using DraftTG.Data;

namespace DraftTG.Application;

public sealed record OverlayCalibrationLoadResult(OverlayCalibration? Calibration, string? Diagnostic);

public sealed class OverlayCalibrationService(IOverlaySettingsFile file)
{
    public static OverlayCalibrationService CreateDefault() =>
        new(new OverlaySettingsFile(ApplicationDataPathProviderFactory.CreateDefault()));

    public async Task<OverlayCalibrationLoadResult> LoadAsync(IReadOnlyList<OverlayScreen> screens,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await file.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (json is null) return new(null, null);
            var document = JsonSerializer.Deserialize<CalibrationDocument>(json);
            if (document is null || document.Version != 1 || document.Screen is null
                || document.Region is null || document.Slots is null) return Invalid();
            var calibration = new OverlayCalibration(document.Screen, document.Region, new(document.Slots));
            return screens.Any(calibration.IsValidFor) ? new(calibration, null) : Invalid();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { return Invalid(); }
    }

    public async Task<string?> SaveAsync(OverlayCalibration calibration, CancellationToken cancellationToken = default)
    {
        if (!calibration.IsValidFor(calibration.Screen)) return "Overlay region is outside the screen or too small. Recalibrate.";
        try
        {
            var json = JsonSerializer.Serialize(new CalibrationDocument(1, calibration.Screen, calibration.Region,
                calibration.Layout.Slots.ToArray()), new JsonSerializerOptions { WriteIndented = true });
            await file.WriteAsync(json, cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { return "Overlay position could not be saved. It will need calibration after restart."; }
    }

    private static OverlayCalibrationLoadResult Invalid() => new(null, "Saved overlay position is invalid for this display. Set overlay position again.");
    private sealed record CalibrationDocument(int Version, OverlayScreen Screen, NormalizedDraftRegion Region, CardSlot[] Slots);
}
