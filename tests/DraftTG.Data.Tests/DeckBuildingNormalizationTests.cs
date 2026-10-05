using System.Text.Json;
using System.Text.Json.Nodes;
using DraftTG.Domain;

namespace DraftTG.Data.Tests;

public sealed class DeckBuildingNormalizationTests
{
    private static string Fixture() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "phase10a-scryfall-cards.json"));

    [Fact]
    public void CachedRealCardsPopulateMetadataIncludingAdventureAndDoubleFacedCards()
    {
        var catalog = new ScryfallCardCatalogDecoder().DecodeCatalogData(Fixture()).Catalog;
        Card Card(string name) => Assert.Single(catalog.FindByExactName(name));
        Assert.All(catalog.Cards, c => Assert.True(c.GameplayMetadata.CardTypes.IsKnown));
        Assert.All(catalog.Cards, c => Assert.NotNull(c.GameplayMetadata.ManaValue));
        var ginger = Card("Gingerbrute").GameplayMetadata;
        Assert.True(ginger.IsCreature);
        Assert.True(ginger.CardTypes.Contains(CardType.Artifact));
        Assert.Equal(1, ginger.ManaValue);
        Assert.Contains("Haste", ginger.Keywords);
        Assert.Equal("1", ginger.Power);
        Assert.Equal("{1}{B}", Card("Candy Grapple").GameplayMetadata.ManaCost);
        Assert.True(Card("Return from the Wilds").GameplayMetadata.CardTypes.Contains(CardType.Sorcery));
        Assert.True(Card("Hopeless Nightmare").GameplayMetadata.CardTypes.Contains(CardType.Enchantment));
        Assert.True(Card("Voracious Vermin").GameplayMetadata.IsCreature);
        var forest = Card("Forest").GameplayMetadata;
        Assert.True(forest.IsBasicLand);
        Assert.Equal(BasicLandType.Forest, forest.CardTypes.BasicLandType);
        Assert.Equal(0, forest.ManaValue);
        Assert.Equal("", forest.ManaCost);
        Assert.Contains(ManaKind.Green, forest.ProducedMana!);
        Assert.False(Card("Evolving Wilds").GameplayMetadata.IsBasicLand);
        Assert.Null(Card("Evolving Wilds").GameplayMetadata.ProducedMana);
        Assert.Equal(2, Card("Greta, Sweettooth Scourge").Colors.Count);
        var giant = Card("Grabby Giant // That's Mine").GameplayMetadata;
        Assert.True(giant.IsCreature);
        Assert.False(giant.CardTypes.Contains(CardType.Instant));
        Assert.Equal(4, giant.ManaValue);
        Assert.Equal("{3}{R} // {1}{R}", giant.ManaCost);
        Assert.Equal(CardLayout.Adventure, giant.Layout);
        Assert.Equal(2, giant.Faces.Count);
        Assert.True(giant.Faces[1].CardTypes.Contains(CardType.Instant));
        Assert.Null(giant.Faces[1].ManaValue); // No face value supplied; no symbol evaluator.
        Assert.Contains("Treasure", giant.Faces[1].OracleText!);
        var modal = catalog.Cards.Single(c => c.GameplayMetadata.Layout == CardLayout.ModalDoubleFaced);
        Assert.True(modal.GameplayMetadata.IsCreature);
        Assert.False(modal.GameplayMetadata.IsLand);
        Assert.True(modal.GameplayMetadata.Faces[1].CardTypes.Contains(CardType.Land));
        Assert.Equal("{1}{W}", modal.GameplayMetadata.ManaCost);
        Assert.Equal(2, modal.GameplayMetadata.ManaValue);
        var transform = catalog.Cards.Single(c => c.GameplayMetadata.Layout == CardLayout.Transform).GameplayMetadata;
        Assert.True(transform.IsLand);
        Assert.False(transform.CardTypes.Contains(CardType.Artifact));
        var split = catalog.Cards.Single(c => c.GameplayMetadata.Layout == CardLayout.Split).GameplayMetadata;
        Assert.Equal(6, split.ManaValue);
        Assert.True(split.CardTypes.Contains(CardType.Instant | CardType.Sorcery));
    }

    [Fact]
    public void InvalidPresentManaValuesAndTypesProduceFocusedImportErrors()
    {
        var card = JsonNode.Parse(CardDataTestFiles.Card("test"))!.AsObject();
        foreach (var raw in new[] { "-1", "1e999", "\"NaN\"" })
        {
            card["cmc"] = JsonNode.Parse(raw);
            var error = Assert.Throws<CardCatalogImportException>(() => new ScryfallCardCatalogDecoder().DecodeCatalogData($"[{card}]"));
            Assert.Equal(CardCatalogImportErrorKind.InvalidManaValue, error.Kind);
        }
        card.Remove("cmc");
        foreach (var raw in new[] { "\"\"", "42", "\"Legendary\"" })
        {
            card["type_line"] = JsonNode.Parse(raw);
            Assert.Equal(CardCatalogImportErrorKind.InvalidCardType,
                Assert.Throws<CardCatalogImportException>(() => new ScryfallCardCatalogDecoder().DecodeCatalogData($"[{card}]")).Kind);
        }
    }

    [Fact]
    public void AbsentMetadataStaysUnknownAndOptionalFieldsPreserveSourceValues()
    {
        var card = JsonNode.Parse(CardDataTestFiles.Card("test"))!.AsObject();
        var decoder = new ScryfallCardCatalogDecoder();
        var unknown = Assert.Single(decoder.DecodeCatalogData($"[{card}]").Catalog.Cards).GameplayMetadata;
        Assert.Null(unknown.ManaValue);
        Assert.False(unknown.CardTypes.IsKnown);
        card["cmc"] = 0.5; card["type_line"] = "Artifact Creature";
        card["power"] = "*"; card["toughness"] = "1+*";
        card["produced_mana"] = new JsonArray("C", "G", "T");
        var metadata = Assert.Single(decoder.DecodeCatalogData($"[{card}]").Catalog.Cards).GameplayMetadata;
        Assert.Equal(0.5, metadata.ManaValue);
        Assert.Equal("*", metadata.Power);
        Assert.Equal("1+*", metadata.Toughness);
        Assert.Contains(ManaKind.Colorless, metadata.ProducedMana!);
        Assert.Equal("T", Assert.Single(metadata.UnrecognizedManaSymbols));
    }

    [Fact]
    public async Task ExistingRawCacheReparsesMetadataWithoutAnyHttpRequest()
    {
        using var directory = new TemporaryDirectory();
        var now = DateTimeOffset.Parse("2026-10-04T12:00:00Z");
        using var document = JsonDocument.Parse(Fixture());
        var lines = document.RootElement.EnumerateArray().Select(c => JsonSerializer.Serialize(c)).ToArray();
        await CardDataTestFiles.WriteCacheAsync(directory.Path, CardDataTestFiles.Gzip(lines), now, now);
        var handler = new StubHttpMessageHandler((_, _) => throw new InvalidOperationException("No network is allowed in this test."));
        var provider = new ScryfallCardDataProvider(new ScryfallBulkDataClient(new HttpClient(handler)),
            new ScryfallJsonlGzipCardDataLoader(), new FixedApplicationDataPathProvider(directory.Path), new FixedTimeProvider(now));
        var result = await provider.LoadAsync();
        Assert.Equal(ScryfallCardDataLoadSource.Cache, result.Source);
        Assert.False(result.RefreshAttempted);
        Assert.Equal(0, handler.RequestCount);
        Assert.All(result.CardData.Catalog.Cards, c => Assert.True(c.GameplayMetadata.CardTypes.IsKnown));
    }
}
