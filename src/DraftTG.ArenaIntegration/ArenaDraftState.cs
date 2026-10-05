using System.Collections;

namespace DraftTG.ArenaIntegration;

public enum ArenaDraftSessionStatus
{
    Idle,
    Active,
    Completed
}

public sealed record ArenaDraftPackState(
    ArenaDraftIdentifier? DraftIdentifier,
    ArenaDraftCoordinate Coordinate,
    ArenaCardIdentifierList CardIdentifiers);

public sealed record ArenaDraftPickRecord(
    ArenaDraftIdentifier? DraftIdentifier,
    ArenaDraftCoordinate Coordinate,
    ArenaCardIdentifierList CardIdentifiers);

public sealed class ArenaDraftPickRecordList
    : IReadOnlyList<ArenaDraftPickRecord>, IEquatable<ArenaDraftPickRecordList>
{
    private readonly ArenaDraftPickRecord[] _items;

    public ArenaDraftPickRecordList(IEnumerable<ArenaDraftPickRecord> items) =>
        _items = items.ToArray();

    public ArenaDraftPickRecord this[int index] => _items[index];
    public int Count => _items.Length;

    public IEnumerator<ArenaDraftPickRecord> GetEnumerator() =>
        ((IEnumerable<ArenaDraftPickRecord>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool Equals(ArenaDraftPickRecordList? other) =>
        other is not null && _items.SequenceEqual(other._items);

    public override bool Equals(object? obj) => Equals(obj as ArenaDraftPickRecordList);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in _items) hash.Add(item);
        return hash.ToHashCode();
    }
}

public sealed record ArenaDraftStateSnapshot(
    ArenaDraftSessionStatus Status,
    ArenaDraftIdentifier? DraftIdentifier,
    ArenaDraftMode? Mode,
    string? EventName,
    ArenaDraftPackState? CurrentPack,
    ArenaDraftPickRecordList CompletedPicks)
{
    public bool IsCompleted => Status == ArenaDraftSessionStatus.Completed;
    public ArenaDraftPoolState? RecoveredPool { get; init; }
    public ArenaPickedCardsDiagnostic? PickedCardsDiagnostic { get; init; }

    /// <summary>Snapshot pool plus exact selections not yet covered by that snapshot.</summary>
    public ArenaCardMultiset DraftedPool
    {
        get
        {
            if (RecoveredPool is null) return new(CompletedPicks.SelectMany(p => p.CardIdentifiers));
            var covered = new ArenaCardMultiset(CompletedPicks.Where(p => IsCoveredByPool(p.Coordinate)).SelectMany(p => p.CardIdentifiers));
            // Incoherent late exact facts are diagnosed, but never disappear from the known pool.
            var missingKnown = covered.Counts.SelectMany(p => Enumerable.Repeat(p.Key,
                Math.Max(0, p.Value - RecoveredPool.Cards.CountOf(p.Key))));
            return new(RecoveredPool.Cards.Occurrences.Concat(missingKnown)
                .Concat(CompletedPicks.Where(p => !IsCoveredByPool(p.Coordinate)).SelectMany(p => p.CardIdentifiers)));
        }
    }
    private bool IsCoveredByPool(ArenaDraftCoordinate coordinate) => RecoveredPool is not null &&
        (RecoveredPool.CurrentCoordinate is not { } current || coordinate.Pack < current.Pack
            || (coordinate.Pack == current.Pack && coordinate.Pick < current.Pick));
    public int ExactHistoryCardCount => CompletedPicks.Sum(p => p.CardIdentifiers.Count);
    public int UnqualifiedHistoryCardCount => Math.Max(0, DraftedPool.Count - ExactHistoryCardCount);
}

public sealed record ArenaDraftPoolState(ArenaDraftCoordinate? CurrentCoordinate, ArenaCardMultiset Cards);
public enum ArenaPickedCardsDiagnosticKind { UnexpectedCardCount, RemovedCards, BackwardCoordinate, ExactHistoryMismatch, FinalPoolMismatch }
public sealed record ArenaPickedCardsDiagnostic(ArenaPickedCardsDiagnosticKind Kind, string Message);

public sealed record ArenaDraftStateUpdate(
    ArenaDraftStateSnapshot Snapshot,
    bool Changed);

public enum ArenaDraftStateConflictKind
{
    PackPresentation,
    PickSubmission
}

public sealed class ArenaDraftStateConflictException : Exception
{
    public ArenaDraftStateConflictException(
        ArenaDraftStateConflictKind kind,
        ArenaDraftCoordinate coordinate,
        ArenaCardIdentifierList existingCardIdentifiers,
        ArenaCardIdentifierList incomingCardIdentifiers)
        : base($"Conflicting {kind} facts at pack {coordinate.Pack}, pick {coordinate.Pick}.")
    {
        Kind = kind;
        Coordinate = coordinate;
        ExistingCardIdentifiers = existingCardIdentifiers;
        IncomingCardIdentifiers = incomingCardIdentifiers;
    }

    public ArenaDraftStateConflictKind Kind { get; }
    public ArenaDraftCoordinate Coordinate { get; }
    public ArenaCardIdentifierList ExistingCardIdentifiers { get; }
    public ArenaCardIdentifierList IncomingCardIdentifiers { get; }
}
