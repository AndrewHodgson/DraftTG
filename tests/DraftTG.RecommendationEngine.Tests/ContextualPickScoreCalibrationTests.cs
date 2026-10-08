using DraftTG.Domain;

namespace DraftTG.RecommendationEngine.Tests;

/// <summary>Phase 5: the single global 0–50 calibration, canonical tiers and the semantic anchors. Scenario tests state
/// the expected drafting meaning first; ranges are the documented anchors, not exact integers.</summary>
public sealed class ContextualPickScoreCalibrationTests
{
    private static readonly SetArchetypeProfile Profile = new("WOE", "https://example.test/roles",
        new[] { "WU", "UB", "BR", "RG", "GW", "WB", "UR", "BG", "RW", "GU" }
            .Select(p => new ArchetypeDefinition("WOE", ArchetypeColorPair.Create(p), p, "Fixture")));
    private static readonly LimitedCardStatisticsCatalog Environment = new([new(CardIdentifier.Create("environment"), 1000000, GameInHandWinRate: .56)]);

    private sealed record Entry(Card Card, double? Rate, int Games = 10000);
    private static Entry E(string id, string cost, int mv, double? rate = .56, string type = "Creature", string? oracle = null, int games = 10000,
        CardRarity rarity = CardRarity.Common)
    {
        var colors = cost.Where("WUBRG".Contains).Distinct().Select(c => (MagicColor)"WUBRG".IndexOf(c));
        return new(new Card(CardIdentifier.Create(id), id, new(colors), rarity, CardSetCode.Create("WOE"), CollectorNumber.Create(id))
        { GameplayMetadata = new(mv, cost, type, oracleText: oracle) }, rate, games);
    }
    private static IEnumerable<Entry> OffColour(int count) => Enumerable.Range(0, count).Select(i => E($"off{i}", "{2}{" + "WUB"[i % 3] + "}", 3, .55));
    /// <summary>Red-green deck: `early` 2-drops and `creatures` 3-drops at `rate`, plus off-colour cards to reach 33 picks.</summary>
    private static Entry[] Deck(int early = 4, int creatures = 21, double rate = .56)
    {
        var onColour = Enumerable.Range(0, early).Select(i => E($"e{i}", "{1}{G}", 2, rate))
            .Concat(Enumerable.Range(0, creatures).Select(i => E($"c{i}", i % 2 == 0 ? "{2}{R}" : "{2}{G}", 3, rate))).ToArray();
        return onColour.Concat(OffColour(33 - onColour.Length)).ToArray();
    }
    private static ContextualPickScore[] Score(IReadOnlyList<Entry> pack, IReadOnlyList<Entry>? pool = null)
    {
        pool ??= [];
        var picks = pool.Count;
        var snapshot = new DraftSnapshot(new(new(PackNumber.Create(picks / 14 + 1), PickNumber.Create(picks % 14 + 1)), pack.Select(e => e.Card.Identifier)),
            new(pool.Select((e, i) => new DraftPick(new(PackNumber.Create(i / 14 + 1), PickNumber.Create(i % 14 + 1)), e.Card.Identifier))), DraftFormat.BestOfOne);
        var all = pool.Concat(pack).DistinctBy(e => e.Card.Identifier).ToArray();
        var catalog = new CardCatalog(all.Select(e => e.Card));
        var stats = new LimitedCardStatisticsCatalog(all.Select(e => new LimitedCardStatistics(e.Card.Identifier, e.Rate is null ? null : e.Games,
            GameInHandWinRate: e.Rate, AverageLastSeenAt: 4)));
        var statistical = new StatisticalRecommendationEngine().Recommend(snapshot.CurrentPack, stats, Environment);
        var colors = new ContextualRecommendationEngine().Recommend(snapshot, catalog, statistical);
        var lane = new LaneContextualRecommendationEngine().Recommend(snapshot, colors, DraftPackObservationHistory.Empty, LimitedStatisticsFormat.PremierDraft);
        var archetype = new ArchetypeRecommendationEngine().Recommend(lane, Profile, new("WOE", LimitedStatisticsFormat.PremierDraft));
        return new ContextualPickScoreEngine().Recommend(snapshot, catalog, archetype, stats, Environment).Cards.ToArray();
    }
    private static ContextualPickScore One(Entry card, IReadOnlyList<Entry>? pool = null) => Score([card, E("filler", "{2}{U}", 3, .55)], pool)[0];
    private static int Display(ContextualPickScore score) => score.Score0To50!.Value;
    private static void InAnchor(string key, ContextualPickScore score)
    {
        var anchor = ContextualPickScoreCalibration.Anchors.Single(a => a.Key == key);
        Assert.InRange(Display(score), anchor.ExpectedLow, anchor.ExpectedHigh);
    }

