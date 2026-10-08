using DraftTG.Data;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;

namespace DraftTG.Application;

public sealed record LimitedStatisticsMappingResult(LimitedCardStatisticsCatalog Catalog, string? Diagnostic)
{
    public IReadOnlyDictionary<CardIdentifier, string> ResolvedNames { get; init; }
        = System.Collections.Frozen.FrozenDictionary<CardIdentifier, string>.Empty;
    /// <summary>Statistical-identity outcome for every participant, including unresolved ones.</summary>
    public IReadOnlyDictionary<CardIdentifier, StatisticalIdentityResolution> Resolutions { get; init; }
        = System.Collections.Frozen.FrozenDictionary<CardIdentifier, StatisticalIdentityResolution>.Empty;
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
        var groups = rows.GroupBy(row => row.Name, StringComparer.Ordinal).ToArray();
        // Only the catalog-side name -> printing direction is needed here, so no row lookup is supplied.
        var resolver = SeventeenLandsStatisticalIdentity.For(new Dictionary<string, SeventeenLandsRating?>(), catalog);
        foreach (var group in groups)
        {
            var evidence = group.Distinct().ToArray();
            if (evidence.Length != 1) continue;
            // Front-face-named multiface rows belong to the environment too (same rule as participants).
            var representative = resolver.CardsFor(group.Key)
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
        var resolver = SeventeenLandsStatisticalIdentity.For(byName, catalog);
        if (snapshot is not null) return MapParticipants(resolver, catalog, snapshot);
        return MapStandalone(byName, resolver, context);
    }

    private static LimitedStatisticsMappingResult MapParticipants(
        SeventeenLandsStatisticalIdentity.Resolver resolver, CardCatalog catalog, DraftSnapshot snapshot)
    {
        var mapped = new List<LimitedCardStatistics>();
        var current = snapshot.CurrentPack.AvailableCardIdentifiers.ToHashSet();
        var names = new Dictionary<CardIdentifier, string>();
        var resolutions = new Dictionary<CardIdentifier, StatisticalIdentityResolution>();
        var unavailable = 0;
        foreach (var identifier in snapshot.CurrentPack.AvailableCardIdentifiers
            .Concat(snapshot.DraftedPool.CardIdentifiers).Distinct())
        {
            // Identity was resolved by Arena/Scryfall already. Never re-resolve printing identity by name;
            // only the statistical identity (which provider row describes this exact card) is resolved here.
            var card = catalog.Find(identifier);
            var (resolution, row) = card is null ? (new(StatisticalIdentityKind.NoProviderRow, null), null) : resolver.Resolve(card);
            resolutions.Add(identifier, resolution);
            if (row is not null)
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
        })
        {
            ResolvedNames = System.Collections.Frozen.FrozenDictionary.ToFrozenDictionary(names),
            Resolutions = System.Collections.Frozen.FrozenDictionary.ToFrozenDictionary(resolutions)
        };
    }

    // Standalone catalogs have no known participant identities. Keep their conservative
    // printing analysis separate from the live path and never emit a name-list audit.
    private static LimitedStatisticsMappingResult MapStandalone(IReadOnlyDictionary<string, SeventeenLandsRating?> byName,
        SeventeenLandsStatisticalIdentity.Resolver resolver, LimitedStatisticsContext context)
    {
        var mapped = new List<LimitedCardStatistics>();
        var ambiguous = 0;
        foreach (var (name, row) in byName)
        {
            if (row is null) { ambiguous++; continue; }
            var matches = resolver.CardsFor(name);
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
