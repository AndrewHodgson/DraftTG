using System.Collections.Frozen;
using DraftTG.Domain;

namespace DraftTG.RecommendationEngine;

public enum LimitedStatisticsFormat { PremierDraft, TraditionalDraft, QuickDraft }

public sealed record LimitedStatisticsContext(string Expansion, LimitedStatisticsFormat Format);

/// <summary>Rates are fractions, not percentages. Improvement is a signed rate difference.
/// GameInHandGameCount is the sample for GameInHandWinRate; null means unknown.</summary>
public sealed record LimitedCardStatistics(
    CardIdentifier CardIdentifier,
    int? GameInHandGameCount = null,
    double? PlayRate = null,
    double? GameInHandWinRate = null,
    double? OpeningHandWinRate = null,
    double? DrawnWinRate = null,
    double? DrawnImprovementWinRate = null,
    double? AverageLastSeenAt = null,
    double? AverageTakenAt = null);

public sealed class LimitedCardStatisticsCatalog
{
    private readonly FrozenDictionary<CardIdentifier, LimitedCardStatistics> _statistics;

    public LimitedCardStatisticsCatalog(IEnumerable<LimitedCardStatistics>? statistics = null)
    {
        // ToDictionary deliberately rejects duplicate identities before freezing the copy.
        _statistics = (statistics ?? []).ToDictionary(row => row.CardIdentifier).ToFrozenDictionary();
    }

    public int Count => _statistics.Count;
    public LimitedCardStatistics? StatisticsFor(CardIdentifier identifier) =>
        _statistics.GetValueOrDefault(identifier);
}
