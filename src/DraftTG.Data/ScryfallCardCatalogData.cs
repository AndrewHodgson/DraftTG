using System.Collections.Frozen;
using System.Collections.ObjectModel;
using DraftTG.Domain;

namespace DraftTG.Data;

/// <summary>
/// One coherent Scryfall import: provider-neutral cards plus auxiliary raw
/// Scryfall arena_id mappings. Arena-specific types remain outside Data.
/// </summary>
public sealed class ScryfallCardCatalogData
{
    internal ScryfallCardCatalogData(
        CardCatalog catalog,
        IEnumerable<KeyValuePair<int, CardIdentifier>> arenaIdMappings,
        IEnumerable<KeyValuePair<int, IReadOnlyList<CardIdentifier>>> ambiguousArenaIds)
    {
        Catalog = catalog;
        ArenaIdMappings = arenaIdMappings.ToFrozenDictionary();
        AmbiguousArenaIds = ambiguousArenaIds.ToFrozenDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<CardIdentifier>)new ReadOnlyCollection<CardIdentifier>(
                pair.Value.ToArray()));
    }

    public CardCatalog Catalog { get; }
    public IReadOnlyDictionary<int, CardIdentifier> ArenaIdMappings { get; }
    public IReadOnlyDictionary<int, IReadOnlyList<CardIdentifier>> AmbiguousArenaIds { get; }
}
