using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using DraftTG.Application;
using DraftTG.Data;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;

namespace DraftTG.App.Tests;

public sealed partial class MainWindowViewModelTests
{
    private static XElement PassiveBadgeView()
    {
        using var stream = typeof(OverlayHandleTests).Assembly.GetManifestResourceStream("CardOverlayWindow.axaml")!;
        return XElement.Load(stream);
    }

    // Resolve the shipped ItemsSource rather than testing a second, unbound collection.
    private static IReadOnlyList<CardBadgeViewModel> BoundBadges(OverlayViewModel overlay)
    {
        var items = Assert.Single(PassiveBadgeView().Descendants(), e =>
            e.Name.LocalName == "ItemsControl" && (string?)e.Attribute("IsVisible") == "{Binding ShowBadges}");
        var binding = (string)items.Attribute("ItemsSource")!;
        var property = binding["{Binding ".Length..^1];
        return Assert.IsAssignableFrom<IReadOnlyList<CardBadgeViewModel>>(
            typeof(OverlayViewModel).GetProperty(property, BindingFlags.Instance | BindingFlags.Public)!.GetValue(overlay));
    }

    private static (ScryfallCardCatalogData Data, LimitedStatisticsLoadResult Statistics) BadgeFixture(
        params (string Name, double GIH, double ALSA)[] cards)
    {
        var json = JsonSerializer.Serialize(cards.Select((c, i) => new
        {
            id = $"badge-{i}", arena_id = 601 + i, name = c.Name,
            colors = c.Name == "Hatching Plans" ? new[] { "U" } : Array.Empty<string>(),
            rarity = "common", set = "woe", collector_number = (i + 1).ToString()
        }));
        var data = new ScryfallCardCatalogDecoder().DecodeCatalogData(json);
        var context = new LimitedStatisticsContext("WOE", LimitedStatisticsFormat.QuickDraft);
        var statistics = new LimitedStatisticsLoadResult(context, context,
            new(cards.Select((c, i) => new LimitedCardStatistics(CardIdentifier.Create($"badge-{i}"),
                GameInHandWinRate: c.GIH, GameInHandGameCount: c.Name == "Hatching Plans" ? 46464
                    : c.Name == "Scarecrow Guide" ? 21880 : 5000, AverageLastSeenAt: c.ALSA)).Reverse()),
            LimitedStatisticsSource.Live, null, null)
        {
            CardCatalog = data.Catalog,
            EnvironmentCatalog = new([new(CardIdentifier.Create("baseline"), GameInHandWinRate: .578, GameInHandGameCount: 10000)])
        };
        return (data, statistics);
    }

    private static (ScryfallCardCatalogData Data, LimitedStatisticsLoadResult Statistics) FourBadgeFixture() => BadgeFixture(
        ("A", .580, 3.10), ("B", .590, 7.80), ("C", .570, 5.40), ("D", .600, 8.90));

    [Fact]
    public void ShippedPassiveCollectionBindsHatchingRankOneAndScarecrowRankEightToTheirOwnEvidence()
    {
        var (data, statistics) = BadgeFixture(("Hatching Plans", .60593147, 5.2198577),
            ("Second", .599, 2.1), ("Third", .593, 3.2), ("Fourth", .587, 4.3),
            ("Fifth", .581, 5.4), ("Sixth", .575, 6.5), ("Seventh", .569, 7.6),
            ("Scarecrow Guide", .55585009, 6.9474995));
        var session = AssociationSession(data.Catalog);
        using var overlay = new OverlayViewModel(session); // Same construction used by OverlayDesktopSession.
        var pack = AssociationPack(data, Enumerable.Range(601, 8).ToArray(), 6);
        session.ApplySessionUpdate(pack);
        session.ApplyStatisticsUpdate(new(pack.SnapshotResult.Snapshot, false, statistics));
        var bound = BoundBadges(overlay);
        Assert.Equal(("Hatching Plans", "60.6%", "ALSA 5.22", "#1", true),
            (bound[0].Name, bound[0].Presentation!.DisplayedGIH, bound[0].Presentation!.DisplayedALSA,
                bound[0].Presentation!.DisplayedContextRank, bound[0].Presentation!.IsContextPick));
        Assert.Equal(("Scarecrow Guide", "55.6%", "ALSA 6.95", "#8", false),
            (bound[7].Name, bound[7].Presentation!.DisplayedGIH, bound[7].Presentation!.DisplayedALSA,
                bound[7].Presentation!.DisplayedContextRank, bound[7].Presentation!.IsContextPick));
        Assert.Equal(.60593147, bound[0].Presentation!.RawGIH);
        Assert.Equal(.55585009, bound[7].Presentation!.RawGIH);
        Assert.Contains("Context Pick\nHatching Plans", session.LaneRecommendationStatusText);
        for (var i = 0; i < bound.Count; i++) Assert.Same(session.CurrentPackPresentations[i], bound[i].Presentation);
    }

