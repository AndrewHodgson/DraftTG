using DraftTG.Domain;

namespace DraftTG.RecommendationEngine;

/// <summary>Explicit Phase 9C product heuristics; no statistical optimality claim.</summary>
public sealed record ArchetypeConfiguration
{
    public ArchetypeConfiguration(double minimumArchetypeConfidence = .35, double minimumArchetypeLead = .10,
        int pairPriorEquivalentGames = 300, double archetypeLiftScale = .04, double maxArchetypeAdjustment = .015,
        int maxPairDatasetsPerDraft = 3)
    {
        if (!double.IsFinite(minimumArchetypeConfidence) || minimumArchetypeConfidence is < 0 or > 1
            || !double.IsFinite(minimumArchetypeLead) || minimumArchetypeLead is < 0 or > 1
            || pairPriorEquivalentGames <= 0 || !double.IsFinite(archetypeLiftScale) || archetypeLiftScale <= 0
            || !double.IsFinite(maxArchetypeAdjustment) || maxArchetypeAdjustment is < 0 or > 1 || maxPairDatasetsPerDraft is < 0 or > 10)
            throw new ArgumentOutOfRangeException(nameof(minimumArchetypeConfidence), "Invalid archetype model configuration.");
        MinimumArchetypeConfidence = minimumArchetypeConfidence; MinimumArchetypeLead = minimumArchetypeLead;
        PairPriorEquivalentGames = pairPriorEquivalentGames; ArchetypeLiftScale = archetypeLiftScale;
        MaxArchetypeAdjustment = maxArchetypeAdjustment; MaxPairDatasetsPerDraft = maxPairDatasetsPerDraft;
    }
    public double MinimumArchetypeConfidence { get; }
    public double MinimumArchetypeLead { get; }
    public int PairPriorEquivalentGames { get; }
    public double ArchetypeLiftScale { get; }
    public double MaxArchetypeAdjustment { get; }
    public int MaxPairDatasetsPerDraft { get; }
    public string ModelVersion => "pair-gih-affinity-v1";
}

public sealed record ArchetypePairEvidence(ArchetypeDefinition Definition, double Coverage, double Balance,
    double PairPoolFit, double ProgressFactor, double Confidence);

public sealed class ArchetypeContextProfile
{
    private ArchetypeContextProfile(SetArchetypeProfile? set, ColorCommitmentProfile pool, ArchetypeConfiguration configuration)
    {
        Set = set; Configuration = configuration;
        var total = pool.Colors.Sum(c => c.Evidence);
        Candidates = Array.AsReadOnly((set?.Archetypes ?? []).Select(definition =>
        {
            var evidence = definition.Pair.Colors.Colors.Select(c => pool.For(c).Evidence).ToArray();
            var sum = evidence.Sum();
            var coverage = total > 0 ? sum / total : 0;
            var balance = sum > 0 ? 2 * evidence.Min() / sum : 0;
            var fit = coverage * (.75 + .25 * balance);
            return new ArchetypePairEvidence(definition, coverage, balance, fit, pool.ProgressFactor,
                Math.Clamp(fit * pool.ProgressFactor, 0, 1));
        }).OrderByDescending(p => p.Confidence).ThenBy(p => p.Definition.Pair.Code, StringComparer.Ordinal).ToArray());
        var first = Candidates.FirstOrDefault();
        Lead = (first?.Confidence ?? 0) - (Candidates.Skip(1).FirstOrDefault()?.Confidence ?? 0);
        if (first is not null && first.Confidence > 0 && first.Confidence >= configuration.MinimumArchetypeConfidence
            && Lead >= configuration.MinimumArchetypeLead) Active = first;
    }
    public static ArchetypeContextProfile Analyze(SetArchetypeProfile? set, ColorCommitmentProfile pool,
        ArchetypeConfiguration? configuration = null) => new(set, pool, configuration ?? new());
    public SetArchetypeProfile? Set { get; }
    public ArchetypeConfiguration Configuration { get; }
    public IReadOnlyList<ArchetypePairEvidence> Candidates { get; }
    public ArchetypePairEvidence? Active { get; }
    public double Lead { get; }
}

public sealed record PairGihStatistics(double? WinRate, int? SampleCount);
public sealed class ArchetypePairStatistics
{
    public ArchetypePairStatistics(LimitedStatisticsContext context, ArchetypeColorPair pair,
        LimitedCardStatisticsCatalog catalog, IEnumerable<PairGihStatistics> environment)
    { Context = context; Pair = pair; Catalog = catalog; Baseline = ArchetypeMath.Baseline(environment); }
    public LimitedStatisticsContext Context { get; }
    public ArchetypeColorPair Pair { get; }
    public LimitedCardStatisticsCatalog Catalog { get; }
    public double? Baseline { get; }
}

public static class ArchetypeMath
{
    public static bool Valid(PairGihStatistics statistics) => statistics.SampleCount is > 0
        && statistics.WinRate is { } rate && double.IsFinite(rate) && rate is >= 0 and <= 1;
    public static double? Baseline(IEnumerable<PairGihStatistics> environment)
    {
        var valid = environment.Where(Valid).ToArray();
        var total = valid.Sum(s => (double)s.SampleCount!.Value);
        return total > 0 ? valid.Sum(s => s.WinRate!.Value * s.SampleCount!.Value) / total : null;
    }
    public static double Shrink(double overall, double pairRate, int pairSample, ArchetypeConfiguration configuration) =>
        (pairSample * pairRate + configuration.PairPriorEquivalentGames * overall) / (pairSample + (double)configuration.PairPriorEquivalentGames);
    public static double Lift(double overall, double overallBaseline, double pairAdjusted, double pairBaseline) =>
        (pairAdjusted - pairBaseline) - (overall - overallBaseline);
    public static double Normalize(double lift, ArchetypeConfiguration configuration) => Math.Clamp(lift / configuration.ArchetypeLiftScale, -1, 1);
    public static double Adjustment(double affinity, double confidence, ArchetypeConfiguration configuration) =>
        affinity * confidence * configuration.MaxArchetypeAdjustment;
}

