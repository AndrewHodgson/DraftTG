using DraftTG.Domain;

namespace DraftTG.RecommendationEngine;

public enum PickScoreAvailability { Measured, EstimatedMissingStatistics, NoEnvironmentBaseline }
/// <summary>Evidence behind the intrinsic quality component Q only. Lane, archetype and deck fit always use real draft
/// context, so an estimated Q never means the whole score lacks evidence. Unspecified is only for hand-built fixtures.</summary>
public enum PickScoreQualityEvidence
{
    Unspecified,
    /// <summary>A valid GIH rate with a positive sample from this card's own row in the selected set/format.</summary>
    DirectCardStatistics,
    /// <summary>No provider row was resolved for this exact card (absent, conflicting or ambiguous).</summary>
    NoProviderRow,
    /// <summary>The card's row exists but has no usable GIH rate or sample (other fields such as ALSA may exist).</summary>
    ProviderRowWithoutUsableGih,
    NoEnvironmentBaseline
}
public sealed record PickScoreWeights(double Quality, double Lane, double Archetype, double DeckNeed)
{
    internal static PickScoreWeights Blend(PickScoreWeights a, PickScoreWeights b, double t)
    {
        t = Math.Clamp(t, 0, 1); t = t * t * (3 - 2 * t);
        return new(a.Quality + (b.Quality - a.Quality) * t, a.Lane + (b.Lane - a.Lane) * t,
            a.Archetype + (b.Archetype - a.Archetype) * t, a.DeckNeed + (b.DeckNeed - a.DeckNeed) * t);
    }
}

/// <summary>Phase 7: the future-pick supply calibration behind the expected-future cut line, with explicit provenance.
/// Only FRA is calibrated (best red-green card a drafter sees per pick, `fraprobe supply`, 2,000 packs per pick:
/// baseline +0.9 pp at pick 1, −0.6 at 5, −1.5 at 9, −2.8 at 13; a playable at baseline −4 pp or better at
/// 99/97/88/77/54% of picks 1/5/9/11/13). Other sets use the same values as a provisional default, labelled as such.</summary>
public sealed record FuturePickSupplyProfile(string Name, string Provenance, bool IsSetCalibrated,
    double ExpectedPlayableShare, double NormalFillerOffset, double SelectiveSupplyCoverage, double SelectiveFillerOffset,
    double DeepSupplyCoverage, double DeepFillerOffset, double EmergencyFillerOffset)
{
    public static FuturePickSupplyProfile Fra { get; } = new("FRA", "calibrated from FRA PremierDraft red-green card availability", true,
        .8, .03, 3, .01, 10, -.008, .10);
    /// <summary>FRA-derived values for sets without their own calibration; scores stay comparable, provenance says so.</summary>
    public static FuturePickSupplyProfile ProvisionalDefault { get; } = Fra with
    {
        Name = "provisional-default", Provenance = "provisional: FRA-derived values, not calibrated for this set", IsSetCalibrated = false
    };
    public static FuturePickSupplyProfile For(string? expansion) =>
        string.Equals(expansion, "FRA", StringComparison.OrdinalIgnoreCase) ? Fra : ProvisionalDefault;
    internal bool IsValid => double.IsFinite(ExpectedPlayableShare) && ExpectedPlayableShare is > 0 and <= 1
        && SelectiveSupplyCoverage > 1 && DeepSupplyCoverage > SelectiveSupplyCoverage
        && EmergencyFillerOffset >= NormalFillerOffset && NormalFillerOffset >= SelectiveFillerOffset && SelectiveFillerOffset >= DeepFillerOffset;
}

