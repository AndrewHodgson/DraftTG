using System.Collections.ObjectModel;

namespace DraftTG.ArenaIntegration;

/// <summary>Immutable card occurrences; equality ignores ordering, never multiplicity.</summary>
public sealed class ArenaCardMultiset : IEquatable<ArenaCardMultiset>
{
    public ArenaCardMultiset(IEnumerable<ArenaCardIdentifier> cards)
    {
        Counts = new ReadOnlyDictionary<ArenaCardIdentifier, int>(cards.GroupBy(c => c)
            .ToDictionary(g => g.Key, g => g.Count()));
        Occurrences = new(Counts.OrderBy(p => p.Key.Value).SelectMany(p => Enumerable.Repeat(p.Key, p.Value)));
    }

    public IReadOnlyDictionary<ArenaCardIdentifier, int> Counts { get; }
    public ArenaCardIdentifierList Occurrences { get; }
    public int Count => Occurrences.Count;
    public int CountOf(ArenaCardIdentifier card) => Counts.GetValueOrDefault(card);
    public bool Contains(ArenaCardMultiset other) => other.Counts.All(p => CountOf(p.Key) >= p.Value);

    public bool TryGetSingleAddition(ArenaCardMultiset previous, out ArenaCardIdentifier? added)
    {
        added = null;
        if (Count != previous.Count + 1 || !Contains(previous)) return false;
        added = Counts.Single(p => p.Value > previous.CountOf(p.Key)).Key;
        return true;
    }

    public bool Equals(ArenaCardMultiset? other) => other is not null && Occurrences.Equals(other.Occurrences);
    public override bool Equals(object? obj) => Equals(obj as ArenaCardMultiset);
    public override int GetHashCode() => Occurrences.GetHashCode();
}

/// <summary>The supported normal Quick Draft progression, in normalized Arena coordinates.</summary>
public static class ArenaQuickDraftCoordinates
{
    public const int PackCount = 3;
    public const int PicksPerPack = 14;
    public static bool IsSupported(ArenaDraftCoordinate coordinate) =>
        coordinate.Pack is >= 1 and <= PackCount && coordinate.Pick is >= 1 and <= PicksPerPack;
    public static int CompletedPicksBefore(ArenaDraftCoordinate coordinate) =>
        IsSupported(coordinate) ? (coordinate.Pack - 1) * PicksPerPack + coordinate.Pick - 1
            : throw new ArgumentOutOfRangeException(nameof(coordinate));

    public static bool IsNext(ArenaDraftCoordinate previous, ArenaDraftCoordinate current)
    {
        if (!IsSupported(previous) || !IsSupported(current)) return false;
        var next = previous.Pick < PicksPerPack ? ArenaDraftCoordinate.Create(previous.Pack, previous.Pick + 1)
            : previous.Pack < PackCount ? ArenaDraftCoordinate.Create(previous.Pack + 1, 1) : null;
        return next == current;
    }
}
