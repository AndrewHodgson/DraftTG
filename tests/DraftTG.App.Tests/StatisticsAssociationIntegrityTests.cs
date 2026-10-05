using System.Net;
using System.Text;
using System.Text.Json;
using DraftTG.Application;
using DraftTG.ArenaIntegration;
using DraftTG.Data;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;

namespace DraftTG.App.Tests;

public sealed partial class MainWindowViewModelTests
{
    private static ScryfallCardCatalogData AssociationData() => new ScryfallCardCatalogDecoder().DecodeCatalogData("""
        [
          {"id":"hatching","arena_id":201,"name":"Hatching Plans","colors":["U"],"rarity":"rare","set":"wot","collector_number":"20"},
          {"id":"scarecrow","arena_id":202,"name":"Scarecrow Guide","colors":[],"rarity":"common","set":"woe","collector_number":"250"}
        ]
        """);
    private static MainWindowViewModel AssociationSession(CardCatalog catalog)
    {
        var session = CreateViewModel();
        session.ApplyRuntime(CreateRuntime(new ControlledLogSource(), catalog: catalog));
        return session;
    }
    private static DraftSessionUpdate AssociationPack(ScryfallCardCatalogData data, int[] cards, int pick = 1) =>
        Update(State(ArenaDraftSessionStatus.Active, ArenaDraftMode.Quick, cards, pick: pick),
            new ArenaDraftSnapshotAdapter(new(data)));
    private static LimitedStatisticsLoadResult AssociationStatistics(CardCatalog catalog, DraftSnapshot snapshot)
    {
        var context = new LimitedStatisticsContext("WOE", LimitedStatisticsFormat.QuickDraft);
        var mapping = LimitedStatisticsMapper.Map([
            new("Scarecrow Guide", GameInHandWinRate: 0.55585009, GameInHandGameCount: 21880, AverageLastSeenAt: 6.9474995),
            new("Hatching Plans", GameInHandWinRate: 0.60593147, GameInHandGameCount: 46464, AverageLastSeenAt: 5.2198577)
        ], catalog, context, snapshot);
        return new(context, context, mapping.Catalog, LimitedStatisticsSource.Live, null, null)
        {
            CardCatalog = catalog,
            StatisticsResolvedNames = mapping.ResolvedNames,
            EnvironmentCatalog = new([new(CardIdentifier.Create("env"), GameInHandWinRate: 0.578, GameInHandGameCount: 10000)])
        };
    }

    [Fact]
    public void ReorderedNamedRowsCannotSwapHatchingPlansAndScarecrowGuideStatistics()
    {
        var data = AssociationData();
        var session = AssociationSession(data.Catalog);
        using var hud = new OverlayViewModel(session);
        var pack = AssociationPack(data, [201, 202]);
        session.ApplySessionUpdate(pack);
        // Reproduce the unguarded boundary: row names and the snapshot's ID list drift apart.
        session.CurrentPackCards.Move(0, 1);
        session.ApplyStatisticsUpdate(new(pack.SnapshotResult.Snapshot, false,
            AssociationStatistics(data.Catalog, pack.SnapshotResult.Snapshot!)));
        var hatching = Assert.Single(session.CurrentPackCards, row => row.Name == "Hatching Plans");
        var scarecrow = Assert.Single(session.CurrentPackCards, row => row.Name == "Scarecrow Guide");
        Assert.Equal("60.6%", hatching.Statistics.GameInHand);
        Assert.Equal("5.22", hatching.Statistics.AverageLastSeen);
        Assert.Equal("55.6%", scarecrow.Statistics.GameInHand);
        Assert.Equal("6.95", scarecrow.Statistics.AverageLastSeen);
        Assert.Equal(["Hatching Plans", "Scarecrow Guide"], hud.Badges.Select(b => b.Name));
        Assert.Equal(["60.6%", "55.6%"], hud.Badges.Select(b => b.WinRate));
        Assert.Contains("Stats Pick\nHatching Plans", session.RecommendationStatusText);
    }

