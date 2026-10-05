using DraftTG.Application;
using DraftTG.ArenaIntegration;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;

namespace DraftTG.App.Tests;

public sealed partial class MainWindowViewModelTests
{
    private static DraftPackObservationHistory WhiteLaneHistory()
    {
        var signal = new Card(CardIdentifier.Create("lane-white"), "Lane White", new([MagicColor.White]),
            CardRarity.Common, CardSetCode.Create("TST"), CollectorNumber.Create("9"));
        var catalog = new CardCatalog([signal]);
        var statistics = new LimitedCardStatisticsCatalog([new(signal.Identifier,
            GameInHandWinRate: 0.65, GameInHandGameCount: 500, AverageLastSeenAt: 3)]);
        var environment = new LimitedCardStatisticsCatalog([new(CardIdentifier.Create("env"),
            GameInHandWinRate: 0.55, GameInHandGameCount: 10000)]);
        var history = DraftPackObservationHistory.Empty;
        for (var pick = 8; pick <= 11; pick++)
        {
            var pack = new DraftPack(new(PackNumber.Create(1), PickNumber.Create(pick)), [signal.Identifier]);
            history = history.Observe(pack, catalog, null,
                new StatisticalRecommendationEngine().Recommend(pack, statistics, environment), statistics);
        }
        return history;
    }

    [Fact]
    public void FinalLanePickGetsGoldAndRailShowsThreeStagesWithoutMovingOrEnlargingBadges()
    {
        var session = ReadyViewModel();
        using var hud = new OverlayViewModel(session);
        var native = new FakeClickThrough();
        var modes = new DraftTG.App.Platform.OverlayInteractionController(native);
        Assert.True(modes.TryApply(false, out _));
        hud.SetCalibrationState(false, true);
        var state = ReadyUpdate(ArenaDraftMode.Quick, [101, 102], [], pick: 12);
        session.ApplySessionUpdate(state);
        var badges = hud.Badges.ToArray();
        var geometry = badges.Select(b => (b.X, b.Y, b.Width)).ToArray();
        var update = new LimitedStatisticsUpdate(state.SnapshotResult.Snapshot, false, ContextStatistics(),
            observationHistory: WhiteLaneHistory());
        session.ApplyStatisticsUpdate(update);
        Assert.Equal(1, update.Recommendation!.TopRecommendedPackIndex);
        Assert.Equal(1, update.ContextualRecommendation!.TopRecommendedPackIndex);
        Assert.Equal(0, update.LaneRecommendation!.TopRecommendedPackIndex);
        Assert.Equal(["#1", "#2"], hud.Badges.Select(b => b.Rank));
        Assert.Equal([true, false], hud.Badges.Select(b => b.IsContextPick));
        Assert.Equal("#FFE2BE64", hud.Badges[0].BorderColor);
        Assert.NotEqual("#FFE2BE64", hud.Badges[1].BorderColor);
        Assert.Equal(["58.0%", "59.0%"], hud.Badges.Select(b => b.WinRate));
        Assert.Equal("ALSA 6.24", hud.Badges[0].Secondary);
        Assert.Contains("Context Pick\nAlpha", session.LaneRecommendationStatusText);
        Assert.Contains("Stats rank: #2 · Pool rank: #2 · Context rank: #1", session.LaneRecommendationStatusText);
        Assert.Contains("Lane signal: W +1.00", session.LaneRecommendationStatusText);
        Assert.Contains("Quick Draft / bot", session.LaneRecommendationStatusText);
        Assert.Equal("Stats Pick: Beta", session.StatisticalPickStatusText);
        Assert.Contains("lane evidence", session.ContextualRecommendationDiagnosticsText);
        Assert.Contains("ALSA 3", session.ContextualRecommendationDiagnosticsText);
        Assert.Equal(geometry, hud.Badges.Select(b => (b.X, b.Y, b.Width)));
        for (var i = 0; i < badges.Length; i++) Assert.Same(badges[i], hud.Badges[i]);
        Assert.Empty(hud.Guides);
        Assert.Equal([DraftTG.App.Platform.OverlayInteractionMode.Passive], native.Modes);
    }

    [Fact]
    public void NewPackAndNoPackClearFinalLanePresentationAndRejectStaleResults()
    {
        var session = ReadyViewModel();
        using var hud = new OverlayViewModel(session);
        var old = ReadyUpdate(ArenaDraftMode.Quick, [101, 102], [], pick: 12);
        session.ApplySessionUpdate(old);
        var previous = new LimitedStatisticsUpdate(old.SnapshotResult.Snapshot, false, ContextStatistics(),
            observationHistory: WhiteLaneHistory());
        session.ApplyStatisticsUpdate(previous);
        Assert.True(hud.Badges[0].IsContextPick);
        session.ApplySessionUpdate(ReadyUpdate(ArenaDraftMode.Quick, [102, 103], [], pick: 13));
        Assert.All(session.CurrentPackCards, card => Assert.Null(card.LaneRecommendation));
        Assert.DoesNotContain("Lane signal: W", session.LaneRecommendationStatusText);
        session.ApplyStatisticsUpdate(previous);
        Assert.All(hud.Badges, b => Assert.False(b.IsContextPick));
        session.ApplySessionUpdate(Update(State(ArenaDraftSessionStatus.Active, ArenaDraftMode.Quick, null)));
        Assert.Empty(session.LaneRecommendationStatusText);
        Assert.Empty(hud.Badges);
        session.ApplyStatisticsUpdate(previous);
        Assert.Empty(session.LaneRecommendationStatusText);
    }
}