/// <summary>Transparent V1 calibration choices, not win probabilities or learned parameters.</summary>
public sealed record ContextualPickScoreConfiguration
{
    public ContextualPickScoreConfiguration(double qualityRelativeSpan = .16, double calibrationGain = ContextualPickScoreCalibration.DefaultGain,
        double marginalQualityScale = .01, int expectedDraftPicks = 42, int picksPerPack = 14,
        int earlyAnchor = 4, int middleAnchor = 21, int lateAnchor = 35, FuturePickSupplyProfile? supplyProfile = null)
    {
        SupplyProfile = supplyProfile ?? FuturePickSupplyProfile.ProvisionalDefault;
        if (!SupplyProfile.IsValid) throw new ArgumentOutOfRangeException(nameof(supplyProfile), "Invalid future-pick supply profile.");
        if (!double.IsFinite(qualityRelativeSpan) || qualityRelativeSpan <= 0 || !double.IsFinite(calibrationGain)
            || calibrationGain <= 0 || !double.IsFinite(marginalQualityScale) || marginalQualityScale <= 0
            || expectedDraftPicks <= 0 || picksPerPack <= 0 || earlyAnchor < 0 || middleAnchor <= earlyAnchor
            || lateAnchor <= middleAnchor || lateAnchor > expectedDraftPicks)
            throw new ArgumentOutOfRangeException(nameof(qualityRelativeSpan), "Invalid contextual pick-score configuration.");
        QualityRelativeSpan = qualityRelativeSpan; CalibrationGain = calibrationGain; MarginalQualityScale = marginalQualityScale;
        ExpectedDraftPicks = expectedDraftPicks; PicksPerPack = picksPerPack;
        EarlyAnchor = earlyAnchor; MiddleAnchor = middleAnchor; LateAnchor = lateAnchor;
    }
    public string ModelVersion => "contextual-pick-score-v1.6";
    public double QualityRelativeSpan { get; }
    public double CalibrationGain { get; }
    /// <summary>Retained for configuration compatibility; phase 4 D no longer adds a separate average-quality gain
    /// (it double counted the cut-line margin). CommonDeckQualityDelta remains a diagnostic.</summary>
    public double MarginalQualityScale { get; }
    public int ExpectedDraftPicks { get; }
    public int PicksPerPack { get; }
    public int EarlyAnchor { get; }
    public int MiddleAnchor { get; }
    public int LateAnchor { get; }
    // ---- Deck need D (phase 4). D = clamp(F + StructureWeight·confidence·membership·S, 0, 1), neutral ≈ .55. ----
    /// <summary>F's floor: a card that cannot enter any realistic projected maindeck keeps only sideboard/future value.</summary>
    public double DeckFitFloor => .10;
    /// <summary>Second, wider logistic scale (4 pp) so F keeps rising with how weak a card it replaces.</summary>
    public double ReplacementScale => .04;
    /// <summary>Maximum structural adjustment of D (creature/early/removal need minus top-end redundancy).</summary>
    public double StructureWeight => .30;
    /// <summary>Projected eligible spells at which structural deficits get full confidence (shrinks early shells).</summary>
    public double StructureConfidenceSpells => 18;
    /// <summary>Conservative V1 healthy interaction count for a 23-spell deck (hard removal 1, conditional .5). Not data-derived.</summary>
    public double HealthyRemovalCount => 3;
    /// <summary>High-cost cards above (cap − 1) over this span reach full redundancy.</summary>
    public double TopEndRedundancySpan => 3;
    /// <summary>A land earns deck need only by reducing a measured colour-source shortfall: .55 plus up to .35. Otherwise the floor.</summary>
    public double LandFixingBase => .55;
    public double LandFixingWeight => .35;
    // Graded deck fit (stabilization phase 1). Rates are fractions: .01 = one GIH percentage point.
    /// <summary>Logistic width of the cut-line margin late; wider early because future picks still move the cut line.</summary>
    public double CutlineScaleLate => .01;
    public double CutlineScaleEarly => .02;
    // ---- Expected future replacement (phase 6). Later picks contest the marginal maindeck slot. The virtual cut line
    // is the expected marginal (23rd) card of the final deck without this candidate: an order-statistic approximation
    // over the contested slots (open slots plus maindeck spells below baseline − 3 pp) and the expected later supply.
    // The values come from the (phase 7) supply profile; see FuturePickSupplyProfile for evidence and provenance.
    public FuturePickSupplyProfile SupplyProfile { get; }
    /// <summary>Share of remaining picks a committed drafter turns into on-colour playables (FRA availability ≈ .8–.9).</summary>
    public double ExpectedPlayableShare => SupplyProfile.ExpectedPlayableShare;
    /// <summary>Coverage ≥ 3: later picks can be selective, so the marginal slot gets about a median later card.</summary>
    public double SelectiveSupplyCoverage => SupplyProfile.SelectiveSupplyCoverage;
    public double SelectiveFillerOffset => SupplyProfile.SelectiveFillerOffset;
    /// <summary>Coverage 1: every later playable is needed, so the marginal slot gets about a lower-quartile later card.
    /// Maindeck spells below baseline minus this offset are contested by later picks.</summary>
    public double NormalFillerOffset => SupplyProfile.NormalFillerOffset;
    /// <summary>An open slot that later picks cannot fill is played with emergency filler at baseline minus this offset.</summary>
    public double EmergencyFillerOffset => SupplyProfile.EmergencyFillerOffset;
    /// <summary>Coverage ≥ 10: the marginal slot gets about the best of many later picks (90th percentile of the best
    /// on-colour card per pick, about baseline + 0.8 pp in FRA).</summary>
    public double DeepSupplyCoverage => SupplyProfile.DeepSupplyCoverage;
    public double DeepFillerOffset => SupplyProfile.DeepFillerOffset;
    /// <summary>Quality of the later pick that takes the marginal contested slot when supply covers all of them:
    /// baseline − 3 pp at coverage 1, − 1 pp at coverage 3, + 0.8 pp at coverage ≥ 10, linear in between.</summary>
    public double FutureFillerLevel(double baseline, double coverage)
    {
        var c = Math.Clamp(double.IsFinite(coverage) ? coverage : DeepSupplyCoverage, 1, DeepSupplyCoverage);
        var offset = c <= SelectiveSupplyCoverage
            ? NormalFillerOffset + (SelectiveFillerOffset - NormalFillerOffset) * (c - 1) / (SelectiveSupplyCoverage - 1)
            : SelectiveFillerOffset + (DeepFillerOffset - SelectiveFillerOffset) * (c - SelectiveSupplyCoverage) / (DeepSupplyCoverage - SelectiveSupplyCoverage);
        return baseline - offset;
    }
    /// <summary>Virtual cut line. With supply S ≥ contested slots m, every contested slot gets a later pick and the
    /// marginal one is FutureFillerLevel(S/m). With S &lt; m, later picks (better than any contested card) take S
    /// slots and the final marginal card is the (m − S)-th best existing contested card, open slots counting as
    /// emergency filler. Fractional ranks interpolate, so the line is continuous and non-decreasing in S.</summary>
    public double FinalMarginalCard(double baseline, IReadOnlyCollection<double> weakMaindeckValues, int openSlots, double supply)
    {
        var contested = weakMaindeckValues.OrderByDescending(v => v)
            .Concat(Enumerable.Repeat(baseline - EmergencyFillerOffset, Math.Max(0, openSlots))).ToArray();
        var s = Math.Max(0, double.IsFinite(supply) ? supply : 0);
        if (s >= contested.Length) return FutureFillerLevel(baseline, s / Math.Max(1, contested.Length));
        var kept = contested.Length - s;
        var normal = baseline - NormalFillerOffset;
        // Fewer than one kept card: between the worst later pick used (normal level) and the best contested card.
        if (kept < 1) return Math.Min(normal, normal + (contested[0] - normal) * kept);
        var position = kept - 1;
        var low = (int)Math.Floor(position);
        var high = Math.Min(contested.Length - 1, low + 1);
        return Math.Min(normal, contested[low] + (contested[high] - contested[low]) * (position - low));
    }
    /// <summary>Diagnostic urgency in [0, 1]: 1 − coverage / selective coverage (0 when later picks can be selective).</summary>
    public double SupplyUrgency(double coverage) => Math.Clamp(1 - (double.IsFinite(coverage) ? coverage : SelectiveSupplyCoverage) / SelectiveSupplyCoverage, 0, 1);
    /// <summary>How far structural need is realised: logistic(margin / 2 pp) against the (real or virtual) cut line,
    /// wider than membership so a well-below-cut card keeps a little need credit and a strong one most of it.</summary>
    public double StructureGateScale => .02;
    // ---- Quality relevance (phase 6). Q stays the intrinsic quality; only its upside is scaled by R when composing. ----
    /// <summary>Plausibility of each viable projected pair (after this pick) from its average spell quality: 1 pp.</summary>
    public double PlayabilityTemperature => .01;
    /// <summary>A single off-pair colour with one pip earns this share of eligibility at full fixing support.</summary>
    public double SplashCredit => .6;
    /// <summary>Drafted lands producing the splash colour needed for full splash support.</summary>
    public double SplashSourcesForFullSupport => 3;
    /// <summary>R ramps from no reduction (≤ 10 completed picks, pivots still realistic) to full at the late anchor.</summary>
    public int QualityRelevanceStartPicks => 10;
    /// <summary>Minimum R for a card no realistic build can play: some hate-draft and pivot value remains.</summary>
    public double QualityRelevanceFloor => .20;
    /// <summary>R = 1 − ramp(picks)·(1 − P)·(1 − floor), ramp a smoothstep from the start picks to the late anchor.</summary>
    public double QualityRelevance(double playability, double progress)
    {
        var picks = Math.Clamp(double.IsFinite(progress) ? progress : 0, 0, 1) * ExpectedDraftPicks;
        var t = Math.Clamp((picks - QualityRelevanceStartPicks) / (LateAnchor - QualityRelevanceStartPicks), 0, 1);
        var ramp = t * t * (3 - 2 * t);
        var p = Math.Clamp(double.IsFinite(playability) ? playability : 1, 0, 1);
        return 1 - ramp * (1 - p) * (1 - QualityRelevanceFloor);
    }
    /// <summary>Only strength above neutral is scaled: an unplayable card's excellence is less actionable, but a
    /// weak card is never made to look better. Continuous and non-decreasing in Q for any R in (0, 1].</summary>
    public static double EffectiveQuality(double quality, double relevance) => quality > .5 ? .5 + Math.Clamp(relevance, 0, 1) * (quality - .5) : quality;
    /// <summary>Phase 7: pairs up to this many eligible spells short of the leading shell are still projected (at their own
    /// size) as near-viable alternatives, so a pair does not switch on or off at a one-card count threshold.</summary>
    public int ViabilityTolerance => 2;
    /// <summary>Share of a near-viable pair's blend weight it keeps: 1 − shortfall / (tolerance + 1), i.e. 1, 2/3, 1/3, then 0.</summary>
    public double ViabilityTaper(int shortfall) => Math.Clamp(1 - shortfall / (ViabilityTolerance + 1d), 0, 1);
    /// <summary>Softmax temperature over viable projected pairs' average spell quality.</summary>
    public double PairTemperature => .0025;
    public double CutlineScaleAt(double progress) =>
        CutlineScaleLate + (CutlineScaleEarly - CutlineScaleLate) * (1 - Math.Clamp(double.IsFinite(progress) ? progress : 0, 0, 1));
    public static double Logistic(double x) => 1 / (1 + Math.Exp(-x));
    public PickScoreWeights EarlyWeights { get; } = new(.70, .20, .10, 0);
    public PickScoreWeights MiddleWeights { get; } = new(.50, .20, .15, .15);
    public PickScoreWeights LateWeights { get; } = new(.30, .10, .20, .40);
    public PickScoreWeights WeightsAt(double progress)
    {
        var picks = Math.Clamp(double.IsFinite(progress) ? progress : 0, 0, 1) * ExpectedDraftPicks;
        return picks <= MiddleAnchor
            ? PickScoreWeights.Blend(EarlyWeights, MiddleWeights, (picks - EarlyAnchor) / (MiddleAnchor - EarlyAnchor))
            : PickScoreWeights.Blend(MiddleWeights, LateWeights, (picks - MiddleAnchor) / (LateAnchor - MiddleAnchor));
    }
    // Stabilization phase 2: hard clamps discarded every difference beyond their bounds (all cards ≥ baseline+8 pp
    // shared Q=1, and strong context pinned the final value at 1), so ranking fell through to sample size.
    // Soft limits keep the V1 linear map inside the knee and approach the bounds exponentially beyond it.
    /// <summary>Q stays linear within ±5.6 pp of baseline (.5 ± .35); stronger or weaker cards keep diminishing, nonzero differences.</summary>
    public double QualitySoftKnee => .35;
    /// <summary>The final value stays linear for displayed scores 2.5–47.5 (.5 ± .45); only the outermost points are compressed.</summary>
    public double CalibrationSoftKnee => ContextualPickScoreCalibration.SoftKnee;
    public double NormalizeQuality(double adjusted, double baseline) => SoftLimit(.5 + (adjusted - baseline) / QualityRelativeSpan, QualitySoftKnee);
    /// <summary>The single global final calibration (phase 5 kept v1.4 unchanged; see ContextualPickScoreCalibration).</summary>
    public double Calibrate(double weighted) => ContextualPickScoreCalibration.Calibrate(weighted, CalibrationGain);
    public static double SoftLimit(double value, double knee) => ContextualPickScoreCalibration.SoftLimit(value, knee);
}

