namespace DraftTG.App.Platform;

internal sealed record DesktopOutput(int Adapter, int Index, string Name, WindowCaptureBounds Bounds, bool Attached, int Rotation);

internal static class DesktopDuplicationGeometry
{
    // Both values are physical screen pixels. Negative secondary-monitor origins are valid.
    public static DesktopOutput Select(ArenaWindowGeometry client, IReadOnlyList<DesktopOutput> outputs)
    {
        var containing = outputs.Where(o => o.Attached && client.X >= o.Bounds.X && client.Y >= o.Bounds.Y
            && client.X + (long)client.Width <= o.Bounds.X + (long)o.Bounds.Width
            && client.Y + (long)client.Height <= o.Bounds.Y + (long)o.Bounds.Height).ToArray();
        if (containing.Length != 1)
            throw new InvalidDataException("Arena client must be fully contained in exactly one attached DXGI output; spanning/ambiguous/offscreen windows are not captured.");
        if (containing[0].Rotation != 1) throw new NotSupportedException("Rotated DXGI outputs are not supported by this benchmark; no guessed coordinate transform.");
        return containing[0];
    }
    public static ArenaDraftCrop Map(DesktopOutput output, ArenaWindowGeometry client, ArenaDraftCrop crop) => output.Bounds.MapCrop(client, crop);
    public static bool IsPixelUpdate(long presentQpc, uint accumulatedFrames) => presentQpc > 0 && accumulatedFrames > 0;
}

internal sealed record DesktopDuplicationMetrics(long CaptureGeneration, long FramesAcquired, long PointerOnlySkipped,
    long PriorPresentationSkipped, DateTimeOffset? FirstUsableImageAt, string? Output, double InitializationMs,
    double AcquisitionMs, double CropMs, string Stage, string? Failure = null);
