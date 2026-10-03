using DraftTG.Application;
using DraftTG.App.Platform;
using DraftTG.ArenaIntegration;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;

namespace DraftTG.App.Tests;

public sealed partial class MainWindowViewModelTests
{
    private static LimitedStatisticsLoadResult RecommendationStatistics(double alpha = 0.70, int alphaSample = 20,
        double beta = 0.58, int betaSample = 5000)
    {
        var context = new LimitedStatisticsContext("HOB", LimitedStatisticsFormat.QuickDraft);
        return new(context, context, new([
            new(CardIdentifier.Create("domain-a"), GameInHandWinRate: alpha,
                GameInHandGameCount: alphaSample, AverageLastSeenAt: 6.24),
            new(CardIdentifier.Create("domain-b"), GameInHandWinRate: beta,
                GameInHandGameCount: betaSample, AverageLastSeenAt: 4.1),
            new(CardIdentifier.Create("domain-c"), AverageLastSeenAt: 5.8)
        ]), LimitedStatisticsSource.Live, DateTimeOffset.UtcNow, null)
        {
            EnvironmentCatalog = new([new(CardIdentifier.Create("environment"),
                GameInHandWinRate: 0.55, GameInHandGameCount: 10000)])
        };
    }

    [Fact]
    public void StatsPickRanksOccurrencesInPlaceAndShowsRawGihWithPartialCoverage()
    {
        var session = ReadyViewModel();
        using var hud = new OverlayViewModel(session);
        hud.SetCalibrationState(false, true);
        hud.SetViewport(1400, 600);
        var update = ReadyUpdate(ArenaDraftMode.Quick, [101, 102, 101, 103], []);
        session.ApplySessionUpdate(update);
        Assert.All(hud.Badges, badge => Assert.Empty(badge.Rank));
        Assert.All(hud.Badges, badge => Assert.False(badge.IsStatisticalPick));
        var before = hud.Badges.ToArray();
        var geometry = before.Select(badge => (badge.Index, badge.X, badge.Y, badge.Width)).ToArray();
        session.ApplyStatisticsUpdate(new(update.SnapshotResult.Snapshot, true, null));
        Assert.Contains("loading", session.RecommendationStatusText, StringComparison.Ordinal);
        Assert.All(hud.Badges, badge => Assert.Empty(badge.Rank));
        var statistics = new LimitedStatisticsUpdate(update.SnapshotResult.Snapshot, false, RecommendationStatistics());
        session.ApplyStatisticsUpdate(statistics);
        Assert.Equal(["#2", "#1", "#3", ""], hud.Badges.Select(badge => badge.Rank));
        Assert.Equal([false, true, false, false], hud.Badges.Select(badge => badge.IsStatisticalPick));
        Assert.Equal(["70.0%*", "58.0%", "70.0%*", "\u2014"], hud.Badges.Select(badge => badge.WinRate));
        Assert.Equal("ALSA 5.80", hud.Badges[3].Secondary);
        Assert.True(statistics.Recommendation!.Cards[0].AdjustedValue < 0.56);
        Assert.Equal("#FFE2BE64", hud.Badges[1].BorderColor);
        Assert.NotEqual(hud.Badges[0].BorderColor, hud.Badges[1].BorderColor);
        Assert.Contains("Stats Pick\nBeta", session.RecommendationStatusText, StringComparison.Ordinal);
        Assert.Contains("Coverage: 3 / 4", session.RecommendationStatusText, StringComparison.Ordinal);
        Assert.Contains("Partial statistics", session.RecommendationStatusText, StringComparison.Ordinal);
        Assert.Contains("Baseline: 55.0%", session.RecommendationStatusText, StringComparison.Ordinal);
        Assert.DoesNotContain("Best Pick", session.RecommendationStatusText, StringComparison.Ordinal);
        Assert.Equal(geometry, hud.Badges.Select(badge => (badge.Index, badge.X, badge.Y, badge.Width)));
        for (var i = 0; i < before.Length; i++) Assert.Same(before[i], hud.Badges[i]);
        Assert.Equal(["Alpha", "Beta", "Alpha", "Gamma"], hud.Badges.Select(badge => badge.Name));
        Assert.Empty(hud.Guides);
        Assert.True(hud.ShowBadges);
    }

    [Fact]
    public void StatisticsChangeUpdatesRanksWithoutRebuildingSlotsOrUiDrivenRecomputation()
    {
        var session = ReadyViewModel();
        using var hud = new OverlayViewModel(session);
        var update = ReadyUpdate(ArenaDraftMode.Quick, [101, 102, 101], []);
        session.ApplySessionUpdate(update);
        session.ApplyStatisticsUpdate(new(update.SnapshotResult.Snapshot, false, RecommendationStatistics()));
        var first = hud.Badges.ToArray();
        session.ApplyStatisticsUpdate(new(update.SnapshotResult.Snapshot, false,
            RecommendationStatistics(alpha: 0.65, alphaSample: 900, beta: 0.5, betaSample: 900)));
        Assert.Equal(["#1", "#3", "#2"], hud.Badges.Select(badge => badge.Rank));
        Assert.Contains("Stats Pick\nAlpha", session.RecommendationStatusText, StringComparison.Ordinal);
        Assert.DoesNotContain("Partial statistics", session.RecommendationStatusText, StringComparison.Ordinal);
        var recommendation = session.CurrentPackCards[0].Recommendation;
        hud.SetPanel(RailPanel.Status);
        hud.ToggleStatistics();
        hud.SetViewport(1100, 550);
        Assert.Same(recommendation, session.CurrentPackCards[0].Recommendation);
        for (var i = 0; i < first.Length; i++) Assert.Same(first[i], hud.Badges[i]);
    }

