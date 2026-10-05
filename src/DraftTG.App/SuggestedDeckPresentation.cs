using System.Globalization;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;

namespace DraftTG.App;

internal static class SuggestedDeckPresentation
{
    internal static string Coverage(SuggestedDeck? selected) => selected is null ? "" :
        $"\nMeasured cards: {selected.Comparison.MeasuredStatCards} / {selected.Deck.NonlandCount}"
        + $"\nNeutral fallback: {selected.Comparison.NeutralFallbackCards}; unscored: {selected.Comparison.UnscoredCards}"
        + $"\nComposition relaxations: {selected.Comparison.CompositionRelaxations}";

    internal static string Difference(SuggestedDeck? selected, CardCatalog? catalog)
    {
        if (selected is null || selected.IsRecommended || catalog is null) return "";
        var d = selected.Difference;
        string Cards(IEnumerable<DeckCardEntry> cards) => string.Join("\n", cards.Select(e => $"{catalog.Find(e.CardIdentifier)?.Name ?? e.CardIdentifier.Value} ×{e.Count}"));
        string Basics(IEnumerable<GeneratedBasicLandEntry> basics) => string.Join("\n", basics.Select(e => $"{e.Type} ×{e.Count} (generated)"));
        return $"Compared with Recommended Build 1:\n+ {d.CardsAdded.Sum(e => e.Count)} spells / − {d.CardsRemoved.Sum(e => e.Count)} spells"
            + $"\nShared spells: {d.SharedNonlandCount}; changed: {d.ChangedNonlandCount}"
            + $"\nLands: + {d.Lands.AddedCount} / − {d.Lands.RemovedCount}"
            + "\n\nAdd\n" + Cards(d.CardsAdded) + "\n\nRemove\n" + Cards(d.CardsRemoved)
            + "\n\nLand additions\n" + Cards(d.Lands.DraftedAdded) + "\n" + Basics(d.Lands.BasicsAdded)
            + "\n\nLand removals\n" + Cards(d.Lands.DraftedRemoved) + "\n" + Basics(d.Lands.BasicsRemoved);
    }

    internal static string Diagnostics(SuggestedDeckSet? set)
    {
        if (set is null) return "";
        string Number(double? value) => value?.ToString("0.######", CultureInfo.InvariantCulture) ?? "unavailable";
        return $"\n\nSuggested builds: {set.Builds.Count}; viable pairs evaluated: {set.ViablePairCandidatesEvaluated} / 10"
            + "\nCommon spell quality is an internal ordering metric, not a predicted win rate."
            + "\n" + string.Join("\n", set.Builds.Select(b => $"{b.Label}: {b.PlanLabel}; common spell quality {Number(b.Comparison.CommonSpellQuality)}; "
                + $"pool fit {Number(b.Comparison.PairPoolFit)}; measured {b.Comparison.MeasuredStatCards}/{b.Deck.NonlandCount}; "
                + $"neutral {b.Comparison.NeutralFallbackCards}; composition relaxations {b.Comparison.CompositionRelaxations}"))
            + "\nExcluded pairs\n" + string.Join("\n", set.ExcludedCandidates.Select(c => c.Diagnostic));
    }
}
