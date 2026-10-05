using DraftTG.Domain;

namespace DraftTG.RecommendationEngine;

/// <summary>One baseline plus independently optimized pairs; no provider calls or trophy scoring.</summary>
public sealed class SuggestedDeckBuilder(BaselineDeckBuilder? baselineBuilder = null, SuggestedDeckConfiguration? configuration = null)
{
    private readonly BaselineDeckBuilder _baseline = baselineBuilder ?? new();
    public SuggestedDeckConfiguration Configuration { get; } = configuration ?? new();

    public SuggestedDeckSet Build(DeckBuildInput input, string? sessionIdentity = null, CancellationToken cancellationToken = default)
    {
        var baseline = _baseline.Build(input, cancellationToken);
        var poolIdentity = SuggestedDeckIdentity.Pool(input.Pool);
        sessionIdentity = string.IsNullOrWhiteSpace(sessionIdentity) ? poolIdentity : sessionIdentity;
        var diagnostics = new List<SuggestedDeckCandidateDiagnostic>();
        var alternatives = new List<(DeckBuildResult Result, SuggestedDeckComparison Comparison)>();
        var pairs = DeckPlanSelector.RankPairs(input).OrderBy(p => p.Pair.Colors.Colors[0]).ThenBy(p => p.Pair.Colors.Colors[1]);
        foreach (var pair in pairs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var viable = pair.EligibleNonlands >= _baseline.Configuration.TargetNonlandCount;
            if (!viable)
            {
                diagnostics.Add(new(pair, false, false, $"{pair.Pair.DisplayCode}: {pair.EligibleNonlands}/{_baseline.Configuration.TargetNonlandCount} eligible nonland copies."));
                continue;
            }
            if (baseline.Deck?.Plan.Pair == pair.Pair)
            {
                diagnostics.Add(new(pair, true, true, "Recommended Phase 10B baseline preserved."));
                continue;
            }
            try
            {
                var result = _baseline.BuildForPair(input, pair.Pair, cancellationToken);
                var error = Validate(result.Deck, input);
                diagnostics.Add(new(pair, true, error is null, error ?? "Valid independently optimized alternative."));
                if (error is null) alternatives.Add((result, Compare(result.Deck!, pair)));
            }
            catch (InvalidOperationException error) { diagnostics.Add(new(pair, true, false, $"Optimization failed: {error.Message}")); }
        }
        if (baseline.Deck is not { } recommended)
            return new(sessionIdentity, poolIdentity, input.Pool, baseline, [], diagnostics);
        var builds = new List<SuggestedDeck>
        {
            new(SuggestedDeckId.For(sessionIdentity, recommended.Plan.Pair), new(1), baseline.Availability, recommended,
                Compare(recommended, diagnostics.Single(d => d.Evidence.Pair == recommended.Plan.Pair).Evidence),
                SuggestedDeckDifference.Compare(recommended, recommended))
        };
        foreach (var candidate in OrderAlternatives(alternatives).Take(Configuration.MaximumSuggestedBuilds - 1))
        {
            var deck = candidate.Result.Deck!;
            builds.Add(new(SuggestedDeckId.For(sessionIdentity, deck.Plan.Pair), new(builds.Count + 1), candidate.Result.Availability,
                deck, candidate.Comparison, SuggestedDeckDifference.Compare(recommended, deck)));
        }
        return new(sessionIdentity, poolIdentity, input.Pool, baseline, builds, diagnostics);
    }

    private static IOrderedEnumerable<(DeckBuildResult Result, SuggestedDeckComparison Comparison)> OrderAlternatives(
        IEnumerable<(DeckBuildResult Result, SuggestedDeckComparison Comparison)> candidates) => candidates
        .OrderByDescending(c => c.Comparison.CommonSpellQuality).ThenByDescending(c => c.Comparison.PairPoolFit)
        .ThenByDescending(c => c.Comparison.MeasuredStatCards).ThenBy(c => c.Comparison.NeutralFallbackCards)
        .ThenBy(c => c.Comparison.CompositionRelaxations).ThenByDescending(c => c.Comparison.CombinedColorEvidence)
        .ThenBy(c => c.Result.Deck!.Plan.Pair.Colors.Colors[0]).ThenBy(c => c.Result.Deck!.Plan.Pair.Colors.Colors[1]);

    private static SuggestedDeckComparison Compare(BaselineDeck deck, DeckPairEvidence pair)
    {
        var strengths = deck.Decisions.ToDictionary(d => d.CardIdentifier, d => d.Strength);
        double total = 0; var measured = 0; var neutral = 0; var unscored = 0;
        foreach (var entry in deck.Nonlands)
        {
            var strength = strengths[entry.CardIdentifier]!;
            if (strength.Phase8AdjustedValue is { } value) { total += value * entry.Count; measured += entry.Count; }
            else if (strength.Source == DeckCardStrengthSource.NeutralBaselineFallback && strength.SelectionValue is { } prior)
            { total += prior * entry.Count; neutral += entry.Count; }
            else unscored += entry.Count;
        }
        return new(unscored > 0 ? null : total / deck.NonlandCount, pair.PoolFit, measured, neutral, unscored,
            deck.Composition.Relaxations.Count, pair.CombinedEvidence);
    }

    private string? Validate(BaselineDeck? deck, DeckBuildInput input)
    {
        if (deck is null) return "Optimizer did not produce a deck.";
        if (deck.NonlandCount != _baseline.Configuration.TargetNonlandCount || deck.LandCount != _baseline.Configuration.TargetLandCount
            || deck.TotalCardCount != _baseline.Configuration.TargetDeckSize) return "Deck composition invariant failed.";
        if (deck.Nonlands.Any(e => input.Catalog.Find(e.CardIdentifier) is not { } c || !DeckPlanSelector.ColorEligible(c, deck.Plan.Pair)))
            return "Off-color spell invariant failed.";
        var inventory = input.Pool.Entries.ToDictionary(e => e.CardIdentifier, e => e.Count);
        if (deck.Nonlands.Concat(deck.NonbasicLands).GroupBy(e => e.CardIdentifier)
            .Any(g => g.Sum(e => e.Count) > inventory.GetValueOrDefault(g.Key))) return "Drafted inventory invariant failed.";
        return null;
    }
}
