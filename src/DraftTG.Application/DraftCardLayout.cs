namespace DraftTG.Application;

/// <summary>All coordinates are fractions of the manually calibrated draft region.</summary>
public sealed record CardSlot(int Index, double X, double Y, double Width, double Height)
{
    public bool IsValid => Index >= 0 && new NormalizedDraftRegion(X, Y, Width, Height).IsValid;
    public SlotBounds Scale(double width, double height) => new(X * width, Y * height, Width * width, Height * height);
}

public sealed record SlotBounds(double X, double Y, double Width, double Height);

public sealed class DraftCardLayout
{
    public const int MaximumCards = 14;
    public DraftCardLayout(IEnumerable<CardSlot> slots)
    {
        var copy = slots.ToArray();
        if (copy.Length != MaximumCards || copy.Where((slot, index) => slot is null || !slot.IsValid || slot.Index != index).Any())
            throw new ArgumentException("A layout must contain 14 valid, ordered normalized slots.", nameof(slots));
        Slots = Array.AsReadOnly(copy);
    }
    public IReadOnlyList<CardSlot> Slots { get; }

    // Explicit profiles, not an assumption that Arena always displays seven columns.
    public static DraftCardLayout Grid(int columns)
    {
        if (columns is < 4 or > 7) throw new ArgumentOutOfRangeException(nameof(columns));
        var rows = (int)Math.Ceiling(MaximumCards / (double)columns);
        return new(Enumerable.Range(0, MaximumCards).Select(index =>
            new CardSlot(index, (index % columns + 0.025) / columns, (index / columns + 0.025) / rows,
                0.95 / columns, 0.95 / rows)));
    }
}

public sealed record NormalizedDraftRegion(double X, double Y, double Width, double Height)
{
    public bool IsValid => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Width) && double.IsFinite(Height)
        && X >= 0 && Y >= 0 && Width > 0 && Height > 0 && X + Width <= 1.000001 && Y + Height <= 1.000001;
}

/// <summary>Desktop screen coordinates and coordinate scale; never Arena window geometry.
/// macOS uses points here (scale 1), independently of Retina render scaling.</summary>
public sealed record OverlayScreen(double X, double Y, double Width, double Height, double Scaling)
{
    public bool IsValid => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Width)
        && double.IsFinite(Height) && double.IsFinite(Scaling) && Width > 0 && Height > 0 && Scaling > 0;
}

public sealed record OverlayCalibration(OverlayScreen Screen, NormalizedDraftRegion Region, DraftCardLayout Layout)
{
    public bool IsValidFor(OverlayScreen current) => Screen.IsValid && current.IsValid && Screen == current
        && Region.IsValid && Region.Width * current.Width / current.Scaling >= 400
        && Region.Height * current.Height / current.Scaling >= 220;
}

/// <summary>Live desktop window position and client extent in logical pixels.</summary>
public sealed record OverlayWindowGeometry(int X, int Y, double Width, double Height)
{
    public OverlayCalibration Capture(OverlayScreen screen, DraftCardLayout layout) =>
        Capture(screen, screen.Scaling, layout);

    // Retained for callers already supplying an explicit desktop coordinate scale.
    public OverlayCalibration Capture(OverlayScreen screen, double coordinateScaling, DraftCardLayout layout) =>
        new(screen, new((X - screen.X) / screen.Width, (Y - screen.Y) / screen.Height,
            Width * coordinateScaling / screen.Width, Height * coordinateScaling / screen.Height), layout);

    public static OverlayWindowGeometry Restore(OverlayCalibration calibration)
    {
        var screen = calibration.Screen;
        var region = calibration.Region;
        return new((int)Math.Round(screen.X + region.X * screen.Width),
            (int)Math.Round(screen.Y + region.Y * screen.Height),
            // Remove normalization noise before Avalonia rounds layout extents upward to pixels.
            Math.Round(region.Width * screen.Width / screen.Scaling, 8),
            Math.Round(region.Height * screen.Height / screen.Scaling, 8));
    }
}