    [Fact]
    public void BadgeRefreshCannotTakeAnotherCardsStatisticsWhenPresentationOrderDrifts()
    {
        var data = AssociationData();
        var session = AssociationSession(data.Catalog);
        session.PackPresentationChanged += newPack =>
        {
            if (!newPack) session.CurrentPackCards.Move(0, 1);
        };
        using var hud = new OverlayViewModel(session);
        var pack = AssociationPack(data, [201, 202]);
        session.ApplySessionUpdate(pack);
        session.ApplyStatisticsUpdate(new(pack.SnapshotResult.Snapshot, false,
            AssociationStatistics(data.Catalog, pack.SnapshotResult.Snapshot!)));
        Assert.Contains("Stats Pick\nHatching Plans", session.RecommendationStatusText);
        Assert.Equal("Hatching Plans", hud.Badges[0].Name);
        Assert.Equal("60.6%", hud.Badges[0].WinRate);
        Assert.Equal("ALSA 5.22", hud.Badges[0].Secondary);
        Assert.Equal("Scarecrow Guide", hud.Badges[1].Name);
        Assert.Equal("55.6%", hud.Badges[1].WinRate);
        Assert.Equal("ALSA 6.95", hud.Badges[1].Secondary);
    }

    private static ScryfallCardCatalogData DistinctAssociationData() => new ScryfallCardCatalogDecoder().DecodeCatalogData("""
        [
          {"id":"a","arena_id":301,"name":"Card A","colors":[],"rarity":"common","set":"woe","collector_number":"1"},
          {"id":"b","arena_id":302,"name":"Card B","colors":[],"rarity":"common","set":"woe","collector_number":"2"},
          {"id":"c","arena_id":303,"name":"Card C","colors":[],"rarity":"common","set":"woe","collector_number":"3"},
          {"id":"d","arena_id":304,"name":"Card D","colors":[],"rarity":"common","set":"woe","collector_number":"4"},
          {"id":"e","arena_id":305,"name":"Card E","colors":[],"rarity":"common","set":"woe","collector_number":"5"}
        ]
        """);

    private static LimitedStatisticsLoadResult DistinctAssociationStatistics(CardCatalog catalog, DraftSnapshot snapshot)
    {
        var context = new LimitedStatisticsContext("WOE", LimitedStatisticsFormat.QuickDraft);
        var mapped = LimitedStatisticsMapper.Map([
            new("Card E", GameInHandWinRate: .580, GameInHandGameCount: 5000, AverageLastSeenAt: 6.10),
            new("Card C", GameInHandWinRate: .550, GameInHandGameCount: 5000, AverageLastSeenAt: 5.40),
            new("Card A", GameInHandWinRate: .510, GameInHandGameCount: 5000, AverageLastSeenAt: 3.10),
            new("Card D", GameInHandWinRate: .530, GameInHandGameCount: 5000, AverageLastSeenAt: 4.20),
            new("Card B", GameInHandWinRate: .620, GameInHandGameCount: 5000, AverageLastSeenAt: 7.80)
        ], catalog, context, snapshot);
        return new(context, context, mapped.Catalog, LimitedStatisticsSource.Live, null, null)
        {
            CardCatalog = catalog, StatisticsResolvedNames = mapped.ResolvedNames,
            EnvironmentCatalog = new([new(CardIdentifier.Create("env"), GameInHandWinRate: .578, GameInHandGameCount: 10000)])
        };
    }

