namespace DraftTG.RecommendationEngine;

/// <summary>A qualitative calibration target for the displayed 0–50 Pick Score (phase 5). Anchors describe what the
/// scale should mean; they are validated by scenario tests and the offline replay, never fitted.</summary>
public sealed record PickScoreSemanticAnchor(string Key, string Situation, int ExpectedLow, int ExpectedHigh);

/// <summary>The single global map from the weighted contextual value w = Σ wᵢ·cᵢ to the displayed score (phase 5).
/// There is no per-pack, per-stage or per-rarity scale: draft stage acts only through the component weights, so a
/// given score means the same thing at P1P1 and P3P10. The map is strictly increasing, so it never reorders cards;
/// the unrounded value stays the ranking authority and rounding is monotone (a higher-ranked card never shows a
/// lower integer).</summary>
public static class ContextualPickScoreCalibration
{
    public const int MaximumScore = 50;
    /// <summary>Linear gain around the neutral value .5: V = .5 + 1.4·(w − .5) before the soft limit (v1.0, kept in phase 5).</summary>
    public const double DefaultGain = 1.4;
    /// <summary>V stays linear for exact scores 2.5–47.5 (.5 ± .45); beyond that it approaches 0 or 50 exponentially
    /// (phase 2). 50 needs an exceptional card and strong context; distinct values never tie before rounding.</summary>
    public const double SoftKnee = .45;

    public static double Calibrate(double weighted, double gain = DefaultGain) => SoftLimit(.5 + gain * (weighted - .5), SoftKnee);
    /// <summary>Unrounded 0–50 score of a calibrated value.</summary>
    public static double ExactScore(double calibrated) => MaximumScore * Math.Clamp(calibrated, 0, 1);
    /// <summary>Displayed integer: round half away from zero (monotone, so it can tie but never reverse two cards).</summary>
    public static int DisplayScore(double calibrated) => (int)Math.Round(ExactScore(calibrated), MidpointRounding.AwayFromZero);

    /// <summary>Identity within `knee` of .5; beyond it, approaches 0 or 1 with matching slope, so the map is
    /// continuous, strictly increasing and bounded in (0, 1). Non-finite input is clamped.</summary>
    public static double SoftLimit(double value, double knee)
    {
        if (!double.IsFinite(value)) return double.IsNaN(value) ? .5 : value > 0 ? 1 : 0;
        var offset = value - .5;
        var distance = Math.Abs(offset);
        if (distance <= knee) return value;
        var headroom = .5 - knee;
        return .5 + Math.CopySign(knee + headroom * (1 - Math.Exp(-(distance - knee) / headroom)), offset);
    }

    /// <summary>What the scale should express (phase 5). Ranges are deliberately broad. Anchor I was a v1.4 gap
    /// (about 24) closed in phase 6 by playability-gated quality (see CONTEXTUAL_PICK_SCORE_PHASE6_REPORT.md).</summary>
    public static IReadOnlyList<PickScoreSemanticAnchor> Anchors { get; } =
    [
        new("A", "Average card at P1P1", 20, 26),
        new("B", "Clearly above-average card at P1P1 (+3 pp GIH)", 30, 36),
        new("C", "True statistical bomb at P1P1 (+10–13 pp GIH)", 43, 49),
        new("D", "Exceptional bomb with positive context", 45, 50),
        new("E", "Solid on-colour playable late that makes the deck", 28, 35),
        new("F", "Strong on-colour card that improves the final deck late", 35, 43),
        new("G", "Average card that fills a real structural need late (above neutral, below premium)", 27, 39),
        new("H", "Excellent off-colour card early in pack 1 (stays competitive)", 40, 50),
        new("I", "Excellent off-colour card late in pack 3 with no realistic deck path", 10, 20),
        new("J", "Card that does not make the projected deck late (below one that does)", 0, 24),
    ];
}

/// <summary>Canonical visual quality tier of a displayed Pick Score. Printed rarity never participates.</summary>
public enum PickScoreTier { Unavailable, VeryWeak, Marginal, Solid, Strong, Excellent, Premium }

/// <summary>Tier thresholds aligned with the 0–50 score semantics (phase 5, tier scheme v2):
/// 0–14 very weak (irrelevant / very unlikely to improve the deck), 15–24 marginal (weak / marginal),
/// 25–34 solid (solid playable / clearly desirable), 35–39 strong, 40–44 excellent, 45–50 premium.
/// The badge palette consumes this classification; it never re-derives bands.</summary>
public static class PickScoreTierThresholds
{
    public const string Version = "pick-score-tiers-v2";
    public const int Marginal = 15, Solid = 25, Strong = 35, Excellent = 40, Premium = 45;

    public static PickScoreTier Classify(int? score) => score switch
    {
        null or < 0 or > ContextualPickScoreCalibration.MaximumScore => PickScoreTier.Unavailable,
        >= Premium => PickScoreTier.Premium,
        >= Excellent => PickScoreTier.Excellent,
        >= Strong => PickScoreTier.Strong,
        >= Solid => PickScoreTier.Solid,
        >= Marginal => PickScoreTier.Marginal,
        _ => PickScoreTier.VeryWeak
    };

    /// <summary>Inclusive score range of a tier; null for Unavailable.</summary>
    public static (int Low, int High)? Range(PickScoreTier tier) => tier switch
    {
        PickScoreTier.VeryWeak => (0, Marginal - 1),
        PickScoreTier.Marginal => (Marginal, Solid - 1),
        PickScoreTier.Solid => (Solid, Strong - 1),
        PickScoreTier.Strong => (Strong, Excellent - 1),
        PickScoreTier.Excellent => (Excellent, Premium - 1),
        PickScoreTier.Premium => (Premium, ContextualPickScoreCalibration.MaximumScore),
        _ => null
    };

    public static string Describe(PickScoreTier tier) => tier switch
    {
        PickScoreTier.VeryWeak => "very weak", PickScoreTier.Marginal => "marginal", PickScoreTier.Solid => "solid",
        PickScoreTier.Strong => "strong", PickScoreTier.Excellent => "excellent", PickScoreTier.Premium => "premium",
        _ => "unavailable"
    };
}
