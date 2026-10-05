using DraftTG.Domain;

namespace DraftTG.RecommendationEngine.Tests;

public sealed class ColorCommitmentTests
{
    internal static Card Card(string id, params MagicColor[] colors) => new(CardIdentifier.Create(id), id,
        new(colors), CardRarity.Common, CardSetCode.Create("WOE"), CollectorNumber.Create("1"));
    internal static DraftPosition Position(int ordinal) => new(PackNumber.Create(ordinal / 14 + 1), PickNumber.Create(ordinal % 14 + 1));
    internal static DraftHistory History(params Card[] cards) => new(cards.Select((c, i) => new DraftPick(Position(i), c.Identifier)));
    internal static DraftSnapshot Snapshot(Card[] pool, params Card[] pack) =>
        new(new(Position(pool.Length), pack.Select(c => c.Identifier)), History(pool), DraftFormat.BestOfOne);
    private static readonly Card Green = Card("green", MagicColor.Green);
    private static readonly Card Red = Card("red", MagicColor.Red);
    private static readonly Card Colorless = Card("colorless");

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1.0 / 14)]
    [InlineData(7, 0.5)]
    [InlineData(14, 1)]
    [InlineData(20, 1)]
    public void ProgressUsesCompletedPicksIncludingColorless(int count, double expected)
    {
        var profile = ColorCommitmentProfile.From(History(Enumerable.Repeat(Colorless, count).ToArray()), new([Colorless]));
        Assert.Equal(expected, profile.ProgressFactor, 12);
        Assert.Equal(count, profile.CompletedPickCount);
        Assert.All(profile.Colors, c => { Assert.Equal(0, c.Evidence); Assert.Equal(0, c.RawSupport); Assert.Equal(0, c.EffectiveSupport); });
        Assert.Null(profile.PrimaryColor);
    }

    [Fact]
    public void FirstGreenPickHasVeryWeakEffectiveInfluence()
    {
        var profile = ColorCommitmentProfile.From(History(Green), new([Green]));
        Assert.Equal(0.2, profile.For(MagicColor.Green).RawSupport, 12);
        Assert.Equal(0.2 / 14, profile.For(MagicColor.Green).EffectiveSupport, 12);
        Assert.Equal(MagicColor.Green, profile.PrimaryColor);
        Assert.Null(profile.SecondaryColor);
        Assert.InRange(profile.Fit(Green.Colors) * 0.025, 0, 0.0004);
        Assert.Equal(-0.05 / 14, profile.For(MagicColor.Blue).EffectiveSupport, 12);
    }

    [Fact]
    public void FiveGreenOccurrencesIncreaseSupportWithoutDeduplicatingIdentifiers()
    {
        var profile = ColorCommitmentProfile.From(History(Enumerable.Repeat(Green, 5).ToArray()), new([Green]));
        Assert.Equal(5, profile.For(MagicColor.Green).Evidence);
        Assert.Equal(1, profile.For(MagicColor.Green).RawSupport);
        Assert.Equal(5.0 / 14, profile.For(MagicColor.Green).EffectiveSupport, 12);
        Assert.Equal(1, profile.MeanEvidence);
    }

    [Fact]
    public void EstablishedTwoColorPoolSupportsBothColorsAndMinimumRequiredColor()
    {
        var pool = Enumerable.Repeat(Green, 7).Concat(Enumerable.Repeat(Red, 7)).ToArray();
        var profile = ColorCommitmentProfile.From(History(pool), new([Green, Red]));
        Assert.Equal(1, profile.For(MagicColor.Green).EffectiveSupport);
        Assert.Equal(1, profile.For(MagicColor.Red).EffectiveSupport);
        Assert.Equal(MagicColor.Red, profile.PrimaryColor); // deterministic enum-order tie
        Assert.Equal(MagicColor.Green, profile.SecondaryColor);
        Assert.Equal(1, profile.Fit(new([MagicColor.Green, MagicColor.Red])));
        Assert.Equal(-0.7, profile.Fit(new([MagicColor.Green, MagicColor.Blue])), 12);
        Assert.Equal(0, profile.Fit(ColorSet.Colorless));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void KColorPickContributesOneTotalFractionally(int count)
    {
        var card = Card("multi", Enum.GetValues<MagicColor>().Take(count).ToArray());
        var profile = ColorCommitmentProfile.From(History(card), new([card]));
        Assert.Equal(1, profile.Colors.Sum(c => c.Evidence), 12);
        foreach (var color in card.Colors.Colors) Assert.Equal(1.0 / count, profile.For(color).Evidence, 12);
    }

    [Fact]
    public void ColorlessChangesProgressButNotEvidenceAndPackDoesNotContribute()
    {
        var snapshot = Snapshot([Green, Colorless], Red, Red, Red);
        var profile = ColorCommitmentProfile.From(snapshot.History, new([Green, Red, Colorless]));
        Assert.Equal(1, profile.For(MagicColor.Green).Evidence);
        Assert.Equal(0, profile.For(MagicColor.Red).Evidence);
        Assert.Equal(2.0 / 14, profile.ProgressFactor, 12);
    }

    [Fact]
    public void UnknownSelectedMetadataIsExplicitAndDoesNotInventEvidence()
    {
        var profile = ColorCommitmentProfile.From(History(Green), new());
        Assert.Equal(1, profile.UnresolvedPickCount);
        Assert.Equal(1.0 / 14, profile.ProgressFactor, 12);
        Assert.All(profile.Colors, c => Assert.Equal(0, c.Evidence));
    }

    [Fact]
    public void ConfigurationControlsScaleProgressAndMaximumAdjustment()
    {
        var configuration = new ColorCommitmentConfiguration(2, 2, 0.01);
        var profile = ColorCommitmentProfile.From(History(Green), new([Green]), configuration);
        Assert.Same(configuration, profile.Configuration);
        Assert.Equal(0.4, profile.For(MagicColor.Green).RawSupport, 12);
        Assert.Equal(0.2, profile.For(MagicColor.Green).EffectiveSupport, 12);
        Assert.Equal(0.002, new ContextualRecommendationEngine(configuration).AdjustValue(0, profile.Fit(Green.Colors)), 12);
    }

    [Theory]
    [InlineData(0, 14, 0.025)]
    [InlineData(-1, 14, 0.025)]
    [InlineData(double.NaN, 14, 0.025)]
    [InlineData(double.PositiveInfinity, 14, 0.025)]
    [InlineData(4, 0, 0.025)]
    [InlineData(4, -1, 0.025)]
    [InlineData(4, 14, -0.01)]
    [InlineData(4, 14, double.NaN)]
    [InlineData(4, 14, double.PositiveInfinity)]
    [InlineData(4, 14, 1.1)]
    public void InvalidConfigurationIsRejected(double scale, int picks, double adjustment) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ColorCommitmentConfiguration(scale, picks, adjustment));
}
