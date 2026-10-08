using DraftTG.Domain;

namespace DraftTG.RecommendationEngine.Tests;

/// <summary>Stabilization phase 1: graded deck fit. Each test states the expected drafting behaviour first;
/// assertions check that behaviour (continuity, ordering, bounded sensitivity), not incidental numbers.</summary>
public sealed class ContextualPickScoreDeckFitTests
{
    private static readonly SetArchetypeProfile Profile = new("WOE", "https://example.test/roles",
        new[] { "WU", "UB", "BR", "RG", "GW", "WB", "UR", "BG", "RW", "GU" }
            .Select(p => new ArchetypeDefinition("WOE", ArchetypeColorPair.Create(p), p, "Fixture")));
    private static readonly LimitedCardStatisticsCatalog Environment = new([new(CardIdentifier.Create("environment"), 1000000, GameInHandWinRate: .56)]);

    private sealed record Entry(Card Card, double Rate);
    private static Entry E(string id, string cost, int mv, double rate, string type = "Creature")
    {
        var colors = cost.Where("WUBRG".Contains).Distinct().Select(c => (MagicColor)"WUBRG".IndexOf(c));
        return new(new Card(CardIdentifier.Create(id), id, new(colors), CardRarity.Common, CardSetCode.Create("WOE"), CollectorNumber.Create(id))
        { GameplayMetadata = new(mv, cost, type) }, rate);
    }
    private static IEnumerable<Entry> Many(string prefix, string cost, int mv, double rate, int count, string type = "Creature") =>
        Enumerable.Range(0, count).Select(i => E($"{prefix}{i}", cost, mv, rate, type));

    /// <summary>P3P8 committed red-green: 27 on-colour playables at 57.0%, 8 off-colour white cards. The S18 audit fixture.</summary>
    private static Entry[] DeepRedGreen(double rate = .57) =>
        Many("gc", "{1}{G}", 2, rate, 3).Concat(Many("gc3", "{2}{G}", 3, rate, 6)).Concat(Many("rc", "{1}{R}", 2, rate, 3))
            .Concat(Many("rc4", "{3}{R}", 4, rate, 5)).Concat(Many("gs", "{1}{G}", 2, rate, 5, "Instant"))
            .Concat(Many("rs", "{1}{R}", 2, rate, 5, "Instant")).Concat(Many("w", "{2}{W}", 3, .55, 8)).ToArray();

    private static ContextualPickScoreResult Score(IEnumerable<Entry> pack, IReadOnlyList<Entry> pool, int? completed = null)
    {
        var packEntries = pack.ToArray();
        var picks = completed ?? pool.Count;
        var snapshot = new DraftSnapshot(new(new(PackNumber.Create(picks / 14 + 1), PickNumber.Create(picks % 14 + 1)), packEntries.Select(e => e.Card.Identifier)),
            new(pool.Select((e, i) => new DraftPick(new(PackNumber.Create(i / 14 + 1), PickNumber.Create(i % 14 + 1)), e.Card.Identifier))), DraftFormat.BestOfOne);
        var all = pool.Concat(packEntries).DistinctBy(e => e.Card.Identifier).ToArray();
        var catalog = new CardCatalog(all.Select(e => e.Card));
        var stats = new LimitedCardStatisticsCatalog(all.Select(e => new LimitedCardStatistics(e.Card.Identifier, 10000, GameInHandWinRate: e.Rate, AverageLastSeenAt: 4)));
        var statistical = new StatisticalRecommendationEngine().Recommend(snapshot.CurrentPack, stats, Environment);
        var colors = new ContextualRecommendationEngine().Recommend(snapshot, catalog, statistical);
        var lane = new LaneContextualRecommendationEngine().Recommend(snapshot, colors, DraftPackObservationHistory.Empty, LimitedStatisticsFormat.PremierDraft);
        var archetype = new ArchetypeRecommendationEngine().Recommend(lane, Profile, new("WOE", LimitedStatisticsFormat.PremierDraft));
        return new ContextualPickScoreEngine().Recommend(snapshot, catalog, archetype, stats, Environment);
    }
    private static ContextualPickScore One(Entry candidate, IReadOnlyList<Entry> pool, int? completed = null) =>
        Score([candidate, E("comparison", "{2}{U}", 3, .55)], pool, completed).Cards[0];

