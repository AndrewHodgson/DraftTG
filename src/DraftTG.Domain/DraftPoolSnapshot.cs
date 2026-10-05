namespace DraftTG.Domain;

public enum DraftPoolCompleteness { Unknown, Partial, Complete }
public sealed record DraftPoolEntry(CardIdentifier CardIdentifier, int Count);

/// <summary>Owned card counts without pick chronology. Unresolved occurrences are explicitly accounted for.</summary>
public sealed class DraftPoolSnapshot : IEquatable<DraftPoolSnapshot>
{
    public DraftPoolSnapshot(DraftedCardPool inventory, DraftPoolCompleteness completeness,
        int unresolvedOccurrenceCount = 0)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        if (!Enum.IsDefined(completeness)) throw new ArgumentOutOfRangeException(nameof(completeness));
        if (unresolvedOccurrenceCount < 0) throw new ArgumentOutOfRangeException(nameof(unresolvedOccurrenceCount));
        if (completeness == DraftPoolCompleteness.Complete && unresolvedOccurrenceCount > 0)
            throw new ArgumentException("An inventory with unresolved identities cannot be complete.", nameof(completeness));
        Inventory = inventory;
        Completeness = completeness;
        UnresolvedOccurrenceCount = unresolvedOccurrenceCount;
        Entries = Array.AsReadOnly(inventory.CardIdentifiers.GroupBy(id => id)
            .Select(g => new DraftPoolEntry(g.Key, g.Count())).ToArray());
    }
    public DraftedCardPool Inventory { get; }
    public IReadOnlyList<DraftPoolEntry> Entries { get; }
    public DraftPoolCompleteness Completeness { get; }
    public int UnresolvedOccurrenceCount { get; }
    public int KnownCardCount => Inventory.Count;
    public int TotalCardCount => checked(KnownCardCount + UnresolvedOccurrenceCount);
    /// <summary>Unique resolved identities; unknown identities cannot contribute a reliable unique count.</summary>
    public int UniqueCardCount => Entries.Count;
    public bool Equals(DraftPoolSnapshot? other) => other is not null && Inventory.Equals(other.Inventory)
        && Completeness == other.Completeness && UnresolvedOccurrenceCount == other.UnresolvedOccurrenceCount;
    public override bool Equals(object? obj) => Equals(obj as DraftPoolSnapshot);
    public override int GetHashCode() => HashCode.Combine(Inventory, Completeness, UnresolvedOccurrenceCount);
}