public sealed record PickScoreContribution(string Component, double Value, double Weight, double PointsFromNeutral);
public sealed record CandidateDeckImpact(bool IsAvailable, bool MakesProjectedDeck, CardIdentifier? ReplacesCard,
    double? CommonDeckQualityDelta, int CreatureCountDelta, int EarlyPlayDelta, int HighCostDelta,
    double ManaBaseDelta, bool ColorPairChanged, bool ConstraintRelaxationChanged, bool IsIncompleteProjection,
    ArchetypeColorPair? BeforePair, ArchetypeColorPair? AfterPair, double StructuralGain, double DeckNeed,
    IReadOnlyList<string> Reasons)
{
    /// <summary>Best pair: selection value minus the projected cut line (rate units). Null off-pair, for lands or unavailable.</summary>
    public double? CutlineMargin { get; init; }
    /// <summary>Best pair: logistic(margin / scale), the graded replacement for binary membership.</summary>
    public double MembershipValue { get; init; }
    /// <summary>Phase 6 virtual cut line: expected quality of a later pick for the marginal maindeck slot.</summary>
    public double? ReplacementLevel { get; init; }
    /// <summary>Best pair: the optimizer's real comparison card value (null for an open slot).</summary>
    public double? RealCutline { get; init; }
    /// <summary>True when the margin is measured against the virtual cut line (open slot, or a weakest card later picks would replace).</summary>
    public bool ComparedWithFutureFiller { get; init; }
    public int OpenSlots { get; init; }
    /// <summary>Open slots plus maindeck spells below baseline − 3 pp: the slots later picks contest.</summary>
    public int ContestedSlots { get; init; }
    /// <summary>Expected later on-colour playables: remaining picks × playable share.</summary>
    public double ExpectedSupply { get; init; }
    public int RemainingPicks { get; init; }
    public double SupplyCoverage { get; init; }
    public double SupplyUrgency { get; init; }
    /// <summary>Best pair: logistic(margin / structure gate scale), the share of structural need realised.</summary>
    public double StructureGate { get; init; }
    /// <summary>Future-pick supply profile name and provenance (phase 7), e.g. FRA calibrated or provisional default.</summary>
    public string? SupplyProfile { get; init; }
    /// <summary>Plausibility-weighted share of realistic projected builds (after this pick) that can play the card, with
    /// splash credit; 1 when no projection exists.</summary>
    public double Playability { get; init; } = 1;
    /// <summary>Softmax weight of the best projected pair; DeckNeed blends all viable pairs by these weights.</summary>
    public double BestPairWeight { get; init; } = 1;
    public int ProjectedPairCount { get; init; }
    /// <summary>Best pair: the card this candidate would displace (included) or must beat (excluded).</summary>
    public CardIdentifier? DisplacedCard { get; init; }
    /// <summary>Projected deck before this pick (best pair): spells, creatures, early plays, MV5+ and weighted removal.</summary>
    public ProjectedDeckStructure? ProjectedStructure { get; init; }
    public double StructureConfidence { get; init; }
    public double CreatureNeed { get; init; }
    public double EarlyPlayNeed { get; init; }
    public double RemovalNeed { get; init; }
    public double TopEndRedundancy { get; init; }
}

