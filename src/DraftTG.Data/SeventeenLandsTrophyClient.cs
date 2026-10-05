using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using DraftTG.Domain;

namespace DraftTG.Data;

public sealed record TrophySourceConfiguration
{
    public TrophySourceConfiguration(int maximumDecks = 20, TimeSpan? cacheTtl = null,
        TimeSpan? failureCooldown = null, TimeSpan? requestSpacing = null)
    {
        MaximumDecks = maximumDecks; CacheTtl = cacheTtl ?? TimeSpan.FromHours(24);
        FailureCooldown = failureCooldown ?? TimeSpan.FromHours(1); RequestSpacing = requestSpacing ?? TimeSpan.FromSeconds(1);
        if (maximumDecks is < 1 or > 100 || CacheTtl <= TimeSpan.Zero || FailureCooldown <= TimeSpan.Zero || RequestSpacing < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(maximumDecks));
    }
    public int MaximumDecks { get; }
    public TimeSpan CacheTtl { get; }
    public TimeSpan FailureCooldown { get; }
    public TimeSpan RequestSpacing { get; }
}

/// <summary>Internal unversioned website API. One recent summary query and a bounded number of
/// nominated builds; no pagination, bulk download, automatic retry or format substitution.
/// Current API responses prohibit outside use: they fail closed before any deck requests.</summary>
public sealed class SeventeenLandsTrophyClient(HttpClient http, IApplicationDataPathProvider paths,
    TrophySourceConfiguration? configuration = null, TimeProvider? timeProvider = null,
    Func<int, string, string?>? canonicalCardName = null) : ISuccessfulDeckProvider
{
    public const string SummaryEndpoint = "/api/trophies/";
    public const string DeckEndpoint = "/api/deck/draft/";
    public const string QueryMode = "recent-strict-pair";
    public const int SchemaVersion = 1;
    public const string RestrictedSourceReason = "17Lands trophy API restricts outside use; no permitted exact-format corpus loaded.";
    private readonly TrophySourceConfiguration _configuration = configuration ?? new();
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly string _directory = Path.Combine(paths.GetApplicationDataDirectory(), "limited-data", "17lands", "trophy");
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<SuccessfulDeckKey, SuccessfulDeckCorpus> _memory = [];
    private readonly Dictionary<SuccessfulDeckKey, (DateTimeOffset Until, SuccessfulDeckLoadResult Result)> _failures = [];
    private DateTimeOffset _nextRequest;
    private DateTimeOffset _retryAfter;

    public static string Format(SuccessfulDeckFormat format) => format switch
    {
        SuccessfulDeckFormat.QuickDraft => "QuickDraft", SuccessfulDeckFormat.PremierDraft => "PremierDraft",
        SuccessfulDeckFormat.TraditionalDraft => "TradDraft", _ => throw new ArgumentOutOfRangeException(nameof(format))
    };
    public string CacheFileName(SuccessfulDeckKey key) =>
        $"{key.Expansion}_{Format(key.Format)}_{key.Pair.Code}_all-ranks_{QueryMode}_limit{_configuration.MaximumDecks}_v{SchemaVersion}.json";

    public async Task<SuccessfulDeckLoadResult> LoadAsync(SuccessfulDeckKey key, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = _time.GetUtcNow();
            var cached = _memory.GetValueOrDefault(key) ?? await ReadCache(key, cancellationToken).ConfigureAwait(false);
            if (cached is not null && now - cached.RetrievedAt < _configuration.CacheTtl)
                return new(key, cached, SuccessfulDeckSource.Cache);
            if (_failures.TryGetValue(key, out var failed) && now < failed.Until) return failed.Result;
            try
            {
                if (now < _retryAfter) throw new TrophyUnavailableException("17Lands trophy source rate limited; waiting for Retry-After.");
                var query = new TrophyQuery(key.Expansion, Format(key.Format), [], [], [key.Pair.Code]);
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://www.17lands.com" + SummaryEndpoint)
                    { Content = JsonContent.Create(query) };
                var summaries = await Send<TrophyEnvelope<TrophySummaryDto[]>>(request, cancellationToken).ConfigureAwait(false);
                CheckUse(summaries.Notes);
                var eligible = ValidateSummaries(summaries.Data, key);
                var decks = new Dictionary<string, TrophyEnvelope<TrophyDeckDto>>(StringComparer.Ordinal);
                foreach (var summary in eligible.Take(_configuration.MaximumDecks))
                {
                    using var deckRequest = new HttpRequestMessage(HttpMethod.Get,
                        $"https://www.17lands.com{DeckEndpoint}?draft_id={summary.AggregateId}&deck_index={summary.DeckIndex}");
                    var deck = await Send<TrophyEnvelope<TrophyDeckDto>>(deckRequest, cancellationToken).ConfigureAwait(false);
                    CheckUse(deck.Notes);
                    decks.Add(summary.AggregateId, deck);
                }
                var raw = new TrophyCache(SchemaVersion, SummaryEndpoint, DeckEndpoint, key.Expansion, Format(key.Format), key.Pair.Code,
                    QueryMode, "all-ranks", _configuration.MaximumDecks, _time.GetUtcNow(), summaries, decks);
                var corpus = Decode(raw, key, _configuration.MaximumDecks);
                _memory[key] = corpus; _failures.Remove(key);
                try { await WriteCache(key, raw, cancellationToken).ConfigureAwait(false); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                { return new(key, corpus, SuccessfulDeckSource.Live, "Evidence loaded; cache could not be saved."); }
                return new(key, corpus, SuccessfulDeckSource.Live);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception error) when (error is HttpRequestException or IOException or JsonException or ArgumentException or OperationCanceledException)
            {
                // A use restriction invalidates older API-derived caches too.
                if (error is TrophyUseRestrictedException) { cached = null; _memory.Remove(key); }
                var diagnostic = error is TrophyUnavailableException ? error.Message : "Exact-format trophy evidence unavailable or invalid.";
                var result = new SuccessfulDeckLoadResult(key, cached, cached is null ? SuccessfulDeckSource.Unavailable : SuccessfulDeckSource.StaleCache,
                    diagnostic + (cached is null ? "" : " Using stale valid evidence."));
                _failures[key] = (_time.GetUtcNow() + _configuration.FailureCooldown, result);
                return result;
            }
        }
        finally { _gate.Release(); }
    }

    private async Task<T> Send<T>(HttpRequestMessage request, CancellationToken token)
    {
        var delay = _nextRequest - _time.GetUtcNow();
        if (delay > TimeSpan.Zero) await Task.Delay(delay, token).ConfigureAwait(false);
        _nextRequest = _time.GetUtcNow() + _configuration.RequestSpacing;
        request.Headers.UserAgent.ParseAdd("DraftTG/0.9D (bounded recent successful-deck evidence; 24-hour cache)");
        request.Headers.Accept.ParseAdd("application/json");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            _retryAfter = response.Headers.RetryAfter?.Date
                ?? _time.GetUtcNow() + (response.Headers.RetryAfter?.Delta ?? TimeSpan.FromHours(1));
            if (_retryAfter <= _time.GetUtcNow()) _retryAfter = _time.GetUtcNow() + TimeSpan.FromMinutes(1);
            throw new TrophyUnavailableException("17Lands trophy source rate limited; waiting for Retry-After.");
        }
        response.EnsureSuccessStatusCode();
        // The audited endpoints label JSON text/html; validate JSON, not its advertised media type.
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<T>(stream, cancellationToken: token).ConfigureAwait(false)
            ?? throw new JsonException("Missing trophy payload.");
    }

    private static void CheckUse(string? notes)
    {
        if (notes?.Contains("only for use on 17Lands.com", StringComparison.OrdinalIgnoreCase) == true
            || notes?.Contains("only data permitted for outside use", StringComparison.OrdinalIgnoreCase) == true)
            throw new TrophyUseRestrictedException(RestrictedSourceReason);
    }

    private static TrophySummaryDto[] ValidateSummaries(TrophySummaryDto[]? summaries, SuccessfulDeckKey key)
    {
        if (summaries is null || summaries.Length > 100) throw new JsonException("Invalid bounded trophy summaries.");
        var eligible = new Dictionary<string, TrophySummaryDto>(StringComparer.Ordinal);
        foreach (var row in summaries)
        {
            if (row is null || !Regex.IsMatch(row.AggregateId ?? "", "^[a-f0-9]{32}\\z") || row.DeckIndex is < 0 or > 100
                || row.Wins is <= 0 or > 100 || row.Losses is < 0 or > 100 || !Date(row.Time, out _)
                || string.IsNullOrEmpty(row.Colors) || row.Colors.Any(c => !"WUBRGwubrg".Contains(c)))
                throw new JsonException("Malformed trophy event.");
            // API BG filter includes BGu/BGr etc. Initial corpus deliberately excludes splashes.
            if (row.Colors != key.Pair.Code) continue;
            if (eligible.TryGetValue(row.AggregateId!, out var previous) && previous != row)
                throw new JsonException("Conflicting builds for one trophy event.");
            eligible.TryAdd(row.AggregateId!, row);
        }
        if (eligible.Count == 0) throw new TrophyUnavailableException("No exact-format, strict-pair trophy events in this recent sample.");
        return eligible.Values.OrderByDescending(s => s.Time, StringComparer.Ordinal).ThenBy(s => s.AggregateId, StringComparer.Ordinal).ToArray();
    }

    private static bool Date(string? value, out DateTimeOffset time) => DateTimeOffset.TryParseExact(value,
        "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out time);

    private SuccessfulDeckCorpus Decode(TrophyCache cache, SuccessfulDeckKey key, int limit)
    {
        if (cache.Schema != SchemaVersion || cache.Expansion != key.Expansion || cache.Format != Format(key.Format)
            || cache.SummaryEndpoint != SummaryEndpoint || cache.DeckEndpoint != DeckEndpoint
            || cache.Pair != key.Pair.Code || cache.Mode != QueryMode || cache.Rank != "all-ranks" || cache.Limit != limit
            || cache.Summaries is null || cache.Decks is null) throw new JsonException("Mismatched trophy cache identity.");
        CheckUse(cache.Summaries.Notes);
        var rows = ValidateSummaries(cache.Summaries.Data, key).Take(limit).ToArray();
        if (cache.Decks.Count != rows.Length) throw new JsonException("Incomplete bounded corpus.");
        var samples = rows.Select(row =>
        {
            if (!cache.Decks.TryGetValue(row.AggregateId, out var envelope) || envelope is null)
                throw new JsonException("Missing deck list.");
            CheckUse(envelope.Notes);
            var deck = envelope.Data; var info = deck?.EventInfo;
            if (info is null || info.Id != row.AggregateId || info.Expansion != key.Expansion || info.Format != Format(key.Format)
                || info.Wins != row.Wins || info.Losses != row.Losses || info.Wins != info.TrophyWinCount
                || info.BestOf != (key.Format == SuccessfulDeckFormat.TraditionalDraft ? 3 : 1)
                || info.Losses > (key.Format == SuccessfulDeckFormat.TraditionalDraft ? 0 : 2)
                || info.DeckColors != key.Pair.Code || deck!.MainColors != key.Pair.Code || !string.IsNullOrEmpty(deck.SplashColors))
                throw new JsonException("Mismatched or unsuccessful trophy event metadata.");
            var criteria = new SuccessfulEventCriteria(info.TrophyWinCount, info.BestOf);
            var main = Counts(deck, "Maindeck"); var side = Counts(deck, "Sideboard", allowEmpty: true);
            var pool = main.Keys.Concat(side.Keys).Distinct(StringComparer.Ordinal)
                .ToDictionary(name => name, name => main.GetValueOrDefault(name) + side.GetValueOrDefault(name), StringComparer.Ordinal);
            Date(row.Time, out var time);
            return new SuccessfulDeckSample(key, row.AggregateId, criteria, info.Wins, info.Losses, time, main, pool,
                SuccessfulDeckPoolBasis.MaindeckAndSideboard, $"provider-nominated build {row.DeckIndex}");
        }).ToArray();
        var query = JsonSerializer.Serialize(new TrophyQuery(key.Expansion, Format(key.Format), [], [], [key.Pair.Code]));
        return new(key, samples, cache.RetrievedAt, new("17Lands " + SummaryEndpoint, QueryMode, query, 100, limit,
            cache.Summaries.Data.Length, "One provider-nominated deck_index per aggregate_id; not verified as most-played or final."));
    }

    private Dictionary<string, int> Counts(TrophyDeckDto deck, string groupName, bool allowEmpty = false)
    {
        var matches = deck.Groups?.Where(g => g?.Name == groupName).ToArray();
        if (matches?.Length != 1 || matches[0].Cards is null || (!allowEmpty && matches[0].Cards.Length == 0)
            || matches[0].Cards.Length > 1000 || deck.Cards is null) throw new JsonException("Missing or malformed deck group.");
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var id in matches[0].Cards)
        {
            if (id <= 0 || !deck.Cards.TryGetValue(id.ToString(CultureInfo.InvariantCulture), out var card) || card is null
                || card.Id != id || string.IsNullOrWhiteSpace(card.Name)) throw new JsonException("Invalid card identity in trophy list.");
            // Composition may resolve a provider ID through the existing safe Arena/Scryfall
            // resolver, including canonical multi-face names. Unresolved names stay exact;
            // they cannot attach to an arbitrary printing by position or a fuzzy name match.
            var name = canonicalCardName?.Invoke(id, card.Name) ?? card.Name;
            counts[name] = counts.GetValueOrDefault(name) + 1;
        }
        return counts;
    }

    private async Task<SuccessfulDeckCorpus?> ReadCache(SuccessfulDeckKey key, CancellationToken token)
    {
        try
        {
            await using var stream = File.OpenRead(Path.Combine(_directory, CacheFileName(key)));
            var raw = await JsonSerializer.DeserializeAsync<TrophyCache>(stream, cancellationToken: token).ConfigureAwait(false);
            if (raw is null || raw.RetrievedAt == default || raw.RetrievedAt > _time.GetUtcNow()) return null;
            return Decode(raw, key, _configuration.MaximumDecks);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { return null; }
    }
    private async Task WriteCache(SuccessfulDeckKey key, TrophyCache value, CancellationToken token)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, CacheFileName(key)); var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = File.Create(temporary))
                await JsonSerializer.SerializeAsync(stream, value, cancellationToken: token).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private class TrophyUnavailableException(string message) : JsonException(message);
    private sealed class TrophyUseRestrictedException(string message) : TrophyUnavailableException(message);
    private sealed record TrophyQuery(
        [property: JsonPropertyName("expansion")] string Expansion,
        [property: JsonPropertyName("event_type")] string EventType,
        [property: JsonPropertyName("card_names")] string[] Cards,
        [property: JsonPropertyName("ranks")] string[] Ranks,
        [property: JsonPropertyName("deck_colors")] string[] Colors);
    private sealed record TrophyCache(int Schema, string SummaryEndpoint, string DeckEndpoint, string Expansion, string Format, string Pair, string Mode, string Rank,
        int Limit, DateTimeOffset RetrievedAt, TrophyEnvelope<TrophySummaryDto[]> Summaries,
        Dictionary<string, TrophyEnvelope<TrophyDeckDto>> Decks);
}

