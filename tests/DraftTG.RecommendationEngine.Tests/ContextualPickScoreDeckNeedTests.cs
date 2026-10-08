using DraftTG.Domain;

namespace DraftTG.RecommendationEngine.Tests;

/// <summary>Phase 4: deck need, card roles and marginal deck improvement. Each test states the expected drafting
/// behaviour first and compares candidates with identical Q/L/A, so only deck need can separate them.</summary>
public sealed class ContextualPickScoreDeckNeedTests
{
    private static readonly SetArchetypeProfile Profile = new("WOE", "https://example.test/roles",
        new[] { "WU", "UB", "BR", "RG", "GW", "WB", "UR", "BG", "RW", "GU" }
            .Select(p => new ArchetypeDefinition("WOE", ArchetypeColorPair.Create(p), p, "Fixture")));
    private static readonly LimitedCardStatisticsCatalog Environment = new([new(CardIdentifier.Create("environment"), 1000000, GameInHandWinRate: .56)]);
    private static readonly double Floor = new ContextualPickScoreConfiguration().DeckFitFloor;

    private sealed record Entry(Card Card, double Rate);
    private static Entry E(string id, string cost, int mv, double rate = .56, string type = "Creature", string? oracle = null,
        IEnumerable<ManaKind>? produced = null)
    {
        var colors = cost.Where("WUBRG".Contains).Distinct().Select(c => (MagicColor)"WUBRG".IndexOf(c));
        return new(new Card(CardIdentifier.Create(id), id, new(colors), CardRarity.Common, CardSetCode.Create("WOE"), CollectorNumber.Create(id))
        { GameplayMetadata = new(mv, type == "Land" ? "" : cost, type, oracleText: oracle, producedMana: produced) }, rate);
    }
    private static IEnumerable<Entry> Many(string prefix, string cost, int mv, int count, double rate = .56, string type = "Creature", string? oracle = null) =>
        Enumerable.Range(0, count).Select(i => E($"{prefix}{i}", cost, mv, rate, type, oracle));
    // Spread across three colours so no off-colour pair becomes a viable alternative deck.
    private static IEnumerable<Entry> OffColour(int count) => Enumerable.Range(0, count).Select(i => E($"off{i}", "{2}{" + "WUB"[i % 3] + "}", 3, .55));

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
    /// <summary>Red-green deck: `early` 2-drops, the rest 3/4-drop creatures, plus off-colour cards to reach `picks`.</summary>
    private static Entry[] Deck(int early, int creatures, int noncreatures = 0, int highCost = 0, int removal = 0, int picks = 33)
    {
        var onColour = Many("e", "{1}{G}", 2, early).Concat(Enumerable.Range(0, creatures).Select(i => E($"c{i}", i % 2 == 0 ? "{2}{R}" : "{2}{G}", 3)))
            .Concat(Many("n", "{2}{G}", 3, noncreatures, type: "Sorcery")).Concat(Many("h", "{4}{G}{G}", 6, highCost))
            .Concat(Many("k", "{2}{R}", 3, removal, type: "Instant", oracle: "Destroy target creature.")).ToArray();
        return onColour.Concat(OffColour(Math.Max(0, picks - onColour.Length))).ToArray();
    }
    private static (ContextualPickScore A, ContextualPickScore B) Pair(Entry a, Entry b, IReadOnlyList<Entry> pool, int? completed = null)
    {
        var result = Score([a, b], pool, completed).Cards; return (result[0], result[1]);
    }

    [Fact] public void A_ShortOnEarlyPlaysAnAverageTwoDropBeatsAnEquivalentThreeDrop()
    {
        // Expected: the projected deck has 2 early plays (minimum 4). An average 2-drop fills a real curve need, so it
        // scores clearly above an otherwise identical 3-drop.
        var (two, three) = Pair(E("two", "{1}{G}", 2), E("three", "{2}{G}", 3), Deck(early: 2, creatures: 23));
        Assert.True(two.DeckImpact.EarlyPlayNeed > 0);
        Assert.True(two.Score0To50 - three.Score0To50 >= 3);
        Assert.Contains(two.DeckImpact.Reasons, r => r.StartsWith("+ Fills missing early plays", StringComparison.Ordinal));
    }

