using DraftTG.Data;
using DraftTG.Domain;
using System.Collections;

namespace DraftTG.Data.Tests;

public sealed class ScryfallCardCatalogDecoderTests
{
    private readonly ScryfallCardCatalogDecoder _decoder = new();

    [Fact]
    public void OrdinaryRecordNormalizesIntoDomainCard()
    {
        var catalog = Decode("""
            [{
              "id":"scryfall-printing-1","name":"Synthetic Adept","colors":["U"],
              "rarity":"uncommon","set":"tst","collector_number":"42"
            }]
            """);

        var card = Assert.Single(catalog.Cards);
        Assert.Equal("scryfall-printing-1", card.Identifier.Value);
        Assert.Equal("Synthetic Adept", card.Name);
        Assert.Equal([MagicColor.Blue], card.Colors.Colors);
        Assert.Equal(CardRarity.Uncommon, card.Rarity);
        Assert.Equal("tst", card.SetCode.Value);
        Assert.Equal("42", card.CollectorNumber.Value);
    }

    [Fact]
    public void EmptyColorsProduceColorlessCard()
    {
        var card = Assert.Single(Decode(Record(colors: "[]")).Cards);
        Assert.True(card.Colors.IsColorless);
    }

    [Fact]
    public void MultipleAndDuplicateColorsProduceCanonicalSet()
    {
        var card = Assert.Single(Decode(Record(colors: "[\"R\",\"W\",\"R\"]")).Cards);
        Assert.Equal([MagicColor.White, MagicColor.Red], card.Colors.Colors);
    }

    [Fact]
    public void FaceColorsAreUnionedWhenTopLevelColorsAreAbsent()
    {
        var json = """
            [{"id":"one","name":"Split","rarity":"rare","set":"tst","collector_number":"1",
              "card_faces":[{"colors":["U"]},{"colors":["R","U"]}]}]
            """;
        var card = Assert.Single(Decode(json).Cards);
        Assert.Equal([MagicColor.Blue, MagicColor.Red], card.Colors.Colors);
    }

    [Fact]
    public void PresentTopLevelColorsTakePrecedenceOverFaces()
    {
        var json = """
            [{"id":"one","name":"Split","colors":[],"rarity":"rare","set":"tst","collector_number":"1",
              "card_faces":[{"colors":["U"]},{"colors":["R"]}]}]
            """;
        Assert.True(Assert.Single(Decode(json).Cards).Colors.IsColorless);
    }

    [Theory]
    [InlineData("[\"X\"]", CardCatalogImportErrorKind.UnsupportedColorCode)]
    [InlineData("[\"W\"]", CardCatalogImportErrorKind.UnsupportedRarity, "ultra")]
    public void UnsupportedProviderValuesProduceTypedErrors(
        string colors,
        CardCatalogImportErrorKind expected,
        string rarity = "common")
    {
        var error = Assert.Throws<CardCatalogImportException>(
            () => Decode(Record(colors: colors, rarity: rarity)));
        Assert.Equal(expected, error.Kind);
    }

    [Theory]
    [InlineData("common", CardRarity.Common)]
    [InlineData("uncommon", CardRarity.Uncommon)]
    [InlineData("rare", CardRarity.Rare)]
    [InlineData("mythic", CardRarity.Mythic)]
    [InlineData("special", CardRarity.Special)]
    [InlineData("bonus", CardRarity.Bonus)]
    public void DocumentedScryfallRaritiesNormalizeToDomainValues(
        string rarity,
        CardRarity expected)
    {
        var card = Assert.Single(Decode(Record(rarity: rarity)).Cards);

        Assert.Equal(expected, card.Rarity);
    }

