using DraftTG.Domain;

namespace DraftTG.Domain.Tests;

public sealed class DeckBuildingMetadataTests
{
    [Theory]
    [InlineData("Creature — Rat", CardType.Creature)]
    [InlineData("Artifact Creature — Food Golem", CardType.Artifact | CardType.Creature)]
    [InlineData("Instant", CardType.Instant)]
    [InlineData("Sorcery", CardType.Sorcery)]
    [InlineData("Enchantment — Aura", CardType.Enchantment)]
    [InlineData("Land", CardType.Land)]
    [InlineData("Basic Land — Forest", CardType.Land)]
    [InlineData("Legendary Planeswalker — Liliana", CardType.Planeswalker)]
    [InlineData("Battle — Siege", CardType.Battle)]
    public void CentralTypeInterpretationPreservesMultipleTypes(string line, CardType expected)
    {
        var metadata = new CardGameplayMetadata(typeLine: line);
        Assert.Equal(expected, metadata.CardTypes.Types);
        Assert.Equal(expected.HasFlag(CardType.Creature), metadata.IsCreature);
        Assert.Equal(expected.HasFlag(CardType.Land), metadata.IsLand);
        Assert.Equal(line.StartsWith("Basic", StringComparison.Ordinal), metadata.IsBasicLand);
        Assert.Equal(!metadata.IsLand, metadata.IsNonlandSpell);
        if (metadata.IsBasicLand) Assert.Equal(BasicLandType.Forest, metadata.CardTypes.BasicLandType);
    }

    [Fact]
    public void ManaValuesAllowFractionsAndUnknownButRejectInvalidNumbers()
    {
        Assert.Equal(0.5, new CardGameplayMetadata(manaValue: 0.5).ManaValue);
        Assert.Null(CardGameplayMetadata.Unknown.ManaValue);
        foreach (var value in new[] { -1d, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Assert.Throws<ArgumentOutOfRangeException>(() => new CardGameplayMetadata(manaValue: value));
        Assert.False(CardGameplayMetadata.Unknown.CardTypes.IsKnown);
        Assert.False(CardGameplayMetadata.Unknown.IsNonlandSpell);
    }

    [Fact]
    public void MetadataDefensivelyCopiesCollectionsAndPreservesUnusualPowerAndMana()
    {
        var keywords = new[] { "Flying" };
        var production = new[] { ManaKind.Colorless, ManaKind.Green };
        var faces = new[] { new CardFaceMetadata("Front", "{X}{G}", null, null, "Creature", null, "*", "1+*") };
        var metadata = new CardGameplayMetadata(typeLine: "Creature", faces: faces, keywords: keywords,
            power: "*", toughness: "1+*", producedMana: production);
        keywords[0] = "Changed"; production[0] = ManaKind.White; faces[0] = faces[0] with { Name = "Changed" };
        Assert.Equal("Flying", Assert.Single(metadata.Keywords));
        Assert.Contains(ManaKind.Colorless, metadata.ProducedMana!);
        Assert.Equal("Front", Assert.Single(metadata.Faces).Name);
        Assert.Equal("*", metadata.Power);
        Assert.Equal("1+*", metadata.Toughness);
        Assert.Equal(5, Enum.GetValues<MagicColor>().Length);
        foreach (var land in Enum.GetValues<BasicLandType>())
            Assert.Equal(land, CardTypeSet.Parse($"Basic Land — {land}").BasicLandType);
    }

    [Fact]
    public void PoolInventoryPreservesCopyCountsWithoutCoordinates()
    {
        var a = CardIdentifier.Create("A"); var b = CardIdentifier.Create("B"); var c = CardIdentifier.Create("C");
        var cards = new[] { a, a, b, b, b, c };
        var pool = new DraftPoolSnapshot(new(cards), DraftPoolCompleteness.Complete);
        cards[0] = c;
        Assert.Equal(6, pool.TotalCardCount);
        Assert.Equal(3, pool.UniqueCardCount);
        Assert.Equal(2, pool.Entries.Single(e => e.CardIdentifier == a).Count);
        Assert.Equal(3, pool.Entries.Single(e => e.CardIdentifier == b).Count);
        Assert.Throws<ArgumentException>(() => new DraftPoolSnapshot(new([]), DraftPoolCompleteness.Complete, 1));
    }
}
