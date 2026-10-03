using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace DraftTG.Data.Tests;

public sealed class SeventeenLandsCardRatingsClientTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "DraftTG-ratings-" + Guid.NewGuid());
    private readonly TestTime _time = new();
    private const string Valid = """[{"name":"Alpha","game_count":4820,"play_rate":0.7,"win_rate":0.5874,"opening_hand_win_rate":0.59,"drawn_win_rate":0.58,"drawn_improvement_win_rate":-0.012,"avg_seen":6.24,"avg_pick":4.2,"future_field":{}}]""";

    [Fact]
    public async Task DecodesMetricsAndUsesWholeEnvironmentRequestWithCourtesyHeaders()
    {
        using var handler = new Handler(request =>
        {
            Assert.Equal("https://www.17lands.com/card_ratings/data?expansion=HOB&format=QuickDraft", request.RequestUri!.AbsoluteUri);
            Assert.Contains("DraftTG/0.7", request.Headers.UserAgent.ToString(), StringComparison.Ordinal);
            Assert.Contains(request.Headers.Accept, value => value.MediaType == "application/json");
            return Json(Valid);
        });
        var result = await Client(handler).LoadAsync("hob", SeventeenLandsFormat.QuickDraft);
        var row = Assert.Single(result.Rows);
        Assert.Equal(4820, row.GameCount);
        Assert.Equal(0.7, row.PlayRate);
        Assert.Equal(0.5874, row.GameInHandWinRate);
        Assert.Equal(0.59, row.OpeningHandWinRate);
        Assert.Equal(0.58, row.DrawnWinRate);
        Assert.Equal(-0.012, row.DrawnImprovementWinRate);
        Assert.Equal(6.24, row.AverageLastSeenAt);
        Assert.Equal(4.2, row.AverageTakenAt);
        Assert.Equal(SeventeenLandsSource.Live, result.Source);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task OptionalAndNullMetricsRemainMissing()
    {
        using var handler = new Handler(_ => Json("""[{"name":"Alpha","win_rate":null}]"""));
        var row = Assert.Single((await Client(handler).LoadAsync("HOB", SeventeenLandsFormat.QuickDraft)).Rows);
        Assert.Null(row.GameInHandWinRate);
        Assert.Null(row.GameCount);
        Assert.Null(row.AverageTakenAt);
    }

    [Theory]
    [InlineData("[{\"name\":\"A\",\"win_rate\":1.1}]")]
    [InlineData("[{\"name\":\"A\",\"play_rate\":-0.1}]")]
    [InlineData("[{\"name\":\"A\",\"game_count\":-1}]")]
    [InlineData("[{\"name\":\"A\",\"avg_seen\":0}]")]
    [InlineData("[{\"name\":\"A\",\"avg_pick\":-2}]")]
    [InlineData("[{\"name\":\"A\",\"win_rate\":1e999}]")]
    [InlineData("[{\"name\":\"A\",\"win_rate\":\"NaN\"}]")]
    [InlineData("[{\"name\":\"A\",\"drawn_improvement_win_rate\":-1.1}]")]
    [InlineData("[{\"name\":\"A\"},{\"name\":\"A\"}]")]
    [InlineData("[{}]")]
    [InlineData("[null]")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("<html>changed endpoint</html>")]
    public async Task CorruptResponsesAreNonfatalUnavailable(string json)
    {
        using var handler = new Handler(_ => Json(json));
        var result = await Client(handler).LoadAsync("HOB", SeventeenLandsFormat.QuickDraft);
        Assert.Equal(SeventeenLandsSource.Unavailable, result.Source);
        Assert.Empty(result.Rows);
        Assert.Equal("17Lands returned invalid statistics.", result.Diagnostic);
    }

    [Fact]
    public async Task HtmlBodyIsRejected()
    {
        using var handler = new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent("<html/>", Encoding.UTF8, "text/html") });
        Assert.Equal(SeventeenLandsSource.Unavailable,
            (await Client(handler).LoadAsync("HOB", SeventeenLandsFormat.QuickDraft)).Source);
    }

    [Fact]
    public async Task HttpFailureWithoutCacheIsUnavailable()
    {
        using var handler = new Handler(_ => new(HttpStatusCode.ServiceUnavailable));
        var result = await Client(handler).LoadAsync("HOB", SeventeenLandsFormat.QuickDraft);
        Assert.Equal(SeventeenLandsSource.Unavailable, result.Source);
        Assert.NotNull(result.Diagnostic);
    }

    [Fact]
    public async Task FreshDiskCacheAvoidsHttpEvenAcrossClientInstances()
    {
        using var handler = new Handler(_ => Json(Valid));
        await Client(handler).LoadAsync("HOB", SeventeenLandsFormat.QuickDraft);
        _time.Now += TimeSpan.FromHours(23);
        var result = await Client(handler).LoadAsync("HOB", SeventeenLandsFormat.QuickDraft);
        Assert.Equal(SeventeenLandsSource.Cache, result.Source);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(0.5874, Assert.Single(result.Rows).GameInHandWinRate);
    }

    [Fact]
    public async Task EmptyDatasetIsCached()
    {
        using var handler = new Handler(_ => Json("[]"));
        await Client(handler).LoadAsync("HOB", SeventeenLandsFormat.QuickDraft);
        Assert.Empty((await Client(handler).LoadAsync("HOB", SeventeenLandsFormat.QuickDraft)).Rows);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task TtlBoundaryRefreshesAndPersistsNewValues()
    {
        using var handler = new Handler(_ => Json(Valid));
        await Client(handler).LoadAsync("HOB", SeventeenLandsFormat.QuickDraft);
        _time.Now += TimeSpan.FromHours(24);
        handler.Response = _ => Json(Valid.Replace("0.5874", "0.61", StringComparison.Ordinal));
        var refreshed = await Client(handler).LoadAsync("HOB", SeventeenLandsFormat.QuickDraft);
        Assert.Equal(SeventeenLandsSource.Live, refreshed.Source);
        var cached = await Client(handler).LoadAsync("HOB", SeventeenLandsFormat.QuickDraft);
        Assert.Equal(0.61, Assert.Single(cached.Rows).GameInHandWinRate);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task StaleCacheSurvivesFailedRefreshAndRetainsTimestamp()
    {
        using var handler = new Handler(_ => Json(Valid));
        var original = await Client(handler).LoadAsync("HOB", SeventeenLandsFormat.QuickDraft);
        _time.Now += TimeSpan.FromDays(2);
        handler.Response = _ => throw new HttpRequestException("Offline");
        var result = await Client(handler).LoadAsync("HOB", SeventeenLandsFormat.QuickDraft);
        Assert.Equal(SeventeenLandsSource.StaleCache, result.Source);
        Assert.Equal(original.FetchedAt, result.FetchedAt);
        Assert.Single(result.Rows);
        Assert.Contains("cached", result.Diagnostic, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RateLimitHonorsDeltaOrDateWithoutRetryLoops(bool date)
    {
        using var handler = new Handler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = date ? new RetryConditionHeaderValue(_time.Now.AddMinutes(10))
                : new RetryConditionHeaderValue(TimeSpan.FromMinutes(10));
            return response;
        });
        var client = Client(handler);
        await client.LoadAsync("HOB", SeventeenLandsFormat.QuickDraft);
        await client.LoadAsync("HOB", SeventeenLandsFormat.PremierDraft, forceRefresh: true);
        Assert.Equal(1, handler.Calls);
        _time.Now += TimeSpan.FromMinutes(10);
        handler.Response = _ => Json(Valid);
        Assert.Equal(SeventeenLandsSource.Live, (await client.LoadAsync("HOB", SeventeenLandsFormat.QuickDraft)).Source);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task CancellationPropagatesDuringHttp()
    {
        using var handler = new BlockingHandler();
        using var http = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        var task = new SeventeenLandsCardRatingsClient(http, new Paths(_directory), _time)
            .LoadAsync("HOB", SeventeenLandsFormat.QuickDraft, cancellationToken: cancellation.Token);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public async Task ConcurrentLoadsShareOneRequest()
    {
        using var handler = new Handler(_ => Json(Valid));
        var client = Client(handler);
        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => client.LoadAsync("HOB", SeventeenLandsFormat.QuickDraft)));
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("../HOB")]
    [InlineData("HOB&format=Other")]
    [InlineData("/tmp")]
    public async Task UnsafeExpansionIsRejectedBeforeIo(string expansion)
    {
        using var handler = new Handler(_ => Json(Valid));
        await Assert.ThrowsAsync<ArgumentException>(() => Client(handler).LoadAsync(expansion, SeventeenLandsFormat.QuickDraft));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task MismatchedCacheContextIsNotReused()
    {
        using var handler = new Handler(_ => Json(Valid));
        await Client(handler).LoadAsync("HOB", SeventeenLandsFormat.QuickDraft);
        var path = Path.Combine(_directory, "limited-data", "17lands", "HOB_QuickDraft.json");
        await File.WriteAllTextAsync(path, (await File.ReadAllTextAsync(path)).Replace("HOB", "OTHER", StringComparison.Ordinal));
        await Client(handler).LoadAsync("HOB", SeventeenLandsFormat.QuickDraft);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task FailedRefreshDoesNotOverwriteValidCache()
    {
        using var handler = new Handler(_ => Json(Valid));
        await Client(handler).LoadAsync("HOB", SeventeenLandsFormat.QuickDraft);
        handler.Response = _ => Json("{}");
        var result = await Client(handler).LoadAsync("HOB", SeventeenLandsFormat.QuickDraft, forceRefresh: true);
        Assert.Equal(SeventeenLandsSource.StaleCache, result.Source);
        Assert.Single((await Client(handler).LoadAsync("HOB", SeventeenLandsFormat.QuickDraft)).Rows);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task FailedEnvironmentLoadsHaveOneHourCooldown()
    {
        using var handler = new Handler(_ => new(HttpStatusCode.ServiceUnavailable));
        var client = Client(handler);
        await client.LoadAsync("HOB", SeventeenLandsFormat.QuickDraft);
        await client.LoadAsync("HOB", SeventeenLandsFormat.QuickDraft);
        await client.LoadAsync("HOB", SeventeenLandsFormat.QuickDraft);
        Assert.Equal(1, handler.Calls);
        _time.Now += TimeSpan.FromHours(1);
        handler.Response = _ => Json(Valid);
        Assert.Equal(SeventeenLandsSource.Live, (await client.LoadAsync("HOB", SeventeenLandsFormat.QuickDraft)).Source);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task RequiredCacheMetadataCannotDefaultToPremierFormat()
    {
        using var handler = new Handler(_ => Json(Valid));
        await Client(handler).LoadAsync("HOB", SeventeenLandsFormat.PremierDraft);
        var path = Path.Combine(_directory, "limited-data", "17lands", "HOB_PremierDraft.json");
        var json = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        json.Remove("RequestedFormat");
        await File.WriteAllTextAsync(path, json.ToJsonString());
        await Client(handler).LoadAsync("HOB", SeventeenLandsFormat.PremierDraft);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task ValidJsonWithIncorrectContentTypeStillDecodes()
    {
        using var handler = new Handler(_ => new(HttpStatusCode.OK)
            { Content = new StringContent(Valid, Encoding.UTF8, "text/html") });
        var result = await Client(handler).LoadAsync("HOB", SeventeenLandsFormat.QuickDraft);
        Assert.Equal(SeventeenLandsSource.Live, result.Source);
        Assert.Equal(0.5874, Assert.Single(result.Rows).GameInHandWinRate);
    }

    private SeventeenLandsCardRatingsClient Client(Handler handler) =>
        new(new HttpClient(handler, disposeHandler: false), new Paths(_directory), _time);
    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
        { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private sealed class Paths(string directory) : IApplicationDataPathProvider
    { public string GetApplicationDataDirectory() => directory; }
    private sealed class TestTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.Parse("2026-09-26T12:00:00Z");
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public Func<HttpRequestMessage, HttpResponseMessage> Response { get; set; } = response;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; return Task.FromResult(Response(request)); }
    }
    private sealed class BlockingHandler : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Started.SetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); return Json("[]"); }
    }
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
