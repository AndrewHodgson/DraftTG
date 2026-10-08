using DraftTG.Domain;

namespace DraftTG.RecommendationEngine.Tests;

/// <summary>Stabilization phase 2: saturation and ranking precision. Each test states the expected drafting
/// behaviour first; assertions check ordering and bounds, not incidental output values.</summary>
public sealed class ContextualPickScoreRankingTests
{
    private static readonly SetArchetypeProfile Profile = new("WOE", "https://example.test/roles",
        new[] { "WU", "UB", "BR", "RG", "GW", "WB", "UR", "BG", "RW", "GU" }
            .Select(p => new ArchetypeDefinition("WOE", ArchetypeColorPair.Create(p), p, "Fixture")));

    private sealed record Entry(Card Card, double Rate, int Games);
    private static Entry E(string id, string cost, int mv, double rate, int games = 10000)
    {
        var colors = cost.Where("WUBRG".Contains).Distinct().Select(c => (MagicColor)"WUBRG".IndexOf(c));
        return new(new Card(CardIdentifier.Create(id), id, new(colors), CardRarity.Common, CardSetCode.Create("WOE"), CollectorNumber.Create(id))
        { GameplayMetadata = new(mv, cost, "Creature") }, rate, games);
    }
    private static IEnumerable<Entry> Many(string prefix, string cost, int mv, double rate, int count) =>
        Enumerable.Range(0, count).Select(i => E($"{prefix}{i}", cost, mv, rate));
    /// <summary>P3P8 committed red-green: 27 on-colour playables at 57%, 8 off-colour white cards.</summary>
    private static Entry[] DeepRedGreen() => Many("g2", "{1}{G}", 2, .57, 8).Concat(Many("g3", "{2}{G}", 3, .57, 6))
        .Concat(Many("r2", "{1}{R}", 2, .57, 8)).Concat(Many("r4", "{3}{R}", 4, .57, 5)).Concat(Many("w", "{2}{W}", 3, .55, 8)).ToArray();

    private static ContextualPickScoreResult Score(IEnumerable<Entry> pack, IReadOnlyList<Entry>? pool = null, double environmentRate = .56)
    {
        pool ??= [];
        var packEntries = pack.ToArray();
        var picks = pool.Count;
        var snapshot = new DraftSnapshot(new(new(PackNumber.Create(picks / 14 + 1), PickNumber.Create(picks % 14 + 1)), packEntries.Select(e => e.Card.Identifier)),
            new(pool.Select((e, i) => new DraftPick(new(PackNumber.Create(i / 14 + 1), PickNumber.Create(i % 14 + 1)), e.Card.Identifier))), DraftFormat.BestOfOne);
        var all = pool.Concat(packEntries).DistinctBy(e => e.Card.Identifier).ToArray();
        var catalog = new CardCatalog(all.Select(e => e.Card));
        var stats = new LimitedCardStatisticsCatalog(all.Select(e => new LimitedCardStatistics(e.Card.Identifier, e.Games, GameInHandWinRate: e.Rate, AverageLastSeenAt: 4)));
        var environment = new LimitedCardStatisticsCatalog([new(CardIdentifier.Create("environment"), 1000000, GameInHandWinRate: environmentRate)]);
        var statistical = new StatisticalRecommendationEngine().Recommend(snapshot.CurrentPack, stats, environment);
        var colors = new ContextualRecommendationEngine().Recommend(snapshot, catalog, statistical);
        var lane = new LaneContextualRecommendationEngine().Recommend(snapshot, colors, DraftPackObservationHistory.Empty, LimitedStatisticsFormat.PremierDraft);
        var archetype = new ArchetypeRecommendationEngine().Recommend(lane, Profile, new("WOE", LimitedStatisticsFormat.PremierDraft));
        return new ContextualPickScoreEngine().Recommend(snapshot, catalog, archetype, stats, environment);
    }
    private static ContextualPickScore Top(ContextualPickScoreResult result) => result.Cards.Single(c => c.IsContextPick);

    [Fact] public void AuditCaseSeventyTwoPercentMythicOutranksSixtyFivePercentUncommonAtPackOnePickOne()
    {
        // Expected: after Phase 8 shrinkage the mythic is still ~4.4 pp stronger (69.3% vs 64.9%), so it must be the
        // pick even though the uncommon has 16x the games. V1 tied them at 50 and ranked the uncommon first.
        var result = Score([E("uncommon-65", "{2}{G}", 3, .65, 40000), E("mythic-72", "{3}{G}", 4, .72, 2500), E("common-63.5", "{1}{G}", 2, .635, 60000)]);
        Assert.Equal("mythic-72", Top(result).CardIdentifier.Value);
        Assert.True(result.Cards[1].ContextualValue > result.Cards[0].ContextualValue);
        Assert.True(result.Cards[1].Score0To50 >= result.Cards[0].Score0To50);
        Assert.Equal([2, 1, 3], result.Cards.Select(c => c.CurrentPackRank!.Value));
    }

