using System.Text.Json;
using DraftTG.Application;
using DraftTG.ArenaIntegration;
using DraftTG.Data;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;

namespace DraftTG.App.Tests;

public sealed partial class MainWindowViewModelTests
{
    private static CardOccurrenceKey[] VisualKeys(OverlayViewModel overlay, params int[] logIndexes) =>
        logIndexes.Select(index => overlay.Session.CurrentPackPresentations[index].Key).ToArray();

    private static void AssertVisualGeometry(CardBadgeViewModel badge, CardSlot slot, double width, double height)
    {
        var bounds = slot.Scale(width, height);
        var badgeWidth = Math.Min(86, bounds.Width);
        Assert.True(badge.IsPlaced);
        Assert.Equal(slot.Index, badge.VisualSlotIndex);
        Assert.Equal(badgeWidth, badge.Width, 8);
        Assert.Equal(bounds.X + (bounds.Width - badgeWidth) / 2, badge.X, 8);
        Assert.Equal(Math.Max(bounds.Y, bounds.Y + bounds.Height - 44), badge.Y, 8);
    }

    [Fact]
    public void ConfirmedLiveP1P6OrderPlacesEachOccurrenceOnItsOwnImageSlot()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Fixtures", "arena-woe-p1p6-visual-placement.json")));
        var root = fixture.RootElement;
        var data = new ScryfallCardCatalogDecoder().DecodeCatalogData(root.GetProperty("Cards").GetRawText());
        var rawPack = root.GetProperty("LogPack");
        var envelope = JsonSerializer.Serialize(new { CurrentModule = "BotDraft", Payload = rawPack.GetRawText() });
        var engine = new ArenaDraftStateEngine();
        ArenaDraftStateSnapshot? arena = null;
        foreach (var fact in new ArenaDraftLogParser().Parse(new ArenaLogSourceEvent.Line(envelope)))
            arena = engine.Apply(fact).Snapshot;
        var state = Update(arena!, new ArenaDraftSnapshotAdapter(new(data)));
        var session = AssociationSession(data.Catalog);
        using var overlay = new OverlayViewModel(session);
        overlay.SetCalibrationState(false, true);
        overlay.SetLayout(DraftCardLayout.Grid(5));
        overlay.SetViewport(1400, 900);
        session.ApplySessionUpdate(state);
        var context = new LimitedStatisticsContext("WOE", LimitedStatisticsFormat.QuickDraft);
        var ratings = root.GetProperty("Ratings").EnumerateArray().Select(r => new SeventeenLandsRating(
            r.GetProperty("name").GetString()!, GameInHandWinRate: r.GetProperty("ever_drawn_win_rate").GetDouble(),
            GameInHandGameCount: r.GetProperty("ever_drawn_game_count").GetInt32(),
            AverageLastSeenAt: r.GetProperty("avg_seen").GetDouble())).ToArray();
        var mapped = LimitedStatisticsMapper.Map(ratings, data.Catalog, context, state.SnapshotResult.Snapshot);
        var statistics = new LimitedStatisticsLoadResult(context, context, mapped.Catalog, LimitedStatisticsSource.Cache, null, null)
        {
            CardCatalog = data.Catalog, StatisticsResolvedNames = mapped.ResolvedNames,
            EnvironmentCatalog = new([new(CardIdentifier.Create("baseline"), GameInHandWinRate: .578, GameInHandGameCount: 10000)])
        };
        session.ApplyStatisticsUpdate(new(state.SnapshotResult.Snapshot, false, statistics));
        var before = session.CurrentPackPresentations.ToArray();
        var logIds = rawPack.GetProperty("DraftPack").EnumerateArray().Select(c => int.Parse(c.GetString()!)).ToArray();
        Assert.Equal("Scarecrow Guide", before[0].CardName);
        Assert.Equal("Hatching Plans", before[8].CardName);
        var order = root.GetProperty("VisualArenaIds").EnumerateArray().Select(id =>
            before[Array.IndexOf(logIds, id.GetInt32())].Key).ToArray();
        Assert.True(overlay.ConfirmVisualOrder(overlay.PlacementContext!, order));
        var visual = BoundBadges(overlay).OrderBy(b => b.VisualSlotIndex).ToArray();
        Assert.Equal(["Hatching Plans", "Stonesplitter Bolt", "Rat Out", "Grabby Giant // That's Mine", "Merry Bards",
            "Redcap Thief", "Return from the Wilds", "Gingerbrute", "Scarecrow Guide"], visual.Select(b => b.Name));
        for (var slot = 0; slot < visual.Length; slot++)
        {
            var badge = visual[slot];
            Assert.Same(before[badge.Index], badge.Presentation);
            Assert.Equal(before[badge.Index].DisplayedGIH, badge.WinRate);
            Assert.Equal(before[badge.Index].DisplayedALSA, badge.Secondary);
            Assert.Equal(before[badge.Index].DisplayedContextRank, badge.Rank);
            AssertVisualGeometry(badge, overlay.Layout.Slots[slot], 1400, 900);
        }
        Assert.Equal((8, 0, "60.6%", "ALSA 5.22", "#1"),
            (visual[0].Index, visual[0].VisualSlotIndex!.Value, visual[0].WinRate, visual[0].Secondary, visual[0].Rank));
        Assert.Equal((0, 8, "55.6%", "ALSA 6.95", "#8"),
            (visual[8].Index, visual[8].VisualSlotIndex!.Value, visual[8].WinRate, visual[8].Secondary, visual[8].Rank));
        Assert.Contains("Context Pick\nHatching Plans", session.LaneRecommendationStatusText);
        Assert.Equal("Hatching Plans", Assert.Single(visual, b => b.IsContextPick).Name);
        Assert.All(overlay.Layout.Slots.Take(5), slot => Assert.Equal(overlay.Layout.Slots[0].Y, slot.Y));
        Assert.All(overlay.Layout.Slots.Skip(5).Take(4), slot => Assert.Equal(overlay.Layout.Slots[5].Y, slot.Y));
        Assert.Contains("Log occurrence index: 8; Visual slot: 0", overlay.BadgeBindingDiagnosticsText);
        Assert.Contains("Log occurrence index: 0; Visual slot: 8", overlay.BadgeBindingDiagnosticsText);
    }

    [Fact]
    public void UnconfirmedPlacementHidesEveryMetricBadgeAndExplainsHowToAssignIt()
    {
        var (data, statistics) = FourBadgeFixture();
        var session = AssociationSession(data.Catalog);
        using var overlay = new OverlayViewModel(session);
        overlay.SetCalibrationState(false, true);
        var pack = AssociationPack(data, [601, 602, 603, 604]);
        session.ApplySessionUpdate(pack);
        session.ApplyStatisticsUpdate(new(pack.SnapshotResult.Snapshot, false, statistics));
        Assert.All(BoundBadges(overlay), badge => { Assert.False(badge.IsPlaced); Assert.Null(badge.VisualSlotIndex); });
        Assert.False(overlay.HasConfirmedVisualPlacement);
        Assert.Contains("Card positions unconfirmed", overlay.Diagnostics);
        Assert.Contains("Visual slot: unmapped", overlay.BadgeBindingDiagnosticsText);
        var item = Assert.Single(PassiveBadgeView().Descendants(), e => (string?)e.Attribute("ItemsSource") == "{Binding Badges}");
        var gate = Assert.Single(item.Descendants(), e => e.Name.LocalName == "Border"
            && (string?)e.Attribute("IsVisible") == "{Binding IsPlaced}");
        Assert.Equal("{Binding IsPlaced}", (string?)gate.Attribute("IsVisible"));
    }

    [Fact]
    public void PartialDuplicateOrForeignVisualAssignmentsFailClosed()
    {
        var (data, _) = FourBadgeFixture();
        var session = AssociationSession(data.Catalog);
        using var overlay = new OverlayViewModel(session);
        session.ApplySessionUpdate(AssociationPack(data, [601, 602, 603, 604]));
        var valid = VisualKeys(overlay, 3, 1, 0, 2);
        var invalid = new[] { valid[..3], new[] { valid[0], valid[0], valid[2], valid[3] },
            new[] { new CardOccurrenceKey(0, CardIdentifier.Create("foreign")), valid[1], valid[2], valid[3] },
            new[] { valid[0] with { PackIndex = 14 }, valid[1], valid[2], valid[3] } };
        foreach (var order in invalid)
        {
            Assert.True(overlay.ConfirmVisualOrder(overlay.PlacementContext!, valid));
            Assert.False(overlay.ConfirmVisualOrder(overlay.PlacementContext!, order));
            Assert.False(overlay.HasConfirmedVisualPlacement);
            Assert.All(BoundBadges(overlay), badge => Assert.False(badge.IsPlaced));
            Assert.Contains("Assign each current card once", overlay.PlacementDiagnostic);
        }
    }

    [Fact]
    public void ChangingRecommendationRanksCannotChangeConfirmedVisualSlots()
    {
        var (data, statistics) = FourBadgeFixture();
        var session = AssociationSession(data.Catalog);
        using var overlay = new OverlayViewModel(session);
        var pack = AssociationPack(data, [601, 602, 603, 604]);
        session.ApplySessionUpdate(pack);
        session.ApplyStatisticsUpdate(new(pack.SnapshotResult.Snapshot, false, statistics));
        Assert.True(overlay.ConfirmVisualOrder(overlay.PlacementContext!, VisualKeys(overlay, 2, 0, 3, 1)));
        var geometry = BoundBadges(overlay).ToDictionary(b => b.OccurrenceKey!.Value, b => (b.VisualSlotIndex, b.X, b.Y));
        var replacement = statistics with { Catalog = new([
            new(CardIdentifier.Create("badge-0"), GameInHandWinRate: .65, GameInHandGameCount: 5000, AverageLastSeenAt: 3.10),
            new(CardIdentifier.Create("badge-1"), GameInHandWinRate: .59, GameInHandGameCount: 5000, AverageLastSeenAt: 7.80),
            new(CardIdentifier.Create("badge-2"), GameInHandWinRate: .57, GameInHandGameCount: 5000, AverageLastSeenAt: 5.40),
            new(CardIdentifier.Create("badge-3"), GameInHandWinRate: .52, GameInHandGameCount: 5000, AverageLastSeenAt: 8.90)]) };
        session.ApplyStatisticsUpdate(new(pack.SnapshotResult.Snapshot, false, replacement));
        Assert.All(BoundBadges(overlay), b => Assert.Equal(geometry[b.OccurrenceKey!.Value], (b.VisualSlotIndex, b.X, b.Y)));
        var gold = Assert.Single(BoundBadges(overlay), b => b.IsContextPick);
        Assert.Equal("A", gold.Name); Assert.Equal(1, gold.VisualSlotIndex); Assert.Equal("#1", gold.Rank);
        Assert.Equal("65.0%", gold.WinRate); Assert.Equal("ALSA 3.10", gold.Secondary);
    }

    [Fact]
    public void AllShrinkingPackSizesUseExplicitSlotsWithinEachExistingCalibratedProfile()
    {
        var cards = Enumerable.Range(0, 14).Select(i => ($"Card {i}", .55 + .001 * i, 3.0 + i / 10.0)).ToArray();
        var (data, statistics) = BadgeFixture(cards);
        foreach (var columns in new[] { 4, 5, 6, 7 })
        {
            var session = AssociationSession(data.Catalog);
            using var overlay = new OverlayViewModel(session);
            var layout = DraftCardLayout.Grid(columns);
            overlay.SetLayout(layout);
            overlay.SetViewport(1400, 900);
            VisualPlacementContext? previous = null;
            for (var count = 14; count >= 1; count--)
            {
                var pack = AssociationPack(data, Enumerable.Range(601, count).ToArray(), 15 - count);
                session.ApplySessionUpdate(pack);
                session.ApplyStatisticsUpdate(new(pack.SnapshotResult.Snapshot, false, statistics));
                Assert.Equal(count, overlay.VisualSlots.Count);
                Assert.All(BoundBadges(overlay), b => Assert.False(b.IsPlaced));
                if (previous is not null) Assert.False(overlay.ConfirmVisualOrder(previous, []));
                var order = session.CurrentPackPresentations.Reverse().Select(c => c.Key).ToArray();
                Assert.True(overlay.ConfirmVisualOrder(overlay.PlacementContext!, order));
                foreach (var badge in BoundBadges(overlay))
                {
                    var slot = count - 1 - badge.Index;
                    Assert.Equal(order[slot], badge.OccurrenceKey);
                    AssertVisualGeometry(badge, layout.Slots[slot], 1400, 900);
                    Assert.Same(session.CurrentPackPresentations[badge.Index], badge.Presentation);
                }
                Assert.Same(layout, overlay.Layout); // No invented row distribution or changed calibration.
                previous = overlay.PlacementContext;
            }
        }
    }

    [Fact]
    public void RailAssignmentsRequireConfirmationAndStaleSelectorsCannotAlterANewPack()
    {
        var (data, _) = FourBadgeFixture();
        var session = AssociationSession(data.Catalog);
        using var overlay = new OverlayViewModel(session);
        session.ApplySessionUpdate(AssociationPack(data, [601, 602, 603, 604]));
        foreach (var row in overlay.VisualSlots) row.SelectedCard = row.Choices[3 - row.VisualSlot];
        Assert.True(overlay.CanConfirmVisualPlacement);
        Assert.All(BoundBadges(overlay), b => Assert.False(b.IsPlaced));
        Assert.True(overlay.ConfirmVisualSelections());
        var oldContext = overlay.PlacementContext!;
        var oldSelector = overlay.VisualSlots[0];
        overlay.VisualSlots[0].SelectedCard = overlay.VisualSlots[0].Choices[0];
        Assert.False(overlay.HasConfirmedVisualPlacement);
        Assert.All(BoundBadges(overlay), b => Assert.False(b.IsPlaced));
        Assert.False(overlay.ConfirmVisualSelections()); // Duplicated A.
        session.ApplySessionUpdate(AssociationPack(data, [601, 603, 604], 2));
        Assert.True(overlay.ConfirmVisualOrder(overlay.PlacementContext!, VisualKeys(overlay, 2, 0, 1)));
        oldSelector.SelectedCard = oldSelector.Choices[1];
        Assert.True(overlay.HasConfirmedVisualPlacement);
        Assert.False(overlay.ConfirmVisualOrder(oldContext, []));
        Assert.False(overlay.ConfirmVisualOrder(overlay.PlacementContext! with { }, VisualKeys(overlay, 0, 1, 2)));
        Assert.Equal([1, 2, 0], BoundBadges(overlay).Select(b => b.VisualSlotIndex!.Value));
    }

    [Fact]
    public void DuplicateCardCopiesRetainSeparateKeysRanksAndConfirmedVisualSlots()
    {
        var (data, statistics) = FourBadgeFixture();
        var session = AssociationSession(data.Catalog);
        using var overlay = new OverlayViewModel(session);
        var pack = AssociationPack(data, [601, 601, 602]);
        session.ApplySessionUpdate(pack);
        session.ApplyStatisticsUpdate(new(pack.SnapshotResult.Snapshot, false, statistics));
        Assert.Contains("A (copy 1)", overlay.VisualSlots[0].Choices.Select(c => c.Label));
        Assert.Contains("A (copy 2)", overlay.VisualSlots[0].Choices.Select(c => c.Label));
        Assert.True(overlay.ConfirmVisualOrder(overlay.PlacementContext!, VisualKeys(overlay, 1, 2, 0)));
        var visual = BoundBadges(overlay).OrderBy(b => b.VisualSlotIndex).ToArray();
        Assert.Equal(["A", "B", "A"], visual.Select(b => b.Name));
        Assert.Equal(["#3", "#1", "#2"], visual.Select(b => b.Rank));
        Assert.Equal(visual[0].Presentation!.CardIdentifier, visual[2].Presentation!.CardIdentifier);
        Assert.NotEqual(visual[0].OccurrenceKey, visual[2].OccurrenceKey);
        Assert.Equal(visual[0].WinRate, visual[2].WinRate);
        Assert.Equal(visual[0].Secondary, visual[2].Secondary);
    }
}
