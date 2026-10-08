using DraftTG.Application;
using DraftTG.Domain;

namespace DraftTG.App.Tests;

public sealed partial class MainWindowViewModelTests
{
    [Fact]
    public void TrophyRailExposesCountsAndDisabledReasonWhileBadgesKeepRawMetricsRanksAndGeometry()
    {
        var data = ArchetypeUiData(); var session = AssociationSession(data.Catalog); using var overlay = new OverlayViewModel(session);
        var state = ArchetypeUiPack(data); session.ApplySessionUpdate(state);
        var initial = new LimitedStatisticsUpdate(state.SnapshotResult.Snapshot, false, ArchetypeUiStatistics(data)); session.ApplyStatisticsUpdate(initial);
        Assert.True(overlay.ConfirmVisualOrder(overlay.PlacementContext!, overlay.Badges.Select(b => b.OccurrenceKey!.Value)));
        var badges = overlay.Badges.ToArray(); var original = badges.Select(b => (b.Rank, b.WinRate, b.Secondary, b.X, b.Y, b.Width)).ToArray();
        var key = new SuccessfulDeckKey("WOE", SuccessfulDeckFormat.QuickDraft, ArchetypeColorPair.Create("BG")); var time = DateTimeOffset.UtcNow;
        var sample = new SuccessfulDeckSample(key, "synthetic-event", new(7, 1), 7, 0, time,
            new Dictionary<string, int> { ["Beta"] = 2 }, new Dictionary<string, int> { ["Beta"] = 3, ["Alpha"] = 1 });
        var corpus = new SuccessfulDeckCorpus(key, [sample], time, new("synthetic", "recent", "WOE QuickDraft BG", 100, 20, 1, "final"));
        var final = new LimitedStatisticsUpdate(state.SnapshotResult.Snapshot, false, ArchetypeUiStatistics(data), trophyCorpus: corpus,
            trophyDataStatus: new(false, SuccessfulDeckSource.Cache)); session.ApplyStatisticsUpdate(final);
        Assert.Contains("Archetype rank:", session.ContextualRecommendationDiagnosticsText); Assert.Contains("1 recent BG QuickDraft decks", session.ContextualRecommendationDiagnosticsText);
        Assert.Contains("Card seen in pool: 1; main-decked: 1", session.ContextualRecommendationDiagnosticsText);
        Assert.Contains("100.0%", session.ContextualRecommendationDiagnosticsText); Assert.Contains("Adjustment disabled: incompatible baseline semantics", session.ContextualRecommendationDiagnosticsText);
        Assert.Contains("copy utilization", session.ContextualRecommendationDiagnosticsText);
        Assert.Equal(original, badges.Select(b => (b.Rank, b.WinRate, b.Secondary, b.X, b.Y, b.Width)));
        Assert.True(overlay.HasConfirmedVisualPlacement); Assert.All(final.Occurrences.Values, c => Assert.Equal(0, c.Phase9DAdjustment));
    }

    [Fact]
    public void TrophyOccurrenceMismatchFailsClosedAndUnavailableRailStatesActualReason()
    {
        var data = ArchetypeUiData(); var session = AssociationSession(data.Catalog); var state = ArchetypeUiPack(data); session.ApplySessionUpdate(state);
        var update = new LimitedStatisticsUpdate(state.SnapshotResult.Snapshot, false, ArchetypeUiStatistics(data),
            trophyDataStatus: new(false, SuccessfulDeckSource.Unavailable, "Exact-format source unavailable."));
        session.ApplyStatisticsUpdate(update); Assert.Contains("Trophy evidence unavailable", session.ContextualRecommendationDiagnosticsText);
        Assert.Contains("Exact-format source unavailable", session.ContextualRecommendationDiagnosticsText);
        var first = update.Occurrences.Values.First(); var other = update.Occurrences.Values.Last();
        var invalid = new CurrentPackCardPresentation(first.Key, first.Card, first.RawStatistics, first.ResolvedStatisticsName,
            first.Statistical, first.Pool, first.Lane, archetype: first.Archetype, trophy: other.Trophy);
        Assert.False(invalid.IsIdentityConsistent); Assert.Null(invalid.Trophy); Assert.Null(invalid.RawGIH);
        session.ApplySessionUpdate(AssociationPack(data, [102, 103], 2)); session.ApplyStatisticsUpdate(update);
        Assert.DoesNotContain("Exact-format source unavailable", session.ContextualRecommendationDiagnosticsText);
    }
}
