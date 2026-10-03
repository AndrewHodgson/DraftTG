using DraftTG.Domain;

namespace DraftTG.RecommendationEngine.Tests;

public sealed class StatisticalRecommendationEngineTests
{
    private readonly StatisticalRecommendationEngine _engine = new();
    private static CardIdentifier Id(string value) => CardIdentifier.Create(value);
    private static LimitedCardStatistics Row(string id, double? rate, int? sample) =>
        new(Id(id), GameInHandWinRate: rate, GameInHandGameCount: sample);
    private static DraftPack Pack(params string[] ids) =>
        new(new(PackNumber.Create(1), PickNumber.Create(1)), ids.Select(Id));
    private static LimitedCardStatisticsCatalog Baseline(double rate = 0.55) => new([Row("environment", rate, 10000)]);

    [Fact]
    public void WeightedEnvironmentBaselineIsNotAnUnweightedMean()
    {
        var result = _engine.Recommend(Pack("a", "b"), new([Row("a", 0.50, 100), Row("b", 0.60, 300)]));
        Assert.Equal(0.575, result.EnvironmentBaseline!.Value, 12);
        Assert.Equal(2, result.EnvironmentBaselineCardCount);
        Assert.NotEqual(0.55, result.EnvironmentBaseline);
    }

    [Fact]
    public void ShrinkageRegressionUsesConfiguredPriorAndExposesDataWeight()
    {
        var result = _engine.Recommend(Pack("a", "b"), new([Row("a", 0.60, 500), Row("b", 0.50, 500)]), Baseline());
        var card = result.Cards[0];
        Assert.Equal(0.575, card.AdjustedValue!.Value, 12);
        Assert.Equal(0.5, card.DataWeight);
        Assert.Equal(0.60, card.RawGamesInHandWinRate);
        Assert.Equal(500, result.Configuration.PriorEquivalentGames);
        Assert.Equal("gih-shrinkage-v1", result.Configuration.ModelVersion);
        Assert.Equal(0, result.TopRecommendedPackIndex);
    }

    [Fact]
    public void LargeSampleStrongCardBeatsLowerPerformingCard()
    {
        var result = _engine.Recommend(Pack("weak", "strong"),
            new([Row("weak", 0.52, 5000), Row("strong", 0.60, 5000)]), Baseline());
        Assert.Equal(1, result.TopRecommendedPackIndex);
        Assert.Equal(2, result.Cards[0].StatisticalRank);
        Assert.Equal(1, result.Cards[1].StatisticalRank);
    }

    [Fact]
    public void TinySampleExtremeShrinksEnoughToLoseToStrongLargeSample()
    {
        var result = _engine.Recommend(Pack("tiny", "large"),
            new([Row("tiny", 0.70, 20), Row("large", 0.58, 5000)]), Baseline());
        var expectedTiny = (20 * 0.70 + 500 * 0.55) / 520;
        var expectedLarge = (5000 * 0.58 + 500 * 0.55) / 5500;
        Assert.Equal(expectedTiny, result.Cards[0].AdjustedValue!.Value, 12);
        Assert.Equal(expectedLarge, result.Cards[1].AdjustedValue!.Value, 12);
        Assert.True(expectedLarge > expectedTiny);
        Assert.Equal(1, result.TopRecommendedPackIndex);
        Assert.True(result.Cards[0].AdjustedValue < 0.56);
    }

    [Fact]
    public void MaximumSampleApproachesRawWithoutIntegerOverflow()
    {
        var result = _engine.Recommend(Pack("a", "b"),
            new([Row("a", 0.60, int.MaxValue), Row("b", 0.40, int.MaxValue)]));
        Assert.Equal(0.50, result.EnvironmentBaseline);
        Assert.InRange(result.Cards[0].AdjustedValue!.Value, 0.5999999, 0.60);
        Assert.True(double.IsFinite(result.Cards[0].DataWeight!.Value));
        var maximumPrior = new StatisticalRecommendationEngine(new(int.MaxValue));
        Assert.Equal(0.55, maximumPrior.Recommend(Pack("a", "a"),
            new([Row("a", 0.60, int.MaxValue)]), Baseline(0.50)).Cards[0].AdjustedValue!.Value, 12);
    }

