using DraftTG.ArenaIntegration.Memory;
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
    /// <summary>Additional counts require live list layout evidence, never count-based mode guessing.</summary>
    public static CaptureFreeDraftLayoutResult Map(int width, int height, int count, string? mode, bool windowed,
        ArenaPackLayoutMemory? evidence)
    {
        if (evidence is not null && evidence is not { Columns: 5, Rows: 0, Scale: 43, OffsetX: 196, OffsetY: -268 })
            return new(null, [], "live layout differs from fixed five-column list; geometry withheld");
        if (count is >= 13 and <= 14) return Map(width, height, count, mode, windowed);
        if (count is < 1 or > 14) return new(null, [], "offered count outside 1–14 implementation");
        if (evidence is not { Columns: 5, Rows: 0, Scale: 43, OffsetX: 196, OffsetY: -268 } || evidence.SourcePath != "DraftPackHolder._currentPackLayoutData")
            return new(null, [], "shrinking pack requires stable live five-column fixed list layout (RowCount=0)");
        var envelope = Map(width, height, 14, mode, windowed);
        return envelope.IsAvailable ? new("arena-list-compact-index-1-14-v1", envelope.Rectangles.Take(count).ToArray(), null) : envelope;
    }
}
