using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DraftTG.Data;

public sealed record ScryfallBulkDataMetadata(
    string DatasetType,
    DateTimeOffset UpdatedAt,
    Uri JsonlDownloadUri,
    long? CompressedSize = null);

public interface IScryfallBulkDataClient
{
    Task<ScryfallBulkDataMetadata> GetDefaultCardsMetadataAsync(
        CancellationToken cancellationToken = default);

    Task DownloadAsync(
        Uri downloadUri,
        string destinationPath,
        CancellationToken cancellationToken = default);
}

public sealed class ScryfallBulkDataClient : IScryfallBulkDataClient
{
    private static readonly Uri BulkMetadataUri = new("https://api.scryfall.com/bulk-data");
    private readonly HttpClient _httpClient;
    private readonly string _userAgent;

    public ScryfallBulkDataClient(HttpClient httpClient, string userAgent = "DraftTG/1.0")
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        ArgumentException.ThrowIfNullOrWhiteSpace(userAgent);
        _userAgent = userAgent;
    }

    public async Task<ScryfallBulkDataMetadata> GetDefaultCardsMetadataAsync(
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, BulkMetadataUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            EnsureHttpsResponse(response);
            if (!response.IsSuccessStatusCode)
            {
                throw new ScryfallCardDataException(
                    ScryfallCardDataErrorKind.MetadataRequestFailed,
                    $"Scryfall bulk metadata request failed with status {(int)response.StatusCode}.");
            }

            await using var stream = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            BulkDataResponse payload;
            try
            {
                payload = await JsonSerializer.DeserializeAsync<BulkDataResponse>(
                    stream,
                    cancellationToken: cancellationToken).ConfigureAwait(false)
                    ?? throw new JsonException("The Scryfall bulk metadata response was null.");
            }
            catch (JsonException error)
            {
                throw new ScryfallCardDataException(
                    ScryfallCardDataErrorKind.InvalidBulkMetadata,
                    "The Scryfall bulk metadata response was invalid.",
                    error);
            }

            var matches = payload.Data?.Where(entry =>
                string.Equals(entry.Type, "default_cards", StringComparison.Ordinal)).ToArray()
                ?? [];
            if (matches.Length != 1)
            {
                throw new ScryfallCardDataException(
                    ScryfallCardDataErrorKind.DefaultCardsNotFound,
                    "Scryfall bulk metadata did not contain exactly one default_cards dataset.");
            }
            var item = matches[0];
            if (item.UpdatedAt == default)
            {
                throw new ScryfallCardDataException(
                    ScryfallCardDataErrorKind.InvalidBulkMetadata,
                    "The Scryfall default_cards metadata omitted a valid updated_at value.");
            }
            if (!Uri.TryCreate(item.JsonlDownloadUri, UriKind.Absolute, out var downloadUri)
                || downloadUri.Scheme != Uri.UriSchemeHttps)
            {
                throw new ScryfallCardDataException(
                    ScryfallCardDataErrorKind.InvalidDownloadUri,
                    "The Scryfall default_cards jsonl_download_uri was not a valid HTTPS URI.");
            }

            return new ScryfallBulkDataMetadata(
                item.Type!,
                item.UpdatedAt,
                downloadUri,
                item.CompressedSize);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ScryfallCardDataException)
        {
            throw;
        }
        catch (Exception error) when (error is HttpRequestException or IOException)
        {
            throw new ScryfallCardDataException(
                ScryfallCardDataErrorKind.MetadataRequestFailed,
                "The Scryfall bulk metadata request failed.",
                error);
        }
    }

    public async Task DownloadAsync(
        Uri downloadUri,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(downloadUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        if (!downloadUri.IsAbsoluteUri || downloadUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ScryfallCardDataException(
                ScryfallCardDataErrorKind.InvalidDownloadUri,
                "The Scryfall bulk download URI must use HTTPS.");
        }

        using var request = CreateRequest(HttpMethod.Get, downloadUri);
        try
        {
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            EnsureHttpsResponse(response);
            if (!response.IsSuccessStatusCode)
            {
                throw new ScryfallCardDataException(
                    ScryfallCardDataErrorKind.BulkDownloadFailed,
                    $"Scryfall bulk download failed with status {(int)response.StatusCode}.");
            }

            await using var source = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            await using var destination = new FileStream(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await source.CopyToAsync(destination, 64 * 1024, cancellationToken)
                .ConfigureAwait(false);
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ScryfallCardDataException)
        {
            throw;
        }
        catch (Exception error) when (error is HttpRequestException or IOException)
        {
            throw new ScryfallCardDataException(
                ScryfallCardDataErrorKind.BulkDownloadFailed,
                "The Scryfall bulk download failed.",
                error);
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, Uri uri)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.TryAddWithoutValidation("User-Agent", _userAgent);
        return request;
    }

    private static void EnsureHttpsResponse(HttpResponseMessage response)
    {
        if (response.RequestMessage?.RequestUri is { } finalUri
            && finalUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ScryfallCardDataException(
                ScryfallCardDataErrorKind.InvalidDownloadUri,
                "Scryfall redirected the request to a non-HTTPS URI.");
        }
    }

    private sealed class BulkDataResponse
    {
        [JsonPropertyName("data")]
        public BulkDataItem[]? Data { get; init; }
    }

    private sealed class BulkDataItem
    {
        [JsonPropertyName("type")]
        public string? Type { get; init; }

        [JsonPropertyName("updated_at")]
        public DateTimeOffset UpdatedAt { get; init; }

        [JsonPropertyName("jsonl_download_uri")]
        public string? JsonlDownloadUri { get; init; }

        [JsonPropertyName("compressed_size")]
        public long? CompressedSize { get; init; }
    }
}
