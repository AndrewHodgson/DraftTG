using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using DraftTG.Domain;

namespace DraftTG.Data;

internal sealed class ScryfallCardRecord
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
}

internal sealed class ScryfallCardFace
{
    [JsonPropertyName("colors")]
    public string[]? Colors { get; init; }
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
                collectorNumber!),
            record.ArenaIdentifier);
    }

    private static ColorSet NormalizeColors(ScryfallCardRecord record)
    {
        IEnumerable<string> codes = record.Colors
            ?? record.CardFaces?.SelectMany(face => face.Colors ?? [])
            ?? [];

        return new ColorSet(codes.Select(code => code switch
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
    }

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
