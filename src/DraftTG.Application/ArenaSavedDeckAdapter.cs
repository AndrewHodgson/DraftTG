using DraftTG.ArenaIntegration;
using DraftTG.Domain;

namespace DraftTG.Application;

/// <summary>Resolves an audited saved snapshot without promoting it to current editor state.</summary>
public sealed class ArenaSavedDeckAdapter(ArenaCardResolver resolver, CardCatalog catalog)
{
    public ArenaDeckSnapshot Convert(ArenaDeckLogEvent.SavedDeckObserved observed, ArenaDeckRevision revision, DraftPoolSnapshot pool)
    {
        var unresolved = 0; var unknownSideboard = false;
        var main = new List<ArenaDeckEntry>(); var sideboard = new List<ArenaDeckEntry>(); var basics = new List<ArenaBasicLandEntry>();
        foreach (var (cards, isMain) in new[] { (observed.MainDeck, true), (observed.Sideboard, false) })
        foreach (var row in cards)
        {
            if (!resolver.TryResolve(row.CardIdentifier, out var id) || catalog.Find(id) is not { } card
                || !card.GameplayMetadata.CardTypes.IsKnown)
            {
                if (isMain) unresolved += row.Count; else unknownSideboard = true;
                continue;
            }
            if (card.GameplayMetadata.IsBasicLand)
            {
                if (isMain && card.GameplayMetadata.CardTypes.BasicLandType is { } type) basics.Add(new(type, row.Count));
                else if (isMain) unresolved += row.Count;
            }
            else (isMain ? main : sideboard).Add(new(id, row.Count));
        }
        var actualOwned = main.Concat(sideboard).GroupBy(e => e.CardIdentifier).ToDictionary(g => g.Key, g => g.Sum(e => e.Count));
        var expectedOwned = pool.Entries.Where(e => catalog.Find(e.CardIdentifier)?.GameplayMetadata.IsBasicLand != true)
            .ToDictionary(e => e.CardIdentifier, e => e.Count);
        var sameInventory = actualOwned.Count == expectedOwned.Count && expectedOwned.All(e => actualOwned.GetValueOrDefault(e.Key) == e.Value);
        var complete = unresolved == 0 && !unknownSideboard && sameInventory && pool.Completeness == DraftPoolCompleteness.Complete;
        return new(revision, main, basics, complete ? ArenaDeckCompleteness.Complete : ArenaDeckCompleteness.Partial,
            ArenaDeckSnapshotScope.SavedDeck, ArenaDeckSnapshotSource.StructuredLog, unresolved == 0, unresolved, sideboard);
    }
}
