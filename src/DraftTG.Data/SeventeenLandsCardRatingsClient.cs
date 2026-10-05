using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using DraftTG.Domain;

namespace DraftTG.Data;

/// <summary>One whole-environment request, persistent cache, no polling or automatic retries.</summary>
public sealed class SeventeenLandsCardRatingsClient : ISeventeenLandsCardRatingsClient, ISeventeenLandsPairRatingsClient
{
    private readonly HttpClient _http;
    private readonly string _directory;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, SeventeenLandsRatingsResult> _memory = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (DateTimeOffset RetryAt, SeventeenLandsRatingsResult Result)> _failures = new(StringComparer.Ordinal);
    private DateTimeOffset _retryAfter;
    private DateTimeOffset _nextPairRequest;
    private const int CacheSchemaVersion = 3;
    private const string SourceEndpoint = "/api/card_data";
    private const string TimePeriod = "ALL_TIME";
    // A conservative sanity floor for whole draft environments, not an expected set size.
    private const int MinimumDatasetRows = 20;
    public static TimeSpan CacheTtl { get; } = TimeSpan.FromHours(24);

    public SeventeenLandsCardRatingsClient(HttpClient http, IApplicationDataPathProvider paths,
        TimeProvider? timeProvider = null)
    {
        _http = http;
        _directory = Path.Combine(paths.GetApplicationDataDirectory(), "limited-data", "17lands");
        _time = timeProvider ?? TimeProvider.System;
    }

    public Task<SeventeenLandsRatingsResult> LoadAsync(string expansion, SeventeenLandsFormat format,
        bool forceRefresh = false, CancellationToken cancellationToken = default) =>
        LoadCoreAsync(expansion, format, null, forceRefresh, cancellationToken);

    public Task<SeventeenLandsRatingsResult> LoadPairAsync(string expansion, SeventeenLandsFormat format,
        ArchetypeColorPair colorPair, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(colorPair);
        return LoadCoreAsync(expansion, format, colorPair, false, cancellationToken);
    }

    private async Task<SeventeenLandsRatingsResult> LoadCoreAsync(string expansion, SeventeenLandsFormat format,
        ArchetypeColorPair? pair, bool forceRefresh, CancellationToken cancellationToken)
    {
        // Allowlisted components cannot escape the cache directory or inject query parameters.
        if (!Regex.IsMatch(expansion, @"^[A-Za-z0-9]{2,8}\z", RegexOptions.CultureInvariant))
            throw new ArgumentException("Invalid expansion code.", nameof(expansion));
        if (!Enum.IsDefined(format)) throw new ArgumentOutOfRangeException(nameof(format));
        expansion = expansion.ToUpperInvariant();
        var key = $"{expansion}_{format}_{TimePeriod}_v{CacheSchemaVersion}" + (pair is null ? "" : $"_{pair.Code}_pair_v1");
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = _time.GetUtcNow();
            var cached = _memory.GetValueOrDefault(key)
                ?? await ReadCacheAsync(key, expansion, format, pair, now, cancellationToken).ConfigureAwait(false);
            if (!forceRefresh && cached?.FetchedAt is { } fetched && now - fetched < CacheTtl)
                return Result(cached, SeventeenLandsSource.Cache);

            if (!forceRefresh && _failures.TryGetValue(key, out var failure) && now < failure.RetryAt)
                return failure.Result;

            if (now < _retryAfter)
                return Failure(cached, expansion, format, $"17Lands rate limited; retry after {_retryAfter:u}.", pair);

            try
            {
                if (pair is not null)
                {
                    var wait = _nextPairRequest - _time.GetUtcNow();
                    if (wait > TimeSpan.Zero) await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
                    _nextPairRequest = _time.GetUtcNow() + TimeSpan.FromSeconds(1);
                }
                using var request = new HttpRequestMessage(HttpMethod.Get,
                    $"https://www.17lands.com{SourceEndpoint}?expansion={expansion}&event_type={format}&time_period={TimePeriod}"
                    + (pair is null ? "" : $"&colors={pair.Code}"));
                request.Headers.UserAgent.ParseAdd("DraftTG/0.8.1 (Limited statistics; whole-environment data cached for 24 hours)");
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    _retryAfter = response.Headers.RetryAfter?.Date
                        ?? now + (response.Headers.RetryAfter?.Delta ?? TimeSpan.FromHours(1));
                    if (_retryAfter <= now) _retryAfter = now + TimeSpan.FromMinutes(1);
                    return Failure(cached, expansion, format, $"17Lands rate limited; retry after {_retryAfter:u}.", pair);
                }
                response.EnsureSuccessStatusCode();
                // Some unversioned endpoints mislabel JSON as text/html. Validate the body;
                // real HTML still fails JSON decoding and never replaces valid cached data.
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                var payload = await JsonSerializer.DeserializeAsync<SeventeenLandsApiCardDataDto>(stream,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                var validated = Validate(payload?.Data);
                CheckDataset(validated);
                var timestamp = _time.GetUtcNow();
                var result = new SeventeenLandsRatingsResult(expansion, format, validated,
                    SeventeenLandsSource.Live, timestamp, metadata: Metadata(validated.Length), colorPair: pair);
                _memory[key] = result;
                _failures.Remove(key);
                try
                {
                    await WriteCacheAsync(key, new RatingsCache(CacheSchemaVersion, SourceEndpoint, TimePeriod,
                        expansion, format, format, timestamp, payload!, pair?.Code, pair is null ? null : 1),
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
                    error is SuspiciousDatasetException ? error.Message
                        : error is JsonException ? "17Lands returned invalid statistics." : "17Lands refresh unavailable.", pair);
                // Avoid repeated calls after failures, including callers outside the live coordinator.
                _failures[key] = (_time.GetUtcNow() + TimeSpan.FromHours(1), result);
                return result;
            }
        }
        finally { _gate.Release(); }
    }

