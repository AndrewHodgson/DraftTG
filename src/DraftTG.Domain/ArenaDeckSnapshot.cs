namespace DraftTG.Domain;

public enum ArenaDeckCompleteness { Unknown, Partial, Complete }
public enum ArenaDeckSnapshotScope { SavedDeck, CurrentEditor }
public enum ArenaDeckSnapshotSource { StructuredLog, ConfirmedVisualSnapshot }

/// <summary>Session-local revision, independent of a draft pick position or a deck proposal's rank.</summary>
public sealed record ArenaDeckRevision
{
    public ArenaDeckRevision(string sessionIdentity, long number)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionIdentity);
        if (number < 1) throw new ArgumentOutOfRangeException(nameof(number));
        SessionIdentity = sessionIdentity; Number = number;
    }
    public string SessionIdentity { get; }
    public long Number { get; }
}

public sealed record ArenaDeckEntry(CardIdentifier CardIdentifier, int Count);
public sealed record ArenaBasicLandEntry(BasicLandType Type, int Count);

/// <summary>Observed deck contents, never a drafted inventory or a suggested build. Saved snapshots cannot certify unsaved edits.</summary>
public sealed class ArenaDeckSnapshot
{
    public ArenaDeckSnapshot(ArenaDeckRevision revision, IEnumerable<ArenaDeckEntry> mainDeck,
        IEnumerable<ArenaBasicLandEntry> basics, ArenaDeckCompleteness completeness,
        ArenaDeckSnapshotScope scope, ArenaDeckSnapshotSource source, bool basicCountsKnown,
        int unresolvedMainDeckCount = 0, IEnumerable<ArenaDeckEntry>? sideboard = null)
    {
        ArgumentNullException.ThrowIfNull(revision); ArgumentNullException.ThrowIfNull(mainDeck); ArgumentNullException.ThrowIfNull(basics);
        if (!Enum.IsDefined(completeness) || !Enum.IsDefined(scope) || !Enum.IsDefined(source)) throw new ArgumentOutOfRangeException(nameof(completeness));
        if (unresolvedMainDeckCount < 0) throw new ArgumentOutOfRangeException(nameof(unresolvedMainDeckCount));
        if (completeness == ArenaDeckCompleteness.Complete && (unresolvedMainDeckCount > 0 || !basicCountsKnown))
            throw new ArgumentException("A complete deck must resolve every main-deck copy and basic-land count.");
        Revision = revision; MainDeck = Entries(mainDeck); Sideboard = Entries(sideboard ?? []);
        var lands = basics.ToArray();
        if (lands.Any(e => !Enum.IsDefined(e.Type) || e.Count < 0 || e.Count > 1000)) throw new ArgumentOutOfRangeException(nameof(basics));
        Basics = Array.AsReadOnly(lands.GroupBy(e => e.Type).OrderBy(g => g.Key)
            .Select(g => new ArenaBasicLandEntry(g.Key, checked(g.Sum(e => e.Count)))).Where(e => e.Count > 0).ToArray());
        Completeness = completeness; Scope = scope; Source = source; BasicCountsKnown = basicCountsKnown;
        UnresolvedMainDeckCount = unresolvedMainDeckCount;
    }
    public ArenaDeckRevision Revision { get; }
    public IReadOnlyList<ArenaDeckEntry> MainDeck { get; }
    public IReadOnlyList<ArenaBasicLandEntry> Basics { get; }
    public IReadOnlyList<ArenaDeckEntry> Sideboard { get; }
    public ArenaDeckCompleteness Completeness { get; }
    public ArenaDeckSnapshotScope Scope { get; }
    public ArenaDeckSnapshotSource Source { get; }
    public bool BasicCountsKnown { get; }
    public int UnresolvedMainDeckCount { get; }
    public int MainDeckCount => MainDeck.Sum(e => e.Count) + Basics.Sum(e => e.Count) + UnresolvedMainDeckCount;
    public bool HasTrustworthyCurrentCounts => Scope == ArenaDeckSnapshotScope.CurrentEditor
        && Completeness == ArenaDeckCompleteness.Complete && BasicCountsKnown && UnresolvedMainDeckCount == 0;

    private static IReadOnlyList<ArenaDeckEntry> Entries(IEnumerable<ArenaDeckEntry> entries)
    {
        var rows = entries.ToArray();
        if (rows.Any(e => e.CardIdentifier is null || e.Count is < 1 or > 1000)) throw new ArgumentOutOfRangeException(nameof(entries));
        return Array.AsReadOnly(rows.GroupBy(e => e.CardIdentifier).OrderBy(g => g.Key.Value, StringComparer.Ordinal)
            .Select(g => new ArenaDeckEntry(g.Key, checked(g.Sum(e => e.Count)))).ToArray());
    }
}
