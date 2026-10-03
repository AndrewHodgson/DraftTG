using System.Net;
using System.Text;
using DraftTG.Data;

namespace DraftTG.Data.Tests;

public sealed class ScryfallBulkDataClientTests
{
    [Fact]
    public async Task MetadataSelectsDefaultCardsJsonlDataset()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler((_, _) => Response("""
            {"data":[
              {"type":"oracle_cards","updated_at":"2026-09-01T00:00:00Z","jsonl_download_uri":"https://data.scryfall.io/oracle.jsonl.gz"},
              {"type":"default_cards","updated_at":"2026-09-20T12:30:00Z","jsonl_download_uri":"https://data.scryfall.io/default.jsonl.gz","compressed_size":123456}
            ]}
            """)));

        var metadata = await new ScryfallBulkDataClient(httpClient)
            .GetDefaultCardsMetadataAsync();

        Assert.Equal("default_cards", metadata.DatasetType);
        Assert.Equal(DateTimeOffset.Parse("2026-09-20T12:30:00Z"), metadata.UpdatedAt);
        Assert.Equal(new Uri("https://data.scryfall.io/default.jsonl.gz"), metadata.JsonlDownloadUri);
        Assert.Equal(123456, metadata.CompressedSize);
    }

    [Fact]
    public async Task MissingDefaultCardsProducesFocusedFailure()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler((_, _) => Response("""
            {"data":[{"type":"oracle_cards","updated_at":"2026-09-20T00:00:00Z","jsonl_download_uri":"https://data.scryfall.io/oracle.jsonl.gz"}]}
            """)));

        var error = await Assert.ThrowsAsync<ScryfallCardDataException>(() =>
            new ScryfallBulkDataClient(httpClient).GetDefaultCardsMetadataAsync());

        Assert.Equal(ScryfallCardDataErrorKind.DefaultCardsNotFound, error.Kind);
    }

    [Theory]
    [InlineData("http://data.scryfall.io/default.jsonl.gz")]
    [InlineData("not-a-uri")]
    public async Task InvalidOrInsecureJsonlDownloadUriProducesFocusedFailure(string uri)
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler((_, _) => Response($$"""
            {"data":[{"type":"default_cards","updated_at":"2026-09-20T00:00:00Z","jsonl_download_uri":"{{uri}}"}]}
            """)));

        var error = await Assert.ThrowsAsync<ScryfallCardDataException>(() =>
            new ScryfallBulkDataClient(httpClient).GetDefaultCardsMetadataAsync());

        Assert.Equal(ScryfallCardDataErrorKind.InvalidDownloadUri, error.Kind);
    }

    [Fact]
    public async Task MetadataRequestSendsDescriptiveUserAgentAndJsonAcceptHeader()
    {
        string? userAgent = null;
        string[] accept = [];
        using var httpClient = new HttpClient(new StubHttpMessageHandler((request, _) =>
        {
            userAgent = request.Headers.GetValues("User-Agent").Single();
            accept = request.Headers.Accept.Select(value => value.MediaType!).ToArray();
            return Response("""
                {"data":[{"type":"default_cards","updated_at":"2026-09-20T00:00:00Z","jsonl_download_uri":"https://data.scryfall.io/default.jsonl.gz"}]}
                """);
        }));

        await new ScryfallBulkDataClient(httpClient, "DraftTG.Tests/1.2")
            .GetDefaultCardsMetadataAsync();

        Assert.Equal("DraftTG.Tests/1.2", userAgent);
        Assert.Contains("application/json", accept);
    }

    private static HttpResponseMessage Response(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };
}

internal sealed class StubHttpMessageHandler(
    Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler)
    : HttpMessageHandler
{
    public int RequestCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        RequestCount++;
        return Task.FromResult(handler(request, cancellationToken));
    }
}
