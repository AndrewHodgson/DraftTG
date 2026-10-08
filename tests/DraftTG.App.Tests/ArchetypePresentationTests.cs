using DraftTG.Application;
using DraftTG.ArenaIntegration;
using DraftTG.Data;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;

namespace DraftTG.App.Tests;

public sealed partial class MainWindowViewModelTests
{
    private static ScryfallCardCatalogData ArchetypeUiData() => new ScryfallCardCatalogDecoder().DecodeCatalogData("""
        [
          {"id":"a","arena_id":101,"name":"Alpha","colors":["G"],"rarity":"common","set":"woe","collector_number":"1"},
          {"id":"b","arena_id":102,"name":"Beta","colors":["G"],"rarity":"common","set":"woe","collector_number":"2"},
          {"id":"c","arena_id":103,"name":"Black","colors":["B"],"rarity":"common","set":"woe","collector_number":"3"}
        ]
        """);
    private static DraftSessionUpdate ArchetypeUiPack(ScryfallCardCatalogData data) => Update(
        State(ArenaDraftSessionStatus.Active, ArenaDraftMode.Quick, [101, 102],
            Enumerable.Range(1, 14).Select(i => (1, i, i <= 7 ? 101 : 103)).ToArray(), pack: 2)
            with { EventName = "QuickDraft_WOE" },
        new ArenaDraftSnapshotAdapter(new(data)));
    private static LimitedStatisticsLoadResult ArchetypeUiStatistics(ScryfallCardCatalogData data) => new(
        new("WOE", LimitedStatisticsFormat.QuickDraft), new("WOE", LimitedStatisticsFormat.QuickDraft),
        new([new(CardIdentifier.Create("a"), GameInHandWinRate: .58, GameInHandGameCount: 5000, AverageLastSeenAt: 6.24),
            new(CardIdentifier.Create("b"), GameInHandWinRate: .586, GameInHandGameCount: 5000, AverageLastSeenAt: 5)]),
        LimitedStatisticsSource.Live, null, null)
        { CardCatalog = data.Catalog, EnvironmentCatalog = new([new(CardIdentifier.Create("env"), GameInHandWinRate: .55, GameInHandGameCount: 10000)]) };

    [Fact]
    public void ActiveArchetypeRailFinalGoldAndRawBadgeContentUpdateWithoutGeometryChanges()
    {
        var data = ArchetypeUiData(); var session = AssociationSession(data.Catalog); using var overlay = new OverlayViewModel(session);
        var state = ArchetypeUiPack(data); session.ApplySessionUpdate(state);
        var initial = new LimitedStatisticsUpdate(state.SnapshotResult.Snapshot, false, ArchetypeUiStatistics(data)); session.ApplyStatisticsUpdate(initial);
        Assert.True(overlay.ConfirmVisualOrder(overlay.PlacementContext!, overlay.Badges.Select(b => b.OccurrenceKey!.Value)));
        var badges = overlay.Badges.ToArray(); var geometry = badges.Select(b => (b.X, b.Y, b.Width)).ToArray();
        var raw = badges.Select(b => (b.WinRate, b.Secondary)).ToArray();
        var pair = new ArchetypePairStatistics(new("WOE", LimitedStatisticsFormat.QuickDraft), ArchetypeColorPair.Create("BG"),
            new([new(CardIdentifier.Create("a"), GameInHandWinRate: .62, GameInHandGameCount: 10000),
                new(CardIdentifier.Create("b"), GameInHandWinRate: .52, GameInHandGameCount: 10000)]), [new(.56, 10000)]);
        var final = new LimitedStatisticsUpdate(state.SnapshotResult.Snapshot, false, ArchetypeUiStatistics(data), archetypeStatistics: pair,
            archetypeDataStatus: new(false, LimitedStatisticsSource.Cache)); session.ApplyStatisticsUpdate(final);
        Assert.Contains("BG — Food Midrange", session.ArchetypeStatusText); Assert.Contains("Confidence: 1.00", session.ArchetypeStatusText);
        Assert.Contains("Context Pick\nAlpha", session.FinalRecommendationStatusText); Assert.Contains("Lane rank: #2 · Final rank: #1", session.ContextualRecommendationDiagnosticsText);
        Assert.Contains("Lane observations:", session.ContextualRecommendationDiagnosticsText); Assert.Contains("Pool colors:", session.ContextualRecommendationDiagnosticsText);
        Assert.Contains("Pair GIH: 62.0%", session.ContextualRecommendationDiagnosticsText); Assert.Contains("pair baseline", session.ContextualRecommendationDiagnosticsText);
        Assert.Equal(["#1", "#2"], badges.Select(b => b.Rank)); Assert.Equal([true, false], badges.Select(b => b.IsContextPick));
        Assert.Equal("#FFE2BE64", badges[0].BorderColor); Assert.Equal(raw, badges.Select(b => (b.WinRate, b.Secondary)));
        Assert.Equal(geometry, badges.Select(b => (b.X, b.Y, b.Width))); Assert.True(overlay.HasConfirmedVisualPlacement);
        for (var i = 0; i < badges.Length; i++) Assert.Same(badges[i], overlay.Badges[i]);
    }

    [Fact]
    public void ArchetypePresentationClearsAcrossPacksAndRejectsForeignOccurrenceStage()
    {
        var data = ArchetypeUiData(); var session = AssociationSession(data.Catalog); var state = ArchetypeUiPack(data); session.ApplySessionUpdate(state);
        var old = new LimitedStatisticsUpdate(state.SnapshotResult.Snapshot, false, ArchetypeUiStatistics(data)); session.ApplyStatisticsUpdate(old);
        var first = old.Occurrences.Values.First(); var other = old.Occurrences.Values.Last();
        var invalid = new CurrentPackCardPresentation(first.Key, first.Card, first.RawStatistics, first.ResolvedStatisticsName,
            first.Statistical, first.Pool, first.Lane, archetype: other.Archetype);
        Assert.False(invalid.IsIdentityConsistent); Assert.Null(invalid.Archetype); Assert.Null(invalid.RawGIH);
        session.ApplySessionUpdate(AssociationPack(data, [102, 103], 2)); Assert.Empty(session.ArchetypeStatusText);
        session.ApplyStatisticsUpdate(old); Assert.Empty(session.ArchetypeStatusText);
    }
}
