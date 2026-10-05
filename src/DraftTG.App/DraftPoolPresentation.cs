using System.Globalization;
using System.Text;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;

namespace DraftTG.App;

internal static class DraftPoolPresentation
{
    internal static string Summary(DraftPoolAnalysis analysis, bool completed)
    {
        var pool = analysis.Pool;
        var colors = string.Join("  ", Enum.GetValues<MagicColor>().Select(c => $"{ColorCode(c)} {analysis.ColorCounts[c]}"));
        var curve = string.Join(" · ", Enum.GetValues<ManaCurveBucket>().Select(b => $"{BucketLabel(b)}: {analysis.NonlandCurve[b]}"));
        return (completed ? "Draft complete\n" : "") +
            $"Cards: {pool.TotalCardCount} · Known unique: {pool.UniqueCardCount}\n" +
            $"Creatures: {analysis.CreatureCount} · Lands: {analysis.LandCount}\nNonland spells: {analysis.NonlandSpellCount}\n" +
            $"Colors: {colors}\nColorless: {analysis.ColorlessCount} · Multicolor: {analysis.MulticolorCount}\n" +
            $"Spell curve: {curve}\nPool status: {pool.Completeness}\n" +
            $"Unknown types: {analysis.UnknownTypeCount} · Unknown colors: {analysis.UnknownColorCount}\n" +
            $"Unresolved identities: {pool.UnresolvedOccurrenceCount} · Missing catalog: {analysis.MissingCatalogCardCount}";
    }

    internal static string Entries(DraftPoolSnapshot pool, CardCatalog catalog)
    {
        var text = new StringBuilder();
        foreach (var entry in pool.Entries)
        {
            var card = catalog.Find(entry.CardIdentifier);
            text.AppendLine($"{card?.Name ?? "Unknown card"} ×{entry.Count}\nID: {entry.CardIdentifier.Value}");
            if (card is null) { text.AppendLine("Catalog metadata unavailable\n"); continue; }
            var m = card.GameplayMetadata;
            text.AppendLine($"Colors: {(m.ColorsKnown ? Colors(card.Colors) : "Unknown")}\nMV: {Number(m.ManaValue)} · Cost: {m.ManaCost ?? "Unknown"}");
            text.AppendLine($"Types: {m.CardTypes.Types} ({m.TypeLine ?? "Unknown"})\nCreature: {m.IsCreature} · Land: {m.IsLand} · Basic: {m.IsBasicLand} · Nonland spell: {m.IsNonlandSpell}");
            text.AppendLine($"Basic land type: {m.CardTypes.BasicLandType?.ToString() ?? "None/unknown"}\nLayout: {m.Layout} · Faces: {m.Faces.Count}");
            text.AppendLine($"Keywords: {string.Join(", ", m.Keywords)}\nP/T: {m.Power ?? "Unknown"}/{m.Toughness ?? "Unknown"}");
            text.AppendLine($"Produced mana: {(m.ProducedMana is null ? "Unknown/unavailable" : string.Join(", ", m.ProducedMana))}\nOracle text: {m.OracleText ?? "Unavailable"}");
            if (m.UnrecognizedManaSymbols.Count > 0) text.AppendLine($"Unrecognized mana symbols: {string.Join(", ", m.UnrecognizedManaSymbols)}");
            foreach (var face in m.Faces)
                text.AppendLine($"Face: {face.Name ?? "Unknown"} · {face.TypeLine ?? "Unknown"}\n  Cost: {face.ManaCost ?? "Unknown"} · MV: {Number(face.ManaValue)} · Colors: {(face.Colors is { } fc ? Colors(fc) : "Unknown")}\n  P/T: {face.Power ?? "Unknown"}/{face.Toughness ?? "Unknown"}\n  {face.OracleText ?? "Oracle text unavailable"}");
            text.AppendLine();
        }
        return text.ToString().TrimEnd();
    }

    private static string Number(double? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "Unknown";
    private static string Colors(ColorSet colors) => colors.IsColorless ? "Colorless" : string.Concat(colors.Colors.Select(ColorCode));
    private static string ColorCode(MagicColor color) => color switch
    { MagicColor.White => "W", MagicColor.Blue => "U", MagicColor.Black => "B", MagicColor.Red => "R", MagicColor.Green => "G", _ => "?" };
    private static string BucketLabel(ManaCurveBucket bucket) => bucket switch
    { ManaCurveBucket.BelowTwo => "0–<2", ManaCurveBucket.SevenPlus => "7+", ManaCurveBucket.Unknown => "?", _ => $"{(int)bucket + 1}–<{(int)bucket + 2}" };
}
