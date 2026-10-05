using System.Globalization;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;

namespace DraftTG.App;

internal static class BaselineDeckPresentation
{
    internal static string Summary(DeckBuildResult? result, bool loading)
    {
        if (loading) return "Baseline build: loading…";
        if (result?.Deck is not { } deck) return result?.Diagnostic ?? "Completed pool unavailable.";
        return (result.Availability == DeckBuildAvailability.Ready ? "" : $"Provisional — {result.Availability}\n")
            + $"{deck.Plan.Pair.DisplayCode}{(deck.Plan.Archetype is { } a ? " — " + a.Name : "")}\n"
            + $"{deck.TotalCardCount} cards · {deck.NonlandCount} spells / {deck.LandCount} lands\n{deck.CreatureCount} creatures\n"
            + $"Plan: {deck.Plan.Source}\nConfidence: {deck.Confidence}\nMana Base\n"
            + string.Join(" · ", deck.GeneratedBasics.Select(b => $"{b.Type} ×{b.Count}"))
            + $"\nDrafted nonbasics: {deck.NonbasicLands.Sum(l => l.Count)}\nCurve: "
            + string.Join(" · ", deck.Analysis.NonlandCurve.OrderBy(p => p.Key).Select(p => $"{CurveLabel(p.Key)}: {p.Value}"));
    }

    internal static string Cards(BaselineDeck? deck, CardCatalog? catalog)
    {
        if (deck is null || catalog is null) return "";
        var creatures = deck.Nonlands.Where(e => catalog.Find(e.CardIdentifier)?.GameplayMetadata.IsCreature == true);
        var other = deck.Nonlands.Where(e => catalog.Find(e.CardIdentifier)?.GameplayMetadata.IsCreature != true);
        return "Creatures\n" + List(creatures, catalog) + "\n\nOther Spells\n" + List(other, catalog)
            + "\n\nLands\n" + List(deck.NonbasicLands, catalog)
            + (deck.NonbasicLands.Count > 0 ? "\n" : "") + string.Join("\n", deck.GeneratedBasics.Select(b => $"{b.Type} ×{b.Count} (generated)"));
    }
    internal static string Sideboard(BaselineDeck? deck, CardCatalog? catalog) => deck is null || catalog is null ? "" : List(deck.Sideboard, catalog);

    internal static string Diagnostics(BaselineDeck? deck, CardCatalog? catalog)
    {
        if (deck is null || catalog is null) return "";
        var c = deck.Composition;
        return $"Required creatures: {c.RequiredCreatures}; early: {c.RequiredEarlyPlays}; 5+ cap: {c.EffectiveHighCostCap}\n"
            + $"Selected early: {c.SelectedEarlyPlays}; high cost: {c.SelectedHighCostCards}\n"
            + $"Selection objective: {Number(deck.Strength.TotalSelectionObjective)}; neutral-prior copies: {deck.Strength.NeutralFallbackCards}\n"
            + "Colored pip demand: " + string.Join(" · ", deck.ColoredDemand.OrderBy(p => p.Key).Select(p => $"{p.Key} {Number(p.Value)}"))
            + "\nKnown sources: " + string.Join(" · ", deck.KnownColoredSources.OrderBy(p => p.Key).Select(p => $"{p.Key} {p.Value}"))
            + "\n" + string.Join("\n", deck.Diagnostics.Concat(c.Relaxations)) + "\n\n"
            + string.Join("\n\n", deck.Decisions.Select(d => $"{catalog.Find(d.CardIdentifier)?.Name ?? d.CardIdentifier.Value}: {d.SelectedCount}/{d.PoolCount} selected\n"
                + $"{d.Reasons}\nSelection strength: {d.Strength?.Source}; value: {(d.Strength?.SelectionValue is { } value ? Number(value) : "unscored")}"));
    }
    private static string List(IEnumerable<DeckCardEntry> entries, CardCatalog catalog) => string.Join("\n", entries
        .Select(e => (Entry: e, Card: catalog.Find(e.CardIdentifier)))
        .OrderBy(e => e.Card?.GameplayMetadata.ManaValue ?? double.MaxValue).ThenBy(e => e.Entry.CardIdentifier.Value, StringComparer.Ordinal)
        .Select(e => $"{e.Card?.Name ?? e.Entry.CardIdentifier.Value} ×{e.Entry.Count}"));
    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static string CurveLabel(ManaCurveBucket bucket) => bucket switch
    {
        ManaCurveBucket.BelowTwo => "0–<2", ManaCurveBucket.Two => "2–<3", ManaCurveBucket.Three => "3–<4",
        ManaCurveBucket.Four => "4–<5", ManaCurveBucket.Five => "5–<6", ManaCurveBucket.Six => "6–<7",
        ManaCurveBucket.SevenPlus => "7+", _ => "Unknown"
    };
}
