namespace DraftTG.ArenaIntegration.Memory;

/// <summary>Optional passive UI evidence. Unresolved native transforms never imply visibility or screen position.</summary>
public sealed record ArenaPackLayoutMemory(int Columns, int Rows, float Scale, float OffsetX, float OffsetY,
    string SourcePath);
public sealed record ArenaDeckEntryMemory(int Index, uint GrpId, int Quantity, string Area, bool? Visible,
    string SourcePath)
{ public ArenaDeckRowGeometryMemory? Geometry { get; init; } }
public sealed record ArenaDeckViewMemory(IReadOnlyList<ArenaDeckEntryMemory> Entries, float? MainScroll,
    float? SideScroll, bool? SideboardExpanded, string? UnsupportedReason)
{ public ArenaDeckHolderGeometryMemory? MainGeometry { get; init; } public ArenaDeckHolderGeometryMemory? SideGeometry { get; init; } }
public readonly record struct ArenaUiRect(double X, double Y, double Width, double Height);
public sealed record ArenaDeckRowGeometryMemory(ArenaUiRect WorldRect, int VisualIndex, bool Active, string Source);
public sealed record ArenaDeckHolderGeometryMemory(ArenaUiRect ViewportWorld, ArenaUiRect ContentWorld,
    ArenaUiRect CanvasWorld, int CanvasMode, double CanvasScale, bool Active, bool Scrolling, string Source);
public sealed record ArenaUiMemorySnapshot(ArenaPackLayoutMemory? PackLayout, ArenaDeckViewMemory? DeckView,
    string? UnsupportedReason, double ReadMilliseconds)
{ public double DeckReadMilliseconds { get; init; } public double DeckIdentityMilliseconds { get; init; } public double DeckGeometryMilliseconds { get; init; } }
