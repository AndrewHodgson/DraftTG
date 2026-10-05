using DraftTG.Domain;

namespace DraftTG.RecommendationEngine;

public sealed record LaneCardRecommendation(ContextualCardRecommendation PoolRecommendation,
    double LaneFit, double LaneAdjustment, double? ContextualValue, int? ContextualRank, bool IsTopContextualCandidate)
{
    public int PackIndex => PoolRecommendation.PackIndex;
    public CardIdentifier CardIdentifier => PoolRecommendation.CardIdentifier;
    public CardRecommendation StatisticalRecommendation => PoolRecommendation.StatisticalRecommendation;
}

public sealed class LaneDraftRecommendation
{
    internal LaneDraftRecommendation(ContextualDraftRecommendation pool, LaneOpennessProfile profile,
        DraftPackObservationHistory observations, LimitedStatisticsFormat? format, IEnumerable<LaneCardRecommendation> cards)
    {
        PoolRecommendation = pool;
        Profile = profile;
        ObservationHistory = observations;
        Format = format;
        Cards = Array.AsReadOnly(cards.ToArray());
    }
    public ContextualDraftRecommendation PoolRecommendation { get; }
    public DraftRecommendation StatisticalRecommendation => PoolRecommendation.StatisticalRecommendation;
    public LaneOpennessProfile Profile { get; }
    public DraftPackObservationHistory ObservationHistory { get; }
    public LimitedStatisticsFormat? Format { get; }
    public double MaximumLaneAdjustment => Profile.Configuration.MaximumAdjustment(Format);
    public IReadOnlyList<LaneCardRecommendation> Cards { get; }
    public int? TopRecommendedPackIndex => Cards.FirstOrDefault(c => c.IsTopContextualCandidate)?.PackIndex;
}

/// <summary>Adds availability inference to the unchanged pool stage using only Phase 8 observation inputs.</summary>
public sealed class LaneContextualRecommendationEngine(LaneDetectionConfiguration? configuration = null)
{
    private readonly LaneDetectionConfiguration _configuration = configuration ?? new();
    public LaneDraftRecommendation Recommend(DraftSnapshot snapshot, ContextualDraftRecommendation pool,
        DraftPackObservationHistory observations, LimitedStatisticsFormat? format)
    {
        if (!snapshot.CurrentPack.AvailableCardIdentifiers.SequenceEqual(pool.Cards.Select(c => c.CardIdentifier))
            || pool.Cards.Where((c, i) => c.PackIndex != i).Any())
            throw new ArgumentException("The pool recommendation must match the current ordered pack.", nameof(pool));
        var profile = LaneOpennessProfile.Analyze(observations, snapshot.CurrentPack.Position, _configuration);
        var maximum = _configuration.MaximumAdjustment(format);
        var cards = pool.Cards.Select(card =>
        {
            var fit = card.Colors is { } colors ? profile.Fit(colors) : 0;
            var adjustment = fit * maximum;
            return new LaneCardRecommendation(card, fit, adjustment,
                card.ContextualValue is { } value ? Math.Clamp(value + adjustment, 0, 1) : null, null, false);
        }).ToArray();
        var ranked = cards.Where(c => c.ContextualValue is not null)
            .OrderByDescending(c => c.ContextualValue)
            .ThenByDescending(c => c.PoolRecommendation.ContextualValue)
            .ThenByDescending(c => c.StatisticalRecommendation.AdjustedValue)
            .ThenByDescending(c => c.StatisticalRecommendation.GamesInHandSampleCount)
            .ThenBy(c => c.PackIndex).ToArray();
        if (ranked.Length >= 2)
            for (var i = 0; i < ranked.Length; i++)
                cards[ranked[i].PackIndex] = ranked[i] with { ContextualRank = i + 1, IsTopContextualCandidate = i == 0 };
        return new(pool, profile, observations, format, cards);
    }
}