    [Fact] public void CardsThatBothDisplayedFiftyKeepTheirTrueOrderAndPremiumTier()
    {
        // Expected: two strong cards that V1 both showed as 50 stay in the premium band but are no longer equal
        // internally, and the stronger one (higher adjusted GIH) ranks first despite fewer games.
        var result = Score([E("uncommon-65.5", "{2}{R}", 3, .655, 30000), E("mythic-68", "{3}{B}", 4, .68, 3000)]);
        Assert.All(result.Cards, c => Assert.True(c.Score0To50 >= 45));
        Assert.True(result.Cards[1].ContextualValue > result.Cards[0].ContextualValue);
        Assert.True(result.Cards[1].IsContextPick);
    }

    [Fact] public void CardsRoundingToTheSameIntegerAreRankedByTheirUnroundedValue()
    {
        // Expected: 58.00% and 58.05% display the same integer, but the slightly stronger card ranks first even
        // though it appears later in the pack.
        var result = Score([E("lower", "{2}{G}", 3, .5800), E("higher", "{2}{U}", 3, .5805)]);
        Assert.Equal(result.Cards[0].Score0To50, result.Cards[1].Score0To50);
        Assert.True(result.Cards[1].ContextualValue > result.Cards[0].ContextualValue);
        Assert.Equal(1, result.Cards[1].CurrentPackRank);
    }

    [Fact] public void GenuinelyEquivalentCandidatesPreferMoreEvidenceThenPackOrder()
    {
        // Expected: 75% over 500 games and 68.75% over 1,000 games shrink to exactly the same 62.5% (baseline 50%).
        // With nothing else to separate them, the better-evidenced card ranks first; identical cards keep pack order.
        var result = Score([E("thin-evidence", "{2}{G}", 3, .75, 500), E("more-evidence", "{2}{U}", 3, .6875, 1000),
            E("twin-a", "{2}{W}", 3, .55, 5000), E("twin-b", "{2}{W}", 3, .55, 5000)], environmentRate: .5);
        Assert.Equal(result.Cards[0].ContextualValue, result.Cards[1].ContextualValue);
        Assert.Equal(1, result.Cards[1].CurrentPackRank); Assert.Equal(2, result.Cards[0].CurrentPackRank);
        Assert.Equal(result.Cards[2].ContextualValue, result.Cards[3].ContextualValue);
        Assert.True(result.Cards[2].CurrentPackRank < result.Cards[3].CurrentPackRank);
    }

    [Fact] public void SmallSampleBombsAreNeitherTrustedBlindlyNorOverPenalized()
    {
        // Expected: 75% from 150 games is mostly noise and should trail a well-measured 62% card. A typical mythic
        // sample (72% over 2,500 games) is still clearly ahead of a 65% card with 40,000 games.
        var noisy = Score([E("noisy-75", "{3}{G}", 4, .75, 150), E("measured-62", "{2}{G}", 3, .62, 40000)]);
        Assert.True(noisy.Cards[1].IsContextPick);
        var mythic = Score([E("measured-65", "{2}{G}", 3, .65, 40000), E("mythic-72", "{3}{G}", 4, .72, 2500)]);
        Assert.True(mythic.Cards[1].IsContextPick);
        Assert.True(mythic.Cards[1].Score0To50 - mythic.Cards[0].Score0To50 >= 1);
    }

    [Fact] public void EarlyDraftRankingFollowsCardQualityAcrossColors()
    {
        // Expected: at P1P3 with two picks made, deck fit has no weight; the ranking is the adjusted-GIH order,
        // including among cards above the old +8 pp saturation point.
        var pool = new[] { E("p0", "{2}{G}", 3, .57), E("p1", "{2}{G}", 3, .57) };
        var pack = new[] { E("a-70", "{2}{U}", 3, .70, 3000), E("b-56", "{2}{G}", 3, .56), E("c-66", "{2}{B}", 3, .66, 8000),
            E("d-61", "{2}{R}", 3, .61), E("e-74", "{2}{W}", 3, .74, 2000), E("f-52", "{2}{G}", 3, .52) };
        var result = Score(pack, pool);
        Assert.Equal(["e-74", "a-70", "c-66", "d-61", "b-56", "f-52"], result.Cards.OrderBy(c => c.CurrentPackRank).Select(c => c.CardIdentifier.Value));
    }

