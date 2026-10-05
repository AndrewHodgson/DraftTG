namespace DraftTG.RecommendationEngine;

/// <summary>Conservative, tunable Phase 9B heuristics, not statistically optimal estimates.</summary>
public sealed record LaneDetectionConfiguration
{
    public LaneDetectionConfiguration(double lateArrivalScale = 3, double laneStrengthScale = 0.04,
        int laneSignalStartPick = 4, int laneSignalFullPick = 8, double currentPackEvidenceWeight = 1,
        double previousPackEvidenceWeight = 0.5, double twoPacksBackEvidenceWeight = 0.25,
        double laneEvidenceScale = 2, int fullLaneConfidenceAfterObservations = 4,
        double maxHumanLaneAdjustment = 0.020, double maxBotLaneAdjustment = 0.010)
    {
        Positive(lateArrivalScale, nameof(lateArrivalScale));
        Positive(laneStrengthScale, nameof(laneStrengthScale));
        Positive(laneEvidenceScale, nameof(laneEvidenceScale));
        if (laneSignalStartPick < 1) throw new ArgumentOutOfRangeException(nameof(laneSignalStartPick));
        if (laneSignalFullPick < laneSignalStartPick) throw new ArgumentOutOfRangeException(nameof(laneSignalFullPick));
        if (fullLaneConfidenceAfterObservations < 1) throw new ArgumentOutOfRangeException(nameof(fullLaneConfidenceAfterObservations));
        Unit(currentPackEvidenceWeight, nameof(currentPackEvidenceWeight));
        Unit(previousPackEvidenceWeight, nameof(previousPackEvidenceWeight));
        Unit(twoPacksBackEvidenceWeight, nameof(twoPacksBackEvidenceWeight));
        Unit(maxHumanLaneAdjustment, nameof(maxHumanLaneAdjustment));
        Unit(maxBotLaneAdjustment, nameof(maxBotLaneAdjustment));
        LateArrivalScale = lateArrivalScale;
        LaneStrengthScale = laneStrengthScale;
        LaneSignalStartPick = laneSignalStartPick;
        LaneSignalFullPick = laneSignalFullPick;
        CurrentPackEvidenceWeight = currentPackEvidenceWeight;
        PreviousPackEvidenceWeight = previousPackEvidenceWeight;
        TwoPacksBackEvidenceWeight = twoPacksBackEvidenceWeight;
        LaneEvidenceScale = laneEvidenceScale;
        FullLaneConfidenceAfterObservations = fullLaneConfidenceAfterObservations;
        MaxHumanLaneAdjustment = maxHumanLaneAdjustment;
        MaxBotLaneAdjustment = maxBotLaneAdjustment;
    }

    public double LateArrivalScale { get; }
    public double LaneStrengthScale { get; }
    public int LaneSignalStartPick { get; }
    public int LaneSignalFullPick { get; }
    public double CurrentPackEvidenceWeight { get; }
    public double PreviousPackEvidenceWeight { get; }
    public double TwoPacksBackEvidenceWeight { get; }
    public double LaneEvidenceScale { get; }
    public int FullLaneConfidenceAfterObservations { get; }
    public double MaxHumanLaneAdjustment { get; }
    public double MaxBotLaneAdjustment { get; }
    public string ModelVersion => "late-color-lane-v1";

    // Inclusive ramp: pick 4 is weak (1/5), pick 8 is full, earlier picks are zero.
    public double PositionWeight(int pick) => Math.Clamp(
        (double)(pick - LaneSignalStartPick + 1) / (LaneSignalFullPick - LaneSignalStartPick + 1), 0, 1);
    public double PackWeight(int distance) => distance switch
    { 0 => CurrentPackEvidenceWeight, 1 => PreviousPackEvidenceWeight, 2 => TwoPacksBackEvidenceWeight, _ => 0 };
    public double MaximumAdjustment(LimitedStatisticsFormat? format) => format switch
    {
        LimitedStatisticsFormat.PremierDraft or LimitedStatisticsFormat.TraditionalDraft => MaxHumanLaneAdjustment,
        LimitedStatisticsFormat.QuickDraft => MaxBotLaneAdjustment,
        _ => 0
    };

    private static void Positive(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0) throw new ArgumentOutOfRangeException(name);
    }
    private static void Unit(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0 || value > 1) throw new ArgumentOutOfRangeException(name);
    }
}
