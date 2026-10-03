using DraftTG.Domain;

namespace DraftTG.Domain.Tests;

public sealed class CardValueTests
{
    [Theory]
    [InlineData("TST")]
    [InlineData("  AbC  ")]
    public void SetCodesPreserveAcceptedText(string value) =>
        Assert.Equal(value, CardSetCode.Create(value).Value);

    [Theory]
    [InlineData("")]
    [InlineData(" \t\n")]
    public void SetCodesRejectEmptyOrWhitespace(string value) =>
        Assert.False(CardSetCode.TryCreate(value, out _));

    [Theory]
    [InlineData("123")]
    [InlineData("123a")]
    [InlineData("  42★  ")]
    public void CollectorNumbersPreserveAcceptedText(string value) =>
        Assert.Equal(value, CollectorNumber.Create(value).Value);

    [Theory]
    [InlineData("")]
    [InlineData(" \t\n")]
    public void CollectorNumbersRejectEmptyOrWhitespace(string value) =>
        Assert.False(CollectorNumber.TryCreate(value, out _));

    [Fact]
    public void PrintingValuesUseEqualityAndHashing()
    {
        Assert.Equal(CardSetCode.Create("TST"), CardSetCode.Create("TST"));
        Assert.NotEqual(CardSetCode.Create("TST"), CardSetCode.Create("ALT"));
        Assert.Equal(CollectorNumber.Create("7"), CollectorNumber.Create("7"));
        Assert.NotEqual(CollectorNumber.Create("7"), CollectorNumber.Create("7a"));
    }

    [Fact]
    public void CardStoresFoundationalInformationWithPrintingEquality()
    {
        var colors = new ColorSet([MagicColor.White, MagicColor.Blue]);
        var card = DomainTestSupport.Card(
            "printing-1", "Example Card", colors, CardRarity.Rare, "TST", "42a");
        var same = DomainTestSupport.Card(
            "printing-1", "Example Card", colors, CardRarity.Rare, "TST", "42a");

        Assert.Equal("printing-1", card.Identifier.Value);
        Assert.Equal("Example Card", card.Name);
        Assert.Equal(colors, card.Colors);
        Assert.Equal(CardRarity.Rare, card.Rarity);
        Assert.Equal("TST", card.SetCode.Value);
        Assert.Equal("42a", card.CollectorNumber.Value);
        Assert.Equal(card, same);
    }

    [Fact]
    public void AllLimitedRaritiesAndFormatsAreRepresented()
    {
        Assert.Equal(
            [
                CardRarity.Common,
                CardRarity.Uncommon,
                CardRarity.Rare,
                CardRarity.Mythic,
                CardRarity.Special,
                CardRarity.Bonus
            ],
            Enum.GetValues<CardRarity>());
        Assert.Equal(
            [DraftFormat.BestOfOne, DraftFormat.BestOfThree],
            Enum.GetValues<DraftFormat>());
    }
}
