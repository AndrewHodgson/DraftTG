using DraftTG.Application;
using DraftTG.ArenaIntegration;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;
using DraftTG.App.Platform;

namespace DraftTG.App.Tests;

public sealed partial class MainWindowViewModelTests
{
    [Theory]
    [InlineData(14, 1)] [InlineData(12, 3)] [InlineData(1, 14)]
    public void PackProducesExactlyOneBadgePerOrderedCard(int count, int pick)
    {
        var session = ReadyViewModel();
        using var hud = new OverlayViewModel(session);
        var ids = Enumerable.Range(0, count).Select(i => 103 - i % 3).ToArray();
        session.ApplySessionUpdate(ReadyUpdate(ArenaDraftMode.Quick, ids, [], pick: pick));
        Assert.Equal(count, hud.Badges.Count);
        Assert.Equal(Enumerable.Range(0, count), hud.Badges.Select(badge => badge.Index));
        Assert.Equal(session.CurrentPackCards.Select(card => card.Name), hud.Badges.Select(badge => badge.Name));
        Assert.Equal("Quick", hud.CompactStatus);
    }
    [Fact]
    public void PackTransitionsPublishAtomicallyAndLeaveNoStaleSlots()
    {
        var session = ReadyViewModel();
        using var hud = new OverlayViewModel(session);
        var counts = new List<int>();
        hud.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(hud.Badges)) counts.Add(hud.Badges.Count); };
        session.ApplySessionUpdate(ReadyUpdate(ArenaDraftMode.Quick, Enumerable.Repeat(101, 14).ToArray(), []));
        session.ApplySessionUpdate(Update(State(ArenaDraftSessionStatus.Active, ArenaDraftMode.Quick, null)));
        Assert.Empty(hud.Badges);
        session.ApplySessionUpdate(ReadyUpdate(ArenaDraftMode.Quick, Enumerable.Repeat(102, 12).ToArray(), [], pick: 3));
        Assert.Equal([14, 0, 12], counts);
        Assert.All(hud.Badges, badge => Assert.Equal("Beta", badge.Name));
    }
    [Fact]
    public void StatisticsArrivalRetainsBadgeInstancesSlotsOrderAndMissingCards()
    {
        var session = ReadyViewModel();
        using var hud = new OverlayViewModel(session);
        hud.SetCalibrationState(false, true);
        hud.SetViewport(1400, 600);
        var update = ReadyUpdate(ArenaDraftMode.Quick, [103, 101, 103, 102], []);
        session.ApplySessionUpdate(update);
        var before = hud.Badges.ToArray();
        var positions = before.Select(badge => (badge.X, badge.Y)).ToArray();
        session.ApplyStatisticsUpdate(new(update.SnapshotResult.Snapshot, true, null));
        Assert.All(hud.Badges, badge => Assert.Equal("…", badge.WinRate));
        var context = new LimitedStatisticsContext("HOB", LimitedStatisticsFormat.QuickDraft);
        session.ApplyStatisticsUpdate(new(update.SnapshotResult.Snapshot, false,
            new(context, context, new([new(CardIdentifier.Create("domain-a"), GameInHandGameCount: 20,
                GameInHandWinRate: 0.5874, AverageLastSeenAt: 6.24)]), LimitedStatisticsSource.Live, DateTimeOffset.UtcNow, null)));
        Assert.Equal(["—", "58.7%*", "—", "—"], hud.Badges.Select(badge => badge.WinRate));
        Assert.Equal("ALSA 6.24", hud.Badges[1].Secondary);
        Assert.Contains("GIH available: 1 / 4", session.StatisticsCoverageText, StringComparison.Ordinal);
        Assert.Equal(positions, hud.Badges.Select(badge => (badge.X, badge.Y)));
        for (var i = 0; i < before.Length; i++) Assert.Same(before[i], hud.Badges[i]);
        Assert.Equal(["Gamma", "Alpha", "Gamma", "Beta"], hud.Badges.Select(badge => badge.Name));
        Assert.True(hud.ShowBadges);
    }
    [Theory]
    [InlineData(null, 100, "\u2014*")]
    [InlineData(null, null, "\u2014")]
    [InlineData(null, 500, "\u2014")]
    [InlineData(0.575, 499, "57.5%*")]
    [InlineData(0.575, 500, "57.5%")]
    public void GihBadgeUsesExplicitSampleAndPreservesAlsa(double? rate, int? count, string expected)
    {
        var card = new CurrentPackCardViewModel("Alpha", "Common", "Colorless")
        {
            Statistics = LimitedCardStatisticsPresentation.From(new(CardIdentifier.Create("a"),
                GameInHandWinRate: rate, GameInHandGameCount: count, AverageLastSeenAt: 6.24))
        };
        var badge = new CardBadgeViewModel(0, card);
        Assert.Equal(expected, badge.WinRate);
        Assert.Equal("ALSA 6.24", badge.Secondary);
    }

    [Fact]
    public void FirstRunCalibrationAndVisibilityDoNotStopDraftPresentation()
    {
        var session = ReadyViewModel();
        using var hud = new OverlayViewModel(session);
        session.ApplySessionUpdate(ReadyUpdate(ArenaDraftMode.Quick, [101], []));
        Assert.True(hud.NeedsCalibration);
        Assert.Equal("Set overlay position", hud.CalibrationLabel);
        Assert.Single(hud.Badges);
        Assert.False(hud.ShowBadges);
        hud.SetCalibrationState(true, false);
        Assert.True(hud.ShowBadges);
        Assert.Equal(14, hud.Guides.Count);
        hud.SetCalibrationState(false, true);
        hud.ToggleStatistics();
        Assert.False(hud.ShowBadges);
        Assert.Single(hud.Badges);
        hud.ToggleStatistics();
        Assert.True(hud.ShowBadges);
    }
    [Fact]
    public void HistoryAndDiagnosticsOnlyAppearInSelectedRailPanel()
    {
        var session = ReadyViewModel();
        using var hud = new OverlayViewModel(session);
        session.ApplySessionUpdate(ReadyUpdate(ArenaDraftMode.Quick, [101], [(1, 1, 102)]));
        hud.SetDiagnostic("A concise warning.");
        Assert.True(hud.HasWarning);
        Assert.Equal("Quick", hud.CompactStatus);
        Assert.False(hud.IsStatusPanel);
        Assert.False(hud.IsHistoryPanel);
        hud.SetPanel(RailPanel.History);
        Assert.True(hud.IsHistoryPanel);
        Assert.Equal("Beta", Assert.Single(hud.Session.DraftedCards).Name);
        hud.SetPanel(RailPanel.Status);
        Assert.False(hud.IsHistoryPanel);
        Assert.True(hud.IsStatusPanel);
        Assert.Contains("A concise warning.", hud.Diagnostics, StringComparison.Ordinal);
        hud.SetPanel(RailPanel.None);
        Assert.False(hud.IsStatusPanel);
    }
    [Fact]
    public void ResizeScalesSlotPositionsButStatsNeverAffectThem()
    {
        var session = ReadyViewModel();
        using var hud = new OverlayViewModel(session);
        session.ApplySessionUpdate(ReadyUpdate(ArenaDraftMode.Quick, [101, 102], []));
        hud.SetCalibrationState(true, false);
        hud.SetViewport(700, 400);
        var firstGuide = hud.Guides[0];
        hud.SetViewport(1400, 800);
        Assert.Equal(firstGuide.X * 2, hud.Guides[0].X);
        Assert.Equal(firstGuide.Height * 2, hud.Guides[0].Height);
        Assert.Equal(2, hud.Badges.Count);
        Assert.All(hud.Badges, badge => Assert.Equal("—", badge.WinRate));
    }
    [Fact]
    public void ExplicitMoveDragIsOnlyStartedForLeftPressOnDragRegion()
    {
        Assert.True(WindowDrag.ShouldBegin(true, true));
        Assert.False(WindowDrag.ShouldBegin(false, true));
        Assert.False(WindowDrag.ShouldBegin(true, false));
    }
    [Fact]
    public void NativeModeSwitchOnlyTargetsInjectedBadgeWindowAndRestoresPassthrough()
    {
        var badge = new FakeClickThrough();
        var rail = new FakeClickThrough();
        var modes = new OverlayInteractionController(badge);
        Assert.True(modes.TryApply(false, out var diagnostic));
        Assert.Null(diagnostic);
        Assert.True(modes.TryApply(true, out _));
        Assert.True(modes.TryApply(false, out _));
        Assert.Equal([true, false, true], badge.Values);
        Assert.Empty(rail.Values);
        Assert.Equal([OverlayInteractionMode.Passive, OverlayInteractionMode.Calibration, OverlayInteractionMode.Passive], badge.Modes);
    }
    [Fact]
    public void NativeFailureIsReportedSoHostCanHideOverlay()
    {
        var modes = new OverlayInteractionController(new FakeClickThrough { Fail = true });
        Assert.False(modes.TryApply(false, out var diagnostic));
        Assert.Contains("hidden", diagnostic, StringComparison.Ordinal);
    }
    [Fact]
    public void CalibrationStartsWithInputAndActivationThenRestoresBothPassiveRequirements()
    {
        var badge = new FakeClickThrough();
        using var controller = new OverlayInteractionController(badge);
        Assert.True(controller.TryApply(true, out _));
        Assert.False(badge.Modes[0].IgnoresMouseEvents);
        Assert.True(badge.Modes[0].AllowsActivation);
        Assert.True(controller.TryApply(false, out _));
        Assert.True(badge.Modes[1].IgnoresMouseEvents);
        Assert.False(badge.Modes[1].AllowsActivation);
    }
    private sealed class FakeClickThrough : IClickThroughWindowController
    {
        public List<bool> Values { get; } = [];
        public List<OverlayInteractionMode> Modes { get; } = [];
        public bool Fail { get; init; }
        public void SetMode(OverlayInteractionMode mode)
        {
            if (Fail) throw new InvalidOperationException("Test native failure.");
            Values.Add(mode == OverlayInteractionMode.Passive);
            Modes.Add(mode);
        }
    }
}
