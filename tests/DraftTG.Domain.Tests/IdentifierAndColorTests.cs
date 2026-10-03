using DraftTG.Domain;

namespace DraftTG.Domain.Tests;

public sealed class IdentifierAndColorTests
{
    [Theory]
    [InlineData("card-1")]
    [InlineData("550e8400-e29b-41d4-a716-446655440000")]
    [InlineData("123456")]
    public void CardIdentifierAcceptsSourceNeutralValues(string value)
    {
        var identifier = CardIdentifier.Create(value);
        Assert.Equal(value, identifier.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t\n")]
    [InlineData("\u00a0")]
    public void CardIdentifierRejectsEmptyOrWhitespace(string value) =>
        Assert.False(CardIdentifier.TryCreate(value, out _));

    [Fact]
    public void CardIdentifierPreservesAcceptedTextAndUsesValueEquality()
    {
        const string value = "  Provider-ID_AbC-123  ";
        var first = CardIdentifier.Create(value);
        var same = CardIdentifier.Create(value);
        var different = CardIdentifier.Create("different");

        Assert.Equal(value, first.Value);
        Assert.Equal(first, same);
        Assert.NotEqual(first, different);
        Assert.Equal(2, new HashSet<CardIdentifier> { first, same, different }.Count);
    }

    [Fact]
    public void MagicColorsUseWubrgOrder() =>
        Assert.Equal(
            [MagicColor.White, MagicColor.Blue, MagicColor.Black, MagicColor.Red, MagicColor.Green],
            Enum.GetValues<MagicColor>());

    [Fact]
    public void EmptyColorSetIsColorless()
    {
        var colors = ColorSet.Colorless;
        Assert.True(colors.IsColorless);
        Assert.Empty(colors.Colors);
        Assert.Equal(0, colors.Count);
    }

    [Fact]
    public void ColorSetCollapsesDuplicatesAndEnumeratesInWubrgOrder()
    {
        var colors = new ColorSet([MagicColor.Red, MagicColor.White, MagicColor.Black, MagicColor.Red]);

        Assert.Equal([MagicColor.White, MagicColor.Black, MagicColor.Red], colors.Colors);
        Assert.True(colors.Contains(MagicColor.White));
        Assert.False(colors.Contains(MagicColor.Blue));
        Assert.Equal(3, colors.Count);
    }

    [Fact]
    public void ColorSetEqualityAndHashingIgnoreInputOrder()
    {
        var first = new ColorSet([MagicColor.Red, MagicColor.White]);
        var second = new ColorSet([MagicColor.White, MagicColor.Red, MagicColor.White]);
        Assert.Equal(first, second);
        Assert.Single(new HashSet<ColorSet> { first, second });
    }
}
