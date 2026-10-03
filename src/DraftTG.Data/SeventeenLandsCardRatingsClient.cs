using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace DraftTG.Data;

/// <summary>One whole-environment request, persistent cache, no polling or automatic retries.</summary>
public sealed class SeventeenLandsCardRatingsClient : ISeventeenLandsCardRatingsClient
{
    private readonly HttpClient _http;
    private readonly string _directory;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, SeventeenLandsRatingsResult> _memory = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (DateTimeOffset RetryAt, SeventeenLandsRatingsResult Result)> _failures = new(StringComparer.Ordinal);
    private DateTimeOffset _retryAfter;
    private const int CacheSchemaVersion = 2;
    public static TimeSpan CacheTtl { get; } = TimeSpan.FromHours(24);

    public SeventeenLandsCardRatingsClient(HttpClient http, IApplicationDataPathProvider paths,
        TimeProvider? timeProvider = null)
    {
        _http = http;
        _directory = Path.Combine(paths.GetApplicationDataDirectory(), "limited-data", "17lands");
        _time = timeProvider ?? TimeProvider.System;
    }

    public async Task<SeventeenLandsRatingsResult> LoadAsync(string expansion, SeventeenLandsFormat format,
        bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        // Allowlisted components cannot escape the cache directory or inject query parameters.
        if (!Regex.IsMatch(expansion, @"^[A-Za-z0-9]{2,8}\z", RegexOptions.CultureInvariant))
            throw new ArgumentException("Invalid expansion code.", nameof(expansion));
        if (!Enum.IsDefined(format)) throw new ArgumentOutOfRangeException(nameof(format));
        expansion = expansion.ToUpperInvariant();
        var key = $"{expansion}_{format}";
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = _time.GetUtcNow();
            var cached = _memory.GetValueOrDefault(key)
                ?? await ReadCacheAsync(key, expansion, format, now, cancellationToken).ConfigureAwait(false);
            if (!forceRefresh && cached?.FetchedAt is { } fetched && now - fetched < CacheTtl)
                return Result(cached, SeventeenLandsSource.Cache);

            if (!forceRefresh && _failures.TryGetValue(key, out var failure) && now < failure.RetryAt)
                return failure.Result;

            if (now < _retryAfter)
                return Failure(cached, expansion, format, $"17Lands rate limited; retry after {_retryAfter:u}.");

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get,
                    $"https://www.17lands.com/card_ratings/data?expansion={expansion}&format={format}");
                request.Headers.UserAgent.ParseAdd("DraftTG/0.7 (Limited statistics; cached for 24 hours)");
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    _retryAfter = response.Headers.RetryAfter?.Date
                        ?? now + (response.Headers.RetryAfter?.Delta ?? TimeSpan.FromHours(1));
                    if (_retryAfter <= now) _retryAfter = now + TimeSpan.FromMinutes(1);
                    return Failure(cached, expansion, format, $"17Lands rate limited; retry after {_retryAfter:u}.");
                }
                response.EnsureSuccessStatusCode();
                // Some unversioned endpoints mislabel JSON as text/html. Validate the body;
                // real HTML still fails JSON decoding and never replaces valid cached data.
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                var rows = await JsonSerializer.DeserializeAsync<SeventeenLandsCardRatingDto[]>(stream,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                var validated = Validate(rows);
                var timestamp = _time.GetUtcNow();
                var result = new SeventeenLandsRatingsResult(expansion, format, validated,
                    SeventeenLandsSource.Live, timestamp);
                _memory[key] = result;
                _failures.Remove(key);
                try
                {
                    await WriteCacheAsync(key, new RatingsCache(CacheSchemaVersion, expansion, format, format, timestamp, rows!),
                        cancellationToken).ConfigureAwait(false);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    return Result(result, SeventeenLandsSource.Live, "Statistics loaded; local cache could not be saved.");
                }
                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception error) when (error is HttpRequestException or IOException or JsonException or OperationCanceledException)
            {
                var result = Failure(cached, expansion, format,
                    error is JsonException ? "17Lands returned invalid statistics." : "17Lands refresh unavailable.");
                // Avoid repeated calls after failures, including callers outside the live coordinator.
                _failures[key] = (_time.GetUtcNow() + TimeSpan.FromHours(1), result);
                return result;
            }
        }
        finally { _gate.Release(); }
    }

    private static SeventeenLandsRatingsResult Result(SeventeenLandsRatingsResult value,
        SeventeenLandsSource source, string? diagnostic = null) =>
        new(value.Expansion, value.Format, value.Rows, source, value.FetchedAt, diagnostic);

    private static SeventeenLandsRatingsResult Failure(SeventeenLandsRatingsResult? cache,
        string expansion, SeventeenLandsFormat format, string diagnostic) => cache is null
        ? new(expansion, format, [], SeventeenLandsSource.Unavailable, diagnostic: diagnostic)
        : Result(cache, SeventeenLandsSource.StaleCache, diagnostic + " Using cached statistics.");

    private async Task<SeventeenLandsRatingsResult?> ReadCacheAsync(string key, string expansion,
        SeventeenLandsFormat format, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(Path.Combine(_directory, key + ".json"));
            var cache = await JsonSerializer.DeserializeAsync<RatingsCache>(stream,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (cache is null || cache.SchemaVersion != CacheSchemaVersion || cache.Expansion != expansion || cache.RequestedFormat != format
                || cache.SourceFormat != format || cache.FetchedAt == default || cache.FetchedAt > now)
                return null;
            return new(expansion, format, Validate(cache.Rows), SeventeenLandsSource.Cache, cache.FetchedAt);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        { return null; }
    }

    private async Task WriteCacheAsync(string key, RatingsCache cache, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, key + ".json");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = File.Create(temporary))
                await JsonSerializer.SerializeAsync(stream, cache, cancellationToken: cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static SeventeenLandsRating[] Validate(SeventeenLandsCardRatingDto[]? rows)
    {
        if (rows is null) throw new JsonException("Expected a ratings array.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        return rows.Select(row =>
        {
            if (row is null || string.IsNullOrWhiteSpace(row.Name) || !names.Add(row.Name)
                || row.GameCount < 0 || row.EverDrawnGameCount < 0
                || row.OpeningHandGameCount < 0 || row.DrawnGameCount < 0
                || !Rate(row.PlayRate) || !Rate(row.WinRate) || !Rate(row.EverDrawnWinRate)
                || !Rate(row.OpeningHandWinRate) || !Rate(row.DrawnWinRate)
                || !Rate(row.DrawnImprovementWinRate, -1) || !Positive(row.AvgSeen) || !Positive(row.AvgPick))
                throw new JsonException("Invalid or duplicate card rating.");
            return new SeventeenLandsRating(row.Name, row.EverDrawnGameCount, row.PlayRate, row.EverDrawnWinRate,
                row.OpeningHandWinRate, row.DrawnWinRate, row.DrawnImprovementWinRate, row.AvgSeen, row.AvgPick);
        }).ToArray();
    }

    private static bool Rate(double? value, double minimum = 0) =>
        value is null || (double.IsFinite(value.Value) && value >= minimum && value <= 1);
    private static bool Positive(double? value) => value is null || (double.IsFinite(value.Value) && value > 0);

    // Version 1 serialized a reduced DTO and discarded the true GIH fields.
    private sealed record RatingsCache(
        [property: JsonRequired] int SchemaVersion,
        [property: JsonRequired] string Expansion,
        [property: JsonRequired] SeventeenLandsFormat RequestedFormat,
        [property: JsonRequired] SeventeenLandsFormat SourceFormat,
        [property: JsonRequired] DateTimeOffset FetchedAt,
        [property: JsonRequired] SeventeenLandsCardRatingDto[] Rows);
}

// Overall rates/counts are retained in the DTO/cache independently; they never substitute for GIH.
internal sealed record SeventeenLandsCardRatingDto(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("game_count")] int? GameCount,
    [property: JsonPropertyName("play_rate")] double? PlayRate,
    [property: JsonPropertyName("win_rate")] double? WinRate,
    [property: JsonPropertyName("ever_drawn_win_rate")] double? EverDrawnWinRate,
    [property: JsonPropertyName("ever_drawn_game_count")] int? EverDrawnGameCount,
    [property: JsonPropertyName("opening_hand_game_count")] int? OpeningHandGameCount,
    [property: JsonPropertyName("drawn_game_count")] int? DrawnGameCount,
    [property: JsonPropertyName("opening_hand_win_rate")] double? OpeningHandWinRate,
    [property: JsonPropertyName("drawn_win_rate")] double? DrawnWinRate,
    [property: JsonPropertyName("drawn_improvement_win_rate")] double? DrawnImprovementWinRate,
    [property: JsonPropertyName("avg_seen")] double? AvgSeen,
    [property: JsonPropertyName("avg_pick")] double? AvgPick);