    [Fact] public void CalibrationIsStrictlyIncreasingAndBoundedOverTheWholeDomain()
    {
        // Expected: every weighted value maps strictly higher than any lower one, the exact score stays inside [0, 50]
        // and the displayed integer never decreases.
        double previous = double.NegativeInfinity; var previousDisplay = 0;
        for (var w = -.5; w <= 1.5; w += .0005)
        {
            var value = ContextualPickScoreCalibration.Calibrate(w);
            Assert.True(value > previous, $"not strictly increasing at w={w}");
            Assert.InRange(ContextualPickScoreCalibration.ExactScore(value), 0, 50);
            var display = ContextualPickScoreCalibration.DisplayScore(value);
            Assert.InRange(display, previousDisplay, 50);
            previous = value; previousDisplay = display;
        }
    }

    [Fact] public void TailsKeepDistinctValuesOrderedWithoutClipping()
    {
        // Expected: exceptional and irrelevant cards keep distinct, ordered exact scores; 50 and 0 are approached, not clipped.
        var high = new[] { .93, .96, .99, 1.0 }.Select(w => ContextualPickScoreCalibration.ExactScore(ContextualPickScoreCalibration.Calibrate(w))).ToArray();
        var low = new[] { 0, .03, .06 }.Select(w => ContextualPickScoreCalibration.ExactScore(ContextualPickScoreCalibration.Calibrate(w))).ToArray();
        Assert.True(high.Zip(high.Skip(1)).All(p => p.First < p.Second) && high[^1] < 50);
        Assert.True(low.Zip(low.Skip(1)).All(p => p.First < p.Second) && low[0] > 0);
    }

    [Fact] public void FinalCalibrationStaysTheFrozenV14MapAndConfigurationDelegatesToIt()
    {
        // Expected: phase 5 measured the v1.4 map and kept it, and phase 6 (model v1.5) leaves it frozen:
        // V = SoftLimit(.5 + 1.4(w − .5), knee .45), score = 50V.
        var configuration = new ContextualPickScoreConfiguration();
        Assert.Equal(1.4, configuration.CalibrationGain); Assert.Equal(.45, configuration.CalibrationSoftKnee);
        Assert.Equal("contextual-pick-score-v1.6", configuration.ModelVersion);
        Assert.Equal(.5, ContextualPickScoreCalibration.Calibrate(.5), 12);
        Assert.Equal(.64, ContextualPickScoreCalibration.Calibrate(.6), 12);
        Assert.Equal(32, ContextualPickScoreCalibration.DisplayScore(.64));
        for (var w = 0d; w <= 1; w += .01) Assert.Equal(ContextualPickScoreCalibration.Calibrate(w), configuration.Calibrate(w));
    }

    [Theory]
    [InlineData(0, PickScoreTier.VeryWeak)] [InlineData(14, PickScoreTier.VeryWeak)]
    [InlineData(15, PickScoreTier.Marginal)] [InlineData(24, PickScoreTier.Marginal)]
    [InlineData(25, PickScoreTier.Solid)] [InlineData(34, PickScoreTier.Solid)]
    [InlineData(35, PickScoreTier.Strong)] [InlineData(39, PickScoreTier.Strong)]
    [InlineData(40, PickScoreTier.Excellent)] [InlineData(44, PickScoreTier.Excellent)]
    [InlineData(45, PickScoreTier.Premium)] [InlineData(50, PickScoreTier.Premium)]
    [InlineData(null, PickScoreTier.Unavailable)] [InlineData(-1, PickScoreTier.Unavailable)] [InlineData(51, PickScoreTier.Unavailable)]
    public void TierBoundariesFollowTheScoreSemantics(int? score, PickScoreTier expected) => Assert.Equal(expected, PickScoreTierThresholds.Classify(score));