    [Fact]
    public void BoundCollectionKeepsABCDWhenContextRankOrderIsDBAC()
    {
        var (data, statistics) = FourBadgeFixture();
        var session = AssociationSession(data.Catalog);
        using var overlay = new OverlayViewModel(session);
        var pack = AssociationPack(data, [601, 602, 603, 604], 5);
        session.ApplySessionUpdate(pack);
        session.ApplyStatisticsUpdate(new(pack.SnapshotResult.Snapshot, false, statistics));
        var bound = BoundBadges(overlay);
        Assert.Equal(["A", "B", "C", "D"], bound.Select(b => b.Name));
        Assert.Equal(["#3", "#2", "#4", "#1"], bound.Select(b => b.Presentation!.DisplayedContextRank));
        Assert.Equal(["58.0%", "59.0%", "57.0%", "60.0%"], bound.Select(b => b.Presentation!.DisplayedGIH));
        Assert.Equal(["ALSA 3.10", "ALSA 7.80", "ALSA 5.40", "ALSA 8.90"], bound.Select(b => b.Presentation!.DisplayedALSA));
        Assert.Equal([0, 1, 2, 3], bound.Select(b => b.Presentation!.PackIndex));
        Assert.Contains("Context Pick\nD", session.LaneRecommendationStatusText);
        Assert.Equal("D", Assert.Single(bound, b => b.IsContextPick).Name);
    }

    [Fact]
    public void ActualPassiveBindingRebuildsACDWhenBIsPickedWithoutShiftingValues()
    {
        var (data, statistics) = FourBadgeFixture();
        var session = AssociationSession(data.Catalog);
        using var overlay = new OverlayViewModel(session);
        var first = AssociationPack(data, [601, 602, 603, 604], 5);
        session.ApplySessionUpdate(first);
        var old = new LimitedStatisticsUpdate(first.SnapshotResult.Snapshot, false, statistics);
        session.ApplyStatisticsUpdate(old);
        var original = BoundBadges(overlay).Where(b => b.Name != "B")
            .ToDictionary(b => b.Presentation!.CardIdentifier, b => (b.Presentation!.RawGIH, b.Presentation!.ALSA));
        var next = AssociationPack(data, [601, 603, 604], 6);
        session.ApplySessionUpdate(next);
        session.ApplyStatisticsUpdate(new(next.SnapshotResult.Snapshot, false, statistics));
        session.ApplyStatisticsUpdate(old);
        var bound = BoundBadges(overlay);
        Assert.Equal(["A", "C", "D"], bound.Select(b => b.Name));
        Assert.Equal(["#2", "#3", "#1"], bound.Select(b => b.Presentation!.DisplayedContextRank));
        Assert.Equal([0, 1, 2], bound.Select(b => b.Presentation!.PackIndex));
        foreach (var badge in bound)
        {
            var model = badge.Presentation!;
            Assert.Equal(original[model.CardIdentifier], (model.RawGIH, model.ALSA));
            Assert.Same(session.CurrentPackPresentations[model.PackIndex], model);
        }
    }