    [Fact] public void LateDraftDeckFitStillOutranksOffColorPowerButOffColorBombsStayOrdered()
    {
        // Expected: at P3P8 in committed red-green a 61% on-colour upgrade beats a 72% off-colour bomb, and between two
        // off-colour bombs the stronger one ranks higher. V1 tied them (Q=1) and preferred the one with more games.
        var result = Score([E("blue-66", "{3}{U}", 4, .66, 40000), E("blue-72", "{3}{U}", 4, .72, 2500), E("red-61", "{2}{R}", 3, .61)], DeepRedGreen());
        Assert.True(result.Cards[2].IsContextPick);
        Assert.True(result.Cards[1].CurrentPackRank < result.Cards[0].CurrentPackRank);
        Assert.True(result.Cards[1].ContextualValue > result.Cards[0].ContextualValue);
    }

    [Fact] public void OffColorPowerWinsEarlyButYieldsToOnColorStrengthLate()
    {
        // Expected: at P1P4 a 68% off-colour card beats a 60% on-colour card; by P3P8 in established colours the
        // on-colour card wins. Precision changes must not erase this stage behaviour.
        var early = Score([E("off-68", "{2}{U}", 3, .68, 5000), E("on-60", "{2}{G}", 3, .60)], [E("p0", "{2}{G}", 3, .57), E("p1", "{2}{G}", 3, .57), E("p2", "{2}{R}", 3, .57)]);
        Assert.True(early.Cards[0].IsContextPick);
        var late = Score([E("off-68", "{2}{U}", 3, .68, 5000), E("on-60", "{2}{G}", 3, .60)], DeepRedGreen());
        Assert.True(late.Cards[1].IsContextPick);
    }

    [Fact] public void ExtremeScoresStayInRangeOrderedAndCannotOverrideLateContext()
    {
        // Expected: implausible extremes stay within 0–50 with distinct internal values, and even a 90% off-colour
        // card late cannot outrank a solid on-colour upgrade (quality still saturates, just not abruptly).
        var high = Score([E("x-85", "{2}{G}", 3, .85, 50000), E("x-92", "{2}{U}", 3, .92, 50000)]);
        Assert.All(high.Cards, c => { Assert.InRange(c.Score0To50!.Value, 0, 50); Assert.True(c.ContextualValue < 1); });
        Assert.True(high.Cards[1].ContextualValue > high.Cards[0].ContextualValue);
        var low = Score([E("x-30", "{2}{G}", 3, .30, 50000), E("x-20", "{2}{U}", 3, .20, 50000)]);
        Assert.All(low.Cards, c => { Assert.InRange(c.Score0To50!.Value, 0, 50); Assert.True(c.ContextualValue > 0); });
        Assert.True(low.Cards[0].ContextualValue > low.Cards[1].ContextualValue);
        var late = Score([E("off-90", "{3}{U}", 4, .90, 50000), E("on-60", "{2}{R}", 3, .60)], DeepRedGreen());
        Assert.True(late.Cards[1].IsContextPick);
    }

    [Fact] public void SmallRatePerturbationsMoveScoresSmoothlyAcrossTheWholeRange()
    {
        // Expected: from 45% to 80% at P1P1, every 0.1 pp step raises the internal value and changes the displayed
        // score by at most one point (no plateau at 50, no jumps).
        double? previousValue = null; int? previousScore = null;
        for (var rate = .45; rate <= .8001; rate += .001)
        {
            var card = Score([E("candidate", "{2}{G}", 3, rate), E("other", "{2}{U}", 3, .55)]).Cards[0];
            if (previousValue is { } value) Assert.True(card.ContextualValue > value);
            if (previousScore is { } score) Assert.InRange(card.Score0To50!.Value - score, 0, 1);
            previousValue = card.ContextualValue; previousScore = card.Score0To50;
        }
    }

    [Fact] public void RankingIsDeterministicAcrossRepeatedRuns()
    {
        var pack = new[] { E("a", "{2}{G}", 3, .72, 2500), E("b", "{2}{R}", 3, .65, 40000), E("c", "{2}{W}", 3, .65, 40000), E("d", "{2}{U}", 3, .6501, 2000) };
        var runs = Enumerable.Range(0, 3).Select(_ => Score(pack, DeepRedGreen())).ToArray();
        Assert.All(runs, run => Assert.Equal(runs[0].Cards.Select(c => (c.CurrentPackRank, c.ContextualValue)), run.Cards.Select(c => (c.CurrentPackRank, c.ContextualValue))));
    }

    [Fact] public void ContributionsIncludingTheSoftLimitSumToTheActualValue()
    {
        // Expected: diagnostics remain truthful when the soft limit compresses a strong card.
        var card = Score([E("bomb", "{2}{G}", 3, .74, 3000), E("other", "{2}{U}", 3, .55)]).Cards[0];
        Assert.Equal(50 * card.ContextualValue!.Value, 25 + card.Contributions.Sum(c => c.PointsFromNeutral), 9);
        Assert.True(card.Contributions.Single(c => c.Component == "Soft limit").PointsFromNeutral < 0);
    }
}
