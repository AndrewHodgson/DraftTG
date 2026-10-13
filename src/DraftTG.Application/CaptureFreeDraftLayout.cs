namespace DraftTG.Application;

public sealed record CaptureFreeDraftLayoutResult(string? Profile, IReadOnlyList<NormalizedDraftRegion> Rectangles, string? Decline)
{
    public bool IsAvailable => Profile is not null && Decline is null;
}

/// <summary>Memory index is the slot index. Reuses independent 9E geometry without comparator or visual verification.</summary>
public static class CaptureFreeDraftLayout
{
    public static CaptureFreeDraftLayoutResult Map(int width, int height, int count, string? mode, bool windowed)
    {
        if (mode == "column") return new(null, [], "column-mode layout not validated");
        if (mode != "list") return new(null, [], "unknown draft view mode");
        var geometry = ArenaDraftSlotGeometry.TryCreateLayout(width, height, count,
            new(ArenaDraftView.DraftPackGrid, windowed ? ArenaWindowMode.Windowed : ArenaWindowMode.Unknown));
        if (geometry.Layout is not { } layout) return new(null, [], geometry.Reason);
        return new("drafttg-measured-list-13-14-v1", Array.AsReadOnly(layout.Slots.Select(s =>
            new NormalizedDraftRegion(s.X / width, s.Y / height, s.Width / width, s.Height / height)).ToArray()), null);
    }
}
