using DraftTG.Domain;

namespace DraftTG.RecommendationEngine;

public enum RecommendationAvailability
{
    ReadyCompleteCoverage,
    ReadyPartialCoverage,
    InsufficientComparableStatistics,
    NoEnvironmentBaseline
}

/// <summary>500 is a Phase 8 model choice, not a claim of statistical optimality.</summary>
public sealed record StatisticalRecommendationConfiguration
{
    public StatisticalRecommendationConfiguration(int priorEquivalentGames = 500)
    {
        if (priorEquivalentGames <= 0) throw new ArgumentOutOfRangeException(nameof(priorEquivalentGames));
        PriorEquivalentGames = priorEquivalentGames;
    }

    public int PriorEquivalentGames { get; }
    public string ModelVersion => "gih-shrinkage-v1";
}

public sealed record CardRecommendation(
    int PackIndex,
    CardIdentifier CardIdentifier,
    double? RawGamesInHandWinRate,
    int? GamesInHandSampleCount,
    double? AdjustedValue,
    double? DataWeight,
    int? StatisticalRank,
    bool IsTopStatisticalCandidate)
{
    public bool IsScoringUnavailable => AdjustedValue is null;
}

public sealed class DraftRecommendation
{
    internal DraftRecommendation(RecommendationAvailability availability, IEnumerable<CardRecommendation> cards,
        double? baseline, int baselineCardCount, StatisticalRecommendationConfiguration configuration)
    {
        Availability = availability;
        Cards = Array.AsReadOnly(cards.ToArray());
        EnvironmentBaseline = baseline;
        EnvironmentBaselineCardCount = baselineCardCount;
        Configuration = configuration;
        ScoredCardCount = Cards.Count(card => !card.IsScoringUnavailable);
        TopRecommendedPackIndex = Cards.FirstOrDefault(card => card.IsTopStatisticalCandidate)?.PackIndex;
    }

    public RecommendationAvailability Availability { get; }
    public IReadOnlyList<CardRecommendation> Cards { get; }
    public int? TopRecommendedPackIndex { get; }
    public int ScoredCardCount { get; }
    public int TotalPackCardCount => Cards.Count;
    public double Coverage => TotalPackCardCount == 0 ? 0 : (double)ScoredCardCount / TotalPackCardCount;
    public double? EnvironmentBaseline { get; }
    public int EnvironmentBaselineCardCount { get; }
    public StatisticalRecommendationConfiguration Configuration { get; }
}

/// <summary>Pure GIH/sample shrinkage. Takes a pack, never its drafted history or card metadata.</summary>
public sealed class StatisticalRecommendationEngine
{
    public StatisticalRecommendationEngine(StatisticalRecommendationConfiguration? configuration = null) =>
        Configuration = configuration ?? new();

    public StatisticalRecommendationConfiguration Configuration { get; }

    public DraftRecommendation Recommend(DraftPack pack, LimitedCardStatisticsCatalog statistics,
        LimitedCardStatisticsCatalog? environmentStatistics = null)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(statistics);
        var environment = (environmentStatistics ?? statistics).Entries
            .Where(IsValid).OrderBy(row => row.CardIdentifier.Value, StringComparer.Ordinal).ToArray();
        // Convert counts to double before arithmetic. Both counts and collection size are
        // int-bounded, so even their maximum possible weighted sums cannot overflow double.
        var total = environment.Sum(row => (double)row.GameInHandGameCount!.Value);
        var weighted = environment.Sum(row => row.GameInHandWinRate!.Value * row.GameInHandGameCount!.Value);
        double? baseline = total > 0 && double.IsFinite(total) && double.IsFinite(weighted)
            ? weighted / total : null;
        if (baseline is not null && !ValidRate(baseline.Value)) baseline = null;

        var cards = pack.AvailableCardIdentifiers.Select((id, index) =>
        {
            var row = statistics.StatisticsFor(id);
            double? adjusted = null;
            double? weight = null;
            if (baseline is { } b && row is not null && IsValid(row))
            {
                var n = (double)row.GameInHandGameCount!.Value;
                var k = (double)Configuration.PriorEquivalentGames;
                adjusted = (n * row.GameInHandWinRate!.Value + k * b) / (n + k);
                weight = n / (n + k);
            }
            return new CardRecommendation(index, id, row?.GameInHandWinRate, row?.GameInHandGameCount,
                adjusted, weight, null, false);
        }).ToArray();

        var ordered = cards.Where(card => !card.IsScoringUnavailable)
            .OrderByDescending(card => card.AdjustedValue)
            .ThenByDescending(card => card.GamesInHandSampleCount)
            .ThenBy(card => card.PackIndex).ToArray();
        var availability = baseline is null ? RecommendationAvailability.NoEnvironmentBaseline
            : ordered.Length < 2 ? RecommendationAvailability.InsufficientComparableStatistics
            : ordered.Length == cards.Length ? RecommendationAvailability.ReadyCompleteCoverage
            : RecommendationAvailability.ReadyPartialCoverage;
        // One scored card retains its actual adjusted value, but gets no comparative rank or pick.
        if (ordered.Length >= 2)
            for (var i = 0; i < ordered.Length; i++)
                cards[ordered[i].PackIndex] = ordered[i] with
                {
                    StatisticalRank = i + 1,
                    IsTopStatisticalCandidate = i == 0
                };
        return new(availability, cards, baseline, environment.Length, Configuration);
    }

    private static bool IsValid(LimitedCardStatistics row) =>
        row.GameInHandGameCount is > 0 && row.GameInHandWinRate is { } rate && ValidRate(rate);

    private static bool ValidRate(double rate) => double.IsFinite(rate) && rate is >= 0 and <= 1;
}
