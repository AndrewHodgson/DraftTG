using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using DraftTG.Domain;

namespace DraftTG.Data;

internal sealed class ScryfallCardRecord : ScryfallGameplayFields
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("colors")]
    public string[]? Colors { get; init; }

    [JsonPropertyName("rarity")]
    public string? Rarity { get; init; }

    [JsonPropertyName("set")]
    public string? SetCode { get; init; }

    [JsonPropertyName("collector_number")]
    public string? CollectorNumber { get; init; }

    [JsonPropertyName("card_faces")]
    public ScryfallCardFace[]? CardFaces { get; init; }

    [JsonPropertyName("arena_id")]
    public int? ArenaIdentifier { get; init; }

    [JsonPropertyName("layout")]
    public string? Layout { get; init; }
}

internal sealed class ScryfallCardFace : ScryfallGameplayFields
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("colors")]
    public string[]? Colors { get; init; }
}

internal class ScryfallGameplayFields
{
    [JsonPropertyName("cmc")] public JsonElement ManaValue { get; init; }
    [JsonPropertyName("mana_cost")] public string? ManaCost { get; init; }
    [JsonPropertyName("type_line")] public JsonElement TypeLine { get; init; }
    [JsonPropertyName("oracle_text")] public string? OracleText { get; init; }
    [JsonPropertyName("keywords")] public string[]? Keywords { get; init; }
    [JsonPropertyName("power")] public string? Power { get; init; }
    [JsonPropertyName("toughness")] public string? Toughness { get; init; }
    [JsonPropertyName("produced_mana")] public string[]? ProducedMana { get; init; }
}

internal sealed record NormalizedScryfallCard(Card Card, int? ArenaIdentifier);

internal static class ScryfallCardNormalizer
{
    internal static NormalizedScryfallCard DecodeAndNormalize(string json)
    {
        ScryfallCardRecord record;
        try
        {
            record = JsonSerializer.Deserialize<ScryfallCardRecord>(json)
                ?? throw new JsonException("The card record was null.");
        }
        catch (JsonException error)
        {
            throw new CardCatalogImportException(
                CardCatalogImportErrorKind.MalformedJson,
                error.Message,
                error);
        }

        return Normalize(record);
    }

    internal static NormalizedScryfallCard Normalize(ScryfallCardRecord record)
    {
        if (!CardIdentifier.TryCreate(record.Id, out var identifier))
        {
            throw new CardCatalogImportException(
                CardCatalogImportErrorKind.InvalidCardIdentifier,
                record.Id ?? string.Empty);
        }
        if (!CardSetCode.TryCreate(record.SetCode, out var setCode))
        {
            throw new CardCatalogImportException(
                CardCatalogImportErrorKind.InvalidSetCode,
                record.SetCode ?? string.Empty);
        }
        if (!CollectorNumber.TryCreate(record.CollectorNumber, out var collectorNumber))
        {
            throw new CardCatalogImportException(
                CardCatalogImportErrorKind.InvalidCollectorNumber,
                record.CollectorNumber ?? string.Empty);
        }
        if (record.ArenaIdentifier is <= 0)
        {
            throw new CardCatalogImportException(
                CardCatalogImportErrorKind.InvalidArenaIdentifier,
                record.ArenaIdentifier.Value.ToString(CultureInfo.InvariantCulture),
                arenaIdentifier: record.ArenaIdentifier);
        }

        return new NormalizedScryfallCard(
            new Card(
                identifier!,
                record.Name ?? string.Empty,
                NormalizeColors(record),
                NormalizeRarity(record.Rarity),
                setCode!,
                collectorNumber!) { GameplayMetadata = NormalizeGameplay(record) },
            record.ArenaIdentifier);
    }

    private static ColorSet NormalizeColors(ScryfallCardRecord record)
    {
        IEnumerable<string> codes = record.Colors
            ?? record.CardFaces?.SelectMany(face => face.Colors ?? [])
            ?? [];

        return NormalizeColorCodes(codes);
    }

    private static ColorSet NormalizeColorCodes(IEnumerable<string> codes) => new(codes.Select(code => code switch
        {
            "W" => MagicColor.White,
            "U" => MagicColor.Blue,
            "B" => MagicColor.Black,
            "R" => MagicColor.Red,
            "G" => MagicColor.Green,
            _ => throw new CardCatalogImportException(
                CardCatalogImportErrorKind.UnsupportedColorCode,
                code)
        }));

