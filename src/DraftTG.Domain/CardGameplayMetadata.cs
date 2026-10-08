namespace DraftTG.Domain;

[Flags]
public enum CardType
{
    None = 0, Creature = 1, Land = 2, Instant = 4, Sorcery = 8, Artifact = 16,
    Enchantment = 32, Planeswalker = 64, Battle = 128, Other = 256
}

public enum BasicLandType { Plains, Island, Swamp, Mountain, Forest }
public enum ManaKind { White, Blue, Black, Red, Green, Colorless }
/// <summary>Scryfall layouts DraftTG distinguishes; Prepare is Scryfall's `prepare` (front creature plus a prepared spell face).</summary>
public enum CardLayout { Normal, Adventure, Transform, ModalDoubleFaced, Split, Other, Prepare }

/// <summary>Central type interpretation. Missing types stay unknown; types and subtypes are distinct.</summary>
public readonly record struct CardTypeSet(CardType Types, bool IsKnown, bool IsBasic, BasicLandType? BasicLandType)
{
    public bool Contains(CardType type) => type != CardType.None && (Types & type) == type;
    public static CardTypeSet Unknown => default;

    public static CardTypeSet Parse(string? typeLine)
    {
        if (typeLine is null) return Unknown;
        if (string.IsNullOrWhiteSpace(typeLine)) throw new ArgumentException("A present type line cannot be empty.", nameof(typeLine));
        CardType types = CardType.None;
        var basic = false;
        BasicLandType? basicLand = null;
        foreach (var part in typeLine.Split("//", StringSplitOptions.TrimEntries))
        {
            var sections = part.Split('—', 2, StringSplitOptions.TrimEntries);
            var tokens = sections[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) throw new ArgumentException("A type line must contain card types.", nameof(typeLine));
            foreach (var token in tokens)
            {
                basic |= token == "Basic";
                types |= token switch
                {
                    "Creature" => CardType.Creature, "Land" => CardType.Land,
                    "Instant" => CardType.Instant, "Sorcery" => CardType.Sorcery,
                    "Artifact" => CardType.Artifact, "Enchantment" => CardType.Enchantment,
                    "Planeswalker" => CardType.Planeswalker, "Battle" => CardType.Battle,
                    "Basic" or "Legendary" or "Snow" or "World" or "Ongoing" or "Kindred" or "Tribal" => CardType.None,
                    _ => CardType.Other
                };
            }
            if (sections.Length == 2 && basic && (types & CardType.Land) != 0)
                foreach (var token in sections[1].Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    if (Enum.TryParse<BasicLandType>(token, out var parsed) && Enum.IsDefined(parsed)) basicLand = parsed;
        }
        if (types == CardType.None) throw new ArgumentException("A type line must contain a card type, not only supertypes.", nameof(typeLine));
        return new(types, true, basic && (types & CardType.Land) != 0, basicLand);
    }
}

public sealed record CardFaceMetadata(
    string? Name, string? ManaCost, double? ManaValue, ColorSet? Colors,
    string? TypeLine, string? OracleText, string? Power, string? Toughness)
{
    public CardTypeSet CardTypes => CardTypeSet.Parse(TypeLine);
}

/// <summary>Immutable descriptive profile. Null values mean unavailable, including an unknown mana value.</summary>
public sealed class CardGameplayMetadata : IEquatable<CardGameplayMetadata>
{
    public static CardGameplayMetadata Unknown { get; } = new(colorsKnown: false);

    public CardGameplayMetadata(double? manaValue = null, string? manaCost = null, string? typeLine = null,
        CardLayout layout = CardLayout.Normal, IEnumerable<CardFaceMetadata>? faces = null,
        string? oracleText = null, IEnumerable<string>? keywords = null, string? power = null,
        string? toughness = null, IEnumerable<ManaKind>? producedMana = null, bool colorsKnown = true,
        IEnumerable<string>? unrecognizedManaSymbols = null)
    {
        ValidateManaValue(manaValue);
        ManaValue = manaValue;
        ManaCost = manaCost;
        TypeLine = typeLine;
        Layout = layout;
        Faces = Array.AsReadOnly((faces ?? []).ToArray());
        foreach (var face in Faces)
        {
            ValidateManaValue(face.ManaValue);
            _ = face.CardTypes;
        }
        // Split cards describe both halves. Other multiface cards use their permanent/front face.
        var primaryTypeLine = layout != CardLayout.Split && Faces.Count > 0 ? Faces[0].TypeLine ?? typeLine?.Split("//")[0] : typeLine;
        CardTypes = CardTypeSet.Parse(primaryTypeLine);
        OracleText = oracleText;
        Keywords = Array.AsReadOnly((keywords ?? []).ToArray());
        Power = power;
        Toughness = toughness;
        ProducedMana = producedMana is null ? null : Array.AsReadOnly(producedMana.Distinct().Order().ToArray());
        ColorsKnown = colorsKnown;
        UnrecognizedManaSymbols = Array.AsReadOnly((unrecognizedManaSymbols ?? []).Distinct().Order(StringComparer.Ordinal).ToArray());
    }

    public double? ManaValue { get; }
    public string? ManaCost { get; }
    public string? TypeLine { get; }
    public CardTypeSet CardTypes { get; }
    public CardLayout Layout { get; }
    public IReadOnlyList<CardFaceMetadata> Faces { get; }
    public string? OracleText { get; }
    public IReadOnlyList<string> Keywords { get; }
    public string? Power { get; }
    public string? Toughness { get; }
    public IReadOnlyList<ManaKind>? ProducedMana { get; }
    public IReadOnlyList<string> UnrecognizedManaSymbols { get; }
    public bool ColorsKnown { get; }
    public bool IsCreature => CardTypes.Contains(CardType.Creature);
    public bool IsLand => CardTypes.Contains(CardType.Land);
    public bool IsBasicLand => IsLand && CardTypes.IsBasic;
    public bool IsNonlandSpell => CardTypes.IsKnown && !IsLand && (CardTypes.Types & ~CardType.Other) != CardType.None;

    private static void ValidateManaValue(double? value)
    {
        if (value is { } number && (!double.IsFinite(number) || number < 0))
            throw new ArgumentOutOfRangeException(nameof(value), "Mana value must be nonnegative and finite, or unknown.");
    }

    public bool Equals(CardGameplayMetadata? other) => other is not null &&
        ManaValue == other.ManaValue && ManaCost == other.ManaCost && TypeLine == other.TypeLine &&
        Layout == other.Layout && CardTypes == other.CardTypes && Faces.SequenceEqual(other.Faces) &&
        OracleText == other.OracleText && Keywords.SequenceEqual(other.Keywords) && Power == other.Power &&
        Toughness == other.Toughness && ColorsKnown == other.ColorsKnown && UnrecognizedManaSymbols.SequenceEqual(other.UnrecognizedManaSymbols) &&
        (ProducedMana is null ? other.ProducedMana is null : other.ProducedMana is not null && ProducedMana.SequenceEqual(other.ProducedMana));
    public override bool Equals(object? obj) => Equals(obj as CardGameplayMetadata);
    public override int GetHashCode() => HashCode.Combine(ManaValue, ManaCost, TypeLine, Layout, OracleText, Power, Toughness, ColorsKnown);
}
