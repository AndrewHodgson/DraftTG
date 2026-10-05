using System.IO.Compression;
using System.Text.Json;
using DraftTG.Domain;
using SkiaSharp;

namespace DraftTG.App;

/// <summary>Only small front images of the current pack. URLs come from the existing local Scryfall bulk file.</summary>
internal sealed class ScryfallVisualReferenceCache(string dataDirectory, HttpClient http) : IDisposable
{
    internal const int MaximumImageBytes = 256 * 1024;
    private readonly Dictionary<CardIdentifier, SKBitmap> _images = [];
    private readonly Dictionary<string, Uri> _urls = new(StringComparer.OrdinalIgnoreCase);
    private bool _indexed;

    public async Task<IReadOnlyDictionary<CardIdentifier, SKBitmap>> GetAsync(IEnumerable<CardIdentifier> identifiers,
        CancellationToken cancellationToken)
    {
        var wanted = identifiers.Distinct().Take(14).ToArray();
        if (!_indexed) await IndexAsync(cancellationToken);
        var cache = Path.Combine(dataDirectory, "visual-references");
        Directory.CreateDirectory(cache);
        foreach (var id in wanted)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_images.ContainsKey(id) || !Guid.TryParse(id.Value, out var guid)) continue;
            var key = guid.ToString("D");
            var path = Path.Combine(cache, key + ".jpg");
            try
            {
                byte[]? bytes = null;
                if (File.Exists(path) && new FileInfo(path).Length <= MaximumImageBytes)
                    bytes = await File.ReadAllBytesAsync(path, cancellationToken);
                var bitmap = Decode(bytes);
                if (bitmap is null && _urls.TryGetValue(key, out var url))
                {
                    // No metadata requests, uncontrolled image corpus, screenshots, or cloud recognition.
                    await Task.Delay(150, cancellationToken);
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.UserAgent.ParseAdd("DraftTG/0.9B.4 (known-pack small image cache)");
                    request.Headers.Accept.ParseAdd("image/jpeg");
                    using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaximumImageBytes) continue;
                    await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    using var buffer = new MemoryStream();
                    var chunk = new byte[8192];
                    int count;
                    while ((count = await stream.ReadAsync(chunk, cancellationToken)) > 0)
                    {
                        if (buffer.Length + count > MaximumImageBytes) break;
                        buffer.Write(chunk, 0, count);
                    }
                    if (count != 0) continue;
                    bytes = buffer.ToArray();
                    bitmap = Decode(bytes);
                    if (bitmap is not null) await File.WriteAllBytesAsync(path, bytes, cancellationToken);
                }
                if (bitmap is not null) _images.Add(id, bitmap);
            }
            catch (Exception e) when (e is IOException or HttpRequestException or UnauthorizedAccessException) { }
        }
        // Keep only this pack in RAM, and at most 128 small thumbnails on disk (~32 MiB worst case).
        foreach (var id in _images.Keys.Except(wanted).ToArray()) { _images[id].Dispose(); _images.Remove(id); }
        foreach (var file in new DirectoryInfo(cache).EnumerateFiles("*.jpg").OrderByDescending(f => f.LastWriteTimeUtc).Skip(128))
            try { file.Delete(); } catch (IOException) { }
        return _images;
    }

    private static SKBitmap? Decode(byte[]? bytes)
    {
        if (bytes is null) return null;
        using var stream = new MemoryStream(bytes);
        using var codec = SKCodec.Create(stream);
        if (codec is null || codec.Info.Width is < 64 or > 320 || codec.Info.Height is < 64 or > 448) return null;
        return SKBitmap.Decode(bytes);
    }

    private async Task IndexAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(dataDirectory, "card-data", "scryfall-default-cards.jsonl.gz");
        if (!File.Exists(path)) return;
        await using var file = File.OpenRead(path);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            using var card = JsonDocument.Parse(line);
            var root = card.RootElement;
            if (!root.TryGetProperty("id", out var id) || !root.TryGetProperty("image_uris", out var images)
                || !images.TryGetProperty("small", out var small)
                || !Uri.TryCreate(small.GetString(), UriKind.Absolute, out var uri) || !IsAllowedImageUri(uri)) continue;
            _urls[id.GetString()!] = uri;
        }
        _indexed = true;
    }

    internal static bool IsAllowedImageUri(Uri uri) => uri.Scheme == "https" && uri.Host == "cards.scryfall.io"
        && uri.AbsolutePath.StartsWith("/small/front/", StringComparison.Ordinal);

    public void Dispose() { foreach (var image in _images.Values) image.Dispose(); _images.Clear(); }
}