    [Fact]
    public async Task RealCardDataPayloadKeepsEvidenceAndAllThreeStagesOnTheirNamedBadges()
    {
        var data = AssociationData();
        var session = AssociationSession(data.Catalog);
        using var hud = new OverlayViewModel(session);
        var pack = AssociationPack(data, [201, 202]);
        session.ApplySessionUpdate(pack);
        // Provider order is deliberately the reverse of the Arena pack order.
        var rows = new List<object>
        {
            new { name = "Scarecrow Guide", avg_seen = 6.9474995, ever_drawn_game_count = 21880, ever_drawn_win_rate = .55585009, win_rate = .49 },
            new { name = "Hatching Plans", avg_seen = 5.2198577, ever_drawn_game_count = 46464, ever_drawn_win_rate = .60593147, win_rate = .48 }
        };
        rows.AddRange(Enumerable.Range(0, 18).Select(i => (object)new
            { name = $"Environment {i}", avg_seen = 6.0, ever_drawn_game_count = 5000, ever_drawn_win_rate = .578 }));
        var directory = Path.Combine(Path.GetTempPath(), "DraftTG-association-" + Guid.NewGuid());
        try
        {
            using var http = new HttpClient(new AssociationHandler(JsonSerializer.Serialize(new { data = rows })));
            var provider = new SeventeenLandsCardRatingsClient(http, new AssociationPaths(directory));
            var context = new LimitedStatisticsContext("WOE", LimitedStatisticsFormat.QuickDraft);
            var loaded = await new LimitedStatisticsService(provider, data.Catalog).LoadAsync(context, pack.SnapshotResult.Snapshot);
            Assert.Equal(LimitedStatisticsSource.Live, loaded.Source);
            loaded = loaded with { EnvironmentCatalog = new([new(CardIdentifier.Create("env"), GameInHandWinRate: .578, GameInHandGameCount: 10000)]) };
            var update = new LimitedStatisticsUpdate(pack.SnapshotResult.Snapshot, false, loaded);
            session.ApplyStatisticsUpdate(update);
            foreach (var occurrence in update.Occurrences.Values)
            {
                Assert.True(occurrence.IsIdentityConsistent);
                Assert.Equal(occurrence.CardIdentifier, occurrence.RawStatistics!.CardIdentifier);
                Assert.Equal(occurrence.Key, new(occurrence.Statistical!.PackIndex, occurrence.Statistical.CardIdentifier));
                Assert.Equal(occurrence.Key, new(occurrence.Pool!.PackIndex, occurrence.Pool.CardIdentifier));
                Assert.Equal(occurrence.Key, new(occurrence.Lane!.PackIndex, occurrence.Lane.CardIdentifier));
                Assert.Equal(occurrence.CardName, occurrence.ResolvedStatisticsName);
            }
            var hatching = update.Occurrences[new(0, CardIdentifier.Create("hatching"))];
            var scarecrow = update.Occurrences[new(1, CardIdentifier.Create("scarecrow"))];
            Assert.Equal(.60593147, hatching.RawGIH); Assert.Equal(46464, hatching.GIHSample); Assert.Equal(5.2198577, hatching.ALSA);
            Assert.Equal(.55585009, scarecrow.RawGIH); Assert.Equal(21880, scarecrow.GIHSample); Assert.Equal(6.9474995, scarecrow.ALSA);
            Assert.Equal((46464 * .60593147 + 500 * .578) / (46464 + 500), hatching.Phase8AdjustedValue);
            Assert.Equal(1, hatching.StatsRank); Assert.Equal(2, scarecrow.StatsRank);
            Assert.Equal(["60.6%", "55.6%"], hud.Badges.Select(b => b.WinRate));
            Assert.Equal(["ALSA 5.22", "ALSA 6.95"], hud.Badges.Select(b => b.Secondary));
            Assert.Contains("n=46,464", hud.Badges[0].Detail); Assert.Contains("n=21,880", hud.Badges[1].Detail);
            Assert.Contains("Stats Pick\nHatching Plans", session.RecommendationStatusText);
            Assert.Contains("Context Pick\nHatching Plans", session.LaneRecommendationStatusText);
            Assert.Contains("Stats resolved: Scarecrow Guide", session.ContextualRecommendationDiagnosticsText);
            Assert.Contains("GIH: 0.55585009; GIH n: 21880; ALSA: 6.9474995", session.ContextualRecommendationDiagnosticsText);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void ThreeDistinctValuesKeepArenaOrderWhileRanksHaveAnotherOrder()
    {
        var data = DistinctAssociationData();
        var session = AssociationSession(data.Catalog);
        using var hud = new OverlayViewModel(session);
        var pack = AssociationPack(data, [301, 302, 303]);
        session.ApplySessionUpdate(pack);
        session.ApplyStatisticsUpdate(new(pack.SnapshotResult.Snapshot, false,
            DistinctAssociationStatistics(data.Catalog, pack.SnapshotResult.Snapshot!)));
        Assert.Equal(["Card A", "Card B", "Card C"], hud.Badges.Select(b => b.Name));
        Assert.Equal(["51.0%", "62.0%", "55.0%"], hud.Badges.Select(b => b.WinRate));
        Assert.Equal(["ALSA 3.10", "ALSA 7.80", "ALSA 5.40"], hud.Badges.Select(b => b.Secondary));
        Assert.Equal(["#3", "#1", "#2"], hud.Badges.Select(b => b.Rank));
        Assert.Equal([0, 1, 2], session.CurrentPackCards.Select(c => c.OccurrenceKey!.Value.PackIndex));
        Assert.Equal([3, 1, 2], session.CurrentPackCards.Select(c => c.Presentation!.StatsRank!.Value));
    }

    [Fact]
    public async Task DelayedPackACalculationCannotOverwriteAppliedCompletelyDifferentPackB()
    {
        var data = DistinctAssociationData();
        var session = AssociationSession(data.Catalog);
        using var hud = new OverlayViewModel(session);
        var packA = AssociationPack(data, [301, 302], 5);
        session.ApplySessionUpdate(packA);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayed = Task.Run(async () =>
        {
            started.SetResult();
            await release.Task;
            return new LimitedStatisticsUpdate(packA.SnapshotResult.Snapshot, false,
                DistinctAssociationStatistics(data.Catalog, packA.SnapshotResult.Snapshot!));
        });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var packB = AssociationPack(data, [304, 305], 6);
        session.ApplySessionUpdate(packB);
        session.ApplyStatisticsUpdate(new(packB.SnapshotResult.Snapshot, false,
            DistinctAssociationStatistics(data.Catalog, packB.SnapshotResult.Snapshot!)));
        var before = session.CurrentPackCards.ToArray();
        var rail = session.LaneRecommendationStatusText;
        release.SetResult();
        session.ApplyStatisticsUpdate(await delayed.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(before, session.CurrentPackCards);
        Assert.Equal(rail, session.LaneRecommendationStatusText);
        Assert.Equal(["Card D", "Card E"], hud.Badges.Select(b => b.Name));
        Assert.Equal(["53.0%", "58.0%"], hud.Badges.Select(b => b.WinRate));
        Assert.Equal(["#2", "#1"], hud.Badges.Select(b => b.Rank));
    }

    [Fact]
    public void ShrinkingFiveToFourRekeysSurvivorsWithoutShiftingTheirEvidenceOrRanks()
    {
        var data = DistinctAssociationData();
        var session = AssociationSession(data.Catalog);
        using var hud = new OverlayViewModel(session);
        var before = AssociationPack(data, [301, 302, 303, 304, 305], 5);
        session.ApplySessionUpdate(before);
        var old = new LimitedStatisticsUpdate(before.SnapshotResult.Snapshot, false,
            DistinctAssociationStatistics(data.Catalog, before.SnapshotResult.Snapshot!));
        session.ApplyStatisticsUpdate(old);
        var evidence = session.CurrentPackCards.Skip(1).Select(c => (c.Presentation!.CardIdentifier, c.Statistics)).ToArray();
        var next = AssociationPack(data, [302, 303, 304, 305], 6);
        session.ApplySessionUpdate(next);
        session.ApplyStatisticsUpdate(new(next.SnapshotResult.Snapshot, false,
            DistinctAssociationStatistics(data.Catalog, next.SnapshotResult.Snapshot!)));
        session.ApplyStatisticsUpdate(old);
        Assert.Equal(evidence, session.CurrentPackCards.Select(c => (c.Presentation!.CardIdentifier, c.Statistics)));
        Assert.DoesNotContain(hud.Badges, b => b.Name == "Card A");
        Assert.Equal([0, 1, 2, 3], hud.Badges.Select(b => b.OccurrenceKey!.Value.PackIndex));
        Assert.Equal(["62.0%", "55.0%", "53.0%", "58.0%"], hud.Badges.Select(b => b.WinRate));
        Assert.Equal(["#1", "#3", "#4", "#2"], hud.Badges.Select(b => b.Rank));
    }

    [Fact]
    public void DifferentStatsAndContextPicksUseOccurrenceNamesEvenAfterRowsMove()
    {
        var session = ReadyViewModel();
        using var hud = new OverlayViewModel(session);
        var pack = ReadyUpdate(ArenaDraftMode.Quick, [102, 101], WhitePool(14), pack: 2);
        session.ApplySessionUpdate(pack);
        session.CurrentPackCards.Move(0, 1);
        var update = new LimitedStatisticsUpdate(pack.SnapshotResult.Snapshot, false, ContextStatistics());
        session.ApplyStatisticsUpdate(update);
        Assert.Equal("Stats Pick: Beta", session.StatisticalPickStatusText);
        Assert.Contains("Stats Pick\nBeta", session.RecommendationStatusText);
        Assert.Contains("Context Pick\nAlpha", session.LaneRecommendationStatusText);
        Assert.Equal(["Beta", "Alpha"], hud.Badges.Select(b => b.Name));
        Assert.Equal(["#2", "#1"], hud.Badges.Select(b => b.Rank));
        Assert.Equal(["59.0%", "58.0%"], hud.Badges.Select(b => b.WinRate));
        Assert.Equal([true, false], hud.Badges.Select(b => b.IsStatisticalPick));
        Assert.Equal([false, true], hud.Badges.Select(b => b.IsContextPick));
        Assert.Equal(1, session.CurrentPackCards[0].Presentation!.StatsRank);
        Assert.Equal(1, session.CurrentPackCards[1].Presentation!.ContextRank);
    }

    [Fact]
    public void DuplicateCardIdentifiersShareRawEvidenceButKeepSeparateOccurrenceRanks()
    {
        var data = AssociationData();
        var session = AssociationSession(data.Catalog);
        using var hud = new OverlayViewModel(session);
        var pack = AssociationPack(data, [201, 202, 201]);
        session.ApplySessionUpdate(pack);
        var update = new LimitedStatisticsUpdate(pack.SnapshotResult.Snapshot, false,
            AssociationStatistics(data.Catalog, pack.SnapshotResult.Snapshot!));
        session.ApplyStatisticsUpdate(update);
        Assert.Equal(3, update.Occurrences.Count);
        Assert.Equal(["60.6%", "55.6%", "60.6%"], hud.Badges.Select(b => b.WinRate));
        Assert.Equal(["#1", "#3", "#2"], hud.Badges.Select(b => b.Rank));
        Assert.NotEqual(hud.Badges[0].OccurrenceKey, hud.Badges[2].OccurrenceKey);
        Assert.Equal(hud.Badges[0].OccurrenceKey!.Value.CardIdentifier, hud.Badges[2].OccurrenceKey!.Value.CardIdentifier);
        Assert.Equal([true, false, false], hud.Badges.Select(b => b.IsContextPick));
    }

    [Fact]
    public void ForeignOccurrenceOrResolvedNameCannotPartiallyMutateABadge()
    {
        var data = AssociationData();
        var pack = AssociationPack(data, [201, 202]);
        var update = new LimitedStatisticsUpdate(pack.SnapshotResult.Snapshot, false,
            AssociationStatistics(data.Catalog, pack.SnapshotResult.Snapshot!));
        var hatching = update.Occurrences[new(0, CardIdentifier.Create("hatching"))];
        var row = CurrentPackCardViewModel.From(hatching, "U") with
        {
            Statistics = LimitedCardStatisticsPresentation.From(update.Result!.Catalog.StatisticsFor(CardIdentifier.Create("scarecrow"))),
            Recommendation = update.Recommendation!.Cards.Single(c => c.CardIdentifier == CardIdentifier.Create("scarecrow"))
        };
        Assert.Equal("60.6%", row.Statistics.GameInHand);
        Assert.Equal(hatching.Statistical, row.Recommendation);
        var badge = new CardBadgeViewModel(0, CurrentPackCardViewModel.From(hatching, "U"));
        var notifications = 0;
        badge.PropertyChanged += (_, _) => notifications++;
        badge.UpdateStatistics(CurrentPackCardViewModel.From(update.Occurrences[new(1, CardIdentifier.Create("scarecrow"))], "—"));
        var badName = new CurrentPackCardPresentation(hatching.Key, hatching.Card,
            hatching.RawStatistics, "Scarecrow Guide", hatching.Statistical, hatching.Pool, hatching.Lane);
        Assert.False(badName.IsIdentityConsistent);
        Assert.Null(badName.RawGIH); Assert.Null(badName.ContextRank);
        Assert.Contains("REJECTED", badName.DiagnosticText);
        badge.UpdateStatistics(CurrentPackCardViewModel.From(badName, "U"));
        var wrongRank = new CurrentPackCardPresentation(hatching.Key, hatching.Card,
            hatching.RawStatistics, hatching.ResolvedStatisticsName,
            hatching.Statistical! with { PackIndex = 1 }, hatching.Pool, hatching.Lane);
        Assert.False(wrongRank.IsIdentityConsistent);
        badge.UpdateStatistics(CurrentPackCardViewModel.From(wrongRank, "U"));
        Assert.Equal(0, notifications);
        Assert.Equal("Hatching Plans", badge.Name); Assert.Equal("60.6%", badge.WinRate);
        Assert.Equal("ALSA 5.22", badge.Secondary); Assert.Equal("#1", badge.Rank);
    }

    [Fact]
    public void ExactNamesKeepPunctuationSplitNamesAndSupplementalPrintingsDistinct()
    {
        var data = new ScryfallCardCatalogDecoder().DecodeCatalogData("""
            [
              {"id":"supplement","arena_id":401,"name":"Wear // Tear","colors":["R","W"],"rarity":"uncommon","set":"wot","collector_number":"1"},
              {"id":"printing","arena_id":402,"name":"Wear // Tear","colors":["R","W"],"rarity":"uncommon","set":"old","collector_number":"1"},
              {"id":"punctuated","arena_id":403,"name":"Urza's Rebuff","colors":["U"],"rarity":"common","set":"woe","collector_number":"2"},
              {"id":"unpunctuated","arena_id":404,"name":"Urzas Rebuff","colors":["U"],"rarity":"common","set":"woe","collector_number":"3"}
            ]
            """);
        var session = AssociationSession(data.Catalog);
        using var hud = new OverlayViewModel(session);
        var pack = AssociationPack(data, [401, 402, 403, 404]);
        session.ApplySessionUpdate(pack);
        var context = new LimitedStatisticsContext("WOE", LimitedStatisticsFormat.QuickDraft);
        var mapping = LimitedStatisticsMapper.Map([
            new("Urzas Rebuff", GameInHandWinRate: .53, GameInHandGameCount: 5000),
            new("Urza's Rebuff", GameInHandWinRate: .57, GameInHandGameCount: 5000),
            new("Wear // Tear", GameInHandWinRate: .61, GameInHandGameCount: 5000),
            new("wear // tear", GameInHandWinRate: .49, GameInHandGameCount: 5000),
            new("Wear", GameInHandWinRate: .45, GameInHandGameCount: 5000)
        ], data.Catalog, context, pack.SnapshotResult.Snapshot);
        var loaded = new LimitedStatisticsLoadResult(context, context, mapping.Catalog, LimitedStatisticsSource.Live, null, null)
            { CardCatalog = data.Catalog, StatisticsResolvedNames = mapping.ResolvedNames, EnvironmentCatalog = mapping.Catalog };
        session.ApplyStatisticsUpdate(new(pack.SnapshotResult.Snapshot, false, loaded));
        Assert.Equal(["61.0%", "61.0%", "57.0%", "53.0%"], hud.Badges.Select(b => b.WinRate));
        Assert.Equal(["supplement", "printing", "punctuated", "unpunctuated"],
            session.CurrentPackCards.Select(c => c.Presentation!.CardIdentifier.Value));
        Assert.All(session.CurrentPackCards, row => Assert.Equal(row.Name, row.Presentation!.ResolvedStatisticsName));
    }

    [Fact]
    public void SamePositionReorderedPackAndEquivalentNewSnapshotBothRejectOldResult()
    {
        var data = AssociationData();
        var session = AssociationSession(data.Catalog);
        using var hud = new OverlayViewModel(session);
        var first = AssociationPack(data, [201, 202], 5);
        session.ApplySessionUpdate(first);
        var old = new LimitedStatisticsUpdate(first.SnapshotResult.Snapshot, false,
            AssociationStatistics(data.Catalog, first.SnapshotResult.Snapshot!));
        var reordered = AssociationPack(data, [202, 201], 5);
        Assert.NotEqual(old.PackIdentity, reordered.SnapshotResult.Snapshot!.CurrentPack);
        session.ApplySessionUpdate(reordered);
        session.ApplyStatisticsUpdate(new(reordered.SnapshotResult.Snapshot, false,
            AssociationStatistics(data.Catalog, reordered.SnapshotResult.Snapshot!)));
        var rows = session.CurrentPackCards.ToArray();
        session.ApplyStatisticsUpdate(old);
        Assert.Same(rows[0], session.CurrentPackCards[0]);
        Assert.Equal(["Scarecrow Guide", "Hatching Plans"], hud.Badges.Select(b => b.Name));
        Assert.Equal(["55.6%", "60.6%"], hud.Badges.Select(b => b.WinRate));
        var equivalent = AssociationPack(data, [201, 202], 5);
        Assert.Equal(old.PackIdentity, equivalent.SnapshotResult.Snapshot!.CurrentPack);
        Assert.NotSame(old.Snapshot, equivalent.SnapshotResult.Snapshot);
        session.ApplySessionUpdate(equivalent);
        session.ApplyStatisticsUpdate(new(equivalent.SnapshotResult.Snapshot, false,
            AssociationStatistics(data.Catalog, equivalent.SnapshotResult.Snapshot!)));
        var latestRow = session.CurrentPackCards[0];
        session.ApplyStatisticsUpdate(old);
        Assert.Same(latestRow, session.CurrentPackCards[0]);
    }

    private sealed class AssociationPaths(string directory) : IApplicationDataPathProvider
    {
        public string GetApplicationDataDirectory() => directory;
    }
    private sealed class AssociationHandler(string payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("https://www.17lands.com/api/card_data?expansion=WOE&event_type=QuickDraft&time_period=ALL_TIME", request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(payload, Encoding.UTF8, "application/json") });
        }
    }
}
