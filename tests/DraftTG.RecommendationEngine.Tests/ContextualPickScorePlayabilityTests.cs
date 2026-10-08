using DraftTG.Domain;

namespace DraftTG.RecommendationEngine.Tests;

/// <summary>Phase 6: playability-gated quality and the expected-future-replacement cut line. Each test states the
/// expected drafting behaviour first; pools are red-green unless stated, padded with white/blue/black cards spread so no
/// padding pair (or padding plus green) reaches the red-green spell count.</summary>
public sealed class ContextualPickScorePlayabilityTests
{
    private static readonly SetArchetypeProfile Profile = new("WOE", "https://example.test/roles",
        new[] { "WU", "UB", "BR", "RG", "GW", "WB", "UR", "BG", "RW", "GU" }
            .Select(p => new ArchetypeDefinition("WOE", ArchetypeColorPair.Create(p), p, "Fixture")));
    private static readonly LimitedCardStatisticsCatalog Environment = new([new(CardIdentifier.Create("environment"), 1000000, GameInHandWinRate: .56)]);
    private static readonly ContextualPickScoreConfiguration Configuration = new();

    private sealed record Entry(Card Card, double? Rate, int Games = 10000);
    private static Entry E(string id, string cost, int mv, double? rate = .56, string type = "Creature", int games = 10000, IEnumerable<ManaKind>? produced = null)
    {
        var colors = cost.Where("WUBRG".Contains).Distinct().Select(c => (MagicColor)"WUBRG".IndexOf(c));
        return new(new Card(CardIdentifier.Create(id), id, new(colors), CardRarity.Common, CardSetCode.Create("WOE"), CollectorNumber.Create(id))
        { GameplayMetadata = new(mv, type == "Land" ? "" : cost, type, producedMana: produced) }, rate, games);
    }
    private static IEnumerable<Entry> Padding(int count) => Enumerable.Range(0, count).Select(i => E($"pad{i}", "{2}{" + "WUB"[i % 3] + "}", 3, .55));
    /// <summary>`count` red-green spells at `rate` (colours alternating, every fourth a 2-drop), padded to `picks`.</summary>
    private static Entry[] Pool(int count, int picks, double rate = .56)
    {
        var spells = Enumerable.Range(0, count).Select(i => E($"rg{i}", (i % 4 == 0 ? "{1}" : "{2}") + (i % 2 == 0 ? "{R}" : "{G}"), i % 4 == 0 ? 2 : 3, rate)).ToArray();
        return spells.Concat(Padding(Math.Max(0, picks - count))).ToArray();
    }
    private static ContextualPickScore[] Score(IReadOnlyList<Entry> pack, IReadOnlyList<Entry>? pool = null, int? completed = null)
    {
        pool ??= [];
        var picks = completed ?? pool.Count;
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
    private static ContextualPickScore One(Entry card, IReadOnlyList<Entry>? pool = null, int? completed = null) => Score([card, E("filler", "{2}{W}", 3, .55)], pool, completed)[0];
    private static int Display(ContextualPickScore score) => score.Score0To50!.Value;
    private static Entry BlueBomb(string id = "blue", double rate = .65) => E(id, "{2}{U}", 3, rate);

    // ---- Formulas -------------------------------------------------------------------------------------------------

    [Fact] public void FutureFillerLevelIsContinuousAndRisesWithCoverage()
    {
        // Expected: baseline − 3 pp when supply just covers the contested slots, − 1 pp at 3×, + 0.8 pp at ≥ 10×; no steps.
        Assert.Equal(.53, Configuration.FutureFillerLevel(.56, 1), 10);
        Assert.Equal(.55, Configuration.FutureFillerLevel(.56, 3), 10);
        Assert.Equal(.568, Configuration.FutureFillerLevel(.56, 10), 10);
        Assert.Equal(.568, Configuration.FutureFillerLevel(.56, 50), 10);
        double previous = double.NegativeInfinity;
        for (var c = 0d; c <= 12; c += .01)
        {
            var level = Configuration.FutureFillerLevel(.56, c);
            Assert.True(level >= previous && level - previous < .001 || double.IsNegativeInfinity(previous));
            previous = level;
        }
    }

    [Fact] public void FinalMarginalCardIsAnOrderStatisticThatMovesSmoothlyWithSupply()
    {
        // Expected: with enough later playables every contested slot gets one (filler level); with fewer, the final
        // marginal card is the weakest contested card later picks cannot replace, and an unfillable open slot is
        // emergency filler. Non-decreasing and continuous in supply.
        double[] weak = [.50, .52, .48];
        Assert.Equal(Configuration.FutureFillerLevel(.56, 2), Configuration.FinalMarginalCard(.56, weak, 1, 8), 10);
        Assert.Equal(.48, Configuration.FinalMarginalCard(.56, weak, 1, 1), 10);
        Assert.Equal(.46, Configuration.FinalMarginalCard(.56, weak, 1, 0), 10);
        Assert.Equal(.50, Configuration.FinalMarginalCard(.56, weak, 1, 2), 10);
        var previous = Configuration.FinalMarginalCard(.56, weak, 1, 0);
        for (var s = 0d; s <= 12; s += .01)
        {
            var level = Configuration.FinalMarginalCard(.56, weak, 1, s);
            Assert.True(level >= previous - 1e-12 && level - previous < .021, $"supply {s}");
            previous = level;
        }
    }

    [Fact] public void QualityRelevanceRampsWithProgressAndNeverReducesWeakness()
    {
        // Expected: no reduction through 10 picks; full ramp to the .20 floor by the late anchor when no build can play
        // the card; more playability never lowers R; only quality above neutral is scaled.
        Assert.Equal(1, Configuration.QualityRelevance(0, 10d / 42), 10);
        Assert.Equal(.2, Configuration.QualityRelevance(0, 1), 10);
        Assert.Equal(1, Configuration.QualityRelevance(1, 1), 10);
        for (var p = 0d; p <= 1; p += .05)
            Assert.True(Configuration.QualityRelevance(p, .8) <= Configuration.QualityRelevance(Math.Min(1, p + .05), .8) + 1e-12);
        for (var t = 0d; t < 1; t += .01)
            Assert.True(Configuration.QualityRelevance(0, t + .01) <= Configuration.QualityRelevance(0, t) + 1e-12);
        Assert.Equal(.3, ContextualPickScoreConfiguration.EffectiveQuality(.3, .2), 10);
        Assert.Equal(.6, ContextualPickScoreConfiguration.EffectiveQuality(1, .2), 10);
        Assert.True(ContextualPickScoreConfiguration.EffectiveQuality(.9, .2) > ContextualPickScoreConfiguration.EffectiveQuality(.8, .2));
    }

    // ---- Part A: playability-gated quality --------------------------------------------------------------------------

    [Fact] public void A_FirstPickOffColourBombIsNotSuppressed()
    {
        // Expected: at P1P1 every colour is plausible, so R is 1 and a blue bomb scores like an equal red bomb.
        var scores = Score([BlueBomb(), E("red", "{2}{R}", 3, .65)]);
        Assert.All(scores, s => Assert.Equal(1, s.QualityRelevance));
        Assert.Equal(scores[1].ContextualValue, scores[0].ContextualValue);
    }

    [Fact] public void B_EarlyOffColourBombStaysHighlyCompetitive()
    {
        // Expected: at P1P3 with two red-green cards, the blue bomb keeps full relevance and stays 40+.
        var bomb = One(BlueBomb(), Pool(2, 2));
        Assert.Equal(1, bomb.QualityRelevance); Assert.True(Display(bomb) >= 40);
    }

    [Fact] public void C_AlternatePairBombStaysCompetitiveWhileADistantColourFades()
    {
        // Expected: mid pack 2, black-green established. A red bomb that makes a close red-green build viable keeps most
        // relevance and scores 40+; with almost no red, the same bomb is less relevant and scores clearly lower.
        Entry[] Mix(int b, int g, int r, double redRate) => Enumerable.Range(0, b).Select(i => E($"b{i}", "{2}{B}", 3, .565))
            .Concat(Enumerable.Range(0, g).Select(i => E($"g{i}", "{2}{G}", 3, .565)))
            .Concat(Enumerable.Range(0, r).Select(i => E($"r{i}", "{2}{R}", 3, redRate))).ToArray();
        var near = One(E("red-bomb", "{2}{R}", 3, .66), Mix(6, 7, 5, .57));
        var far = One(E("red-bomb", "{2}{R}", 3, .66), Mix(8, 9, 1, .53));
        Assert.True(near.QualityRelevance >= .9); Assert.True(Display(near) >= 40);
        Assert.True(far.QualityRelevance < near.QualityRelevance); Assert.True(Display(far) < Display(near) - 5);
    }

    [Fact] public void D_LateOffColourBombWithNoBuildPathLandsInTheIntendedRange()
    {
        // Expected (anchor I): pack 3, committed red-green, no blue in any viable build: excellent and bomb-level blue
        // cards land 10–20, still above a useless blue card, and keep their intrinsic Q.
        var pool = Pool(24, 33);
        var excellent = One(BlueBomb("excellent", .65), pool); var bomb = One(BlueBomb("bomb", .68), pool);
        var useless = One(BlueBomb("useless", .45), pool);
        Assert.InRange(Display(excellent), 10, 20); Assert.InRange(Display(bomb), 10, 20);
        Assert.True(useless.ContextualValue < excellent.ContextualValue && excellent.ContextualValue < bomb.ContextualValue);
        Assert.Equal(0, excellent.DeckImpact.Playability);
        Assert.True(excellent.QualityComponent > .9); Assert.True(excellent.EffectiveQualityComponent < .65);
        var early = One(BlueBomb("excellent", .65), Pool(2, 2));
        Assert.Equal(early.QualityComponent, excellent.QualityComponent);
    }

    [Fact] public void E_SplashableCardWithRealFixingIsNotTreatedAsUnplayable()
    {
        // Expected: a single-pip blue card with three drafted blue-producing lands earns splash playability and scores
        // above the same card without fixing; a double-pip card gets no splash credit.
        Entry[] WithLands(int lands) => Pool(24, 24).Concat(Enumerable.Range(0, lands).Select(i => E($"ur{i}", "", 0, .55, "Land", produced: [ManaKind.Blue, ManaKind.Red])))
            .Concat(Padding(9 - lands)).ToArray();
        var fixedSplash = One(E("splash", "{3}{U}", 4, .65), WithLands(3));
        var noFixing = One(E("splash", "{3}{U}", 4, .65), WithLands(0));
        var doublePip = One(E("double", "{1}{U}{U}", 3, .65), WithLands(3));
        Assert.True(fixedSplash.DeckImpact.Playability > .5); Assert.True(fixedSplash.QualityRelevance > noFixing.QualityRelevance);
        Assert.True(Display(fixedSplash) > Display(noFixing));
        Assert.Equal(0, doublePip.DeckImpact.Playability);
    }

    [Fact] public void OffColourScoreFallsSmoothlyAcrossTheDraft()
    {
        // Expected: with a balanced red-green pool (no blue build ever viable), an excellent blue card's score never
        // rises as the draft advances and falls at most 7 points per four picks (no Pack 1 / Pack 3 boundary).
        var scores = new[] { 2, 6, 10, 14, 18, 22, 26, 30, 34, 38 }.Select(p => Display(One(BlueBomb(), Pool(p, p)))).ToArray();
        Assert.All(scores.Zip(scores.Skip(1)), p => Assert.InRange(p.First - p.Second, 0, 7));
        Assert.True(scores[0] - scores[^1] >= 20);
    }

    [Fact] public void EstimatedQualityIsUnaffectedByRelevance()
    {
        // Expected: an estimated (neutral) Q has no upside to scale, so its composed quality stays exactly neutral.
        var estimated = One(E("estimated", "{2}{U}", 3, rate: null), Pool(24, 33));
        Assert.True(estimated.IsQualityEstimated); Assert.Equal(.5, estimated.EffectiveQualityComponent);
    }

    // ---- Part B: continuous deck fit against the real or expected-future cut line -----------------------------------

    [Fact] public void FG_CardsStraddlingTheRealCutGetNearlyIdenticalDeckNeed()
    {
        // Expected: full deck at the last picks; candidates 0.05 pp above and below the cut differ only marginally.
        var pool = Pool(25, 41);
        var above = One(E("above", "{2}{G}", 3, .5605), pool); var below = One(E("below", "{2}{G}", 3, .5595), pool);
        Assert.InRange(above.DeckNeedComponent - below.DeckNeedComponent, 0, .03);
        Assert.InRange(Display(above) - Display(below), 0, 1);
    }

    [Fact] public void HI_ClearUpgradesAreStrongAndClearMissesAreBelowNeutral()
    {
        // Expected: 3 pp above the weakest replaceable card is a strong deck fit; 3 pp below the cut is below neutral.
        var pool = Pool(25, 41);
        var upgrade = One(E("upgrade", "{2}{G}", 3, .59), pool); var miss = One(E("miss", "{2}{G}", 3, .53), pool);
        Assert.True(upgrade.DeckNeedComponent >= .8); Assert.True(miss.DeckNeedComponent < .45);
    }

    [Fact] public void JK_OpenSlotFillerGainsValueOnlyAsPicksRunOut()
    {
        // Expected: five playables short; with many picks left a mediocre filler is about neutral, with very few picks
        // left the same filler is clearly more valuable.
        var many = One(E("filler", "{2}{G}", 3, .55), Pool(18, 20));
        var few = One(E("filler", "{2}{G}", 3, .55), Pool(18, 39));
        Assert.InRange(Display(many), 20, 27); Assert.True(Display(few) >= Display(many) + 6);
        Assert.Contains(many.DeckImpact.Reasons, r => r.StartsWith("+ Fills an open slot", StringComparison.Ordinal) || r.StartsWith("- Fills an open slot", StringComparison.Ordinal));
        Assert.Contains(few.DeckImpact.Reasons, r => r.StartsWith("+ Deck still needs", StringComparison.Ordinal));
    }

    [Fact] public void UrgencyNeverFallsAsRemainingPicksDisappear()
    {
        // Expected: for the same pool (five playables short), the virtual cut line never rises and D never falls as
        // the draft position advances and remaining picks disappear.
        var pool = Pool(18, 20);
        var results = new[] { 20, 24, 28, 32, 36, 39, 41 }.Select(p => One(E("filler", "{2}{G}", 3, .55), pool, p)).ToArray();
        Assert.All(results.Zip(results.Skip(1)), p =>
        {
            Assert.True(p.Second.DeckImpact.ReplacementLevel <= p.First.DeckImpact.ReplacementLevel + 1e-12);
            Assert.True(p.Second.DeckNeedComponent >= p.First.DeckNeedComponent - 1e-12);
            Assert.True(p.Second.DeckImpact.SupplyUrgency >= p.First.DeckImpact.SupplyUrgency - 1e-12);
        });
    }

    [Theory]
    [InlineData(.52)] [InlineData(.56)]
    public void FillingTheLastSlotIsContinuousWithReplacingTheWeakestCard(double poolRate)
    {
        // Expected: the same average candidate scores about the same whether the projected deck has 22 or 23 spells.
        var open = One(E("candidate", "{2}{G}", 3, .56), Pool(22, 30, poolRate));
        var full = One(E("candidate", "{2}{G}", 3, .56), Pool(23, 30, poolRate));
        Assert.InRange(Math.Abs(Display(open) - Display(full)), 0, 2);
    }

    [Fact] public void AnOpenSlotIsNotFreeMembership()
    {
        // Expected: a solid 22-spell deck with many picks left; an average card fills the open slot but is measured
        // against what later picks should supply, so it reads solid/marginal rather than strong.
        var average = One(E("average", "{2}{G}", 3, .56), Pool(22, 30, .58));
        Assert.True(average.DeckImpact.ComparedWithFutureFiller); Assert.Null(average.DeckImpact.RealCutline);
        Assert.True(Display(average) < 30);
        Assert.True(average.DeckImpact.ReplacementLevel > .55);
    }

    [Fact] public void AWeakCardLaterPicksWillReplaceIsNotTheRealCut()
    {
        // Expected: with many picks left, replacing a single 48% card is measured against the expected later filler, so
        // the upgrade is worth less than at the end of the draft when that 48% card would really stay.
        Entry[] Deck(int picks) => Pool(22, 22).Prepend(E("weak", "{2}{R}", 3, .48)).Concat(Padding(picks - 23)).ToArray();
        var early = One(E("upgrade", "{2}{G}", 3, .57), Deck(28)); var late = One(E("upgrade", "{2}{G}", 3, .57), Deck(41));
        Assert.True(early.DeckImpact.ComparedWithFutureFiller); Assert.False(late.DeckImpact.ComparedWithFutureFiller);
        Assert.Equal("weak", late.DeckImpact.DisplacedCard?.Value);
        Assert.True(late.DeckNeedComponent > early.DeckNeedComponent + .05);
    }

    [Fact] public void LMN_NeedIsCreditedInProportionToQuality()
    {
        // Expected: a deck with no early plays. A terrible needed 2-drop gets some need credit but stays far from
        // Strong; an average one beats an equal-quality 4-drop; a strong one receives both quality and need.
        var noEarly = Enumerable.Range(0, 24).Select(i => E($"f{i}", i % 2 == 0 ? "{3}{R}" : "{3}{G}", 4, .57)).Concat(Padding(9)).ToArray();
        ContextualPickScore[] Pair(double rate) => Score([E($"two{rate}", "{1}{G}", 2, rate), E($"four{rate}", "{3}{R}", 4, rate)], noEarly);
        var terrible = Pair(.50); var average = Pair(.56); var strong = Pair(.61);
        Assert.True(Display(terrible[0]) > Display(terrible[1])); Assert.True(Display(terrible[0]) < 25);
        Assert.True(Display(average[0]) >= Display(average[1]) + 4);
        Assert.True(Display(strong[0]) >= 40); Assert.True(strong[0].ContextualValue > average[0].ContextualValue);
    }

    [Fact] public void BetterQualityNeverLowersDeckNeedOrScore()
    {
        // Expected: monotone in GIH both against a real cut (full deck) and against the virtual cut (open slots).
        foreach (var pool in new[] { Pool(25, 41), Pool(18, 28) })
        {
            var results = Enumerable.Range(0, 13).Select(i => One(E($"g{i}", "{2}{G}", 3, .50 + i * .01), pool)).ToArray();
            Assert.All(results.Zip(results.Skip(1)), p =>
            {
                Assert.True(p.Second.DeckNeedComponent >= p.First.DeckNeedComponent - 1e-12);
                Assert.True(p.Second.ContextualValue > p.First.ContextualValue);
            });
        }
    }
}
