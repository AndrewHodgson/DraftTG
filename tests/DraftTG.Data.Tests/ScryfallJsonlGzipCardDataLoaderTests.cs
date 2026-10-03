using DraftTG.Data;
using DraftTG.Domain;

namespace DraftTG.Data.Tests;

public sealed class ScryfallJsonlGzipCardDataLoaderTests
{
    [Fact]
    public async Task StreamsSpecialAndBonusRarities()
    {
        using var directory = new TemporaryDirectory();
        var file = directory.File("cards.jsonl.gz");
        await File.WriteAllBytesAsync(file, CardDataTestFiles.Gzip(
            CardDataTestFiles.Card("special-printing", 101, "special"),
            CardDataTestFiles.Card("bonus-printing", 202, "bonus")));

        var data = await new ScryfallJsonlGzipCardDataLoader().LoadAsync(file);

        Assert.Equal(
            [CardRarity.Special, CardRarity.Bonus],
            data.Catalog.Cards.Select(card => card.Rarity));
    }

    [Fact]
    public async Task StreamsAllJsonlRecordsAndBuildsArenaMappings()
    {
        using var directory = new TemporaryDirectory();
        var file = directory.File("cards.jsonl.gz");
        await File.WriteAllBytesAsync(file, CardDataTestFiles.Gzip(
            CardDataTestFiles.Card("printing-a", 101),
            CardDataTestFiles.Card("printing-b", 202),
            CardDataTestFiles.Card("printing-without-arena")));

        var data = await new ScryfallJsonlGzipCardDataLoader().LoadAsync(file);

        Assert.Equal(3, data.Catalog.Count);
        Assert.Equal("printing-a", data.ArenaIdMappings[101].Value);
        Assert.Equal("printing-b", data.ArenaIdMappings[202].Value);
        Assert.DoesNotContain(data.ArenaIdMappings.Values,
            identifier => identifier.Value == "printing-without-arena");
    }

    [Fact]
    public async Task MalformedMiddleLineFailsWithLineNumber()
    {
        using var directory = new TemporaryDirectory();
        var file = directory.File("cards.jsonl.gz");
        await File.WriteAllBytesAsync(file, CardDataTestFiles.Gzip(
            CardDataTestFiles.Card("printing-a", 101),
            "{not-json}",
            CardDataTestFiles.Card("printing-b", 202)));

        var error = await Assert.ThrowsAsync<ScryfallCardDataException>(() =>
            new ScryfallJsonlGzipCardDataLoader().LoadAsync(file));

        Assert.Equal(ScryfallCardDataErrorKind.MalformedJsonLine, error.Kind);
        Assert.Equal(2, error.LineNumber);
    }

    [Fact]
    public async Task CorruptedGzipProducesFocusedFailure()
    {
        using var directory = new TemporaryDirectory();
        var file = directory.File("cards.jsonl.gz");
        await File.WriteAllTextAsync(file, "not gzip data");

        var error = await Assert.ThrowsAsync<ScryfallCardDataException>(() =>
            new ScryfallJsonlGzipCardDataLoader().LoadAsync(file));

        Assert.Equal(ScryfallCardDataErrorKind.InvalidGzip, error.Kind);
    }

    [Fact]
    public async Task ArenaIdentifierCollisionLoadsAndMatchesArrayDecoderSemantics()
    {
        using var directory = new TemporaryDirectory();
        var file = directory.File("cards.jsonl.gz");
        var first = CardDataTestFiles.Card("printing-a", 101);
        var second = CardDataTestFiles.Card("printing-b", 101);
        await File.WriteAllBytesAsync(file, CardDataTestFiles.Gzip(first, second));

        var streamed = await new ScryfallJsonlGzipCardDataLoader().LoadAsync(file);
        var array = new ScryfallCardCatalogDecoder().DecodeCatalogData($"[{first},{second}]");

        Assert.Equal(2, streamed.Catalog.Count);
        Assert.False(streamed.ArenaIdMappings.ContainsKey(101));
        Assert.Equal(
            array.AmbiguousArenaIds[101],
            streamed.AmbiguousArenaIds[101]);
    }
}