public sealed record ArchetypeCardAffinity(bool Eligible, string Diagnostic, double? PairRawGih = null, int? PairSample = null,
    double? PairAdjusted = null, double? Lift = null, double? NormalizedAffinity = null, double Adjustment = 0);
public sealed record ArchetypeCardRecommendation(LaneCardRecommendation LaneRecommendation, ArchetypeCardAffinity Affinity,
    double? ContextualValue, int? ContextualRank, bool IsTopContextualCandidate)
{
    public int PackIndex => LaneRecommendation.PackIndex;
    public CardIdentifier CardIdentifier => LaneRecommendation.CardIdentifier;
    public CardRecommendation StatisticalRecommendation => LaneRecommendation.StatisticalRecommendation;
}
public sealed class ArchetypeRecommendationResult
{
    internal ArchetypeRecommendationResult(LaneDraftRecommendation lane, ArchetypeContextProfile profile,
        ArchetypePairStatistics? statistics, IEnumerable<ArchetypeCardRecommendation> cards)
    { LaneRecommendation = lane; Profile = profile; PairStatistics = statistics; Cards = Array.AsReadOnly(cards.ToArray()); }
    public LaneDraftRecommendation LaneRecommendation { get; }
    public ArchetypeContextProfile Profile { get; }
    public ArchetypePairStatistics? PairStatistics { get; }
    public IReadOnlyList<ArchetypeCardRecommendation> Cards { get; }
    public int? TopRecommendedPackIndex => Cards.FirstOrDefault(c => c.IsTopContextualCandidate)?.PackIndex;
}

public sealed class ArchetypeRecommendationEngine(ArchetypeConfiguration? configuration = null)
{
    private readonly ArchetypeConfiguration _configuration = configuration ?? new();
    public ArchetypeRecommendationResult Recommend(LaneDraftRecommendation lane, SetArchetypeProfile? set,
        LimitedStatisticsContext context, ArchetypePairStatistics? pairStatistics = null)
    {
        if (set?.SetCode != context.Expansion) set = null;
        var profile = ArchetypeContextProfile.Analyze(set, lane.PoolRecommendation.Profile, _configuration);
        var data = profile.Active is { } active && pairStatistics?.Pair == active.Definition.Pair
            && pairStatistics.Context == context ? pairStatistics : null;
        var cards = lane.Cards.Select(card =>
        {
            var eligible = profile.Active?.Definition.Pair.Includes(card.PoolRecommendation.Colors ?? ColorSet.Colorless) == true;
            var affinity = new ArchetypeCardAffinity(eligible, profile.Active is null ? "Archetype unsettled or set profile unavailable."
                : !eligible ? "Off-pair, colorless, multicolor or unknown metadata; no archetype adjustment." : "Pair GIH unavailable.");
            var row = data?.Catalog.StatisticsFor(card.CardIdentifier);
            if (eligible && row is not null)
                affinity = affinity with { PairRawGih = row.GameInHandWinRate, PairSample = row.GameInHandGameCount };
            if (eligible && card.StatisticalRecommendation.AdjustedValue is { } overall
                && lane.StatisticalRecommendation.EnvironmentBaseline is { } overallBaseline && data?.Baseline is { } pairBaseline
                && row is not null && ArchetypeMath.Valid(new(row.GameInHandWinRate, row.GameInHandGameCount)))
            {
                var adjusted = ArchetypeMath.Shrink(overall, row.GameInHandWinRate!.Value, row.GameInHandGameCount!.Value, _configuration);
                var lift = ArchetypeMath.Lift(overall, overallBaseline, adjusted, pairBaseline);
                var normalized = ArchetypeMath.Normalize(lift, _configuration);
                affinity = new(true, "Empirical pair GIH affinity.", row.GameInHandWinRate, row.GameInHandGameCount, adjusted, lift,
                    normalized, ArchetypeMath.Adjustment(normalized, profile.Active!.Confidence, _configuration));
            }
            else if (card.StatisticalRecommendation.AdjustedValue is null) affinity = affinity with { Diagnostic = "Phase 8 unscored; no archetype score." };
            else if (eligible && data is not null && data.Baseline is null)
                affinity = affinity with { Diagnostic = "Pair baseline unavailable; no archetype adjustment." };
            else if (eligible && lane.StatisticalRecommendation.EnvironmentBaseline is null)
                affinity = affinity with { Diagnostic = "Overall baseline unavailable; no archetype adjustment." };
            return new ArchetypeCardRecommendation(card, affinity,
                card.ContextualValue is { } value ? Math.Clamp(value + affinity.Adjustment, 0, 1) : null, null, false);
        }).ToArray();
        var ranked = cards.Where(c => c.ContextualValue is not null).OrderByDescending(c => c.ContextualValue)
            .ThenByDescending(c => c.LaneRecommendation.ContextualValue)
            .ThenByDescending(c => c.LaneRecommendation.PoolRecommendation.ContextualValue)
            .ThenByDescending(c => c.StatisticalRecommendation.AdjustedValue)
            .ThenByDescending(c => c.StatisticalRecommendation.GamesInHandSampleCount).ThenBy(c => c.PackIndex).ToArray();
        if (ranked.Length >= 2)
            for (var i = 0; i < ranked.Length; i++) cards[ranked[i].PackIndex] = ranked[i] with { ContextualRank = i + 1, IsTopContextualCandidate = i == 0 };
        return new(lane, profile, data, cards);
    }
}
