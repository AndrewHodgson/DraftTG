using System.Collections.Frozen;
using DraftTG.Domain;

namespace DraftTG.RecommendationEngine;

public sealed class BaselineDeckBuilder(BaselineDeckConfiguration? configuration = null, ArchetypeConfiguration? archetypeConfiguration = null)
{
    public BaselineDeckConfiguration Configuration { get; } = configuration ?? new();
    private readonly ArchetypeConfiguration _archetypeConfiguration = archetypeConfiguration ?? new();

    public DeckBuildResult Build(DeckBuildInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Pool.TotalCardCount > 200) throw new ArgumentException("Baseline construction supports pools up to 200 occurrences.", nameof(input));
        var plan = DeckPlanSelector.Select(input, Configuration, _archetypeConfiguration);
        if (plan is null)
        {
            var missing = input.Pool.UnresolvedOccurrenceCount > 0 || input.Pool.Entries.Any(e => input.Catalog.Find(e.CardIdentifier) is not { } card
                || !card.GameplayMetadata.CardTypes.IsKnown || !card.GameplayMetadata.ColorsKnown
                || card.GameplayMetadata.IsNonlandSpell && !DeckManaDemand.For(card).IsKnown);
            return new(missing ? DeckBuildAvailability.MissingRequiredMetadata : DeckBuildAvailability.InsufficientEligibleCards,
                null, missing ? "Known metadata cannot establish enough eligible spells; missing identities/types/colors/costs remain explicit."
                    : $"No two-color pair supplies {Configuration.TargetNonlandCount} eligible nonland copies; no splash was added.");
        }
        return BuildForPlan(input, plan, cancellationToken);
    }

    /// <summary>Uses the baseline optimizer against the full pool, with an explicit alternative pair.</summary>
    public DeckBuildResult BuildForPair(DeckBuildInput input, ArchetypeColorPair pair, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Pool.TotalCardCount > 200) throw new ArgumentException("Baseline construction supports pools up to 200 occurrences.", nameof(input));
        var evidence = DeckPlanSelector.RankPairs(input).Single(p => p.Pair == pair);
        if (evidence.EligibleNonlands < Configuration.TargetNonlandCount)
            return new(DeckBuildAvailability.InsufficientEligibleCards, null,
                $"{pair.DisplayCode} supplies {evidence.EligibleNonlands}/{Configuration.TargetNonlandCount} eligible nonland copies.");
        var context = DeckPlanSelector.FinalContext(input, _archetypeConfiguration);
        return BuildForPlan(input, new(pair, DeckPlanSource.AlternativeColorPair,
            context.Set?.Archetypes.FirstOrDefault(a => a.Pair == pair),
            evidence.PoolFit * (context.Candidates.FirstOrDefault()?.ProgressFactor ?? 1), context), cancellationToken);
    }

    private DeckBuildResult BuildForPlan(DeckBuildInput input, DeckPlan plan, CancellationToken cancellationToken)
    {
        // This candidate pack is only the unchanged Phase 8 pure scoring API's ordered input, not live Arena state.
        var candidates = new DraftPack(new(PackNumber.Create(1), PickNumber.Create(1)), input.Pool.Entries.Select(e => e.CardIdentifier));
        var statistical = new StatisticalRecommendationEngine().Recommend(candidates, input.Statistics, input.EnvironmentStatistics);
        var strengths = statistical.Cards.ToDictionary(c => c.CardIdentifier, c => Strength(c));
        var spells = input.Pool.Entries.SelectMany(e => input.Catalog.Find(e.CardIdentifier) is { } card
            && DeckPlanSelector.HasSpellMetadata(card) && DeckPlanSelector.ColorEligible(card, plan.Pair)
            ? Enumerable.Range(1, e.Count).Select(copy => new DeckOccurrence(card, copy, strengths[e.CardIdentifier])) : [])
            .ToArray();
        var selection = DeckSpellOptimizer.Select(spells, Configuration, cancellationToken);
        var chosenSpells = selection.Indices.Select(i => spells[i]).ToArray();
        var nonbasics = input.Pool.Entries.SelectMany(e => input.Catalog.Find(e.CardIdentifier) is { } card && LandEligible(card)
            ? Enumerable.Range(1, e.Count).Select(copy => new DeckOccurrence(card, copy, strengths[e.CardIdentifier])) : [])
            .OrderByDescending(o => SourceColors(o.Card).Count(plan.Pair.Colors.Contains))
            .ThenByDescending(o => o.Strength.Phase8AdjustedValue).ThenBy(o => o.Card.Identifier.Value, StringComparer.Ordinal).ThenBy(o => o.CopyIndex)
            .Take(Math.Min(Configuration.MaximumDraftedNonbasicLands, Configuration.TargetLandCount)).ToArray();
        var demand = Enum.GetValues<MagicColor>().ToDictionary(c => c, c => chosenSpells.Sum(o => DeckManaDemand.For(o.Card).Colors[c]));
        var basics = BaselineBasicLandAllocator.Allocate(plan.Pair, demand, Configuration.TargetLandCount - nonbasics.Length,
            Configuration.MinimumBasicsPerUsedColor);
        var selectedEntries = Aggregate(chosenSpells.Concat(nonbasics)).ToDictionary(e => e.CardIdentifier, e => e.Count);
        var sideboard = input.Pool.Entries.Select(e => new DeckCardEntry(e.CardIdentifier, e.Count - selectedEntries.GetValueOrDefault(e.CardIdentifier)))
            .Where(e => e.Count > 0).ToArray();
        var decisions = input.Pool.Entries.Select(e => Decision(e)).ToArray();
        var analysis = new DraftPoolAnalyzer().Analyze(new(new(chosenSpells.Concat(nonbasics).Select(o => o.Card.Identifier)), DraftPoolCompleteness.Complete), input.Catalog);
        var sources = Enum.GetValues<MagicColor>().ToDictionary(c => c, c => basics.Where(b => (int)b.Type == (int)c).Sum(b => b.Count)
            + nonbasics.Count(o => SourceColors(o.Card).Contains(c)));
        var neutral = chosenSpells.Count(o => o.Strength.Source == DeckCardStrengthSource.NeutralBaselineFallback);
        var unscored = chosenSpells.Count(o => o.Strength.Source == DeckCardStrengthSource.DeterministicUnscored);
        var measured = chosenSpells.Where(o => o.Strength.IsMeasured).ToArray();
        var diagnostics = new List<string>
        {
            plan.Source == DeckPlanSource.AlternativeColorPair ? "Alternative color pair; independently optimized against the full completed pool."
                : plan.Source == DeckPlanSource.ActiveArchetype ? "Final active archetype has enough eligible spells."
                : "Final active archetype absent or insufficient; highest viable pool-evidence pair selected.",
            "Primary face costs only. Alternate faces, indirect fixing, colorless-specific requirements and activated costs are not optimized.",
            "Known nonbasic sources are diagnostic; basic allocation takes no nonbasic source credit."
        };
        if (statistical.EnvironmentBaseline is null) diagnostics.Add("InsufficientStatistics: no usable environment baseline; unscored cards use stable ordering with no invented rate.");
        if (neutral > 0) diagnostics.Add($"{neutral} selected copies use a neutral environment-baseline prior, not measured GIH.");
        if (input.Pool.Completeness != DraftPoolCompleteness.Complete) diagnostics.Add($"Provisional: pool status {input.Pool.Completeness}; {input.Pool.UnresolvedOccurrenceCount} unresolved occurrences.");
        var slots = Configuration.TargetLandCount - nonbasics.Length;
        var usedColors = plan.Pair.Colors.Colors.Count(c => demand[c] > 0);
        if (usedColors > 0 && slots < usedColors * Configuration.MinimumBasicsPerUsedColor)
            diagnostics.Add($"Basic minimum relaxed to {slots / usedColors} per used color before largest-remainder distribution.");
        if (usedColors == 0) diagnostics.Add("No colored demand: generated basics use the first canonical plan color for generic mana.");
        var deck = new BaselineDeck(plan, Aggregate(chosenSpells), Aggregate(nonbasics), basics, sideboard, decisions, analysis,
            demand.ToFrozenDictionary(), sources.ToFrozenDictionary(), selection.Composition,
            new(selection.Objective, measured.Length == 0 ? null : measured.Average(o => o.Strength.SelectionValue!.Value), neutral, unscored),
            unscored > 0 ? DeckBuildConfidence.InsufficientStatistics : neutral > 0 ? DeckBuildConfidence.NeutralPriorsUsed : DeckBuildConfidence.Statistical,
            diagnostics);
        if (deck.TotalCardCount != Configuration.TargetDeckSize || deck.NonlandCount != Configuration.TargetNonlandCount
            || deck.LandCount != Configuration.TargetLandCount) throw new InvalidOperationException("Deck composition invariant violated.");
        return new(input.Pool.Completeness switch
        { DraftPoolCompleteness.Complete => DeckBuildAvailability.Ready, DraftPoolCompleteness.Partial => DeckBuildAvailability.ProvisionalPartialPool,
            _ => DeckBuildAvailability.ProvisionalUnknownPool }, deck, "One conservative baseline two-color build; no calibrated deck win prediction.");

        DeckCardStrength Strength(CardRecommendation card)
        {
            if (card.AdjustedValue is not { } overall)
                return new(card.CardIdentifier, statistical.EnvironmentBaseline,
                    statistical.EnvironmentBaseline is null ? DeckCardStrengthSource.DeterministicUnscored : DeckCardStrengthSource.NeutralBaselineFallback, null, 0);
            var pair = input.PairStatistics;
            var row = pair?.Catalog.StatisticsFor(card.CardIdentifier);
            var colors = input.Catalog.Find(card.CardIdentifier)?.Colors;
            if (plan.Source == DeckPlanSource.ActiveArchetype && plan.FinalArchetypeContext.Active is { } active
                && pair?.Pair == plan.Pair && pair.Context == input.StatisticsContext && plan.Pair.Includes(colors ?? ColorSet.Colorless)
                && pair.Baseline is { } pairBaseline && statistical.EnvironmentBaseline is { } baseline && row is not null
                && ArchetypeMath.Valid(new(row.GameInHandWinRate, row.GameInHandGameCount)))
            {
                var adjusted = ArchetypeMath.Shrink(overall, row.GameInHandWinRate!.Value, row.GameInHandGameCount!.Value, _archetypeConfiguration);
                var lift = ArchetypeMath.Lift(overall, baseline, adjusted, pairBaseline);
                var adjustment = ArchetypeMath.Adjustment(ArchetypeMath.Normalize(lift, _archetypeConfiguration), active.Confidence, _archetypeConfiguration);
                return new(card.CardIdentifier, Math.Clamp(overall + adjustment, 0, 1), DeckCardStrengthSource.ArchetypeAdjusted, overall, adjustment);
            }
            return new(card.CardIdentifier, overall, DeckCardStrengthSource.OverallStatistical, overall, 0);
        }
        bool LandEligible(Card card)
        {
            var m = card.GameplayMetadata;
            if (!m.IsLand || m.IsBasicLand || !DeckPlanSelector.ColorEligible(card, plan.Pair)) return false;
            var colored = SourceColors(card);
            if (colored.Count > 0) return colored.Any(plan.Pair.Colors.Contains);
            return strengths[card.Identifier].Phase8AdjustedValue is { } value && statistical.EnvironmentBaseline is { } baseline && value >= baseline;
        }
        DeckCardDecision Decision(DraftPoolEntry entry)
        {
            var reason = DeckDecisionReason.None;
            var card = input.Catalog.Find(entry.CardIdentifier);
            if (card is null || !card.GameplayMetadata.CardTypes.IsKnown || !card.GameplayMetadata.ColorsKnown) reason = DeckDecisionReason.MissingMetadata;
            else if (!DeckPlanSelector.ColorEligible(card, plan.Pair)) reason = DeckDecisionReason.OffColor;
            else if (card.GameplayMetadata.IsBasicLand) reason = DeckDecisionReason.GeneratedBasicsReplaceDraftedBasics;
            else if (card.GameplayMetadata.IsLand) reason = DeckDecisionReason.NonbasicLandPolicy;
            else if (!DeckPlanSelector.HasSpellMetadata(card)) reason = DeckDecisionReason.MissingMetadata;
            foreach (var i in Enumerable.Range(0, spells.Length).Where(i => spells[i].Card.Identifier == entry.CardIdentifier)) reason |= selection.Reasons[i];
            return new(entry.CardIdentifier, entry.Count, selectedEntries.GetValueOrDefault(entry.CardIdentifier), strengths.GetValueOrDefault(entry.CardIdentifier), reason);
        }
    }

    private static IReadOnlyList<MagicColor> SourceColors(Card card) => card.GameplayMetadata.Faces.Count > 0 ? []
        : (card.GameplayMetadata.ProducedMana ?? []).Where(m => m != ManaKind.Colorless)
            .Select(m => (MagicColor)(int)m).Distinct().ToArray();
    private static IReadOnlyList<DeckCardEntry> Aggregate(IEnumerable<DeckOccurrence> cards) => Array.AsReadOnly(cards
        .GroupBy(o => o.Card.Identifier).OrderBy(g => g.Key.Value, StringComparer.Ordinal).Select(g => new DeckCardEntry(g.Key, g.Count())).ToArray());
}