/// <summary>Counts of a projected maindeck's nonland spells; removal is weighted (hard 1, conditional .5).</summary>
public sealed record ProjectedDeckStructure(int Spells, int Creatures, int EarlyPlays, int HighCost, double Removal);

public sealed record ContextualPickScore(int PackIndex, CardIdentifier CardIdentifier, double? ContextualValue,
    double? QualityComponent, double LaneComponent, double ArchetypeComponent, double DeckNeedComponent,
    double DraftProgress, double ColorFit, double? StatisticalDataWeight, PickScoreWeights Weights,
    PickScoreAvailability Availability, string ModelVersion, CandidateDeckImpact DeckImpact,
    LimitedCardRoleProfile Roles, IReadOnlyList<PickScoreContribution> Contributions, IReadOnlyList<string> Reasons)
{
    public int? Score0To50 => ContextualValue is { } value && double.IsFinite(value)
        ? ContextualPickScoreCalibration.DisplayScore(value) : null;
    /// <summary>Canonical visual quality tier of the displayed score (never printed rarity or evidence).</summary>
    public PickScoreTier Tier => PickScoreTierThresholds.Classify(Score0To50);
    public int? CurrentPackRank { get; init; }
    public bool IsContextPick { get; init; }
    public PickScoreQualityEvidence QualityEvidence { get; init; }
    /// <summary>Phase 6 quality relevance R in [floor, 1]; QualityComponent stays the intrinsic (raw) Q.</summary>
    public double QualityRelevance { get; init; } = 1;
    /// <summary>Q as composed: .5 + R·(Q − .5) above neutral, unchanged below.</summary>
    public double? EffectiveQualityComponent { get; init; }
    public bool IsQualityEstimated => Availability == PickScoreAvailability.EstimatedMissingStatistics;
}

public sealed record PickScoreGenerationMetrics(int OptimizerCalls, double ProjectedDeckMilliseconds, double TotalMilliseconds);
public sealed class ContextualPickScoreResult
{
    internal ContextualPickScoreResult(IEnumerable<ContextualPickScore> cards, ContextualPickScoreConfiguration configuration,
        PickScoreGenerationMetrics metrics)
    { Cards = Array.AsReadOnly(cards.ToArray()); Configuration = configuration; Metrics = metrics; }
    public IReadOnlyList<ContextualPickScore> Cards { get; }
    public ContextualPickScoreConfiguration Configuration { get; }
    public PickScoreGenerationMetrics Metrics { get; }
    public int? TopRecommendedPackIndex => Cards.FirstOrDefault(c => c.IsContextPick)?.PackIndex;
}
