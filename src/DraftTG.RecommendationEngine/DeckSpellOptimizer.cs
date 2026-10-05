using System.Numerics;
using DraftTG.Domain;

namespace DraftTG.RecommendationEngine;

internal sealed record DeckOccurrence(Card Card, int CopyIndex, DeckCardStrength Strength);
internal sealed record SpellSelection(IReadOnlyList<int> Indices, IReadOnlyDictionary<int, DeckDecisionReason> Reasons,
    DeckCompositionDiagnostics Composition, double Objective);

/// <summary>Bounded count-state DP. Masks preserve stable occurrence ties, never a four-copy limit.</summary>
internal static class DeckSpellOptimizer
{
    private readonly record struct State(int Count, int Creatures, int Early, int High);
    private readonly record struct Node(double Objective, int Measured, BigInteger Mask);

    internal static SpellSelection Select(IReadOnlyList<DeckOccurrence> cards, BaselineDeckConfiguration config, CancellationToken token)
    {
        var creatureFloor = Math.Min(config.MinimumCreatures, Math.Min(cards.Count(c => c.Card.GameplayMetadata.IsCreature), config.TargetNonlandCount));
        var earlyFloor = Math.Min(config.MinimumEarlyPlays, Math.Min(cards.Count(c => Early(c.Card)), config.TargetNonlandCount));
        var states = new Dictionary<State, Node> { [new(0, 0, 0, 0)] = new(0, 0, BigInteger.Zero) };
        for (var i = 0; i < cards.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var occurrence = cards[i];
            var creature = occurrence.Card.GameplayMetadata.IsCreature ? 1 : 0;
            var early = Early(occurrence.Card) ? 1 : 0;
            var high = occurrence.Card.GameplayMetadata.ManaValue >= config.HighCostThreshold ? 1 : 0;
            foreach (var (state, node) in states.ToArray())
            {
                if (state.Count == config.TargetNonlandCount) continue;
                var next = new State(state.Count + 1, state.Creatures + creature,
                    Math.Min(earlyFloor, state.Early + early), state.High + high);
                var candidate = new Node(node.Objective + (occurrence.Strength.SelectionValue ?? 0),
                    node.Measured + (occurrence.Strength.IsMeasured ? 1 : 0), node.Mask | (BigInteger.One << (cards.Count - i - 1)));
                if (!states.TryGetValue(next, out var current) || Better(candidate, current)) states[next] = candidate;
            }
        }
        var finals = states.Where(p => p.Key.Count == config.TargetNonlandCount).ToArray();
        var minimumHigh = finals.Where(p => p.Key.Creatures >= creatureFloor && p.Key.Early >= earlyFloor).Min(p => p.Key.High);
        var highCap = Math.Max(config.MaximumHighCostCards, minimumHigh);
        var chosen = Best(creatureFloor, earlyFloor, highCap);
        var withoutCreature = Best(0, earlyFloor, highCap);
        var withoutEarly = Best(creatureFloor, 0, highCap);
        var withoutHigh = Best(creatureFloor, earlyFloor, config.TargetNonlandCount);
        var unconstrained = Best(0, 0, config.TargetNonlandCount);
        var reasons = new Dictionary<int, DeckDecisionReason>();
        var indices = new List<int>();
        for (var i = 0; i < cards.Count; i++)
        {
            var included = Has(chosen.Value.Mask, i);
            var reason = DeckDecisionReason.None;
            if (included)
            {
                indices.Add(i);
                reason |= cards[i].Strength.Source switch
                {
                    DeckCardStrengthSource.ArchetypeAdjusted => DeckDecisionReason.StatisticalStrength | DeckDecisionReason.ArchetypeAffinity,
                    DeckCardStrengthSource.OverallStatistical => DeckDecisionReason.StatisticalStrength,
                    DeckCardStrengthSource.NeutralBaselineFallback => DeckDecisionReason.NeutralStatisticalPrior,
                    _ => DeckDecisionReason.DeterministicFallback
                };
                if (!Has(withoutCreature.Value.Mask, i)) reason |= DeckDecisionReason.CreatureRequirement;
                if (!Has(withoutEarly.Value.Mask, i)) reason |= DeckDecisionReason.EarlyCurveRequirement;
                if (!Has(unconstrained.Value.Mask, i)) reason |= DeckDecisionReason.CompositionConstraint;
            }
            else
            {
                if (Has(withoutHigh.Value.Mask, i)) reason |= DeckDecisionReason.HighCostConstraint;
                if (Has(unconstrained.Value.Mask, i) || Has(withoutCreature.Value.Mask, i) || Has(withoutEarly.Value.Mask, i))
                    reason |= DeckDecisionReason.CompositionConstraint;
                if (reason == DeckDecisionReason.None) reason = DeckDecisionReason.LowerSelectionValue;
            }
            reasons[i] = reason;
        }
        var relaxations = new List<string>();
        if (creatureFloor < config.MinimumCreatures) relaxations.Add($"Creature floor relaxed from {config.MinimumCreatures} to {creatureFloor}: eligible pool supply.");
        if (earlyFloor < config.MinimumEarlyPlays) relaxations.Add($"Early-play floor relaxed from {config.MinimumEarlyPlays} to {earlyFloor}: eligible pool supply.");
        if (highCap > config.MaximumHighCostCards) relaxations.Add($"High-cost cap relaxed from {config.MaximumHighCostCards} to {highCap}: minimum feasible with both floors.");
        return new(Array.AsReadOnly(indices.ToArray()), reasons.ToFrozen(), new(creatureFloor, earlyFloor, highCap,
            indices.Count(i => Early(cards[i].Card)), chosen.Key.High, Array.AsReadOnly(relaxations.ToArray())), chosen.Value.Objective);

        bool Has(BigInteger mask, int index) => (mask & (BigInteger.One << (cards.Count - index - 1))) != 0;
        KeyValuePair<State, Node> Best(int creatures, int early, int high) => finals
            .Where(p => p.Key.Creatures >= creatures && p.Key.Early >= early && p.Key.High <= high)
            .OrderByDescending(p => p.Value.Objective).ThenByDescending(p => p.Value.Measured)
            .ThenBy(p => Math.Abs(p.Key.Creatures - config.PreferredCreatures)).ThenByDescending(p => p.Value.Mask).First();
    }

    private static bool Early(Card card) => card.GameplayMetadata.ManaValue is <= 2;
    private static bool Better(Node candidate, Node current) => candidate.Objective > current.Objective
        || candidate.Objective == current.Objective && (candidate.Measured > current.Measured
            || candidate.Measured == current.Measured && candidate.Mask > current.Mask);

    private static IReadOnlyDictionary<int, DeckDecisionReason> ToFrozen(this Dictionary<int, DeckDecisionReason> values) =>
        System.Collections.Frozen.FrozenDictionary.ToFrozenDictionary(values);
}
