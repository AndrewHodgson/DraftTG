using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace DraftTG.Data.Tests;

public sealed partial class SeventeenLandsCardRatingsClientTests
{
    private static string CurrentApiFixture => File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "17lands-api-card-data.json"));
    private string CurrentCachePath => Path.Combine(_directory, "limited-data", "17lands", "WOE_QuickDraft_ALL_TIME_v3.json");
    private static HttpResponseMessage RawApi(string json) => new(HttpStatusCode.OK)
        { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Theory]
    [InlineData(SeventeenLandsFormat.QuickDraft, "QuickDraft")]
    [InlineData(SeventeenLandsFormat.PremierDraft, "PremierDraft")]
    [InlineData(SeventeenLandsFormat.TradDraft, "TradDraft")]
    public async Task CurrentEndpointUsesExactEventTypeAndAllTime(SeventeenLandsFormat format, string eventType)
    {
        using var handler = new Handler(request =>
        {
            Assert.Equal($"https://www.17lands.com/api/card_data?expansion=WOE&event_type={eventType}&time_period=ALL_TIME",
                request.RequestUri!.AbsoluteUri);
            return RawApi(CurrentApiFixture);
        });
        var result = await Client(handler).LoadAsync("woe", format);
        Assert.Equal(format, result.Format);
        Assert.Equal(SeventeenLandsSource.Live, result.Source);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task CurrentEnvelopePreservesGihSemanticsAndCacheSourceMetadata()
    {
        using var handler = new Handler(_ => RawApi(CurrentApiFixture));
        var result = await Client(handler).LoadAsync("WOE", SeventeenLandsFormat.QuickDraft);
        var alpha = Alpha(result.Rows);
        Assert.Equal(0.575, alpha.GameInHandWinRate);
        Assert.Equal(900, alpha.GameInHandGameCount);
        Assert.NotEqual(0.510, alpha.GameInHandWinRate);
        Assert.NotEqual(5000, alpha.GameInHandGameCount);
        Assert.Equal(6.24, alpha.AverageLastSeenAt);
        Assert.Equal(4.2, alpha.AverageTakenAt);
        Assert.Equal(0.59, alpha.OpeningHandWinRate);
        Assert.Equal(0.56, alpha.DrawnWinRate);
        Assert.Equal(0.025, alpha.DrawnImprovementWinRate);
        Assert.Null(Assert.Single(result.Rows, row => row.Name == "Gamma").GameInHandWinRate);
        Assert.Equal(new SeventeenLandsDatasetMetadata("/api/card_data", "ALL_TIME", 20), result.Metadata);
        Assert.Contains("Data: network", result.DatasetDiagnosticText, StringComparison.Ordinal);
        var cached = await Client(handler).LoadAsync("WOE", SeventeenLandsFormat.QuickDraft);
        Assert.Equal(result.Rows, cached.Rows);
        Assert.Equal(result.Metadata, cached.Metadata);
        Assert.Contains("Data: fresh cache", cached.DatasetDiagnosticText, StringComparison.Ordinal);
        var cache = JsonNode.Parse(await File.ReadAllTextAsync(CurrentCachePath))!;
        Assert.Equal(3, cache["SchemaVersion"]!.GetValue<int>());
        Assert.Equal("/api/card_data", cache["SourceEndpoint"]!.GetValue<string>());
        Assert.Equal("ALL_TIME", cache["TimePeriod"]!.GetValue<string>());
        Assert.Equal(0.510, cache["Payload"]!["data"]![0]!["win_rate"]!.GetValue<double>());
        Assert.Equal(0.575, cache["Payload"]!["data"]![0]!["ever_drawn_win_rate"]!.GetValue<double>());
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"data\":null}")]
    [InlineData("{\"data\":{}}")]
    [InlineData("{\"data\":\"not an array\"}")]
    [InlineData("[{\"name\":\"Alpha\",\"ever_drawn_win_rate\":0.6,\"ever_drawn_game_count\":1000}]")]
    public async Task LegacyOrMalformedEnvelopeIsNeverAccepted(string json)
    {
        using var handler = new Handler(_ => RawApi(json));
        var result = await Client(handler).LoadAsync("WOE", SeventeenLandsFormat.QuickDraft);
        Assert.Equal(SeventeenLandsSource.Unavailable, result.Source);
        Assert.Empty(result.Rows);
        Assert.False(File.Exists(CurrentCachePath));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("tiny")]
    [InlineData("no-gih")]
    [InlineData("html")]
    public async Task SuspiciousDatasetWithoutCacheIsUnavailableAndHasBoundedCooldown(string kind)
    {
        using var handler = new Handler(_ => RawApi(Suspicious(kind)));
        var client = Client(handler);
        var result = await client.LoadAsync("WOE", SeventeenLandsFormat.QuickDraft);
        Assert.Equal(SeventeenLandsSource.Unavailable, result.Source);
        Assert.Empty(result.Rows);
        Assert.NotNull(result.Diagnostic);
        Assert.Equal(result, await client.LoadAsync("WOE", SeventeenLandsFormat.QuickDraft));
        Assert.Equal(1, handler.Calls);
        Assert.False(File.Exists(CurrentCachePath));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("tiny")]
    [InlineData("no-gih")]
    [InlineData("html")]
    public async Task SuspiciousRefreshRetainsValidStaleDatasetAndDoesNotOverwriteCache(string kind)
    {
        using var handler = new Handler(_ => RawApi(CurrentApiFixture));
        var initial = await Client(handler).LoadAsync("WOE", SeventeenLandsFormat.QuickDraft);
        var originalCache = await File.ReadAllTextAsync(CurrentCachePath);
        _time.Now += TimeSpan.FromDays(2);
        handler.Response = _ => RawApi(Suspicious(kind));
        var client = Client(handler);
        var result = await client.LoadAsync("WOE", SeventeenLandsFormat.QuickDraft);
        Assert.Equal(SeventeenLandsSource.StaleCache, result.Source);
        Assert.Equal(initial.FetchedAt, result.FetchedAt);
        Assert.Equal(initial.Rows, result.Rows);
        Assert.Equal(initial.Metadata, result.Metadata);
        Assert.Contains("Data: stale cache", result.DatasetDiagnosticText, StringComparison.Ordinal);
        Assert.Contains("cached statistics", result.Diagnostic, StringComparison.Ordinal);
        Assert.Equal(originalCache, await File.ReadAllTextAsync(CurrentCachePath));
        await client.LoadAsync("WOE", SeventeenLandsFormat.QuickDraft);
        Assert.Equal(2, handler.Calls);
    }

    [Theory]
    [InlineData("SourceEndpoint", "/card_ratings/data")]
    [InlineData("TimePeriod", "LAST_DAY")]
    [InlineData("Expansion", "HOB")]
    [InlineData("RequestedFormat", "1")]
    [InlineData("SourceFormat", "0")]
    [InlineData("SchemaVersion", "2")]
    [InlineData("SchemaVersion", null)]
    [InlineData("SourceEndpoint", null)]
    [InlineData("TimePeriod", null)]
    public async Task CacheWithIncompatibleIdentityCannotBecomeStaleFallback(string field, string? value)
    {
        using var handler = new Handler(_ => RawApi(CurrentApiFixture));
        await Client(handler).LoadAsync("WOE", SeventeenLandsFormat.QuickDraft);
        var cache = JsonNode.Parse(await File.ReadAllTextAsync(CurrentCachePath))!.AsObject();
        if (value is null) cache.Remove(field);
        else if (int.TryParse(value, out var number)) cache[field] = number;
        else cache[field] = value;
        await File.WriteAllTextAsync(CurrentCachePath, cache.ToJsonString());
        handler.Response = _ => new(HttpStatusCode.ServiceUnavailable);
        var result = await Client(handler).LoadAsync("WOE", SeventeenLandsFormat.QuickDraft);
        Assert.Equal(SeventeenLandsSource.Unavailable, result.Source);
        Assert.Empty(result.Rows);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task LegacyVersionTwoFilenameIsIgnoredAndCurrentCacheIsCreatedAutomatically()
    {
        var directory = Path.Combine(_directory, "limited-data", "17lands");
        Directory.CreateDirectory(directory);
        var legacy = Path.Combine(directory, "WOE_QuickDraft.json");
        const string oldJson = """{"SchemaVersion":2,"Expansion":"WOE","RequestedFormat":2,"SourceFormat":2,"FetchedAt":"2026-09-26T12:00:00Z","Rows":[{"name":"Alpha","ever_drawn_win_rate":0.9,"ever_drawn_game_count":9999}]}""";
        await File.WriteAllTextAsync(legacy, oldJson);
        using var handler = new Handler(_ => RawApi(CurrentApiFixture));
        var result = await Client(handler).LoadAsync("WOE", SeventeenLandsFormat.QuickDraft);
        Assert.Equal(SeventeenLandsSource.Live, result.Source);
        Assert.Equal(0.575, Alpha(result.Rows).GameInHandWinRate);
        Assert.Equal(oldJson, await File.ReadAllTextAsync(legacy));
        Assert.True(File.Exists(CurrentCachePath));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task FormatAndExpansionCachesAreIndependent()
    {
        using var handler = new Handler(_ => RawApi(CurrentApiFixture));
        foreach (var format in Enum.GetValues<SeventeenLandsFormat>())
            await Client(handler).LoadAsync("WOE", format);
        await Client(handler).LoadAsync("HOB", SeventeenLandsFormat.QuickDraft);
        foreach (var format in Enum.GetValues<SeventeenLandsFormat>())
            Assert.Equal(SeventeenLandsSource.Cache, (await Client(handler).LoadAsync("WOE", format)).Source);
        Assert.Equal(4, handler.Calls);
    }

    [Fact]
    public async Task SuspiciousCachedPayloadCannotBeUsedWhenNetworkIsUnavailable()
    {
        using var handler = new Handler(_ => RawApi(CurrentApiFixture));
        await Client(handler).LoadAsync("WOE", SeventeenLandsFormat.QuickDraft);
        var cache = JsonNode.Parse(await File.ReadAllTextAsync(CurrentCachePath))!;
        cache["Payload"] = JsonNode.Parse(Suspicious("no-gih"));
        await File.WriteAllTextAsync(CurrentCachePath, cache.ToJsonString());
        handler.Response = _ => new(HttpStatusCode.ServiceUnavailable);
        Assert.Equal(SeventeenLandsSource.Unavailable,
            (await Client(handler).LoadAsync("WOE", SeventeenLandsFormat.QuickDraft)).Source);
    }

    [Theory]
    [InlineData(19, SeventeenLandsSource.Unavailable)]
    [InlineData(20, SeventeenLandsSource.Live)]
    public async Task WholeEnvironmentSanityFloorIsConservativeNotAnExactSetCount(int count, SeventeenLandsSource expected)
    {
        var payload = JsonNode.Parse(CurrentApiFixture)!;
        var rows = payload["data"]!.AsArray();
        while (rows.Count > count) rows.RemoveAt(rows.Count - 1);
        using var handler = new Handler(_ => RawApi(payload.ToJsonString()));
        Assert.Equal(expected, (await Client(handler).LoadAsync("WOE", SeventeenLandsFormat.QuickDraft)).Source);
    }

    private static string Suspicious(string kind)
    {
        if (kind == "html") return "<html>Provider unavailable</html>";
        var envelope = JsonNode.Parse(CurrentApiFixture)!;
        var rows = envelope["data"]!.AsArray();
        if (kind == "empty") rows.Clear();
        else if (kind == "tiny") while (rows.Count > 2) rows.RemoveAt(rows.Count - 1);
        else foreach (var row in rows) row!["ever_drawn_win_rate"] = null;
        return envelope.ToJsonString();
    }
}
