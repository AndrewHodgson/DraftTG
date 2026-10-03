using System.Net;
using System.Text;
using DraftTG.Application;
using DraftTG.Data;

namespace DraftTG.App.Tests;

public sealed partial class MainWindowViewModelTests
{
    [Fact]
    public async Task CurrentApiArrivalRecomputesRanksInPlaceAndExposesDatasetDiagnosticsWithoutRestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "DraftTG-api-ui-" + Guid.NewGuid());
        using var handler = new PendingApiHandler();
        using var http = new HttpClient(handler);
        var client = new SeventeenLandsCardRatingsClient(http, new ApiPaths(directory));
        var runtime = CreateRuntime(new StatisticsLogSource()) with
        {
            Statistics = new LimitedStatisticsCoordinator(new LimitedStatisticsService(client, CatalogData().Catalog))
        };
        var session = CreateViewModel(new ImmediateRuntimeFactory(runtime));
        using var hud = new OverlayViewModel(session);
        hud.SetCalibrationState(false, true);
        hud.SetViewport(1400, 600);
        try
        {
            session.Start();
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitUntilAsync(() => hud.Badges.Count == 2);
            var badges = hud.Badges.ToArray();
            var geometry = badges.Select(badge => (badge.Index, badge.X, badge.Y, badge.Width)).ToArray();
            Assert.All(badges, badge => Assert.Empty(badge.Rank));
            var fixture = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,
                "Fixtures", "17lands-api-card-data.json"));
            // The actual endpoint labels its JSON as text/html. Body shape, not the label, is authoritative.
            handler.Response.SetResult(new(HttpStatusCode.OK)
                { Content = new StringContent(fixture, Encoding.UTF8, "text/html") });
            await WaitUntilAsync(() => hud.Badges[1].IsStatisticalPick);
            Assert.Equal(["#2", "#1"], hud.Badges.Select(badge => badge.Rank));
            Assert.Equal(["57.5%", "60.0%"], hud.Badges.Select(badge => badge.WinRate));
            Assert.Equal("ALSA 6.24", hud.Badges[0].Secondary);
            Assert.Equal("#FFE2BE64", hud.Badges[1].BorderColor);
            Assert.Equal(geometry, hud.Badges.Select(badge => (badge.Index, badge.X, badge.Y, badge.Width)));
            for (var i = 0; i < badges.Length; i++) Assert.Same(badges[i], hud.Badges[i]);
            Assert.Contains("Stats Pick\nBeta", session.RecommendationStatusText, StringComparison.Ordinal);
            Assert.Contains("Coverage: 2 / 2", session.RecommendationStatusText, StringComparison.Ordinal);
            Assert.Contains("Expansion: TST", session.StatisticsCoverageText, StringComparison.Ordinal);
            Assert.Contains("Requested format: QuickDraft", session.StatisticsCoverageText, StringComparison.Ordinal);
            Assert.Contains("Source endpoint: /api/card_data", session.StatisticsCoverageText, StringComparison.Ordinal);
            Assert.Contains("Time period: ALL_TIME", session.StatisticsCoverageText, StringComparison.Ordinal);
            Assert.Contains("Provider rows: 20", session.StatisticsCoverageText, StringComparison.Ordinal);
            Assert.Contains("Data: network", session.StatisticsCoverageText, StringComparison.Ordinal);
            Assert.Contains("GIH available: 2 / 2", session.StatisticsCoverageText, StringComparison.Ordinal);
            Assert.Contains("ALSA available: 2 / 2", session.StatisticsCoverageText, StringComparison.Ordinal);
            Assert.Empty(hud.Guides);
            Assert.Equal(1, handler.Calls);
        }
        finally
        {
            await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private sealed class ApiPaths(string directory) : IApplicationDataPathProvider
    {
        public string GetApplicationDataDirectory() => directory;
    }

    private sealed class PendingApiHandler : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<HttpResponseMessage> Response { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("https://www.17lands.com/api/card_data?expansion=TST&event_type=QuickDraft&time_period=ALL_TIME",
                request.RequestUri!.AbsoluteUri);
            Calls++;
            Started.TrySetResult();
            return await Response.Task.WaitAsync(cancellationToken);
        }
    }
}
