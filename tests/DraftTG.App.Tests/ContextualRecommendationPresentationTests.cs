using DraftTG.Application;
using DraftTG.ArenaIntegration;
using DraftTG.Domain;

namespace DraftTG.App.Tests;

public sealed partial class MainWindowViewModelTests
{
    private static (int Pack, int Pick, int Card)[] WhitePool(int count) =>
        Enumerable.Range(1, count).Select(i => (1, i, 101)).ToArray();
    private static LimitedStatisticsLoadResult ContextStatistics() =>
        RecommendationStatistics(alpha: 0.58, alphaSample: 5000, beta: 0.59, betaSample: 5000)
            with { CardCatalog = CatalogData().Catalog };

    [Fact]
    public void ContextPickAloneGetsGoldWhileStatsPickRawGihAndAlsaStayIndependent()
    {
        var session = ReadyViewModel();
        using var hud = new OverlayViewModel(session);
        hud.SetCalibrationState(false, true);
        var state = ReadyUpdate(ArenaDraftMode.Quick, [102, 101, 103, 101], WhitePool(14), pack: 2);
        session.ApplySessionUpdate(state);
        var badges = hud.Badges.ToArray();
        var geometry = badges.Select(b => (b.X, b.Y, b.Width)).ToArray();
        var update = new LimitedStatisticsUpdate(state.SnapshotResult.Snapshot, false, ContextStatistics());
        session.ApplyStatisticsUpdate(update);
        Assert.Equal(["#3", "#1", "", "#2"], badges.Select(b => b.Rank));
        Assert.Equal([true, false, false, false], badges.Select(b => b.IsStatisticalPick));
        Assert.Equal([false, true, false, false], badges.Select(b => b.IsContextPick));
        Assert.Equal("#FFE2BE64", badges[1].BorderColor);
        Assert.NotEqual("#FFE2BE64", badges[0].BorderColor);
        Assert.Equal("59.0%", badges[0].WinRate);
        Assert.Equal("58.0%", badges[1].WinRate);
        Assert.Equal("ALSA 6.24", badges[1].Secondary);
        Assert.Contains("Context Pick\nAlpha", session.ContextualRecommendationStatusText);
        Assert.Contains("Stats rank: #2 · Context rank: #1", session.ContextualRecommendationStatusText);
        Assert.Contains("+2.5 pp", session.ContextualRecommendationStatusText);
        Assert.Contains("W +1.00", session.ContextualRecommendationStatusText);
        Assert.Contains("Stats coverage: 3 / 4", session.ContextualRecommendationStatusText);
        Assert.Contains("Stats Pick\nBeta", session.RecommendationStatusText);
        Assert.Equal("Stats Pick: Beta", session.StatisticalPickStatusText);
        Assert.Contains("W evidence 14", session.ContextualRecommendationDiagnosticsText);
        Assert.Contains("Slot 2: stats", session.ContextualRecommendationDiagnosticsText);
        Assert.Equal(geometry, badges.Select(b => (b.X, b.Y, b.Width)));
        for (var i = 0; i < badges.Length; i++) Assert.Same(badges[i], hud.Badges[i]);
        Assert.Empty(hud.Guides);
    }

    [Theory]
    [InlineData(0, "0.00", 1)]
    [InlineData(1, "0.07", 1)]
    [InlineData(5, "0.36", 0)]
    [InlineData(14, "1.00", 0)]
    public void RailMakesProgressVisibleAndRecommendationsRecomputeFromNewPool(int picks, string progress, int top)
    {
        var session = ReadyViewModel();
        using var hud = new OverlayViewModel(session);
        var state = ReadyUpdate(ArenaDraftMode.Quick, [101, 102], WhitePool(picks), pack: picks == 14 ? 2 : 1,
            pick: picks == 14 ? 1 : picks + 1);
        session.ApplySessionUpdate(state);
        var update = new LimitedStatisticsUpdate(state.SnapshotResult.Snapshot, false, ContextStatistics());
        session.ApplyStatisticsUpdate(update);
        Assert.Equal(top, update.ContextualRecommendation!.TopRecommendedPackIndex);
        Assert.Contains($"Commitment progress: {progress}", session.ContextualRecommendationStatusText);
        Assert.Equal(1, update.Recommendation!.TopRecommendedPackIndex);
        var scoreTop = update.PickScores!.TopRecommendedPackIndex!.Value;
        Assert.True(hud.Badges[scoreTop].IsContextPick);
        Assert.Equal(scoreTop == 1 ? string.Empty : "Stats Pick: Beta", session.StatisticalPickStatusText);
    }

    [Fact]
    public void ChangingPackAndNoPackClearContextAndRejectStaleContextualArrival()
    {
        var session = ReadyViewModel();
        using var hud = new OverlayViewModel(session);
        var old = ReadyUpdate(ArenaDraftMode.Quick, [101, 102], WhitePool(14), pack: 2);
        session.ApplySessionUpdate(old);
        var previous = new LimitedStatisticsUpdate(old.SnapshotResult.Snapshot, false, ContextStatistics());
        session.ApplyStatisticsUpdate(previous);
        Assert.True(hud.Badges[0].IsContextPick);
        var next = ReadyUpdate(ArenaDraftMode.Quick, [102, 103], WhitePool(14), pack: 2, pick: 2);
        session.ApplySessionUpdate(next);
        Assert.All(hud.Badges, b => { Assert.Empty(b.Rank); Assert.False(b.IsContextPick); });
        Assert.DoesNotContain("Context Pick\nAlpha", session.ContextualRecommendationStatusText);
        Assert.Empty(session.ContextualRecommendationDiagnosticsText);
        session.ApplyStatisticsUpdate(previous);
        Assert.All(hud.Badges, b => Assert.False(b.IsContextPick));
        session.ApplySessionUpdate(Update(State(ArenaDraftSessionStatus.Active, ArenaDraftMode.Quick, null, WhitePool(14))));
        Assert.Empty(session.ContextualRecommendationStatusText);
        Assert.Empty(session.StatisticalPickStatusText);
        Assert.Empty(session.ContextualRecommendationDiagnosticsText);
        Assert.Empty(hud.Badges);
        session.ApplyStatisticsUpdate(previous);
        Assert.Empty(session.ContextualRecommendationStatusText);
    }

    [Fact]
    public void ContextualUpdatesPreserveCalibrationGuidesAndPassThroughMode()
    {
        var session = ReadyViewModel();
        using var hud = new OverlayViewModel(session);
        var native = new FakeClickThrough();
        var modes = new DraftTG.App.Platform.OverlayInteractionController(native);
        Assert.True(modes.TryApply(false, out _));
        hud.SetCalibrationState(false, true);
        var state = ReadyUpdate(ArenaDraftMode.Quick, [101, 102], WhitePool(14), pack: 2);
        session.ApplySessionUpdate(state);
        var geometry = hud.Badges.Select(b => (b.X, b.Y, b.Width)).ToArray();
        session.ApplyStatisticsUpdate(new(state.SnapshotResult.Snapshot, false, ContextStatistics()));
        Assert.Equal([DraftTG.App.Platform.OverlayInteractionMode.Passive], native.Modes);
        Assert.True(modes.TryApply(true, out _));
        hud.SetCalibrationState(true, true);
        var guides = hud.Guides.ToArray();
        session.ApplyStatisticsUpdate(new(state.SnapshotResult.Snapshot, false, ContextStatistics()));
        Assert.Equal(guides, hud.Guides);
        Assert.True(hud.IsCalibrating);
        Assert.Equal(geometry, hud.Badges.Select(b => (b.X, b.Y, b.Width)));
    }
}
