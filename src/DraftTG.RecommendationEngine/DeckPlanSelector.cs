using DraftTG.Domain;

namespace DraftTG.RecommendationEngine;

public static class DeckPlanSelector
{
    public static ArchetypeContextProfile FinalContext(DeckBuildInput input, ArchetypeConfiguration? configuration = null)
    {
        var knownColors = new DraftedCardPool(input.Pool.Inventory.CardIdentifiers
            .Where(id => input.Catalog.Find(id)?.GameplayMetadata.ColorsKnown == true));
        var set = input.Archetypes;
        if (input.StatisticsContext is { } context && set?.SetCode != context.Expansion) set = null;
        return ArchetypeContextProfile.Analyze(set, ColorCommitmentProfile.From(knownColors, input.Catalog), configuration);
    }

    public static bool HasSpellMetadata(Card card) => card.GameplayMetadata.ColorsKnown &&
        card.GameplayMetadata.IsNonlandSpell && DeckManaDemand.For(card).IsKnown;
    public static bool ColorEligible(Card card, ArchetypeColorPair pair) =>
        card.GameplayMetadata.ColorsKnown && card.Colors.Colors.All(pair.Colors.Contains);

    public static IReadOnlyList<DeckPairEvidence> RankPairs(DeckBuildInput input)
    {
        var evidence = ColorCommitmentProfile.From(new DraftedCardPool(input.Pool.Inventory.CardIdentifiers
            .Where(id => input.Catalog.Find(id)?.GameplayMetadata.ColorsKnown == true)), input.Catalog);
        // The pair ranking uses the same 1/k pool evidence and coverage/balance fit as Phase 9C.
        var total = evidence.Colors.Sum(c => c.Evidence);
        var candidates = new List<DeckPairEvidence>();
        for (var a = 0; a < 5; a++) for (var b = a + 1; b < 5; b++)
        {
            var pair = ArchetypeColorPair.Create($"{"WUBRG"[a]}{"WUBRG"[b]}");
            var x = evidence.For((MagicColor)a).Evidence; var y = evidence.For((MagicColor)b).Evidence;
            var sum = x + y; var balance = sum > 0 ? 2 * Math.Min(x, y) / sum : 0;
            var fit = (total > 0 ? sum / total : 0) * (.75 + .25 * balance);
            var count = input.Pool.Entries.Sum(e => input.Catalog.Find(e.CardIdentifier) is { } card && HasSpellMetadata(card)
                && ColorEligible(card, pair) ? e.Count : 0);
            candidates.Add(new(pair, fit, sum, balance, count));
        }
        return Array.AsReadOnly(candidates.OrderByDescending(p => p.PoolFit).ThenByDescending(p => p.CombinedEvidence)
            .ThenByDescending(p => p.Balance).ThenBy(p => p.Pair.Colors.Colors[0]).ThenBy(p => p.Pair.Colors.Colors[1]).ToArray());
    }

    public static DeckPlan? Select(DeckBuildInput input, BaselineDeckConfiguration configuration, ArchetypeConfiguration? archetypeConfiguration = null)
    {
        var context = FinalContext(input, archetypeConfiguration);
        var pairs = RankPairs(input);
        var active = context.Active;
        var selected = active is null ? null : pairs.First(p => p.Pair == active.Definition.Pair);
        var source = DeckPlanSource.ActiveArchetype;
        if (selected is null || selected.EligibleNonlands < configuration.TargetNonlandCount)
        { selected = pairs.FirstOrDefault(p => p.EligibleNonlands >= configuration.TargetNonlandCount); source = DeckPlanSource.PoolEvidenceFallback; }
        return selected is null ? null : new(selected.Pair, source,
            context.Set?.Archetypes.FirstOrDefault(a => a.Pair == selected.Pair), selected.PoolFit * (context.Candidates.FirstOrDefault()?.ProgressFactor ?? 1),
            context);
    }
}
