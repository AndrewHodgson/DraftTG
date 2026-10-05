using DraftTG.Data;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;

namespace DraftTG.Application;

public sealed record LimitedStatisticsMappingResult(LimitedCardStatisticsCatalog Catalog, string? Diagnostic)
{
    public IReadOnlyDictionary<CardIdentifier, string> ResolvedNames { get; init; }
        = System.Collections.Frozen.FrozenDictionary<CardIdentifier, string>.Empty;
}

public static class LimitedStatisticsMapper
{
    /// <summary>One entry per exact source name, independent of current pack/history.
    /// A deterministic known printing is only a representative for baseline aggregation;
    /// pack identity continues to come exclusively from the Arena/Scryfall resolver.</summary>
    public static LimitedCardStatisticsCatalog MapEnvironment(IEnumerable<SeventeenLandsRating> rows,
        CardCatalog catalog, LimitedStatisticsContext context)
    {
        var mapped = new List<LimitedCardStatistics>();
        foreach (var group in rows.GroupBy(row => row.Name, StringComparer.Ordinal))
        {
            var evidence = group.Distinct().ToArray();
            if (evidence.Length != 1) continue;
            var representative = catalog.FindByExactName(group.Key)
                .OrderByDescending(card => string.Equals(card.SetCode.Value, context.Expansion,
                    StringComparison.OrdinalIgnoreCase))
                .ThenBy(card => card.Identifier.Value, StringComparer.Ordinal).FirstOrDefault();
            if (representative is not null) mapped.Add(Attach(representative.Identifier, evidence[0]));
        }
        return new(mapped);
    }

    public static LimitedStatisticsMappingResult Map(IEnumerable<SeventeenLandsRating> rows,
        CardCatalog catalog, LimitedStatisticsContext context, DraftSnapshot? snapshot)
    {
        // Identical evidence can be reconciled, but conflicting provider rows must not
        // become an arbitrary first-row choice. Names are always ordinal and exact.
        var byName = rows.GroupBy(row => row.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group =>
            {
                var distinct = group.Distinct().ToArray();
                return distinct.Length == 1 ? distinct[0] : null;
            }, StringComparer.Ordinal);
        if (snapshot is not null) return MapParticipants(byName, catalog, snapshot);
        return MapStandalone(byName, catalog, context);
    }

    private static LimitedStatisticsMappingResult MapParticipants(
        IReadOnlyDictionary<string, SeventeenLandsRating?> byName, CardCatalog catalog, DraftSnapshot snapshot)
    {
        var mapped = new List<LimitedCardStatistics>();
        var current = snapshot.CurrentPack.AvailableCardIdentifiers.ToHashSet();
        var names = new Dictionary<CardIdentifier, string>();
        var unavailable = 0;
        foreach (var identifier in snapshot.CurrentPack.AvailableCardIdentifiers
            .Concat(snapshot.History.SelectedCardIdentifiers).Distinct())
        {
            // Identity was resolved by Arena/Scryfall already. Never re-resolve it by name.
            var card = catalog.Find(identifier);
            if (card is not null && byName.TryGetValue(card.Name, out var row) && row is not null)
            {
                mapped.Add(Attach(identifier, row));
                names.Add(identifier, row.Name);
            }
            else if (current.Contains(identifier))
                unavailable++;
        }
        return new(new(mapped), unavailable switch
        {
            0 => null,
            1 => "1 current-pack card has no statistics.",
            _ => $"{unavailable} current-pack cards have no statistics."
        }) { ResolvedNames = System.Collections.Frozen.FrozenDictionary.ToFrozenDictionary(names) };
    }

    // Standalone catalogs have no known participant identities. Keep their conservative
    // printing analysis separate from the live path and never emit a name-list audit.
    private static LimitedStatisticsMappingResult MapStandalone(
        IReadOnlyDictionary<string, SeventeenLandsRating?> byName, CardCatalog catalog, LimitedStatisticsContext context)
    {
        var mapped = new List<LimitedCardStatistics>();
        var ambiguous = 0;
        foreach (var (name, row) in byName)
        {
            if (row is null) { ambiguous++; continue; }
            var matches = catalog.FindByExactName(name);
            var inSet = matches.Where(card => string.Equals(card.SetCode.Value, context.Expansion,
                StringComparison.OrdinalIgnoreCase)).ToArray();
            if (inSet.Length > 0) matches = inSet;
            if (matches.Count > 1) { ambiguous++; continue; }
            foreach (var card in matches) mapped.Add(Attach(card.Identifier, row));
        }
        return new(new(mapped), ambiguous == 0 ? null
            : $"Statistics mapping ambiguity affects {ambiguous} standalone entries.");
    }

    private static LimitedCardStatistics Attach(CardIdentifier identifier, SeventeenLandsRating row) =>
        new(identifier, row.GameInHandGameCount, row.PlayRate, row.GameInHandWinRate,
            row.OpeningHandWinRate, row.DrawnWinRate, row.DrawnImprovementWinRate,
            row.AverageLastSeenAt, row.AverageTakenAt);
}
