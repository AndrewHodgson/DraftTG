using System.Collections.ObjectModel;
using DraftTG.Domain;

namespace DraftTG.RecommendationEngine;

public enum ManaCurveBucket { BelowTwo, Two, Three, Four, Five, Six, SevenPlus, Unknown }

/// <summary>Counts of copies, with overlapping color/type membership. No deck selection or scoring.</summary>
public sealed record DraftPoolAnalysis(
    DraftPoolSnapshot Pool,
    int CreatureCount, int LandCount, int NonlandSpellCount,
    IReadOnlyDictionary<MagicColor, int> ColorCounts, int ColorlessCount, int MulticolorCount,
    IReadOnlyDictionary<CardType, int> TypeCounts,
    IReadOnlyDictionary<ManaCurveBucket, int> NonlandCurve,
    IReadOnlyDictionary<ManaCurveBucket, int> CreatureCurve,
    int UnknownTypeCount, int UnknownColorCount, int MissingCatalogCardCount)
{
    public int TotalCardCount => Pool.TotalCardCount;
    public int UniqueCardCount => Pool.UniqueCardCount;
}

public sealed class DraftPoolAnalyzer
{
    public DraftPoolAnalysis Analyze(DraftPoolSnapshot pool, CardCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(catalog);
        var colors = Enum.GetValues<MagicColor>().ToDictionary(c => c, _ => 0);
        var types = Enum.GetValues<CardType>().Where(t => t != CardType.None).ToDictionary(t => t, _ => 0);
        var spells = Enum.GetValues<ManaCurveBucket>().ToDictionary(b => b, _ => 0);
        var creatures = Enum.GetValues<ManaCurveBucket>().ToDictionary(b => b, _ => 0);
        int creatureCount = 0, landCount = 0, nonlandCount = 0, colorless = 0, multicolor = 0;
        var unknownTypes = pool.UnresolvedOccurrenceCount;
        var unknownColors = pool.UnresolvedOccurrenceCount;
        var missingCatalog = 0;
        foreach (var entry in pool.Entries)
        {
            var card = catalog.Find(entry.CardIdentifier);
            var count = entry.Count;
            if (card is null)
            {
                missingCatalog += count;
                unknownTypes += count;
                unknownColors += count;
                continue;
            }
            var metadata = card.GameplayMetadata;
            if (!metadata.ColorsKnown) unknownColors += count;
            else
            {
                foreach (var color in card.Colors.Colors) colors[color] += count;
                if (card.Colors.IsColorless) colorless += count;
                if (card.Colors.Count > 1) multicolor += count;
            }
            if (!metadata.CardTypes.IsKnown) unknownTypes += count;
            else foreach (var type in types.Keys)
                if (metadata.CardTypes.Contains(type)) types[type] += count;
            if (metadata.IsLand) landCount += count;
            if (metadata.IsCreature)
            {
                creatureCount += count;
                creatures[Bucket(metadata.ManaValue)] += count;
            }
            if (metadata.IsNonlandSpell)
            {
                nonlandCount += count;
                spells[Bucket(metadata.ManaValue)] += count;
            }
        }
        return new(pool, creatureCount, landCount, nonlandCount,
            new ReadOnlyDictionary<MagicColor, int>(colors), colorless, multicolor,
            new ReadOnlyDictionary<CardType, int>(types), new ReadOnlyDictionary<ManaCurveBucket, int>(spells),
            new ReadOnlyDictionary<ManaCurveBucket, int>(creatures), unknownTypes, unknownColors, missingCatalog);
    }

    private static ManaCurveBucket Bucket(double? manaValue) => manaValue switch
    {
        null => ManaCurveBucket.Unknown, < 2 => ManaCurveBucket.BelowTwo, < 3 => ManaCurveBucket.Two,
        < 4 => ManaCurveBucket.Three, < 5 => ManaCurveBucket.Four, < 6 => ManaCurveBucket.Five,
        < 7 => ManaCurveBucket.Six, _ => ManaCurveBucket.SevenPlus
    };
}