    [Fact] public void B_WithEnoughEarlyPlaysTheTwoDropGetsNoCurveBonus()
    {
        // Expected: with 8 early plays the curve need is met; 2-drop and 3-drop of equal quality are equivalent.
        var (two, three) = Pair(E("two", "{1}{G}", 2), E("three", "{2}{G}", 3), Deck(early: 8, creatures: 17));
        Assert.Equal(0, two.DeckImpact.EarlyPlayNeed);
        Assert.InRange(Math.Abs(two.Score0To50!.Value - three.Score0To50!.Value), 0, 1);
    }

    [Fact] public void C_CreatureShortageLiftsAnAverageCreature()
    {
        // Expected: a projected deck with 10 creatures (preferred 16) values a playable creature above an equal sorcery.
        var (creature, sorcery) = Pair(E("creature", "{2}{R}", 3), E("sorcery", "{2}{R}", 3, type: "Sorcery"), Deck(early: 4, creatures: 6, noncreatures: 15));
        Assert.True(creature.DeckImpact.CreatureNeed > 0);
        Assert.True(creature.Score0To50 - sorcery.Score0To50 >= 3);
        Assert.Contains(creature.DeckImpact.Reasons, r => r.StartsWith("+ Raises creatures", StringComparison.Ordinal));
    }

    [Fact] public void D_HealthyCreatureCountGivesNoGenericCreatureBonus()
    {
        // Expected: with ~20 creatures nothing is gained by being a creature.
        var (creature, sorcery) = Pair(E("creature", "{2}{R}", 3), E("sorcery", "{2}{R}", 3, type: "Sorcery"), Deck(early: 5, creatures: 20));
        Assert.Equal(0, creature.DeckImpact.CreatureNeed);
        Assert.InRange(Math.Abs(creature.Score0To50!.Value - sorcery.Score0To50!.Value), 0, 1);
    }

    [Fact] public void E_ExcessTopEndReducesAnAverageSixDrop()
    {
        // Expected: the projected deck is already at its top-end cap; an average 6-drop is redundant and ranks below an
        // otherwise identical 4-drop. It is a reduction, not a ban.
        var deck = Deck(early: 4, creatures: 12, highCost: 9);
        var (six, four) = Pair(E("six", "{4}{G}{G}", 6), E("four", "{3}{G}", 4), deck);
        Assert.True(six.DeckImpact.TopEndRedundancy > 0);
        Assert.True(six.ContextualValue < four.ContextualValue);
        Assert.True(six.DeckNeedComponent > Floor);
        Assert.Contains(six.DeckImpact.Reasons, r => r.StartsWith("- Projected deck already has", StringComparison.Ordinal) || r.StartsWith("- Does not make", StringComparison.Ordinal));
    }

    [Fact] public void F_AStrongSixDropStaysCompetitiveDespiteRedundancy()
    {
        // Expected: quality still matters — a 66% 6-drop beats an average 3-drop even with a crowded top end.
        var (six, three) = Pair(E("strong-six", "{4}{G}{G}", 6, .66), E("three", "{2}{G}", 3), Deck(early: 4, creatures: 12, highCost: 9));
        Assert.True(six.ContextualValue > three.ContextualValue);
    }

