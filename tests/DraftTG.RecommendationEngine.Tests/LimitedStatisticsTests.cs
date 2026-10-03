using DraftTG.Domain;

namespace DraftTG.RecommendationEngine.Tests;

public sealed class LimitedStatisticsTests
{
    private static readonly CardIdentifier Id = CardIdentifier.Create("card");
    [Fact]
    public void MissingMetricsStayNull()
    {
        var row = new LimitedCardStatistics(Id);
        Assert.Null(row.GameCount);
        Assert.Null(row.PlayRate);
        Assert.Null(row.GameInHandWinRate);
        Assert.Null(row.OpeningHandWinRate);
        Assert.Null(row.DrawnWinRate);
        Assert.Null(row.DrawnImprovementWinRate);
        Assert.Null(row.AverageLastSeenAt);
        Assert.Null(row.AverageTakenAt);
    }
    [Fact]
    public void RatesRemainFractionsAndRecordCopiesDoNotMutateOriginal()
    {
        var row = new LimitedCardStatistics(Id, GameInHandWinRate: 0.5874);
        var copy = row with { GameInHandWinRate = 0.6 };
        Assert.Equal(0.5874, row.GameInHandWinRate);
        Assert.Equal(0.6, copy.GameInHandWinRate);
    }
    [Fact]
    public void CatalogLooksUpDomainIdentityAndUnknownIsAbsent()
    {
        var row = new LimitedCardStatistics(Id);
        var catalog = new LimitedCardStatisticsCatalog([row]);
        Assert.Equal(row, catalog.StatisticsFor(CardIdentifier.Create("card")));
        Assert.Null(catalog.StatisticsFor(CardIdentifier.Create("unknown")));
    }
    [Fact]
    public void DuplicateIdentityIsRejected() => Assert.Throws<ArgumentException>(() =>
        new LimitedCardStatisticsCatalog([new(Id), new(Id)]));
    [Fact]
    public void CatalogTakesImmutableCopy()
    {
        var rows = new List<LimitedCardStatistics> { new(Id) };
        var catalog = new LimitedCardStatisticsCatalog(rows);
        rows.Clear();
        Assert.Equal(1, catalog.Count);
        Assert.NotNull(catalog.StatisticsFor(Id));
    }
    [Fact]
    public void ContextPreservesEnvironmentAndFormat()
    {
        var context = new LimitedStatisticsContext("HOB", LimitedStatisticsFormat.QuickDraft);
        Assert.NotEqual(context, context with { Format = LimitedStatisticsFormat.PremierDraft });
        Assert.Equal("HOB", context.Expansion);
    }
}
