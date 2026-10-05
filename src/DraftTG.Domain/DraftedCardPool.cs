using System.Collections.ObjectModel;

namespace DraftTG.Domain;

/// <summary>Provider-neutral drafted occurrences, with no claim about historical coordinates.</summary>
public sealed class DraftedCardPool : IEquatable<DraftedCardPool>
{
    private readonly ReadOnlyCollection<CardIdentifier> _cards;
    public DraftedCardPool(IEnumerable<CardIdentifier> cards) =>
        _cards = Array.AsReadOnly(cards.OrderBy(c => c.Value, StringComparer.Ordinal).ToArray());
    public IReadOnlyList<CardIdentifier> CardIdentifiers => _cards;
    public int Count => _cards.Count;
    public bool Equals(DraftedCardPool? other) => other is not null && _cards.SequenceEqual(other._cards);
    public override bool Equals(object? obj) => Equals(obj as DraftedCardPool);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var card in _cards) hash.Add(card);
        return hash.ToHashCode();
    }
}