    [Fact]
    public void PackChangesClearOldRanksRejectStaleUpdatesAndRemoveRecommendationWithoutPack()
    {
        var session = ReadyViewModel();
        using var hud = new OverlayViewModel(session);
        var old = ReadyUpdate(ArenaDraftMode.Quick, [101, 102], []);
        session.ApplySessionUpdate(old);
        var previous = new LimitedStatisticsUpdate(old.SnapshotResult.Snapshot, false, RecommendationStatistics());
        session.ApplyStatisticsUpdate(previous);
        Assert.True(hud.Badges[1].IsStatisticalPick);
        var next = ReadyUpdate(ArenaDraftMode.Quick, [102, 103], [], pick: 2);
        session.ApplySessionUpdate(next);
        Assert.All(hud.Badges, badge => Assert.Empty(badge.Rank));
        Assert.All(hud.Badges, badge => Assert.False(badge.IsStatisticalPick));
        session.ApplyStatisticsUpdate(previous);
        Assert.All(hud.Badges, badge => Assert.Empty(badge.Rank));
        session.ApplyStatisticsUpdate(new(next.SnapshotResult.Snapshot, false, RecommendationStatistics()));
        Assert.All(hud.Badges, badge => Assert.Empty(badge.Rank));
        Assert.All(hud.Badges, badge => Assert.False(badge.IsStatisticalPick));
        Assert.Contains("Statistical recommendation unavailable", session.RecommendationStatusText, StringComparison.Ordinal);
        Assert.Contains("Coverage: 1 / 2", session.RecommendationStatusText, StringComparison.Ordinal);
        Assert.Contains("Fewer than two", session.RecommendationStatusText, StringComparison.Ordinal);
        session.ApplySessionUpdate(Update(State(ArenaDraftSessionStatus.Active, ArenaDraftMode.Quick, null)));
        Assert.Empty(hud.Badges);
        Assert.Empty(session.RecommendationStatusText);
        Assert.False(hud.ShowBadges);
        session.ApplyStatisticsUpdate(previous);
        Assert.Empty(session.RecommendationStatusText);
    }

    [Fact]
    public void RecommendationArrivalDoesNotChangeCalibrationOrNativePassthroughPolicy()
    {
        var session = ReadyViewModel();
        using var hud = new OverlayViewModel(session);
        var native = new FakeClickThrough();
        var modes = new OverlayInteractionController(native);
        Assert.True(modes.TryApply(false, out _));
        hud.SetCalibrationState(false, true);
        var update = ReadyUpdate(ArenaDraftMode.Quick, [101, 102], []);
        session.ApplySessionUpdate(update);
        session.ApplyStatisticsUpdate(new(update.SnapshotResult.Snapshot, false, RecommendationStatistics()));
        Assert.Equal([OverlayInteractionMode.Passive], native.Modes);
        Assert.Empty(hud.Guides);
        var geometry = hud.Badges.Select(badge => (badge.X, badge.Y, badge.Width)).ToArray();
        Assert.True(modes.TryApply(true, out _));
        hud.SetCalibrationState(true, true);
        Assert.Equal(14, hud.Guides.Count);
        var guides = hud.Guides.ToArray();
        session.ApplyStatisticsUpdate(new(update.SnapshotResult.Snapshot, false,
            RecommendationStatistics(alpha: 0.65, alphaSample: 900)));
        Assert.Equal(guides, hud.Guides);
        Assert.True(hud.IsCalibrating);
        Assert.Equal([OverlayInteractionMode.Passive, OverlayInteractionMode.Calibration], native.Modes);
        Assert.True(modes.TryApply(false, out _));
        hud.SetCalibrationState(false, true);
        Assert.Empty(hud.Guides);
        Assert.Equal(geometry, hud.Badges.Select(badge => (badge.X, badge.Y, badge.Width)));
        Assert.Equal([OverlayInteractionMode.Passive, OverlayInteractionMode.Calibration, OverlayInteractionMode.Passive], native.Modes);
    }

    [Fact]
    public void MissingEnvironmentBaselineShowsNoRanksAndExplicitUnavailableStatus()
    {
        var session = ReadyViewModel();
        using var hud = new OverlayViewModel(session);
        var update = ReadyUpdate(ArenaDraftMode.Quick, [101, 102], []);
        session.ApplySessionUpdate(update);
        session.ApplyStatisticsUpdate(new(update.SnapshotResult.Snapshot, false,
            RecommendationStatistics() with { EnvironmentCatalog = new() }));
        Assert.All(hud.Badges, badge => Assert.Empty(badge.Rank));
        Assert.Contains("No environment baseline", session.RecommendationStatusText, StringComparison.Ordinal);
        Assert.Contains("Coverage: 0 / 2", session.RecommendationStatusText, StringComparison.Ordinal);
        Assert.Equal("70.0%*", hud.Badges[0].WinRate);
    }
}