    [Fact] public void TinyRateChangeAtTheAuditCutLineMovesTheScoreByAtMostOnePoint()
    {
        // Expected: 57.00% vs 57.05% is far inside GIH noise, so the recommendation must not change materially.
        // Pre-fix this exact fixture moved from 12 to 31.
        var pool = DeepRedGreen();
        var below = One(E("candidate", "{2}{R}", 3, .5700), pool);
        var above = One(E("candidate", "{2}{R}", 3, .5705), pool);
        Assert.InRange(Math.Abs(above.Score0To50!.Value - below.Score0To50!.Value), 0, 1);
        Assert.InRange(Math.Abs(above.DeckNeedComponent - below.DeckNeedComponent), 0, .02);
    }

    [Fact] public void CardsJustAboveAndBelowTheCutLineScoreAlike()
    {
        // Expected: ±0.25 pp around the cut line are near-equivalent decisions, whichever side the optimizer picks.
        var pool = DeepRedGreen();
        var below = One(E("candidate", "{2}{R}", 3, .5675), pool);
        var above = One(E("candidate", "{2}{R}", 3, .5725), pool);
        Assert.False(below.DeckImpact.MakesProjectedDeck); Assert.True(above.DeckImpact.MakesProjectedDeck);
        Assert.InRange(above.Score0To50!.Value - below.Score0To50!.Value, 0, 4);
        Assert.InRange(below.DeckImpact.MembershipValue, .3, .5); Assert.InRange(above.DeckImpact.MembershipValue, .5, .7);
    }

    [Fact] public void LateSweepIsMonotoneWithoutJumpsAndStillSeparatesStrongFromWeak()
    {
        // Expected: across 53%–62% each 0.1 pp step changes at most one displayed point, higher GIH never scores
        // lower, and a clear upgrade (60%) remains far above a sideboard card (55%).
        var pool = DeepRedGreen();
        int? previous = null;
        for (var rate = .530; rate <= .6201; rate += .001)
        {
            var score = One(E("candidate", "{2}{R}", 3, rate), pool).Score0To50!.Value;
            if (previous is { } last) Assert.InRange(score - last, 0, 1);
            previous = score;
        }
        Assert.True(One(E("candidate", "{2}{R}", 3, .60), pool).Score0To50 - One(E("candidate", "{2}{R}", 3, .55), pool).Score0To50 >= 12);
    }

    [Fact] public void PowerfulOnColorCardLateClearsTheCutLineAndKeepsAHighScore()
    {
        // Expected: a 66% on-colour card is a large upgrade over a 57% deck, so it keeps strong deck fit and a high score.
        var result = One(E("bomb", "{3}{G}", 4, .66), DeepRedGreen());
        Assert.True(result.DeckImpact.CutlineMargin > .07); Assert.True(result.DeckImpact.MembershipValue > .99);
        Assert.True(result.DeckNeedComponent >= .7); Assert.True(result.Score0To50 >= 40);
    }

    [Fact] public void PowerfulOffColorCardLateHasNoDeckFitAndTrailsAnOnColorUpgrade()
    {
        // Expected: a 67% blue card cannot enter a committed red-green maindeck, so it gets no deck fit and ranks
        // below a genuine on-colour upgrade, while staying above clearly irrelevant cards.
        var result = Score([E("blue-bomb", "{3}{U}", 4, .67), E("upgrade", "{2}{G}", 3, .61), E("chaff", "{2}{G}", 3, .50)], DeepRedGreen()).Cards;
        Assert.Equal(new ContextualPickScoreConfiguration().DeckFitFloor, result[0].DeckNeedComponent, 12); Assert.Null(result[0].DeckImpact.CutlineMargin);
        Assert.True(result[1].Score0To50 > result[0].Score0To50); Assert.True(result[0].Score0To50 > result[2].Score0To50);
    }