    private static CardGameplayMetadata NormalizeGameplay(ScryfallCardRecord record)
    {
        var layout = record.Layout switch
        {
            null or "normal" => CardLayout.Normal, "adventure" => CardLayout.Adventure,
            "transform" => CardLayout.Transform, "modal_dfc" => CardLayout.ModalDoubleFaced,
            "split" => CardLayout.Split, "prepare" => CardLayout.Prepare, _ => CardLayout.Other
        };
        var manaValue = NormalizeManaValue(record.ManaValue);
        var faces = (record.CardFaces ?? []).Select((f, i) => new CardFaceMetadata(
            f.Name, f.ManaCost, NormalizeManaValue(f.ManaValue) ??
                (i == 0 && layout is CardLayout.Adventure or CardLayout.Transform or CardLayout.ModalDoubleFaced ? manaValue : null),
            f.Colors is null ? null : NormalizeColorCodes(f.Colors),
            NormalizeTypeLine(f.TypeLine),
            f.OracleText, f.Power, f.Toughness)).ToArray();
        var front = faces.FirstOrDefault();
        return new CardGameplayMetadata(manaValue ?? front?.ManaValue,
            record.ManaCost ?? front?.ManaCost, NormalizeTypeLine(record.TypeLine) ?? front?.TypeLine,
            layout, faces, record.OracleText ?? front?.OracleText, record.Keywords,
            record.Power ?? front?.Power, record.Toughness ?? front?.Toughness,
            record.ProducedMana?.Select(NormalizeManaKind).Where(kind => kind.HasValue).Select(kind => kind!.Value),
            record.Colors is not null || record.CardFaces is { Length: > 0 } && record.CardFaces.All(f => f.Colors is not null),
            record.ProducedMana?.Where(code => NormalizeManaKind(code) is null));
    }

    private static double? NormalizeManaValue(JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number) || !double.IsFinite(number) || number < 0)
            throw new CardCatalogImportException(CardCatalogImportErrorKind.InvalidManaValue, value.GetRawText());
        return number;
    }

    private static string? NormalizeTypeLine(JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.String)
            throw new CardCatalogImportException(CardCatalogImportErrorKind.InvalidCardType, value.GetRawText());
        var text = value.GetString();
        try { _ = CardTypeSet.Parse(text); }
        catch (ArgumentException error) { throw new CardCatalogImportException(CardCatalogImportErrorKind.InvalidCardType, text ?? "", error); }
        return text;
    }

    private static ManaKind? NormalizeManaKind(string code) => code switch
    {
        "W" => ManaKind.White, "U" => ManaKind.Blue, "B" => ManaKind.Black,
        "R" => ManaKind.Red, "G" => ManaKind.Green, "C" => ManaKind.Colorless,
        _ => null
    };

    private static CardRarity NormalizeRarity(string? rarity) => rarity switch
    {
        "common" => CardRarity.Common,
        "uncommon" => CardRarity.Uncommon,
        "rare" => CardRarity.Rare,
        "mythic" => CardRarity.Mythic,
        "special" => CardRarity.Special,
        "bonus" => CardRarity.Bonus,
        _ => throw new CardCatalogImportException(
            CardCatalogImportErrorKind.UnsupportedRarity,
            rarity ?? string.Empty)
    };
}

internal sealed class ScryfallCardCatalogBuilder
{
    private readonly List<Card> _cards = [];
    private readonly HashSet<CardIdentifier> _cardIdentifiers = [];
    private readonly Dictionary<int, CardIdentifier> _arenaIdMappings = [];
    private readonly Dictionary<int, List<CardIdentifier>> _ambiguousArenaIds = [];

    internal int Count => _cards.Count;

    internal void Add(NormalizedScryfallCard record)
    {
        if (!_cardIdentifiers.Add(record.Card.Identifier))
        {
            throw new CardCatalogImportException(
                CardCatalogImportErrorKind.DuplicateCardIdentifier,
                record.Card.Identifier.Value,
                cardIdentifier: record.Card.Identifier);
        }

        _cards.Add(record.Card);
        if (record.ArenaIdentifier is { } mappedArenaIdentifier)
        {
            AddArenaMapping(mappedArenaIdentifier, record.Card.Identifier);
        }
    }

    internal ScryfallCardCatalogData Build() => new(
        new CardCatalog(_cards),
        _arenaIdMappings,
        _ambiguousArenaIds.Select(pair =>
            new KeyValuePair<int, IReadOnlyList<CardIdentifier>>(pair.Key, pair.Value)));

    private void AddArenaMapping(int arenaIdentifier, CardIdentifier cardIdentifier)
    {
        if (_ambiguousArenaIds.TryGetValue(arenaIdentifier, out var candidates))
        {
            if (!candidates.Contains(cardIdentifier)) candidates.Add(cardIdentifier);
            return;
        }

        if (_arenaIdMappings.TryGetValue(arenaIdentifier, out var existing))
        {
            if (existing == cardIdentifier) return;
            _arenaIdMappings.Remove(arenaIdentifier);
            _ambiguousArenaIds.Add(arenaIdentifier, [existing, cardIdentifier]);
            return;
        }

        _arenaIdMappings.Add(arenaIdentifier, cardIdentifier);
    }
}