    [Fact] public void TierRangesPartitionTheScaleInOrder()
    {
        // Expected: each score 0–50 belongs to exactly one tier, tiers are contiguous and ordered weak → premium.
        var tiers = Enum.GetValues<PickScoreTier>().Where(t => t != PickScoreTier.Unavailable).ToArray();
        var next = 0;
        foreach (var tier in tiers)
        {
            var (low, high) = PickScoreTierThresholds.Range(tier)!.Value;
            Assert.Equal(next, low); Assert.True(high >= low);
            Assert.All(Enumerable.Range(low, high - low + 1), s => Assert.Equal(tier, PickScoreTierThresholds.Classify(s)));
            next = high + 1;
        }
        Assert.Equal(51, next); Assert.Null(PickScoreTierThresholds.Range(PickScoreTier.Unavailable));
    }

    [Fact] public void FirstPickAnchorsSeparateAverageAboveAverageAndBombs()
    {
        // Expected (anchors A–C): with no context, an average card is mid-20s, +3 pp is low/mid 30s, a +10 pp bomb is
        // 43–49 and a +13 pp bomb stays distinguishable from it without reaching 50.
        var average = One(E("average", "{2}{G}", 3, .56)); var good = One(E("good", "{2}{G}", 3, .59));
        var bomb = One(E("bomb", "{3}{G}", 4, .67, games: 3000)); var bigger = One(E("bigger", "{3}{G}", 4, .70, games: 3000));
        InAnchor("A", average); InAnchor("B", good); InAnchor("C", bomb); InAnchor("C", bigger);
        Assert.True(bigger.ContextualValue > bomb.ContextualValue);
        Assert.Equal(PickScoreTier.Solid, average.Tier); Assert.Equal(PickScoreTier.Premium, bomb.Tier);
    }

    [Fact] public void LateAnchorsRewardRealDeckImprovementAndDemoteCardsThatMissTheDeck()
    {
        // Expected (anchors E, F, J): late in pack 3, a slightly better on-colour playable that makes the deck is solid,
        // a clear upgrade is strong, and a card that does not make the deck sits well below both.
        var deck = Deck();
        var solid = One(E("solid", "{2}{G}", 3, .57), deck); var strong = One(E("strong", "{2}{G}", 3, .61), deck);
        var bench = One(E("bench", "{2}{G}", 3, .53), deck);
        InAnchor("E", solid); InAnchor("F", strong); InAnchor("J", bench);
        Assert.True(bench.ContextualValue < solid.ContextualValue && solid.ContextualValue < strong.ContextualValue);
    }

    [Fact] public void StructuralNeedImprovesAnAverageCardWithoutMakingItPremium()
    {
        // Expected (anchor G): with one early play, an average 2-drop gains meaningfully over the neutral 25 but stays
        // well below premium.
        var filler = One(E("need", "{1}{G}", 2, .56), Deck(early: 1, creatures: 24));
        InAnchor("G", filler); Assert.True(Display(filler) > 25); Assert.True(filler.Tier < PickScoreTier.Excellent);
    }

    [Fact] public void OffColourExcellenceIsCompetitiveEarlyAndDemotedLate()
    {
        // Expected (anchors H, I): an excellent off-colour card stays competitive at P1P3 and loses that standing in
        // pack 3, ranking below an on-colour playable that makes the deck. Phase 6 quality relevance closes the v1.4 gap
        // (about 24): with no realistic build path it now lands in anchor I's 10–20.
        var early = One(E("offP1", "{2}{U}", 3, .65), [E("p0", "{2}{G}", 3), E("p1", "{2}{R}", 3)]);
        var late = One(E("offP3", "{2}{U}", 3, .65), Deck());
        var playable = One(E("playable", "{2}{G}", 3, .57), Deck());
        InAnchor("H", early);
        Assert.True(Display(early) - Display(late) >= 15);
        Assert.True(late.ContextualValue < playable.ContextualValue);
        InAnchor("I", late);
    }