    [Fact] public void AverageCardFillingAGenuineNeedOutscoresTheSameCardAsASidegrade()
    {
        // Expected: an average two-drop matters more when the deck has no early plays at all than when it would just
        // replace an equal card. With needs met, it is a marginal sidegrade.
        var needy = Many("g4", "{3}{G}", 4, .56, 14).Concat(Many("r4", "{3}{R}", 4, .56, 13)).Concat(Many("w", "{2}{W}", 3, .55, 8)).ToArray();
        var served = Many("g2", "{1}{G}", 2, .56, 14).Concat(Many("r2", "{1}{R}", 2, .56, 13)).Concat(Many("w", "{2}{W}", 3, .55, 8)).ToArray();
        var needed = One(E("two-drop", "{1}{G}", 2, .56), needy);
        var redundant = One(E("two-drop", "{1}{G}", 2, .56), served);
        Assert.True(needed.DeckImpact.EarlyPlayDelta > 0); Assert.True(needed.DeckNeedComponent >= .65);
        // Phase 4 scale: an exact sidegrade is neutral (about .55); the needed card is clearly above it.
        Assert.InRange(redundant.DeckNeedComponent, .45, .6);
        Assert.True(needed.Score0To50 - redundant.Score0To50 >= 5);
    }

    [Fact] public void AverageCardWithLittleMarginalValueGetsNoMembershipBonus()
    {
        // Expected: in a deep deck of better cards, an average card is about one point below the cut line: it should
        // read as marginal-to-sideboard, not as "makes the deck" worth +18 points.
        var result = One(E("average", "{2}{G}", 3, .56), DeepRedGreen());
        // Phase 4 scale (neutral about .55): below neutral, and below an average card's context-free 25.
        Assert.InRange(result.DeckNeedComponent, 0, .45); Assert.True(result.Score0To50 <= 23);
    }

    [Fact] public void PackOnePickOneHasNoDeckProjectionAndPureQualityOrdering()
    {
        // Expected: with no pool there is nothing to fit; scores follow card quality and no optimizer work runs.
        var result = Score([E("bomb", "{3}{B}", 4, .66), E("good", "{2}{R}", 3, .61), E("average", "{2}{W}", 3, .56), E("weak", "{2}{U}", 3, .51)], []);
        Assert.Equal(0, result.Metrics.OptimizerCalls); Assert.All(result.Cards, c => Assert.Equal(0, c.Weights.DeckNeed));
        Assert.Equal([1, 2, 3, 4], result.Cards.Select(c => c.CurrentPackRank!.Value));
        Assert.InRange(result.Cards[2].Score0To50!.Value, 24, 26);
    }

    [Fact] public void EarlyUncertainCommitmentStillPrefersTheStrongerOffColorCard()
    {
        // Expected: after eight green picks (P1P9) colours are not settled; a clearly stronger blue card should still
        // be taken over a slightly better-than-average green card, and deck fit should barely move scores.
        var pool = Many("g", "{1}{G}", 2, .58, 8).ToArray();
        var result = Score([E("blue", "{2}{U}", 3, .63), E("green", "{2}{G}", 3, .59)], pool).Cards;
        Assert.True(result[0].Score0To50 > result[1].Score0To50);
        Assert.InRange(result[0].Weights.DeckNeed, 0, .05);
        Assert.InRange(Math.Abs(result[1].Contributions.Single(c => c.Component == "Deck Need").PointsFromNeutral), 0, 1);
    }

    [Fact] public void LateEstablishedColorsPreferAnOnColorUpgradeOverAStrongerOffColorCard()
    {
        // Expected: at P3P8 in committed red-green, a 60% on-colour upgrade beats a 63% off-colour card.
        var result = Score([E("off", "{2}{U}", 3, .63), E("on", "{2}{R}", 3, .60)], DeepRedGreen()).Cards;
        Assert.True(result[1].Score0To50 > result[0].Score0To50);
    }

