using System.Globalization;
using DraftTG.RecommendationEngine;

namespace DraftTG.Application;

public static class PickScorePresentation
{
    public static string Summary(ContextualPickScoreResult? result, string? topName, bool loading = false)
    {
        if (loading) return "Pick Score: loading…";
        if (result is null) return string.Empty;
        var top = result.Cards.FirstOrDefault(c => c.IsContextPick);
        if (top is null) return "Pick Score: unavailable or insufficient comparable candidates";
        return $"Context Pick\n{topName}\nPick Score {top.Score0To50} · pack rank #{top.CurrentPackRank}"
            + (top.Availability == PickScoreAvailability.EstimatedMissingStatistics ? " · estimated" : "")
            + $"\n{result.Configuration.ModelVersion}\n" + string.Join("\n", top.Reasons.Take(3));
    }
    /// <summary>Only Q can be estimated; lane, archetype and deck fit are described separately because they always use
    /// real draft context. EST means card-level quality is estimated, not that the recommendation is weak.</summary>
    public static string Evidence(ContextualPickScore score, LimitedCardStatistics? statistics = null)
    {
        var quality = score.QualityEvidence switch
        {
            PickScoreQualityEvidence.DirectCardStatistics => statistics?.GameInHandWinRate is { } gih
                ? string.Create(CultureInfo.InvariantCulture, $"Direct — 17Lands GIH {gih:P1} over {statistics.GameInHandGameCount:N0} games")
                : "Direct — 17Lands GIH",
            PickScoreQualityEvidence.NoProviderRow => "Estimated — no 17Lands row for this card in this set/format; neutral format prior used",
            PickScoreQualityEvidence.ProviderRowWithoutUsableGih => "Estimated — this card's 17Lands row has no usable GIH win rate; neutral format prior used",
            PickScoreQualityEvidence.NoEnvironmentBaseline => "Unavailable — no format baseline",
            _ => score.IsQualityEstimated ? "Estimated — neutral format prior used" : "Direct"
        };
        static string Signal(double value, string high, string low) => value > .55 ? high : value < .45 ? low : "neutral (no clear signal)";
        // D is centred near .55 (a card exactly at the projected cut line); the label summarises, the lines explain.
        var label = score.DeckNeedComponent >= .65 ? "strong" : score.DeckNeedComponent <= .40 ? "weak" : "neutral";
        var deck = score.Weights.DeckNeed <= 0 ? "not weighted yet (early draft)"
            : string.Create(CultureInfo.InvariantCulture, $"{label} (D {score.DeckNeedComponent:0.00})") + string.Concat(score.DeckImpact.Reasons
                .Where(r => r.StartsWith("+ ", StringComparison.Ordinal) || r.StartsWith("- ", StringComparison.Ordinal)).Take(4).Select(r => "\n  " + r));
        // Phase 6: intrinsic strength and how actionable it is are shown separately; Q itself is never reduced.
        var strength = score.IsQualityEstimated || score.QualityComponent is not { } q ? "" : q >= .85 ? "excellent · " : q >= .65 ? "strong · " : q >= .4 ? "average · " : "below average · ";
        var pair = (score.DeckImpact.BeforePair ?? score.DeckImpact.AfterPair)?.Code;
        var playability = score.QualityRelevance >= .95 ? "realistic for your projected decks"
            : string.Create(CultureInfo.InvariantCulture, $"unlikely to make your current {pair ?? "projected"} deck — quality relevance {score.QualityRelevance:P0}");
        return "Evidence:\nIntrinsic quality: " + strength + quality
            + "\nPlayability: " + playability
            + "\nLane: " + Signal(score.LaneComponent, "candidate colours appear open", "less lane support")
            + "\nArchetype: " + Signal(score.ArchetypeComponent, "strong fit for the active colour pair", "weak fit for the active colour pair")
            + "\nDeck need: " + deck;
    }
    public static string Diagnostics(ContextualPickScore? score, LimitedCardStatistics? statistics = null)
    {
        if (score is null) return "Pick Score: unavailable";
        var text = string.Create(CultureInfo.InvariantCulture,
            $"Pick Score: {score.Score0To50?.ToString(CultureInfo.InvariantCulture) ?? "unavailable"} / 50{(score.IsQualityEstimated ? " (Estimated)" : "")}; Model: {score.ModelVersion}; {score.Availability}\n"
            + $"Exact score {(score.ContextualValue is { } value ? ContextualPickScoreCalibration.ExactScore(value) : double.NaN):0.00}; tier {PickScoreTierThresholds.Describe(score.Tier)} ({PickScoreTierThresholds.Version})\n"
            + $"Pick-score ordinal rank: {score.CurrentPackRank}; progress {score.DraftProgress:0.###}; color fit {score.ColorFit:0.###}; Phase 8 data weight {score.StatisticalDataWeight:0.###}\n"
            + $"Quality raw {score.QualityComponent:0.###}; relevance R {score.QualityRelevance:0.###} (playability {score.DeckImpact.Playability:0.###}); effective {score.EffectiveQualityComponent:0.###}\n"
            + $"Quality {score.EffectiveQualityComponent:0.###}; Lane {score.LaneComponent:0.###}; Archetype {score.ArchetypeComponent:0.###}; Deck Need {score.DeckNeedComponent:0.###}\n"
            + $"Weights Q {score.Weights.Quality:P0}, L {score.Weights.Lane:P0}, A {score.Weights.Archetype:P0}, D {score.Weights.DeckNeed:P0}\n");
        text += "Contributions from neutral 25 (before clamp/rounding):\n" + string.Join("\n", score.Contributions.Select(c =>
            string.Create(CultureInfo.InvariantCulture, $"{c.Component}: {c.PointsFromNeutral:+0.00;-0.00;0.00} points")));
        text += string.Create(CultureInfo.InvariantCulture,
            $"\nDeck: available {score.DeckImpact.IsAvailable}; included {score.DeckImpact.MakesProjectedDeck}; replacement {score.DeckImpact.ReplacesCard?.Value ?? "none"}; "
            + $"quality delta {score.DeckImpact.CommonDeckQualityDelta:+0.0000;-0.0000;0.0000}; creatures {score.DeckImpact.CreatureCountDelta}; early {score.DeckImpact.EarlyPlayDelta}; "
            + $"high {score.DeckImpact.HighCostDelta}; mana {score.DeckImpact.ManaBaseDelta:0.###}; pair {score.DeckImpact.BeforePair?.Code ?? "none"} -> {score.DeckImpact.AfterPair?.Code ?? "none"}; "
            + $"pair changed {score.DeckImpact.ColorPairChanged}; relaxation changed {score.DeckImpact.ConstraintRelaxationChanged}; incomplete shell {score.DeckImpact.IsIncompleteProjection}\n"
            + $"Deck fit: cut-line margin {(score.DeckImpact.CutlineMargin is { } margin ? (margin * 100).ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) + " pp" : "n/a")}; "
            + $"graded membership {score.DeckImpact.MembershipValue:0.###}; real cut line {score.DeckImpact.RealCutline:0.0000}; virtual cut line {score.DeckImpact.ReplacementLevel:0.0000}"
            + $"{(score.DeckImpact.ComparedWithFutureFiller ? " (used)" : "")}; best-pair weight {score.DeckImpact.BestPairWeight:0.###} of {score.DeckImpact.ProjectedPairCount} viable pair(s)\n"
            + $"Future supply: open slots {score.DeckImpact.OpenSlots}; contested slots {score.DeckImpact.ContestedSlots}; remaining picks {score.DeckImpact.RemainingPicks}; "
            + $"expected playables {score.DeckImpact.ExpectedSupply:0.#}; coverage {score.DeckImpact.SupplyCoverage:0.##}; urgency {score.DeckImpact.SupplyUrgency:0.##}; "
            + $"structure gate {score.DeckImpact.StructureGate:0.###}; structural need {score.DeckImpact.StructuralGain:+0.###;-0.###;0} at confidence {score.DeckImpact.StructureConfidence:0.##}; D {score.DeckNeedComponent:0.###}");
        if (score.DeckImpact.SupplyProfile is { } profile) text += "\nSupply profile: " + profile;
        return text + "\n" + Evidence(score, statistics) + $"\nRoles: {score.Roles.Roles}; {score.Roles.Source}\nReasons:\n" + string.Join("\n", score.Reasons);
    }
}
