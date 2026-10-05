using DraftTG.Domain;

namespace DraftTG.RecommendationEngine;

public sealed record LaneColorEvidence(MagicColor Color, double Evidence, double RawSupport, double EffectiveSupport);

public sealed record LaneCardSignal(DraftPosition Position, int PackIndex, CardIdentifier CardIdentifier,
    ColorSet Colors, double AverageLastSeenAt, double Phase8AdjustedValue, double EnvironmentBaseline,
    double LatenessWeight, double StrengthWeight, double PositionWeight, double OpenEvidence,
    double PackRecencyWeight)
{
    public double WeightedOpenEvidence => OpenEvidence * PackRecencyWeight;
}

/// <summary>Relative inference from positive late-card evidence; no absent-card or pool-count signal.</summary>
public sealed class LaneOpennessProfile
{
    private LaneOpennessProfile(DraftPackObservationHistory history, DraftPosition current,
        LaneDetectionConfiguration configuration)
    {
        Configuration = configuration;
        var evidence = new double[5];
        var signals = new List<LaneCardSignal>();
        var eligiblePositions = new HashSet<DraftPosition>();
        foreach (var observation in history.Observations)
        {
            var distance = current.Pack.Value - observation.Position.Pack.Value;
            var recency = configuration.PackWeight(distance);
            if (recency <= 0 || (distance == 0 && observation.Position.Pick.Value > current.Pick.Value)) continue;
            if (observation.EnvironmentBaseline is not { } baseline || !ValidRate(baseline)) continue;
            var pick = observation.Position.Pick.Value;
            var position = configuration.PositionWeight(pick);
            if (position <= 0) continue;
            foreach (var card in observation.Cards)
            {
                if (card.Colors is not { } colors || colors.IsColorless
                    || card.Phase8AdjustedValue is not { } statistical || !ValidRate(statistical)
                    || card.AverageLastSeenAt is not { } alsa || !double.IsFinite(alsa) || alsa <= 0) continue;
                var lateness = Math.Clamp((pick - alsa) / configuration.LateArrivalScale, 0, 1);
                var strength = Math.Clamp((statistical - baseline) / configuration.LaneStrengthScale, 0, 1);
                var open = lateness * strength * position;
                if (open <= 0) continue;
                var signal = new LaneCardSignal(observation.Position, card.PackIndex, card.CardIdentifier,
                    colors, alsa, statistical, baseline, lateness, strength, position, open, recency);
                signals.Add(signal);
                eligiblePositions.Add(observation.Position);
                foreach (var color in colors.Colors) evidence[(int)color] += signal.WeightedOpenEvidence / colors.Count;
            }
        }
        Signals = Array.AsReadOnly(signals.ToArray());
        EligibleObservationCount = eligiblePositions.Count;
        ObservationConfidence = Math.Clamp((double)EligibleObservationCount / configuration.FullLaneConfidenceAfterObservations, 0, 1);
        MeanEvidence = evidence.Sum() / 5;
        Colors = Array.AsReadOnly(Enum.GetValues<MagicColor>().Select(color =>
        {
            var raw = Math.Clamp((evidence[(int)color] - MeanEvidence) / configuration.LaneEvidenceScale, -1, 1);
            return new LaneColorEvidence(color, evidence[(int)color], raw, raw * ObservationConfidence);
        }).ToArray());
    }

    public static LaneOpennessProfile Analyze(DraftPackObservationHistory history, DraftPosition current,
        LaneDetectionConfiguration? configuration = null) => new(history, current, configuration ?? new());
    public LaneDetectionConfiguration Configuration { get; }
    public IReadOnlyList<LaneCardSignal> Signals { get; }
    public IReadOnlyList<LaneColorEvidence> Colors { get; }
    public int EligibleObservationCount { get; }
    public double ObservationConfidence { get; }
    public double MeanEvidence { get; }
    public LaneColorEvidence For(MagicColor color) => Colors[(int)color];
    public double Fit(ColorSet colors) => colors.IsColorless ? 0 : colors.Colors.Min(c => For(c).EffectiveSupport);
    private static bool ValidRate(double value) => double.IsFinite(value) && value >= 0 && value <= 1;
}