internal sealed record TrophyEnvelope<T>([property: JsonPropertyName("data"), JsonRequired] T Data,
    [property: JsonPropertyName("notes")] string? Notes);
internal sealed record TrophySummaryDto(
    [property: JsonPropertyName("aggregate_id"), JsonRequired] string AggregateId,
    [property: JsonPropertyName("deck_index"), JsonRequired] int DeckIndex,
    [property: JsonPropertyName("wins"), JsonRequired] int Wins,
    [property: JsonPropertyName("losses"), JsonRequired] int Losses,
    [property: JsonPropertyName("colors"), JsonRequired] string Colors,
    [property: JsonPropertyName("time"), JsonRequired] string Time);
internal sealed record TrophyDeckDto(
    [property: JsonPropertyName("event_info"), JsonRequired] TrophyEventDto EventInfo,
    [property: JsonPropertyName("groups"), JsonRequired] TrophyGroupDto[] Groups,
    [property: JsonPropertyName("cards"), JsonRequired] Dictionary<string, TrophyCardDto> Cards,
    [property: JsonPropertyName("main_colors"), JsonRequired] string MainColors,
    [property: JsonPropertyName("splash_colors")] string? SplashColors);
internal sealed record TrophyEventDto(
    [property: JsonPropertyName("id"), JsonRequired] string Id,
    [property: JsonPropertyName("expansion"), JsonRequired] string Expansion,
    [property: JsonPropertyName("format"), JsonRequired] string Format,
    [property: JsonPropertyName("wins"), JsonRequired] int Wins,
    [property: JsonPropertyName("losses"), JsonRequired] int Losses,
    [property: JsonPropertyName("best_of_n"), JsonRequired] int BestOf,
    [property: JsonPropertyName("trophy_win_count"), JsonRequired] int TrophyWinCount,
    [property: JsonPropertyName("deck_colors"), JsonRequired] string DeckColors);
internal sealed record TrophyGroupDto([property: JsonPropertyName("name"), JsonRequired] string Name,
    [property: JsonPropertyName("cards"), JsonRequired] int[] Cards);
internal sealed record TrophyCardDto([property: JsonPropertyName("id"), JsonRequired] int Id,
    [property: JsonPropertyName("name"), JsonRequired] string Name);