    [Fact]
    public void SmallSampleApproachesEnvironmentBaseline()
    {
        var result = _engine.Recommend(Pack("a", "a"), new([Row("a", 0.9, 1)]), Baseline());
        Assert.InRange(result.Cards[0].AdjustedValue!.Value, 0.55, 0.551);
        Assert.Equal(1.0 / 501, result.Cards[0].DataWeight);
    }

    [Theory]
    [InlineData(null, 100)]
    [InlineData(0.6, null)]
    [InlineData(0.6, 0)]
    [InlineData(0.6, -1)]
    [InlineData(-0.01, 100)]
    [InlineData(1.01, 100)]
    [InlineData(double.NaN, 100)]
    [InlineData(double.PositiveInfinity, 100)]
    [InlineData(double.NegativeInfinity, 100)]
    public void InvalidGihIsUnscoredAndExcludedFromBaseline(double? rate, int? sample)
    {
        var result = _engine.Recommend(Pack("invalid", "valid", "valid"),
            new([Row("invalid", rate, sample), Row("valid", 0.575, 900)]));
        Assert.Equal(0.575, result.EnvironmentBaseline!.Value, 12);
        Assert.Equal(1, result.EnvironmentBaselineCardCount);
        Assert.True(result.Cards[0].IsScoringUnavailable);
        Assert.Null(result.Cards[0].AdjustedValue);
        Assert.Null(result.Cards[0].DataWeight);
        Assert.Null(result.Cards[0].StatisticalRank);
        Assert.False(result.Cards[0].IsTopStatisticalCandidate);
        Assert.Equal(rate, result.Cards[0].RawGamesInHandWinRate);
        Assert.Equal(sample, result.Cards[0].GamesInHandSampleCount);
        Assert.Equal(RecommendationAvailability.ReadyPartialCoverage, result.Availability);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    public void RateBoundariesAreScorable(double rate)
    {
        var result = _engine.Recommend(Pack("a", "a"), new([Row("a", rate, 100)]));
        Assert.Equal(rate, result.EnvironmentBaseline);
        Assert.Equal(2, result.ScoredCardCount);
    }

    [Fact]
    public void DuplicatesHaveIndependentRanksAndDoNotReorderOrReweightPack()
    {
        var pack = Pack("low", "high", "high", "missing");
        var ids = pack.AvailableCardIdentifiers.ToArray();
        var statistics = new LimitedCardStatisticsCatalog([Row("high", 0.60, 500), Row("low", 0.50, 500)]);
        var result = _engine.Recommend(pack, statistics);
        Assert.Equal(ids, pack.AvailableCardIdentifiers);
        Assert.Equal(ids, result.Cards.Select(card => card.CardIdentifier));
        Assert.Equal([0, 1, 2, 3], result.Cards.Select(card => card.PackIndex));
        Assert.Equal(new int?[] { 3, 1, 2, null }, result.Cards.Select(card => card.StatisticalRank));
        Assert.Equal(0.55, result.EnvironmentBaseline);
        Assert.Equal(2, result.EnvironmentBaselineCardCount);
        Assert.Equal(1, result.TopRecommendedPackIndex);
        Assert.Single(result.Cards, card => card.IsTopStatisticalCandidate);
        Assert.Equal(3, result.ScoredCardCount);
        Assert.Equal(4, result.TotalPackCardCount);
        Assert.Equal(0.75, result.Coverage);
        Assert.Equal(RecommendationAvailability.ReadyPartialCoverage, result.Availability);
    }

    [Fact]
    public void TiesUseSampleThenIndexAndIgnoreCardNamesOrCatalogOrder()
    {
        var statistics = new LimitedCardStatisticsCatalog([Row("z", 0.5, 100), Row("a", 0.5, 1000)]);
        var result = _engine.Recommend(Pack("z", "a", "a", "z"), statistics, Baseline(0.5));
        Assert.Equal(new int?[] { 3, 1, 2, 4 }, result.Cards.Select(card => card.StatisticalRank));
        var reversed = _engine.Recommend(Pack("z", "a", "a", "z"), new(statistics.Entries.Reverse()), Baseline(0.5));
        Assert.Equal(result.Cards, reversed.Cards);
    }

    [Fact]
    public void CompleteCoverageIsExplicit()
    {
        var result = _engine.Recommend(Pack("a", "b"), new([Row("a", 0.6, 100), Row("b", 0.5, 100)]));
        Assert.Equal(RecommendationAvailability.ReadyCompleteCoverage, result.Availability);
        Assert.Equal(1, result.Coverage);
        Assert.Equal(2, result.ScoredCardCount);
    }

    [Fact]
    public void OnlyOneScoredCardHasValueButNoComparativeRankOrTopPick()
    {
        var result = _engine.Recommend(Pack("a", "missing"), new([Row("a", 0.6, 100)]));
        Assert.Equal(RecommendationAvailability.InsufficientComparableStatistics, result.Availability);
        Assert.Equal(1, result.ScoredCardCount);
        Assert.NotNull(result.Cards[0].AdjustedValue);
        Assert.All(result.Cards, card => Assert.Null(card.StatisticalRank));
        Assert.Null(result.TopRecommendedPackIndex);
    }

    [Fact]
    public void MissingPackStatisticsWithValidEnvironmentHasNoComparison()
    {
        var result = _engine.Recommend(Pack("a", "b"), new(), Baseline());
        Assert.Equal(RecommendationAvailability.InsufficientComparableStatistics, result.Availability);
        Assert.Equal(0, result.ScoredCardCount);
        Assert.Null(result.TopRecommendedPackIndex);
        Assert.All(result.Cards, card => Assert.True(card.IsScoringUnavailable));
    }

    [Fact]
    public void NoValidEnvironmentBaselineNeverManufacturesScores()
    {
        var result = _engine.Recommend(Pack("a", "a"), new([Row("a", 0.6, 100)]), new());
        Assert.Equal(RecommendationAvailability.NoEnvironmentBaseline, result.Availability);
        Assert.Null(result.EnvironmentBaseline);
        Assert.Null(result.TopRecommendedPackIndex);
        Assert.Equal(0, result.ScoredCardCount);
        Assert.All(result.Cards, card => Assert.Null(card.AdjustedValue));
        Assert.Equal(RecommendationAvailability.NoEnvironmentBaseline,
            _engine.Recommend(Pack("a", "b"), new()).Availability);
    }

    [Fact]
    public void EmptyPackHasZeroCoverageAndNoPick()
    {
        var result = _engine.Recommend(Pack(), new(), Baseline());
        Assert.Empty(result.Cards);
        Assert.Equal(0, result.Coverage);
        Assert.Null(result.TopRecommendedPackIndex);
        Assert.Equal(RecommendationAvailability.InsufficientComparableStatistics, result.Availability);
    }

    [Fact]
    public void OtherMetricsDoNotChangeScoresOrRanks()
    {
        var rows = new[] { Row("a", 0.60, 900), Row("b", 0.55, 1000) };
        var original = _engine.Recommend(Pack("a", "b"), new(rows));
        var changed = _engine.Recommend(Pack("a", "b"), new(rows.Select(row => row with
        {
            AverageLastSeenAt = 99, AverageTakenAt = 1, OpeningHandWinRate = 0.99,
            DrawnWinRate = 0.01, DrawnImprovementWinRate = -0.9, PlayRate = 0.1
        })));
        Assert.Equal(original.EnvironmentBaseline, changed.EnvironmentBaseline);
        Assert.Equal(original.Cards, changed.Cards);
    }

    [Fact]
    public void ConfigurationCanBeTunedWithoutMutatingPreviousResults()
    {
        var stats = new LimitedCardStatisticsCatalog([Row("a", 0.6, 500)]);
        var original = _engine.Recommend(Pack("a", "a"), stats, Baseline());
        var tuned = new StatisticalRecommendationEngine(new(1000)).Recommend(Pack("a", "a"), stats, Baseline());
        Assert.Equal((500 * 0.6 + 1000 * 0.55) / 1500, tuned.Cards[0].AdjustedValue!.Value, 12);
        Assert.Equal(500, original.Configuration.PriorEquivalentGames);
        Assert.NotEqual(original.Cards[0].AdjustedValue, tuned.Cards[0].AdjustedValue);
        var list = Assert.IsAssignableFrom<IList<CardRecommendation>>(original.Cards);
        Assert.Throws<NotSupportedException>(() => list[0] = list[0] with { PackIndex = 99 });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonpositivePriorIsRejected(int prior) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new StatisticalRecommendationConfiguration(prior));
}