    [Fact]
    public void PassiveSnapshotIgnoresObsoleteRowCollectionAndNotifiesNestedBindingsAsAUnit()
    {
        var (data, statistics) = FourBadgeFixture();
        var session = AssociationSession(data.Catalog);
        session.PackPresentationChanged += _ => session.CurrentPackCards.Clear();
        using var overlay = new OverlayViewModel(session);
        var pack = AssociationPack(data, [601, 602, 603, 604], 5);
        session.ApplySessionUpdate(pack);
        var old = BoundBadges(overlay).ToArray();
        var notices = new List<string?>();
        old[0].PropertyChanged += (_, e) => notices.Add(e.PropertyName);
        session.ApplyStatisticsUpdate(new(pack.SnapshotResult.Snapshot, false, statistics));
        Assert.Empty(session.CurrentPackCards);
        var bound = BoundBadges(overlay);
        Assert.Equal(4, bound.Count);
        Assert.Contains(nameof(CardBadgeViewModel.Presentation), notices);
        for (var i = 0; i < bound.Count; i++)
        {
            Assert.Same(old[i], bound[i]);
            Assert.Same(session.CurrentPackPresentations[i], bound[i].Presentation);
        }
        Assert.Equal("58.0%", bound[0].Presentation!.DisplayedGIH);
        Assert.Equal("#3", bound[0].Presentation!.DisplayedContextRank);
    }

    [Fact]
    public void FinalBadgeDiagnosticDescribesTheObjectsActuallyBoundAfterPublication()
    {
        var (data, statistics) = FourBadgeFixture();
        var session = AssociationSession(data.Catalog);
        using var overlay = new OverlayViewModel(session);
        var pack = AssociationPack(data, [601, 602, 603, 604], 5);
        session.ApplySessionUpdate(pack);
        overlay.SetViewport(1400, 600);
        var observed = new List<string>();
        overlay.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OverlayViewModel.Badges)) observed.Add(overlay.BadgeBindingDiagnosticsText);
        };
        session.ApplyStatisticsUpdate(new(pack.SnapshotResult.Snapshot, false, statistics));
        Assert.Equal(overlay.BadgeBindingDiagnosticsText, Assert.Single(observed));
        Assert.Contains("P1P5 passive badge bindings:", overlay.BadgeBindingDiagnosticsText);
        Assert.Contains("Display index: 0; PackIndex: 0; CardIdentifier: badge-0; CardName: A; RawGIH: 0.58; ALSA: 3.1; ContextRank: 3; IsContextPick: False", overlay.BadgeBindingDiagnosticsText);
        Assert.Contains("Display index: 3; PackIndex: 3; CardIdentifier: badge-3; CardName: D; RawGIH: 0.6; ALSA: 8.9; ContextRank: 1; IsContextPick: True", overlay.BadgeBindingDiagnosticsText);
    }

    [Fact]
    public void WaitingStatisticsCallbackCannotAdoptAPackThatBecomesReadyDuringNotification()
    {
        var (data, _) = FourBadgeFixture();
        var session = AssociationSession(data.Catalog);
        using var overlay = new OverlayViewModel(session);
        var pack = AssociationPack(data, [601, 602, 603, 604], 5);
        var advanced = false;
        session.PropertyChanged += (_, e) =>
        {
            if (advanced || e.PropertyName != nameof(MainWindowViewModel.StatisticsStatusText)) return;
            advanced = true;
            session.ApplySessionUpdate(pack);
        };
        session.ApplyStatisticsUpdate(new(null, true, null));
        Assert.True(advanced);
        Assert.Equal(["A", "B", "C", "D"], BoundBadges(overlay).Select(b => b.Name));
        Assert.All(BoundBadges(overlay), b =>
        {
            Assert.Null(b.Presentation!.RawGIH);
            Assert.Empty(b.Presentation.DisplayedContextRank);
            Assert.Same(session.CurrentPackPresentations[b.Index], b.Presentation);
        });
    }

    [Fact]
    public void ShippedXamlReadsAllPassiveMetricTextThroughTheSameOccurrenceProperty()
    {
        var items = Assert.Single(PassiveBadgeView().Descendants(), e =>
            (string?)e.Attribute("ItemsSource") == "{Binding Badges}");
        var textBindings = items.Descendants().Where(e => e.Name.LocalName == "TextBlock"
                && ((string?)e.Attribute("Text"))?.StartsWith("{Binding Presentation.", StringComparison.Ordinal) == true)
            .Select(e => (string)e.Attribute("Text")!).ToArray();
        Assert.Equal(["{Binding Presentation.DisplayedPickScore}", "{Binding Presentation.DisplayedBadgeGIH}",
            "{Binding Presentation.DisplayedALSA}"], textBindings);
        Assert.DoesNotContain(items.DescendantsAndSelf().Attributes(), a =>
            a.Value.Contains("CurrentPackCards", StringComparison.Ordinal)
            || a.Value.Contains("Recommendations[", StringComparison.Ordinal));
    }
}
