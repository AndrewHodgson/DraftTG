using DraftTG.Domain;
using static DraftTG.RecommendationEngine.Tests.ColorCommitmentTests;

namespace DraftTG.RecommendationEngine.Tests;

public sealed class ContextualRecommendationTests
{
    private static readonly Card Green = Card("green", MagicColor.Green);
    private static readonly Card Blue = Card("blue", MagicColor.Blue);
    private static readonly Card Colorless = Card("colorless");
    private static readonly Card Multi = Card("multi", MagicColor.Green, MagicColor.Blue);

    private static ContextualDraftRecommendation Recommend(Card[] pool, Card[] pack, double?[] rates,
        int[]? samples = null, double baseline = 0.58, ColorCommitmentConfiguration? configuration = null)
    {
        var snapshot = Snapshot(pool, pack);
        var catalog = new CardCatalog(pool.Concat(pack).DistinctBy(c => c.Identifier));
        var statistics = new LimitedCardStatisticsCatalog(pack.Select((c, i) =>
            new LimitedCardStatistics(c.Identifier, GameInHandWinRate: rates[i], GameInHandGameCount: samples?[i] ?? 5000))
            .DistinctBy(c => c.CardIdentifier));
        var statistical = new StatisticalRecommendationEngine().Recommend(snapshot.CurrentPack, statistics,
            new([new(CardIdentifier.Create("environment"), GameInHandWinRate: baseline, GameInHandGameCount: 10000)]));
        return new ContextualRecommendationEngine(configuration).Recommend(snapshot, catalog, statistical);
    }

    [Theory]
    [InlineData(0.58, 0.6, 0.595)]
    [InlineData(0.58, -0.4, 0.570)]
    [InlineData(0.99, 1, 1)]
    [InlineData(0.01, -1, 0)]
    public void ExactAdjustmentRegressionAndClamp(double statistical, double fit, double expected) =>
        Assert.Equal(expected, new ContextualRecommendationEngine(new(maxColorAdjustment: 0.025)).AdjustValue(statistical, fit), 12);

    [Fact]
    public void EmptyPoolPreservesPhase8Exactly()
    {
        var result = Recommend([], [Blue, Green], [0.6, 0.58]);
        Assert.All(result.Cards, c => { Assert.Equal(0, c.ColorAdjustment); Assert.Equal(c.StatisticalRecommendation.AdjustedValue, c.ContextualValue); });
        Assert.Equal(result.StatisticalRecommendation.TopRecommendedPackIndex, result.TopRecommendedPackIndex);
    }

    [Theory]
    [InlineData(1, 0.59, 0)]
    [InlineData(14, 0.59, 1)]
    [InlineData(14, 0.70, 0)]
    [InlineData(14, 0.58, 1)]
    public void EarlyFlexibilityCloseLateReorderLargeGapAndEqualScores(int picks, double blueRate, int top)
    {
        var result = Recommend(Enumerable.Repeat(Green, picks).ToArray(), [Blue, Green], [blueRate, 0.58]);
        Assert.Equal(top, result.TopRecommendedPackIndex);
        Assert.Equal(0, result.StatisticalRecommendation.TopRecommendedPackIndex);
        Assert.Equal([Blue.Identifier, Green.Identifier], result.Cards.Select(c => c.CardIdentifier));
        Assert.Same(result.StatisticalRecommendation.Cards[0], result.Cards[0].StatisticalRecommendation);
    }

    [Fact]
    public void ColorlessScoreUnchangedAndMulticolorUsesWeakestRequiredColor()
    {
        var result = Recommend(Enumerable.Repeat(Green, 14).ToArray(), [Colorless, Multi, Green], [0.58, 0.58, 0.58]);
        Assert.Equal(0, result.Cards[0].ColorFit);
        Assert.Equal(0, result.Cards[0].ColorAdjustment);
        Assert.Equal(result.Cards[0].StatisticalRecommendation.AdjustedValue, result.Cards[0].ContextualValue);
        Assert.Equal(-0.7, result.Cards[1].ColorFit, 12);
        Assert.Equal(2, result.TopRecommendedPackIndex);
    }