    [Fact] public void G_RemovalShortageLiftsReliableRemoval()
    {
        // Expected: a projected deck with no interaction values a high-confidence removal spell above an equal non-removal spell.
        var (removal, plain) = Pair(E("removal", "{2}{R}", 3, type: "Instant", oracle: "Destroy target creature."),
            E("plain", "{2}{R}", 3, type: "Instant", oracle: "Draw a card."), Deck(early: 4, creatures: 21));
        Assert.True(removal.DeckImpact.RemovalNeed > 0); Assert.Equal(0, plain.DeckImpact.RemovalNeed);
        Assert.True(removal.Score0To50 - plain.Score0To50 >= 2);
        Assert.Contains(removal.DeckImpact.Reasons, r => r.Contains("removal spell", StringComparison.Ordinal));
    }

    [Fact] public void H_HealthyRemovalCountRemovesTheRemovalBonus()
    {
        // Expected: with five removal spells already projected, more removal earns no structural bonus.
        var (removal, plain) = Pair(E("removal", "{2}{R}", 3, type: "Instant", oracle: "Destroy target creature."),
            E("plain", "{2}{R}", 3, type: "Instant", oracle: "Draw a card."), Deck(early: 4, creatures: 16, removal: 5));
        Assert.Equal(0, removal.DeckImpact.RemovalNeed);
        Assert.InRange(Math.Abs(removal.Score0To50!.Value - plain.Score0To50!.Value), 0, 1);
    }

    [Fact] public void I_ACardThatMakesTheDeckIsAboveNeutral()
    {
        // Expected: a 60% card in a deck of 56% cards clearly makes it and gets positive deck need.
        var card = Score([E("upgrade", "{2}{G}", 3, .60), E("other", "{2}{U}", 3)], Deck(early: 4, creatures: 21)).Cards[0];
        Assert.True(card.DeckImpact.MakesProjectedDeck); Assert.True(card.DeckNeedComponent > .7);
        Assert.Contains(card.DeckImpact.Reasons, r => r.StartsWith("+ Makes the projected deck", StringComparison.Ordinal));
    }

    [Fact] public void J_ReplacingAVeryWeakTwentyThirdCardIsWorthMore()
    {
        // Expected: the same 58% card is worth more when it would replace a 48% card than when the weakest card is 56%.
        // Exactly 23 on-colour spells, so the weak card is genuinely the 23rd maindeck card. Phase 6: at the last pick no
        // later pick can replace the 48% card, so it is the real cut (earlier, later picks would replace it anyway).
        var strongDeck = Deck(early: 4, creatures: 19, picks: 41);
        var weakDeck = Deck(early: 4, creatures: 18, picks: 40).Prepend(E("weak", "{2}{R}", 3, .48)).ToArray();
        var replacingWeak = Score([E("upgrade", "{2}{G}", 3, .58), E("other", "{2}{U}", 3)], weakDeck).Cards[0];
        var replacingAverage = Score([E("upgrade", "{2}{G}", 3, .58), E("other", "{2}{U}", 3)], strongDeck).Cards[0];
        Assert.True(replacingWeak.DeckNeedComponent > replacingAverage.DeckNeedComponent + .05);
        Assert.Equal("weak", replacingWeak.DeckImpact.DisplacedCard?.Value);
        Assert.Contains(replacingWeak.DeckImpact.Reasons, r => r.StartsWith("+ Replaces weak", StringComparison.Ordinal));
    }

    [Fact] public void K_ACardThatNeverMakesTheDeckLateIsBelowNeutralButNotZero()
    {
        // Expected: a 50% card in a full deck of 57% cards late has sideboard value only: below neutral, at about the floor.
        var deep = Many("e", "{1}{G}", 2, 5, .57).Concat(Many("c", "{2}{R}", 3, 22, .57)).Concat(OffColour(8)).ToArray();
        var card = Score([E("chaff", "{2}{G}", 3, .50), E("other", "{2}{U}", 3)], deep).Cards[0];
        Assert.False(card.DeckImpact.MakesProjectedDeck);
        Assert.InRange(card.DeckNeedComponent, Floor, Floor + .05);
        Assert.Contains(card.DeckImpact.Reasons, r => r.StartsWith("- Does not make the current best build", StringComparison.Ordinal));
    }

