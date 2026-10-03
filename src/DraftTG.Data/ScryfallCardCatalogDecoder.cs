using System.Text.Json;
using DraftTG.Domain;

namespace DraftTG.Data;

/// <summary>Normalizes the legacy JSON-array fixture shape used by DraftTG tests and tools.</summary>
public sealed class ScryfallCardCatalogDecoder
{
    public CardCatalog Decode(string json) => DecodeCatalogData(json).Catalog;

    public CardCatalog Decode(ReadOnlySpan<byte> utf8Json) => DecodeCatalogData(utf8Json).Catalog;

    public ScryfallCardCatalogData DecodeCatalogData(string json) =>
        DecodeCatalogData(System.Text.Encoding.UTF8.GetBytes(json));

    public ScryfallCardCatalogData DecodeCatalogData(ReadOnlySpan<byte> utf8Json)
    {
        ScryfallCardRecord[] records;
        try
        {
            records = JsonSerializer.Deserialize<ScryfallCardRecord[]>(utf8Json)
                ?? throw new JsonException("The catalog root was null.");
        }
        catch (JsonException error)
        {
            throw new CardCatalogImportException(
                CardCatalogImportErrorKind.MalformedJson,
                error.Message,
                error);
        }

        var builder = new ScryfallCardCatalogBuilder();
        foreach (var record in records)
        {
            builder.Add(ScryfallCardNormalizer.Normalize(record));
        }
        return builder.Build();
    }
}
