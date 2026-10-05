using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DraftTG.Domain;

namespace DraftTG.Data.Tests;

public sealed class TrophyDataTests : IDisposable
{
    private const string Id = "00000000000000000000000000000001";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "DraftTG-trophy-" + Guid.NewGuid().ToString("N"));
    private readonly Clock _time = new();
    private static SuccessfulDeckKey Key(SuccessfulDeckFormat format = SuccessfulDeckFormat.QuickDraft, string pair = "BG", string set = "WOE") =>
        new(set, format, ArchetypeColorPair.Create(pair));
    private sealed class Clock : TimeProvider
    { public DateTimeOffset Now { get; set; } = new(2026, 10, 4, 16, 0, 0, TimeSpan.Zero); public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Paths(string directory) : IApplicationDataPathProvider { public string GetApplicationDataDirectory() => directory; }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = respond;
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; return Task.FromResult(Respond(request)); }
    }
    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "text/html") };
    // Synthetic lists reproduce the audited transport shape; no restricted real deck is a test fixture.
    private static string Summary(SuccessfulDeckFormat format = SuccessfulDeckFormat.QuickDraft, string id = Id, string colors = "BG") =>
        JsonSerializer.Serialize(new { aggregate_id = id, deck_index = 2, wins = format == SuccessfulDeckFormat.TraditionalDraft ? 3 : 7,
            losses = 0, colors, time = "2026-10-04 10:00:00" });
    private static string Summaries(string? entries = null, string? notes = null) =>
        "{\"notes\":" + JsonSerializer.Serialize(notes) + ",\"data\":[" + (entries ?? Summary()) + "]}";
    private static string Deck(SuccessfulDeckFormat format = SuccessfulDeckFormat.QuickDraft) => JsonSerializer.Serialize(new
    {
        data = new { event_info = new { id = Id, expansion = "WOE", format = SeventeenLandsTrophyClient.Format(format),
                best_of_n = format == SuccessfulDeckFormat.TraditionalDraft ? 3 : 1,
                trophy_win_count = format == SuccessfulDeckFormat.TraditionalDraft ? 3 : 7,
                wins = format == SuccessfulDeckFormat.TraditionalDraft ? 3 : 7, losses = 0, deck_colors = "BG" },
            main_colors = "BG", splash_colors = "", groups = new[] { new { name = "Maindeck", cards = new[] { 101, 101, 102 } },
                new { name = "Sideboard", cards = new[] { 101, 103 } } },
            cards = new Dictionary<string, object> { ["101"] = new { id = 101, name = "A" }, ["102"] = new { id = 102, name = "B" },
                ["103"] = new { id = 103, name = "C" } } }
    });
    private SeventeenLandsTrophyClient Client(HttpClient http, int limit = 20) => new(http, new Paths(_directory),
        new(maximumDecks: limit, requestSpacing: TimeSpan.Zero), _time);
    private static Handler ValidHandler(SuccessfulDeckFormat format = SuccessfulDeckFormat.QuickDraft) =>
        new(r => Json(r.Method == HttpMethod.Post ? Summaries(Summary(format)) : Deck(format)));

    [Fact]
    public async Task ExactFormatsUseVerifiedPostAndMetadataCriteriaPreserveRepeatedCardCounts()
    {
        foreach (var format in Enum.GetValues<SuccessfulDeckFormat>())
        {
            using var handler = new Handler(r =>
            {
                Assert.Contains("DraftTG/0.9D", r.Headers.UserAgent.ToString());
                if (r.Method == HttpMethod.Post)
                {
                    Assert.Equal("https://www.17lands.com/api/trophies/", r.RequestUri!.AbsoluteUri);
                    var query = JsonNode.Parse(r.Content!.ReadAsStringAsync().GetAwaiter().GetResult())!;
                    Assert.Equal("WOE", query["expansion"]!.GetValue<string>());
                    Assert.Equal(SeventeenLandsTrophyClient.Format(format), query["event_type"]!.GetValue<string>());
                    Assert.Equal("BG", query["deck_colors"]![0]!.GetValue<string>());
                    Assert.Empty(query["ranks"]!.AsArray()); Assert.Empty(query["card_names"]!.AsArray());
                    return Json(Summaries(Summary(format)));
                }
                Assert.Equal($"https://www.17lands.com/api/deck/draft/?draft_id={Id}&deck_index=2", r.RequestUri!.AbsoluteUri);
                return Json(Deck(format));
            });
            using var http = new HttpClient(handler); var result = await Client(http).LoadAsync(Key(format));
            var sample = Assert.Single(result.Corpus!.Samples); Assert.Equal(2, sample.Maindeck["A"]); Assert.Equal(3, sample.Pool!["A"]);
            Assert.Equal(0, sample.Maindeck.GetValueOrDefault("C")); Assert.Equal(1, sample.Pool["C"]);
            Assert.Equal(format == SuccessfulDeckFormat.TraditionalDraft ? 3 : 7, sample.Criteria.MaximumMatchWins);
            Assert.Equal(SuccessfulDeckPoolBasis.MaindeckAndSideboard, sample.PoolBasis);
            Assert.Contains("build 2", sample.RepresentativeDeck); Assert.Equal(2, handler.Calls);
        }
    }

    [Fact]
    public async Task ObservedOutsideUseRestrictionStopsBeforeDeckFetchAndIsNotRetriedPerPick()
    {
        using var handler = new Handler(_ => Json(Summaries(notes:
            "This data is only for use on 17Lands.com. The only data permitted for outside use is available at public_datasets.")));
        using var http = new HttpClient(handler); var client = Client(http);
        var first = await client.LoadAsync(Key()); var second = await client.LoadAsync(Key());
        Assert.Equal(SuccessfulDeckSource.Unavailable, first.Source); Assert.Null(first.Corpus);
        Assert.Equal(SeventeenLandsTrophyClient.RestrictedSourceReason, first.Diagnostic); Assert.Equal(first, second);
        Assert.Equal(1, handler.Calls); Assert.False(Directory.Exists(Path.Combine(_directory, "limited-data")));
    }

    [Fact]
    public async Task CanonicalProviderIdResolutionPreservesMultiFaceNameWithoutPositionOrPrintingGuess()
    {
        using var handler = ValidHandler(); using var http = new HttpClient(handler);
        var client = new SeventeenLandsTrophyClient(http, new Paths(_directory), new(requestSpacing: TimeSpan.Zero), _time,
            canonicalCardName: (id, name) => id == 101 && name == "A" ? "A // Adventure" : null);
        var sample = Assert.Single((await client.LoadAsync(Key())).Corpus!.Samples);
        Assert.Equal(2, sample.Maindeck["A // Adventure"]); Assert.Equal(3, sample.Pool!["A // Adventure"]);
        Assert.False(sample.Maindeck.ContainsKey("A")); Assert.Equal(1, sample.Maindeck["B"]);
    }

    [Fact]
    public async Task MaximumWinsWithImpossibleTerminalLossesRejectsInsteadOfCountingAsTrophy()
    {
        foreach (var format in Enum.GetValues<SuccessfulDeckFormat>())
        {
            var losses = format == SuccessfulDeckFormat.TraditionalDraft ? 1 : 3;
            using var handler = new Handler(r => Json((r.Method == HttpMethod.Post ? Summaries(Summary(format)) : Deck(format))
                .Replace("\"losses\":0", $"\"losses\":{losses}")));
            using var http = new HttpClient(handler); var result = await Client(http).LoadAsync(Key(format));
            Assert.Null(result.Corpus); Assert.Equal(SuccessfulDeckSource.Unavailable, result.Source);
        }
    }

    [Fact]
    public async Task OneBoundedSampleDeduplicatesEventsAndExcludesSplashAndOtherPairs()
    {
        var entries = string.Join(",", Summary(), Summary(), Summary(id: "00000000000000000000000000000002"),
            Summary(id: "00000000000000000000000000000003", colors: "BGu"),
            Summary(id: "00000000000000000000000000000004", colors: "UG"));
        using var handler = new Handler(r => Json(r.Method == HttpMethod.Post ? Summaries(entries) : Deck()));
        using var http = new HttpClient(handler); var result = await Client(http, limit: 1).LoadAsync(Key());
        Assert.Single(result.Corpus!.Samples); Assert.Equal(5, result.Corpus.Provenance.ReturnedEvents);
        Assert.Equal(1, result.Corpus.Provenance.RequestedLimit); Assert.Equal(100, result.Corpus.Provenance.SourceLimit);
        Assert.Equal("recent-strict-pair", result.Corpus.Provenance.QueryMode); Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task InvalidRecordsDecksAndExactIdentityNeverBecomeUsableCorpus()
    {
        var malformed = new[] { Deck().Replace("QuickDraft", "PremierDraft"), Deck().Replace("WOE", "HOB"),
            Deck().Replace("\"trophy_win_count\":7", "\"trophy_win_count\":8"), Deck().Replace("\"id\":101", "\"id\":999"),
            Deck().Replace("Maindeck", "Missing"), Deck().Replace("\"losses\":0", "\"losses\":-1"),
            Deck().Replace("\"main_colors\":\"BG\"", "\"main_colors\":\"UG\""),
            Deck().Replace("\"splash_colors\":\"\"", "\"splash_colors\":\"u\""), "{}", "null" };
        foreach (var json in malformed)
        {
            using var handler = new Handler(r => Json(r.Method == HttpMethod.Post ? Summaries() : json));
            using var http = new HttpClient(handler); var result = await Client(http).LoadAsync(Key());
            Assert.Equal(SuccessfulDeckSource.Unavailable, result.Source); Assert.Null(result.Corpus);
        }
        using var emptyHandler = new Handler(_ => Json(Summaries("")));
        using var emptyHttp = new HttpClient(emptyHandler); Assert.Null((await Client(emptyHttp).LoadAsync(Key())).Corpus);
    }

    [Fact]
    public async Task CacheIdentitySeparatesFormatPairSetAndLimitAndMakesNoPerPickRequest()
    {
        using var handler = ValidHandler(); using var http = new HttpClient(handler); var client = Client(http);
        var names = new[] { client.CacheFileName(Key()), client.CacheFileName(Key(SuccessfulDeckFormat.PremierDraft)),
            client.CacheFileName(Key(pair: "UG")), client.CacheFileName(Key(set: "HOB")), Client(http, 1).CacheFileName(Key()) };
        Assert.Equal(5, names.Distinct().Count()); Assert.Contains("all-ranks_recent-strict-pair", names[0]);
        await client.LoadAsync(Key()); var result = await Client(http).LoadAsync(Key());
        Assert.Equal(SuccessfulDeckSource.Cache, result.Source); Assert.Equal(2, handler.Calls);
        Assert.Empty(Directory.GetFiles(Path.Combine(_directory, "limited-data", "17lands", "trophy"), "*.tmp"));
    }

    [Fact]
    public async Task StaleValidFallbackAndRetryAfterBoundFailuresWithoutAutomaticRetries()
    {
        using var handler = ValidHandler(); using var http = new HttpClient(handler); var client = Client(http);
        var first = await client.LoadAsync(Key()); _time.Now += TimeSpan.FromHours(25);
        handler.Respond = _ => new(HttpStatusCode.ServiceUnavailable);
        var stale = await client.LoadAsync(Key()); Assert.Equal(SuccessfulDeckSource.StaleCache, stale.Source);
        Assert.Same(first.Corpus, stale.Corpus); await client.LoadAsync(Key()); Assert.Equal(3, handler.Calls);
        using var rateHandler = new Handler(_ => { var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromHours(2)); return response; });
        using var rateHttp = new HttpClient(rateHandler); var rateClient = Client(rateHttp);
        await rateClient.LoadAsync(Key(set: "HOB")); await rateClient.LoadAsync(Key(set: "HOB", pair: "UG"));
        Assert.Equal(1, rateHandler.Calls);
    }

    [Fact]
    public async Task CorruptMismatchedFutureOrRestrictedCachesAreRejected()
    {
        using var handler = ValidHandler(); using var http = new HttpClient(handler); var client = Client(http);
        await client.LoadAsync(Key()); var path = Path.Combine(_directory, "limited-data", "17lands", "trophy", client.CacheFileName(Key()));
        var valid = await File.ReadAllTextAsync(path); handler.Respond = _ => new(HttpStatusCode.ServiceUnavailable);
        var bad = new[] { "{}", "invalid", valid.Replace("\"Schema\":1", "\"Schema\":2"), valid.Replace("\"Pair\":\"BG\"", "\"Pair\":\"UG\""),
            valid.Replace("\"Format\":\"QuickDraft\"", "\"Format\":\"PremierDraft\""), valid.Replace("2026-10-04T16:00:00", "2027-10-04T16:00:00"),
            valid.Replace("\"notes\":null", "\"notes\":\"This data is only for use on 17Lands.com.\"") };
        foreach (var json in bad)
        { await File.WriteAllTextAsync(path, json); var result = await Client(http).LoadAsync(Key());
            Assert.Null(result.Corpus); Assert.Equal(SuccessfulDeckSource.Unavailable, result.Source); }
    }

    public void Dispose()
    {
        var full = Path.GetFullPath(_directory); var parent = Path.GetDirectoryName(full);
        if (parent == Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()))
            && Path.GetFileName(full).StartsWith("DraftTG-trophy-", StringComparison.Ordinal) && Directory.Exists(full))
            Directory.Delete(full, recursive: true);
    }
}