    private static SeventeenLandsRatingsResult Result(SeventeenLandsRatingsResult value,
        SeventeenLandsSource source, string? diagnostic = null) =>
        new(value.Expansion, value.Format, value.Rows, source, value.FetchedAt, diagnostic, value.Metadata, value.ColorPair);

    private static SeventeenLandsDatasetMetadata Metadata(int rowCount) => new(SourceEndpoint, TimePeriod, rowCount);

    private static SeventeenLandsRatingsResult Failure(SeventeenLandsRatingsResult? cache,
        string expansion, SeventeenLandsFormat format, string diagnostic, ArchetypeColorPair? pair = null) => cache is null
        ? new(expansion, format, [], SeventeenLandsSource.Unavailable, diagnostic: diagnostic, metadata: Metadata(0), colorPair: pair)
        : Result(cache, SeventeenLandsSource.StaleCache, diagnostic + " Using cached statistics.");

    private async Task<SeventeenLandsRatingsResult?> ReadCacheAsync(string key, string expansion,
        SeventeenLandsFormat format, ArchetypeColorPair? pair, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(Path.Combine(_directory, key + ".json"));
            var cache = await JsonSerializer.DeserializeAsync<RatingsCache>(stream,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (cache is null || cache.SchemaVersion != CacheSchemaVersion || cache.Expansion != expansion || cache.RequestedFormat != format
                || cache.SourceFormat != format || cache.SourceEndpoint != SourceEndpoint || cache.TimePeriod != TimePeriod
                || cache.DeckColors != pair?.Code || cache.PairSchemaVersion != (pair is null ? (int?)null : 1)
                || cache.FetchedAt == default || cache.FetchedAt > now)
                return null;
            var rows = Validate(cache.Payload?.Data);
            CheckDataset(rows);
            return new(expansion, format, rows, SeventeenLandsSource.Cache, cache.FetchedAt, metadata: Metadata(rows.Length), colorPair: pair);
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

    private static SeventeenLandsRating[] Validate(SeventeenLandsApiCardDataRowDto[]? rows)
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

    private static void CheckDataset(SeventeenLandsRating[] rows)
    {
        if (rows.Length < MinimumDatasetRows)
            throw new SuspiciousDatasetException($"17Lands card-data refresh rejected: suspicious dataset ({rows.Length} rows).");
        if (!rows.Any(row => row.GameInHandWinRate is not null && row.GameInHandGameCount is > 0))
            throw new SuspiciousDatasetException("17Lands card-data refresh rejected: no valid GIH samples.");
    }

    private sealed class SuspiciousDatasetException(string message) : JsonException(message);

    // Versions 1/2 came from the legacy endpoint; only the current API envelope is compatible.
    private sealed record RatingsCache(
        [property: JsonRequired] int SchemaVersion,
        [property: JsonRequired] string SourceEndpoint,
        [property: JsonRequired] string TimePeriod,
        [property: JsonRequired] string Expansion,
        [property: JsonRequired] SeventeenLandsFormat RequestedFormat,
        [property: JsonRequired] SeventeenLandsFormat SourceFormat,
        [property: JsonRequired] DateTimeOffset FetchedAt,
        [property: JsonRequired] SeventeenLandsApiCardDataDto Payload,
        string? DeckColors = null, int? PairSchemaVersion = null);
}

// The current API returns an envelope, not a bare ratings array.
internal sealed record SeventeenLandsApiCardDataDto(
    [property: JsonPropertyName("data"), JsonRequired] SeventeenLandsApiCardDataRowDto[] Data,
    [property: JsonPropertyName("copyright")] string? Copyright,
    [property: JsonPropertyName("notes")] string? Notes);

// Overall rates/counts are retained in the DTO/cache independently; they never substitute for GIH.
internal sealed record SeventeenLandsApiCardDataRowDto(
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
