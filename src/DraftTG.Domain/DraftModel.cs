using System.Collections.ObjectModel;

namespace DraftTG.Domain;

public enum DraftFormat
{
    BestOfOne,
    BestOfThree
}

public sealed record DraftPosition(PackNumber Pack, PickNumber Pick);

public sealed record DraftPick(DraftPosition Position, CardIdentifier SelectedCardIdentifier);

public sealed class DraftPack : IEquatable<DraftPack>
{
    private readonly ReadOnlyCollection<CardIdentifier> _availableCardIdentifiers;

    public DraftPack(DraftPosition position, IEnumerable<CardIdentifier> availableCardIdentifiers)
    {
        Position = position;
        _availableCardIdentifiers = Array.AsReadOnly(availableCardIdentifiers.ToArray());
    }

    public DraftPosition Position { get; }
    public IReadOnlyList<CardIdentifier> AvailableCardIdentifiers => _availableCardIdentifiers;

    public bool Equals(DraftPack? other) =>
        other is not null && Position == other.Position
            && _availableCardIdentifiers.SequenceEqual(other._availableCardIdentifiers);

    public override bool Equals(object? obj) => Equals(obj as DraftPack);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Position);
        foreach (var identifier in _availableCardIdentifiers) hash.Add(identifier);
        return hash.ToHashCode();
    }
}

public sealed class DraftHistory : IEquatable<DraftHistory>
{
    private readonly ReadOnlyCollection<DraftPick> _picks;

    public DraftHistory(IEnumerable<DraftPick>? picks = null) =>
        _picks = Array.AsReadOnly((picks ?? []).ToArray());

    public IReadOnlyList<DraftPick> Picks => _picks;
    public int Count => _picks.Count;
    public IReadOnlyList<CardIdentifier> SelectedCardIdentifiers =>
        _picks.Select(pick => pick.SelectedCardIdentifier).ToArray();

    public bool Contains(CardIdentifier identifier) =>
        _picks.Any(pick => pick.SelectedCardIdentifier == identifier);

    public bool Equals(DraftHistory? other) =>
        other is not null && _picks.SequenceEqual(other._picks);

    public override bool Equals(object? obj) => Equals(obj as DraftHistory);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var pick in _picks) hash.Add(pick);
        return hash.ToHashCode();
    }
}

public sealed record DraftSnapshot(
    DraftPack CurrentPack,
    DraftHistory History,
    DraftFormat Format);
