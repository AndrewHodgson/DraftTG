using DraftTG.ArenaIntegration;
using DraftTG.Domain;

namespace DraftTG.Application;

public enum DraftSnapshotAvailability
{
    Ready,
    Idle,
    Completed,
    NoCurrentPack,
    UnsupportedDraftMode,
    UnsupportedCoordinate,
    UnresolvedArenaCard,
    AmbiguousArenaCard,
    UnsupportedMultiCardPick
}

public sealed record ArenaDraftSnapshotResult
{
    private ArenaDraftSnapshotResult(
        DraftSnapshotAvailability availability,
        DraftSnapshot? snapshot,
        ArenaCardIdentifierList unresolvedArenaCards,
        ArenaCardIdentifierList ambiguousArenaCards)
    {
        Availability = availability;
        Snapshot = snapshot;
        UnresolvedArenaCards = unresolvedArenaCards;
        AmbiguousArenaCards = ambiguousArenaCards;
    }

    public DraftSnapshotAvailability Availability { get; }
    public DraftSnapshot? Snapshot { get; }
    /// <summary>Resolved existing Arena history, including the gap after a pick and draft completion.</summary>
    public DraftHistory? ResolvedHistory { get; init; }
    public DraftedCardPool? ResolvedDraftedPool { get; init; }
    public DraftPoolSnapshot? DraftPool { get; init; }
    /// <summary>How the drafted pool's Arena IDs were resolved (Scryfall arena_id, Arena DB fallback, missing, ambiguous).</summary>
    public ArenaCardIdentitySummary? PoolIdentity { get; init; }
    public ArenaCardIdentifierList UnresolvedArenaCards { get; }
    public ArenaCardIdentifierList AmbiguousArenaCards { get; }

    internal static ArenaDraftSnapshotResult Ready(DraftSnapshot snapshot) =>
        new(
            DraftSnapshotAvailability.Ready,
            snapshot,
            new ArenaCardIdentifierList([]),
            new ArenaCardIdentifierList([]));

    internal static ArenaDraftSnapshotResult Unavailable(
        DraftSnapshotAvailability availability,
        IEnumerable<ArenaCardIdentifier>? unresolvedArenaCards = null,
        IEnumerable<ArenaCardIdentifier>? ambiguousArenaCards = null)
    {
        if (availability == DraftSnapshotAvailability.Ready)
        {
            throw new ArgumentException("An unavailable result cannot use Ready availability.", nameof(availability));
        }

        return new ArenaDraftSnapshotResult(
            availability,
            null,
            new ArenaCardIdentifierList(unresolvedArenaCards ?? []),
            new ArenaCardIdentifierList(ambiguousArenaCards ?? []));
    }
}