    [Theory]
    [InlineData("", "tst", "1", CardCatalogImportErrorKind.InvalidCardIdentifier)]
    [InlineData("one", " ", "1", CardCatalogImportErrorKind.InvalidSetCode)]
    [InlineData("one", "tst", "", CardCatalogImportErrorKind.InvalidCollectorNumber)]
    public void InvalidNormalizedValuesProduceTypedErrors(
        string id,
        string setCode,
        string collector,
        CardCatalogImportErrorKind expected)
    {
        var error = Assert.Throws<CardCatalogImportException>(
            () => Decode(Record(id, setCode, collectorNumber: collector)));
        Assert.Equal(expected, error.Kind);
    }

    [Fact]
    public void DuplicateIdentifiersProduceTypedImportError()
    {
        var duplicate = RecordObject("same") + "," + RecordObject("same");
        var error = Assert.Throws<CardCatalogImportException>(() => Decode($"[{duplicate}]"));
        Assert.Equal(CardCatalogImportErrorKind.DuplicateCardIdentifier, error.Kind);
        Assert.Equal(CardIdentifier.Create("same"), error.CardIdentifier);
    }

    [Fact]
    public void UnrelatedPropertiesAreIgnored()
    {
        var json = """
            [{"id":"one","name":"Card","colors":["G"],"rarity":"common","set":"tst",
              "collector_number":"1","oracle_text":"ignored","image_uris":{"normal":"ignored"}}]
            """;
        Assert.Single(Decode(json).Cards);
    }

    [Fact]
    public void MalformedJsonProducesTypedError()
    {
        var error = Assert.Throws<CardCatalogImportException>(() => Decode("[{not-json}]"));
        Assert.Equal(CardCatalogImportErrorKind.MalformedJson, error.Kind);
        Assert.NotEmpty(error.Detail);
    }

    [Fact]
    public void ArenaIdentifierIsImportedAsAuxiliaryMapping()
    {
        var data = _decoder.DecodeCatalogData(Record(arenaIdentifier: "12345"));

        Assert.Single(data.Catalog.Cards);
        Assert.Equal(CardIdentifier.Create("one"), data.ArenaIdMappings[12345]);
    }

    [Fact]
    public void MissingArenaIdentifierKeepsCardWithoutMapping()
    {
        var data = _decoder.DecodeCatalogData(Record());

        Assert.Single(data.Catalog.Cards);
        Assert.Empty(data.ArenaIdMappings);
    }