    [Fact] public void SmallChangesToTheExistingPoolMoveScoresOnlySlightly()
    {
        // Expected: adding one irrelevant off-colour card, or shifting every pool card by 0.1 pp, is not a meaningful
        // context change for a candidate near the cut line.
        var candidate = E("candidate", "{2}{R}", 3, .57);
        var pool = DeepRedGreen();
        var baseScore = One(candidate, pool, 35).Score0To50!.Value;
        var extraOffColor = One(candidate, pool.Append(E("extra-white", "{2}{W}", 3, .55)).ToArray(), 35).Score0To50!.Value;
        var shifted = One(candidate, DeepRedGreen(.571), 35).Score0To50!.Value;
        Assert.InRange(Math.Abs(extraOffColor - baseScore), 0, 1);
        Assert.InRange(Math.Abs(shifted - baseScore), 0, 2);
    }

    [Fact] public void CandidateTippingTwoTiedFullPairsChangesDeckFitContinuously()
    {
        // Expected: green with red and white equally deep (both full 23-card decks). A white card's value grows
        // smoothly with its GIH instead of jumping when it makes white-green the single best projection, and equal
        // red and white cards are worth the same while the pairs are tied.
        var pool = Many("g", "{2}{G}", 3, .57, 12).Concat(Many("r", "{2}{R}", 3, .57, 11)).Concat(Many("w", "{2}{W}", 3, .57, 11))
            .Concat(Many("u", "{2}{U}", 3, .55, 2)).ToArray();
        int? previous = null;
        for (var rate = .55; rate <= .6001; rate += .0025)
        {
            var score = Score([E("white", "{2}{W}", 3, rate), E("red", "{2}{R}", 3, .58)], pool).Cards[0].Score0To50!.Value;
            if (previous is { } last) Assert.InRange(score - last, 0, 2);
            previous = score;
        }
        var tied = Score([E("white", "{2}{W}", 3, .58), E("red", "{2}{R}", 3, .58)], pool).Cards;
        Assert.Equal(tied[0].Score0To50, tied[1].Score0To50);
        Assert.True(tied[0].DeckImpact.BestPairWeight < .5);
    }

    [Fact] public void PlayablesGainValueOnlyWhenRemainingPicksCannotFillTheDeck()
    {
        // Expected: 21 on-colour spells with plenty of picks left: a 50% card is below replacement level. With few
        // picks left, open slots may go unfilled, so it becomes worth taking; a better filler always stays ahead.
        Entry[] Pool(int picks) => Many("gc", "{2}{G}", 3, .57, 8).Concat(Many("rc", "{2}{R}", 3, .57, 7)).Concat(Many("gs", "{1}{G}", 2, .57, 3, "Instant"))
            .Concat(Many("rs", "{1}{R}", 2, .57, 3, "Instant")).Concat(Enumerable.Range(0, picks - 21).Select(i => E($"off{i}", "{2}{" + "WUB"[i % 3] + "}", 3, .55))).ToArray();
        ContextualPickScore[] At(int picks) => Score([E("chaff", "{2}{G}", 3, .50), E("filler", "{2}{G}", 3, .54)], Pool(picks)).Cards.ToArray();
        var comfortable = At(33); var desperate = At(40);
        Assert.True(desperate[0].DeckNeedComponent - comfortable[0].DeckNeedComponent >= .4);
        Assert.True(desperate[0].DeckImpact.ReplacementLevel < comfortable[0].DeckImpact.ReplacementLevel);
        Assert.All(new[] { comfortable, desperate }, cards => Assert.True(cards[1].Score0To50 >= cards[0].Score0To50));
    }

    [Fact] public void GradedDeckFitIsDeterministic()
    {
        var pack = new[] { E("a", "{2}{R}", 3, .5702), E("b", "{2}{W}", 3, .58), E("c", "{1}{G}", 2, .55) };
        var first = Score(pack, DeepRedGreen()); var second = Score(pack, DeepRedGreen());
        Assert.Equal(first.Cards.Select(c => c.ContextualValue), second.Cards.Select(c => c.ContextualValue));
        Assert.Equal(first.Cards.Select(c => c.DeckImpact.CutlineMargin), second.Cards.Select(c => c.DeckImpact.CutlineMargin));
    }
}