    [Fact] public void L_FixingThatReducesAMeasuredShortfallIsPositive()
    {
        // Expected: a red-green dual that reduces the projected colour-source shortfall earns a fixing bonus above neutral.
        var pool = Many("g", "{2}{G}", 3, 12).Concat(Many("r", "{2}{R}", 3, 11)).ToArray();
        var dual = Score([E("rg-dual", "", 0, .55, "Land", produced: [ManaKind.Red, ManaKind.Green]), E("other", "{2}{U}", 3)], pool, completed: 35).Cards[0];
        Assert.True(dual.DeckImpact.ManaBaseDelta > 0);
        Assert.True(dual.DeckNeedComponent >= new ContextualPickScoreConfiguration().LandFixingBase);
        Assert.Contains(dual.DeckImpact.Reasons, r => r.StartsWith("+ Reduces a projected colour-source shortfall", StringComparison.Ordinal));
    }

    [Fact] public void M_AnUnneededFixerGetsNoFixingBonus()
    {
        // Expected: a white-blue dual in a red-green deck fixes nothing it needs: it is not selected and keeps only the floor.
        var pool = Many("g", "{2}{G}", 3, 12).Concat(Many("r", "{2}{R}", 3, 11)).ToArray();
        var dual = Score([E("wu-dual", "", 0, .55, "Land", produced: [ManaKind.White, ManaKind.Blue]), E("other", "{2}{U}", 3)], pool, completed: 35).Cards[0];
        Assert.Equal(Floor, dual.DeckNeedComponent);
        Assert.DoesNotContain(dual.DeckImpact.Reasons, r => r.StartsWith("+ ", StringComparison.Ordinal));
    }

    [Fact] public void N_PackOnePickThreeHasNoStructuralInfluence()
    {
        // Expected: at P1P3 deck need carries no weight, so equal 2-drop and 3-drop cards score identically.
        var (two, three) = Pair(E("two", "{1}{G}", 2), E("three", "{2}{G}", 3), [E("p0", "{3}{G}", 4), E("p1", "{3}{R}", 4)]);
        Assert.Equal(0, two.Weights.DeckNeed);
        Assert.Equal(two.ContextualValue, three.ContextualValue);
    }

    [Fact] public void O_TheSameCurveDeficitMattersMoreLateThanMidDraft()
    {
        // Expected: identical proportions (no early plays) in a 14-card mid-draft pool and a 33-card late pool: the 2-drop's
        // advantage grows late, through both the outer D weight and higher structure confidence.
        var mid = Many("m", "{2}{R}", 3, 12).Concat(OffColour(2)).ToArray();
        var late = Deck(early: 0, creatures: 25);
        double Gap(Entry[] pool) { var (two, three) = Pair(E("two", "{1}{G}", 2), E("three", "{2}{G}", 3), pool); return two.ContextualValue!.Value - three.ContextualValue!.Value; }
        Assert.True(Gap(late) > Gap(mid));
        Assert.True(Gap(mid) > 0);
    }

    [Fact] public void StructureConfidenceShrinksSmallProjectedShells()
    {
        // Expected: a small projected shell gets proportionally lower confidence (18 spells = full), so early structural
        // signals are shrunk, while a full deck gets full confidence.
        // A mono-red shell, so the red candidate is evaluated in the same build whose structure is reported.
        var pool = Many("c", "{2}{R}", 3, 9).ToArray();
        var card = Score([E("two", "{1}{R}", 2), E("other", "{2}{U}", 3)], pool).Cards[0];
        var spells = card.DeckImpact.ProjectedStructure!.Spells;
        Assert.InRange(spells, 9, 12);
        Assert.Equal(spells / 18d, card.DeckImpact.StructureConfidence, 6);
        Assert.Equal(1, Score([E("two", "{1}{G}", 2), E("other", "{2}{U}", 3)], Deck(early: 4, creatures: 21)).Cards[0].DeckImpact.StructureConfidence);
    }
}
