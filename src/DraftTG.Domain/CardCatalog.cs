using System.Collections.ObjectModel;

namespace DraftTG.Domain;

public sealed class DuplicateCardIdentifierException(CardIdentifier identifier)
    : Exception($"The catalog contains duplicate card identifier '{identifier.Value}'.")
{
    public CardIdentifier Identifier { get; } = identifier;
}

public sealed class CardCatalog : IEquatable<CardCatalog>
{
    private readonly ReadOnlyCollection<Card> _cards;
    private readonly Dictionary<CardIdentifier, Card> _byIdentifier;
    private readonly Dictionary<string, ReadOnlyCollection<Card>> _byName;

    public CardCatalog(IEnumerable<Card>? cards = null)
    {
        var copy = (cards ?? []).ToArray();
        _byIdentifier = new Dictionary<CardIdentifier, Card>();
        var byName = new Dictionary<string, List<Card>>(StringComparer.Ordinal);

        foreach (var card in copy)
        {
            if (!_byIdentifier.TryAdd(card.Identifier, card))
            {
                throw new DuplicateCardIdentifierException(card.Identifier);
            }

            if (!byName.TryGetValue(card.Name, out var matches))
            {
                matches = [];
                byName.Add(card.Name, matches);
            }
            matches.Add(card);
        }

        _cards = Array.AsReadOnly(copy);
        _byName = byName.ToDictionary(
            pair => pair.Key,
            pair => Array.AsReadOnly(pair.Value.ToArray()),
            StringComparer.Ordinal);
    }

    public IReadOnlyList<Card> Cards => _cards;
    public int Count => _cards.Count;

    public Card? Find(CardIdentifier identifier) =>
        _byIdentifier.GetValueOrDefault(identifier);

    public IReadOnlyList<Card> FindByExactName(string name) =>
        _byName.TryGetValue(name, out var matches) ? matches : Array.Empty<Card>();

    public bool Equals(CardCatalog? other) =>
        other is not null && _cards.SequenceEqual(other._cards);

    public override bool Equals(object? obj) => Equals(obj as CardCatalog);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var card in _cards) hash.Add(card);
        return hash.ToHashCode();
    }
}