    [Fact]
    public void NullArenaIdentifierKeepsCardWithoutMapping()
    {
        var data = _decoder.DecodeCatalogData(Record(arenaIdentifier: "null"));

        Assert.Single(data.Catalog.Cards);
        Assert.Empty(data.ArenaIdMappings);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void NonPositiveArenaIdentifierProducesFocusedError(int arenaIdentifier)
    {
        var error = Assert.Throws<CardCatalogImportException>(
            () => _decoder.DecodeCatalogData(Record(arenaIdentifier: arenaIdentifier.ToString())));

        Assert.Equal(CardCatalogImportErrorKind.InvalidArenaIdentifier, error.Kind);
        Assert.Equal(arenaIdentifier, error.ArenaIdentifier);
    }

    [Fact]
    public void DuplicateArenaIdentifierBecomesAmbiguousWithoutDroppingCards()
    {
        var records =
            RecordObject("first", arenaIdentifier: "101") + "," +
            RecordObject("second", arenaIdentifier: "101");

        var data = _decoder.DecodeCatalogData($"[{records}]");

        Assert.Equal(2, data.Catalog.Count);
        Assert.False(data.ArenaIdMappings.ContainsKey(101));
        Assert.Equal(
            [CardIdentifier.Create("first"), CardIdentifier.Create("second")],
            data.AmbiguousArenaIds[101]);
    }

    [Fact]
    public void ThreeAmbiguousCandidatesRetainFirstSeenOrderAndUniqueMappingIsUnaffected()
    {
        var records = string.Join(",",
            RecordObject("first", arenaIdentifier: "101"),
            RecordObject("unrelated", arenaIdentifier: "202"),
            RecordObject("second", arenaIdentifier: "101"),
            RecordObject("third", arenaIdentifier: "101"));

        var data = _decoder.DecodeCatalogData($"[{records}]");

        Assert.Equal(
            ["first", "second", "third"],
            data.AmbiguousArenaIds[101].Select(identifier => identifier.Value));
        Assert.Equal(CardIdentifier.Create("unrelated"), data.ArenaIdMappings[202]);
        Assert.DoesNotContain(101, data.ArenaIdMappings.Keys);
    }

    [Fact]
    public void MultipleArenaIdentifiersProduceIndependentMappings()
    {
        var records =
            RecordObject("first", arenaIdentifier: "101") + "," +
            RecordObject("second", arenaIdentifier: "202");
        var data = _decoder.DecodeCatalogData($"[{records}]");

        Assert.Equal(2, data.Catalog.Count);
        Assert.Equal(CardIdentifier.Create("first"), data.ArenaIdMappings[101]);
        Assert.Equal(CardIdentifier.Create("second"), data.ArenaIdMappings[202]);
    }

    [Fact]
    public void ImportedMappingCannotBeMutatedByExternalCallers()
    {
        var data = _decoder.DecodeCatalogData(Record(arenaIdentifier: "101"));

        Assert.IsAssignableFrom<IReadOnlyDictionary<int, CardIdentifier>>(data.ArenaIdMappings);
        var genericDictionary = Assert.IsAssignableFrom<IDictionary<int, CardIdentifier>>(
            data.ArenaIdMappings);
        Assert.True(genericDictionary.IsReadOnly);
        Assert.Throws<NotSupportedException>(
            () => genericDictionary.Add(202, CardIdentifier.Create("another")));

        var dictionary = Assert.IsAssignableFrom<IDictionary>(data.ArenaIdMappings);
        Assert.True(dictionary.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => dictionary.Add(202, "another"));
    }

    [Fact]
    public void ImportedAmbiguitiesAndCandidateListsCannotBeMutatedByExternalCallers()
    {
        var records =
            RecordObject("first", arenaIdentifier: "101") + "," +
            RecordObject("second", arenaIdentifier: "101");
        var data = _decoder.DecodeCatalogData($"[{records}]");

        var ambiguities = Assert.IsAssignableFrom<
            IDictionary<int, IReadOnlyList<CardIdentifier>>>(data.AmbiguousArenaIds);
        Assert.True(ambiguities.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => ambiguities.Add(202, []));

        var candidates = Assert.IsAssignableFrom<IList<CardIdentifier>>(
            data.AmbiguousArenaIds[101]);
        Assert.True(candidates.IsReadOnly);
        Assert.Throws<NotSupportedException>(
            () => candidates.Add(CardIdentifier.Create("third")));
    }

    [Fact]
    public void ExistingDecodeApiStillReturnsCatalog()
    {
        var catalog = Decode(Record(arenaIdentifier: "101"));

        Assert.Equal(CardIdentifier.Create("one"), Assert.Single(catalog.Cards).Identifier);
    }

    private CardCatalog Decode(string json) => _decoder.Decode(json);

    private static string Record(
        string id = "one",
        string setCode = "tst",
        string colors = "[\"W\"]",
        string rarity = "common",
        string collectorNumber = "1",
        string? arenaIdentifier = null) =>
        $"[{RecordObject(id, setCode, colors, rarity, collectorNumber, arenaIdentifier)}]";

    private static string RecordObject(
        string id,
        string setCode = "tst",
        string colors = "[\"W\"]",
        string rarity = "common",
        string collectorNumber = "1",
        string? arenaIdentifier = null) =>
        $$"""{"id":"{{id}}","name":"Card","colors":{{colors}},"rarity":"{{rarity}}","set":"{{setCode}}","collector_number":"{{collectorNumber}}"{{(arenaIdentifier is null ? string.Empty : $",\"arena_id\":{arenaIdentifier}")}}}""";
}
