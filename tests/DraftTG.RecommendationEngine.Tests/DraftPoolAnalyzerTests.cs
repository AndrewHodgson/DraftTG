using DraftTG.Domain;

namespace DraftTG.RecommendationEngine.Tests;

public sealed class DraftPoolAnalyzerTests
{
    private static Card Card(string id, double? mv, string type, params MagicColor[] colors) =>
        new(CardIdentifier.Create(id), id, new(colors), CardRarity.Common, CardSetCode.Create("WOE"), CollectorNumber.Create(id))
        { GameplayMetadata = new(mv, typeLine: type) };
    private static DraftPoolAnalysis Analyze(Card[] cards, params string[] ids) => new DraftPoolAnalyzer().Analyze(
        new(new(ids.Select(CardIdentifier.Create)), DraftPoolCompleteness.Complete), new(cards));

    [Fact]
    public void MultiplicityAndOverlappingTypeAndColorMembershipRemainDescriptive()
    {
        var a = Card("A", 1, "Artifact Creature", MagicColor.Green);
        var b = Card("B", 3, "Enchantment Creature", MagicColor.Black, MagicColor.Green);
        var c = Card("C", 0, "Land");
        var analysis = Analyze([a, b, c], "A", "A", "B", "B", "B", "C");
        Assert.Equal(6, analysis.TotalCardCount);
        Assert.Equal(3, analysis.UniqueCardCount);
        Assert.Equal(5, analysis.CreatureCount);
        Assert.Equal(1, analysis.LandCount);
        Assert.Equal(5, analysis.NonlandSpellCount);
        Assert.Equal(2, analysis.TypeCounts[CardType.Artifact]);
        Assert.Equal(3, analysis.TypeCounts[CardType.Enchantment]);
        Assert.Equal(5, analysis.ColorCounts[MagicColor.Green]);
        Assert.Equal(3, analysis.ColorCounts[MagicColor.Black]);
        Assert.Equal(1, analysis.ColorlessCount);
        Assert.Equal(3, analysis.MulticolorCount);
    }

    [Fact]
    public void CurvesExcludeLandsKeepCopiesAndSeparateUnknownAndFractionalManaValues()
    {
        var cards = new[] { Card("land", 0, "Land"), Card("one", 1, "Instant"), Card("two", 2, "Sorcery"),
            Card("three", 3, "Creature"), Card("seven", 7, "Enchantment"), Card("unknown", null, "Instant"), Card("fraction", 2.5, "Creature") };
        var a = Analyze(cards, "land", "one", "two", "two", "three", "seven", "unknown", "fraction");
        Assert.Equal(1, a.NonlandCurve[ManaCurveBucket.BelowTwo]);
        Assert.Equal(3, a.NonlandCurve[ManaCurveBucket.Two]);
        Assert.Equal(1, a.NonlandCurve[ManaCurveBucket.Three]);
        Assert.Equal(1, a.NonlandCurve[ManaCurveBucket.SevenPlus]);
        Assert.Equal(1, a.NonlandCurve[ManaCurveBucket.Unknown]);
        Assert.Equal(7, a.NonlandCurve.Values.Sum());
        Assert.Equal(2, a.CreatureCurve.Values.Sum());
        Assert.Equal(1, a.CreatureCurve[ManaCurveBucket.Two]);
    }

    [Fact]
    public void MissingMetadataAndIdentitiesAreNotSilentlyClassifiedAsColorlessOrZeroMana()
    {
        var unknown = Card("unknown", null, "Creature") with { GameplayMetadata = CardGameplayMetadata.Unknown };
        var pool = new DraftPoolSnapshot(new([unknown.Identifier, CardIdentifier.Create("missing")]), DraftPoolCompleteness.Partial, 2);
        var a = new DraftPoolAnalyzer().Analyze(pool, new([unknown]));
        Assert.Equal(4, a.TotalCardCount);
        Assert.Equal(4, a.UnknownTypeCount);
        Assert.Equal(4, a.UnknownColorCount);
        Assert.Equal(1, a.MissingCatalogCardCount);
        Assert.Equal(0, a.ColorlessCount);
        Assert.Equal(0, a.LandCount);
        Assert.Equal(0, a.NonlandCurve.Values.Sum());
    }
}