    [Fact]
    public void UnscoredSupportedCardNeverGetsScoreRankOrPick()
    {
        var result = Recommend(Enumerable.Repeat(Green, 14).ToArray(), [Green, Blue, Colorless], [null, 0.58, 0.58]);
        Assert.True(result.Cards[0].ColorFit > 0);
        Assert.Null(result.Cards[0].ContextualValue);
        Assert.Null(result.Cards[0].ContextualRank);
        Assert.False(result.Cards[0].IsTopContextualCandidate);
        Assert.Equal(2, result.TopRecommendedPackIndex);
    }

    [Fact]
    public void OneScorableOccurrenceHasNoContextPickOrRank()
    {
        var result = Recommend([Green], [Green, Blue], [0.58, null]);
        Assert.NotNull(result.Cards[0].ContextualValue);
        Assert.Null(result.TopRecommendedPackIndex);
        Assert.All(result.Cards, c => Assert.Null(c.ContextualRank));
    }

    [Fact]
    public void DuplicatesStaySeparateAndOriginalSlotsBreakExactTies()
    {
        var result = Recommend([Green], [Green, Blue, Green, Green], [0.58, 0.58, 0.58, 0.58]);
        Assert.Equal([1, 4, 2, 3], result.Cards.Select(c => c.ContextualRank));
        Assert.Equal([0, 1, 2, 3], result.Cards.Select(c => c.PackIndex));
        Assert.Single(result.Cards, c => c.IsTopContextualCandidate);
    }

    [Fact]
    public void ContextTiesUsePhase8ValueBeforeSampleAndThenSlot()
    {
        // Clamp both to one, so the independently retained Phase 8 value breaks the tie.
        var secondGreen = Card("second-green", MagicColor.Green);
        var result = Recommend(Enumerable.Repeat(Green, 14).ToArray(), [Green, secondGreen], [0.99, 1],
            [10000, 1000], baseline: 1, configuration: new(maxColorAdjustment: 0.1));
        Assert.All(result.Cards, c => Assert.Equal(1, c.ContextualValue));
        Assert.Equal(1, result.TopRecommendedPackIndex);
        var sampleTie = Recommend([], [Blue, Green], [0.58, 0.58], [100, 10000]);
        Assert.Equal(1, sampleTie.TopRecommendedPackIndex);
    }

    [Fact]
    public void MissingEnvironmentStaysUnscoredAndMismatchedPackIsRejected()
    {
        var snapshot = Snapshot([Green], Blue, Green);
        var statistics = new StatisticalRecommendationEngine().Recommend(snapshot.CurrentPack, new());
        var engine = new ContextualRecommendationEngine();
        var result = engine.Recommend(snapshot, new([Green, Blue]), statistics);
        Assert.All(result.Cards, c => Assert.Null(c.ContextualValue));
        Assert.Null(result.TopRecommendedPackIndex);
        Assert.Throws<ArgumentException>(() => engine.Recommend(snapshot with
        { CurrentPack = new(snapshot.CurrentPack.Position, [Green.Identifier, Blue.Identifier]) }, new([Green, Blue]), statistics));
    }

    [Fact]
    public void UnknownCandidateMetadataRemainsExplicitAndNeutral()
    {
        var snapshot = Snapshot([], Green, Blue);
        var statistical = new StatisticalRecommendationEngine().Recommend(snapshot.CurrentPack,
            new([new(Green.Identifier, GameInHandWinRate: 0.58, GameInHandGameCount: 5000),
                new(Blue.Identifier, GameInHandWinRate: 0.58, GameInHandGameCount: 5000)]));
        var result = new ContextualRecommendationEngine().Recommend(snapshot, new(), statistical);
        Assert.All(result.Cards, c => { Assert.Null(c.Colors); Assert.Equal(0, c.ColorFit); });
    }
}
