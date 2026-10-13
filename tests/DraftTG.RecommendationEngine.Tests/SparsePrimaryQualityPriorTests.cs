using DraftTG.Domain;

namespace DraftTG.RecommendationEngine.Tests;

public sealed class SparsePrimaryQualityPriorTests
{
    private static readonly Card Card = new(CardIdentifier.Create("quality-card"), "Quality Card", ColorSet.Colorless,
        CardRarity.Common, CardSetCode.Create("FRA"), CollectorNumber.Create("1"));
    private static readonly LimitedCardStatistics Primary = new(Card.Identifier, 371);
    private static AlternateFormatQualityEvidence Evidence(double rate = .69, int games = 3717) => new(Card.Identifier,
        new("FRA", LimitedStatisticsFormat.QuickDraft), new("FRA", LimitedStatisticsFormat.PremierDraft), rate, games, .56,
        Card.Name, DateTimeOffset.Parse("2026-10-05T00:00:00Z"), "Cache", "ALL_TIME");

    [Fact]
    public void CenteredSampleShrinkageIsConservativeForStrongWeakAverageTinyAndLargeEvidence()
    {
        var strong = SparsePrimaryQualityPrior.TryCreate(Card, Primary, .57, Evidence())!;
        Assert.Equal(.57 + .9 * 3717 / 4217 * (.69-.56), strong.EffectiveQualityValue, 12);
        Assert.Equal(3717 / 4217d, strong.SampleWeight, 12);
        Assert.Equal(.9, strong.CrossFormatReliability); Assert.Equal(500, strong.ShrinkageGames);
        var weak = SparsePrimaryQualityPrior.TryCreate(Card, Primary, .57, Evidence(.48))!;
        var average = SparsePrimaryQualityPrior.TryCreate(Card, Primary, .57, Evidence(.56))!;
        var tiny = SparsePrimaryQualityPrior.TryCreate(Card, Primary, .57, Evidence(.69, 1))!;
        var large = SparsePrimaryQualityPrior.TryCreate(Card, Primary, .57, Evidence(.69, 100000))!;
        Assert.True(weak.EffectiveQualityValue < .57); Assert.Equal(.57, average.EffectiveQualityValue);
        Assert.InRange(tiny.EffectiveQualityValue-.57, 0, .0003);
        Assert.True(large.EffectiveQualityValue > strong.EffectiveQualityValue);
        Assert.True(large.EffectiveQualityValue < .57 + .69 - .56);
        Assert.Equal(strong, SparsePrimaryQualityPrior.TryCreate(Card, Primary, .57, Evidence()));
    }

    [Fact]
    public void PolicyFailsClosedForHealthyPrimaryMissingPrimaryWrongIdentitySetFormatPeriodOrInvalidEvidence()
    {
        Assert.Null(SparsePrimaryQualityPrior.TryCreate(Card, Primary with { GameInHandWinRate = .58 }, .57, Evidence()));
        Assert.Null(SparsePrimaryQualityPrior.TryCreate(Card, null, .57, Evidence()));
        Assert.Null(SparsePrimaryQualityPrior.TryCreate(Card with { SetCode = CardSetCode.Create("OLD") }, Primary, .57, Evidence()));
        // A special-sheet card is likewise outside this FRA-only ordinary-printing calibration.
        Assert.Null(SparsePrimaryQualityPrior.TryCreate(Card with { SetCode = CardSetCode.Create("BON") }, Primary, .57, Evidence()));
        foreach (var evidence in new[]
        {
            Evidence() with { CardIdentifier = CardIdentifier.Create("other") },
            Evidence() with { SourceContext = new("OLD", LimitedStatisticsFormat.PremierDraft) },
            Evidence() with { PrimaryContext = new("FRA", LimitedStatisticsFormat.PremierDraft), SourceContext = new("FRA", LimitedStatisticsFormat.QuickDraft) },
            Evidence() with { SourceContext = new("FRA", LimitedStatisticsFormat.TraditionalDraft) },
            Evidence() with { TimePeriod = "LAST_WEEK" }, Evidence() with { Source = "Unavailable" },
            Evidence(double.NaN), Evidence(.69, 0), Evidence() with { SourceEnvironmentBaseline = double.NaN }
        }) Assert.Null(SparsePrimaryQualityPrior.TryCreate(Card, Primary, .57, evidence));
        Assert.Null(SparsePrimaryQualityPrior.TryCreate(Card, Primary, double.NaN, Evidence()));
        Assert.Null(SparsePrimaryQualityPrior.TryCreate(Card, Primary, .57, null));
    }
}