    [Fact] public void AnAverageCardMeansTheSameEarlyAndAtTheLateCutLine()
    {
        // Expected: one global scale. An average card with no context at P1P1 and an average sidegrade exactly at the
        // late cut line both read as "solid" and stay within a few points of the neutral 25.
        var early = One(E("avg-early", "{2}{G}", 3, .56));
        var late = One(E("avg-late", "{2}{G}", 3, .56), Deck());
        Assert.Equal(PickScoreTier.Solid, early.Tier); Assert.Equal(PickScoreTier.Solid, late.Tier);
        Assert.InRange(Display(late) - Display(early), -3, 5);
    }

    [Fact] public void MatchedPairsIsolateEachContextFactor()
    {
        // Expected: with identical GIH, a needed 2-drop beats a redundant 4-drop and removal beats an ordinary spell by a
        // few points (need refines, it does not dominate); colour matters a lot late and not at all at P1P1.
        var curve = Score([E("two", "{1}{G}", 2), E("four", "{3}{G}", 4)], Deck(early: 2, creatures: 23));
        var removal = Score([E("removal", "{2}{R}", 3, type: "Instant", oracle: "Destroy target creature."),
            E("spell", "{2}{R}", 3, type: "Instant", oracle: "Draw a card.")], Deck());
        var lateColour = Score([E("on60", "{2}{G}", 3, .60), E("off60", "{2}{U}", 3, .60)], Deck());
        var earlyColour = Score([E("on60e", "{2}{G}", 3, .60), E("off60e", "{2}{U}", 3, .60)]);
        int Delta(ContextualPickScore[] pair) => Display(pair[0]) - Display(pair[1]);
        Assert.InRange(Delta(curve), 1, 8); Assert.InRange(Delta(removal), 1, 8);
        Assert.True(Delta(lateColour) >= 15); Assert.Equal(0, Delta(earlyColour));
    }

    [Fact] public void EstimatedQualityIsCalibratedExactlyLikeDirectQuality()
    {
        // Expected: calibration and tier depend only on the contextual value. An estimated card (no GIH) uses neutral Q,
        // so at P1P1 it scores exactly like a direct card at the environment baseline, with no EST penalty.
        var scores = Score([E("estimated", "{2}{G}", 3, rate: null), E("direct", "{2}{G}", 3, .56)]);
        var estimated = scores.Single(s => s.CardIdentifier.Value == "estimated"); var direct = scores.Single(s => s.CardIdentifier.Value == "direct");
        Assert.True(estimated.IsQualityEstimated); Assert.False(direct.IsQualityEstimated);
        Assert.Equal(direct.Score0To50, estimated.Score0To50); Assert.Equal(direct.Tier, estimated.Tier);
        var relabelled = direct with { Availability = PickScoreAvailability.EstimatedMissingStatistics };
        Assert.Equal(direct.Score0To50, relabelled.Score0To50); Assert.Equal(direct.Tier, relabelled.Tier);
    }

    [Fact] public void PrintedRarityNeverEntersTheScoreOrTier()
    {
        // Expected: the same card as Common and as Mythic produces the identical exact value, integer and tier.
        var common = One(E("card", "{2}{G}", 3, .63, rarity: CardRarity.Common), Deck());
        var mythic = One(E("card", "{2}{G}", 3, .63, rarity: CardRarity.Mythic), Deck());
        Assert.Equal(common.ContextualValue, mythic.ContextualValue); Assert.Equal(common.Tier, mythic.Tier);
    }

    [Fact] public void ReplayIsDeterministicAndDisplayNeverContradictsRank()
    {
        // Expected: scoring the same pack twice gives bit-identical values, and a higher-ranked card never shows a
        // lower integer than a card ranked below it.
        Entry[] pack = [E("a", "{2}{G}", 3, .61), E("b", "{1}{G}", 2, .57), E("c", "{2}{U}", 3, .66), E("d", "{2}{R}", 3, .54), E("e", "{3}{G}", 4, .58)];
        var first = Score(pack, Deck()); var second = Score(pack, Deck());
        Assert.Equal(first.Select(s => s.ContextualValue), second.Select(s => s.ContextualValue));
        var ranked = first.OrderBy(s => s.CurrentPackRank).ToArray();
        Assert.All(ranked.Zip(ranked.Skip(1)), p => Assert.True(p.First.Score0To50 >= p.Second.Score0To50));
    }
}
