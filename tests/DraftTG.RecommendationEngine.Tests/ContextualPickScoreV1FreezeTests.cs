using DraftTG.Domain;

namespace DraftTG.RecommendationEngine.Tests;

/// <summary>Phase 7 freeze: golden V1 fixtures (exact score, displayed score, tier and key components for representative
/// scenarios) plus whole-draft stability invariants. A change to any golden value is a model change and needs a version
/// bump and a regression comparison (see CONTEXTUAL_PICK_SCORE_V1_SPEC.md).</summary>
public sealed class ContextualPickScoreV1FreezeTests
{
    private static readonly SetArchetypeProfile Profile = new("WOE", "https://example.test/roles",
        new[] { "WU", "UB", "BR", "RG", "GW", "WB", "UR", "BG", "RW", "GU" }
            .Select(p => new ArchetypeDefinition("WOE", ArchetypeColorPair.Create(p), p, "Fixture")));
    private static readonly LimitedCardStatisticsCatalog Environment = new([new(CardIdentifier.Create("environment"), 1000000, GameInHandWinRate: .56)]);

    private sealed record Entry(Card Card, double? Rate, int Games = 10000);
    private static Entry E(string id, string cost, int mv, double? rate = .56, string type = "Creature", int games = 10000, string? oracle = null,
        CardRarity rarity = CardRarity.Common)
    {
        var colors = cost.Where("WUBRG".Contains).Distinct().Select(c => (MagicColor)"WUBRG".IndexOf(c));
        return new(new Card(CardIdentifier.Create(id), id, new(colors), rarity, CardSetCode.Create("WOE"), CollectorNumber.Create(id))
        { GameplayMetadata = new(mv, cost, type, oracleText: oracle) }, rate, games);
    }
    private static IEnumerable<Entry> Padding(int count) => Enumerable.Range(0, count).Select(i => E($"pad{i}", "{2}{" + "WUB"[i % 3] + "}", 3, .55));
    /// <summary>`count` spells alternating between two colours (every fourth a 2-drop), padded to `picks`.</summary>
    private static Entry[] Pool(int count, int picks, double rate = .56, string a = "R", string b = "G", string prefix = "p") =>
        Enumerable.Range(0, count).Select(i => E($"{prefix}{i}", (i % 4 == 0 ? "{1}" : "{2}") + "{" + (i % 2 == 0 ? a : b) + "}", i % 4 == 0 ? 2 : 3, rate))
            .Concat(Padding(Math.Max(0, picks - count))).ToArray();
    private static ContextualPickScore[] Score(IReadOnlyList<Entry> pack, IReadOnlyList<Entry>? pool = null, ArchetypePairStatistics? pair = null,
        DraftPackObservationHistory? observations = null)
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
        var lane = new LaneContextualRecommendationEngine().Recommend(snapshot, colors, observations ?? DraftPackObservationHistory.Empty, LimitedStatisticsFormat.PremierDraft);
        var archetype = new ArchetypeRecommendationEngine().Recommend(lane, Profile, new("WOE", LimitedStatisticsFormat.PremierDraft), pair);
        return new ContextualPickScoreEngine().Recommend(snapshot, catalog, archetype, stats, Environment).Cards.ToArray();
    }
    private static ContextualPickScore One(Entry card, IReadOnlyList<Entry>? pool = null, ArchetypePairStatistics? pair = null, DraftPackObservationHistory? obs = null) =>
        Score([card, E("filler", "{2}{W}", 3, .55)], pool, pair, obs)[0];
    private static ArchetypePairStatistics RgPair(params (string Id, double Rate)[] cards) => new(new("WOE", LimitedStatisticsFormat.PremierDraft),
        ArchetypeColorPair.Create("RG"), new(cards.Select(c => new LimitedCardStatistics(CardIdentifier.Create(c.Id), 4000, GameInHandWinRate: c.Rate))), [new(.565, 100000)]);
    private static Entry[] NoEarlyPlays() => Enumerable.Range(0, 24).Select(i => E($"four{i}", i % 2 == 0 ? "{3}{R}" : "{3}{G}", 4, .57)).Concat(Padding(9)).ToArray();
    private static Entry[] TopHeavy() => Enumerable.Range(0, 4).Select(i => E($"two{i}", "{1}{G}", 2)).Concat(Enumerable.Range(0, 12).Select(i => E($"mid{i}", i % 2 == 0 ? "{2}{R}" : "{2}{G}", 3)))
        .Concat(Enumerable.Range(0, 9).Select(i => E($"six{i}", "{4}{G}{G}", 6))).Concat(Padding(8)).ToArray();
    private static Entry[] Bg(int b, int g, int r, double redRate) => Enumerable.Range(0, b).Select(i => E($"b{i}", "{2}{B}", 3, .565))
        .Concat(Enumerable.Range(0, g).Select(i => E($"g{i}", "{2}{G}", 3, .565))).Concat(Enumerable.Range(0, r).Select(i => E($"r{i}", "{2}{R}", 3, redRate))).ToArray();

    // ---- Golden V1 fixtures -----------------------------------------------------------------------------------------

    public static TheoryData<string> Goldens => new(Golden.Keys);
    private static readonly Dictionary<string, (Func<CardRarity, ContextualPickScore> Run, double Exact, int Display, PickScoreTier Tier, double Q, double R, double D)> Golden = new()
    {
        ["p1p1-bomb"] = (r => One(E("bomb", "{3}{G}", 4, .66, games: 3000, rarity: r)), 47.3690, 47, PickScoreTier.Premium, .9565, 1, .5),
        ["p1p1-average"] = (r => One(E("average", "{2}{G}", 3, rarity: r)), 25.0000, 25, PickScoreTier.Solid, .5, 1, .5),
        ["early-off-colour-bomb"] = (r => One(E("blue", "{2}{U}", 3, .65, rarity: r), Pool(2, 2)), 47.3690, 47, PickScoreTier.Premium, .9565, 1, .4929),
        ["late-off-colour-bomb"] = (r => One(E("blue", "{2}{U}", 3, .65, rarity: r), Pool(24, 33)), 16.3204, 16, PickScoreTier.Marginal, .9565, .2145, .1),
        ["needed-two-drop"] = (r => One(E("needed", "{1}{G}", 2, .57, rarity: r), NoEarlyPlays()), 35.7569, 36, PickScoreTier.Strong, .5595, 1, .85),
        ["redundant-top-end"] = (r => One(E("six", "{4}{G}{G}", 6, rarity: r), TopHeavy()), 23.3140, 23, PickScoreTier.Marginal, .5, 1, .4376),
        ["removal-need"] = (r => One(E("removal", "{2}{R}", 3, type: "Instant", oracle: "Destroy target creature.", rarity: r), Pool(25, 33)), 30.4061, 30, PickScoreTier.Solid, .5, 1, .7),
        ["real-cut-upgrade"] = (r => One(E("upgrade", "{2}{G}", 3, .59, rarity: r), Pool(25, 41)), 37.8803, 38, PickScoreTier.Strong, .6786, 1, .8261),
        ["estimated-card"] = (r => One(E("estimated", "{2}{G}", 3, rate: null, rarity: r), Pool(25, 33)), 26.3515, 26, PickScoreTier.Solid, .5, 1, .55),
        ["active-archetype-card"] = (r => One(E("synergy", "{1}{R}", 2, rarity: r), Pool(16, 16), RgPair(("synergy", .61))), 31.1994, 31, PickScoreTier.Solid, .5, 1, .6582),
        ["viable-alternate-build"] = (r => One(E("red-bomb", "{2}{R}", 3, .66, rarity: r), Bg(6, 7, 5, .57)), 45.9700, 46, PickScoreTier.Premium, .9708, .9678, .9669),
        ["open-slot-filler"] = (r => One(E("filler-56", "{2}{G}", 3, rarity: r), Pool(22, 30, .58)), 24.1993, 24, PickScoreTier.Marginal, .5, 1, .4650),
    };

    [Theory, MemberData(nameof(Goldens))]
    public void GoldenV1FixtureIsStableAndRarityInvariant(string name)
    {
        // Expected: each representative scenario reproduces its frozen exact score (±0.01 points), integer, tier and key
        // components, and printing the same card as a Mythic changes nothing.
        var golden = Golden[name];
        var common = golden.Run(CardRarity.Common); var mythic = golden.Run(CardRarity.Mythic);
        Assert.InRange(50 * common.ContextualValue!.Value, golden.Exact - .01, golden.Exact + .01);
        Assert.Equal(golden.Display, common.Score0To50); Assert.Equal(golden.Tier, common.Tier);
        Assert.InRange(common.QualityComponent!.Value, golden.Q - .001, golden.Q + .001);
        Assert.InRange(common.QualityRelevance, golden.R - .001, golden.R + .001);
        Assert.InRange(common.DeckNeedComponent, golden.D - .001, golden.D + .001);
        Assert.Equal(common.ContextualValue, mythic.ContextualValue); Assert.Equal(common.Tier, mythic.Tier);
    }

    // ---- Stability invariants -----------------------------------------------------------------------------------------

    [Fact] public void NearViablePairsFadeInWithATaper()
    {
        // Expected: a pair 0/1/2/3 spells short of the leading shell keeps 1, 2/3, 1/3, 0 of its blend weight.
        var configuration = new ContextualPickScoreConfiguration();
        Assert.All(new[] { 1, 2 / 3d, 1 / 3d, 0 }.Select((expected, shortfall) => (expected, shortfall)),
            x => Assert.Equal(x.expected, configuration.ViabilityTaper(x.shortfall), 12));
    }

    [Fact] public void PivotIntoANewPairIsGradual()
    {
        // Expected: black-green early, then red arrives. Red rises and black declines without a one-pick flip, and red
        // finishes clearly ahead once red-green is the stronger build.
        Entry[] Pivot(int n) => Pool(Math.Min(n, 11), Math.Min(n, 11), .565, "B", "G", "bg")
            .Concat(Enumerable.Range(0, Math.Max(0, n - 11)).Select(i => E($"rr{i}", i % 2 == 0 ? "{2}{R}" : "{1}{G}", i % 2 == 0 ? 3 : 2, i % 2 == 0 ? .59 : .565))).ToArray();
        var scores = Enumerable.Range(3, 13).Select(k => Score([E("red60", "{2}{R}", 3, .60), E("black60", "{2}{B}", 3, .60)], Pivot(2 * k))).ToArray();
        int Red(ContextualPickScore[] s) => s[0].Score0To50!.Value; int Black(ContextualPickScore[] s) => s[1].Score0To50!.Value;
        Assert.All(scores.Zip(scores.Skip(1)), p =>
        {
            Assert.InRange(Math.Abs(Red(p.Second) - Red(p.First)), 0, 8);
            Assert.InRange(Math.Abs(Black(p.Second) - Black(p.First)), 0, 8);
        });
        Assert.True(Red(scores[^1]) >= Black(scores[^1]) + 10);
    }

    [Fact] public void AnUnrelatedPoolCardBarelyMovesScores()
    {
        // Expected: drafting a weak colourless card or an off-colour card moves unrelated candidates by at most a point.
        Entry[] pack = [E("a", "{2}{G}", 3, .58), E("b", "{1}{R}", 2, .55), E("c", "{2}{U}", 3, .62), E("d", "{2}{R}", 3, .56, "Instant", oracle: "Destroy target creature.")];
        foreach (var pool in new[] { Pool(18, 18), Pool(25, 33), Pool(25, 40) })
            foreach (var extra in new[] { E("artifact", "{3}", 3, .45, "Artifact"), E("white", "{2}{W}", 3, .55) })
            {
                var before = Score(pack, pool); var after = Score(pack, pool.Append(extra).ToArray());
                Assert.All(before.Zip(after), p => Assert.InRange(Math.Abs(p.Second.Score0To50!.Value - p.First.Score0To50!.Value), 0, 1));
            }
    }

    [Fact] public void ArchetypeActivationAddsAtMostAFewPoints()
    {
        // Expected: as red-green commitment grows past Phase 9C's activation threshold, a synergy card's score changes
        // by at most 3 points between adjacent pools and a generic card gets no synergy credit.
        var pair = RgPair(("synergy", .61), ("generic", .58));
        var results = Enumerable.Range(2, 15).Select(n => Score([E("synergy", "{1}{R}", 2, .56), E("generic", "{2}{G}", 3, .58)],
            Pool(n, n).Concat(Padding(Math.Max(0, 16 - n) / 2)).ToArray(), pair)).ToArray();
        Assert.All(results.Zip(results.Skip(1)), p => Assert.InRange(Math.Abs(p.Second[0].Score0To50!.Value - p.First[0].Score0To50!.Value), 0, 3));
        Assert.All(results, r => Assert.True(r[1].ArchetypeComponent <= .5));
        Assert.True(results[^1][0].ArchetypeComponent > .9);
    }

    [Fact] public void LaneSignalsGrowWithConsistencyAndOneLateCardIsSmall()
    {
        // Expected: one late red card moves a red candidate by at most 2 points; four consistent late red cards move it
        // more but stay within the lane component's ±7-point early range.
        var pool = Pool(9, 9);
        DraftPackObservationHistory Observed(params int[] picks)
        {
            var history = DraftPackObservationHistory.Empty;
            var cards = picks.SelectMany(p => new[] { E($"late{p}", "{2}{R}", 3, .60), E($"w{p}", "{2}{W}", 3, .52), E($"u{p}", "{2}{U}", 3, .52) }).ToArray();
            var catalog = new CardCatalog(pool.Concat(cards).Select(e => e.Card));
            var stats = new LimitedCardStatisticsCatalog(cards.Concat(pool).Select(e => new LimitedCardStatistics(e.Card.Identifier, 10000, GameInHandWinRate: e.Rate, AverageLastSeenAt: 3)));
            foreach (var p in picks)
            {
                var pack = new DraftPack(new(PackNumber.Create(1), PickNumber.Create(p)), cards.Where(c => c.Card.Identifier.Value.EndsWith(p.ToString(), StringComparison.Ordinal)).Select(c => c.Card.Identifier));
                history = history.Observe(pack, catalog, null, new StatisticalRecommendationEngine().Recommend(pack, stats, Environment), stats);
            }
            return history;
        }
        var none = One(E("red58", "{2}{R}", 3, .58), pool).Score0To50!.Value;
        var one = One(E("red58", "{2}{R}", 3, .58), pool, obs: Observed(8)).Score0To50!.Value;
        var four = One(E("red58", "{2}{R}", 3, .58), pool, obs: Observed(6, 7, 8, 9)).Score0To50!.Value;
        Assert.InRange(one - none, 0, 2); Assert.InRange(four - none, one - none, 7);
    }

    [Fact] public void DeckCutTrajectoryHasNoMembershipJump()
    {
        // Expected: a 57% card tracked from open provisional slot through the virtual cut, the real cut and safe
        // maindeck membership moves at most 3 points between adjacent pool states.
        var states = new[] { (8, 12), (14, 18), (18, 23), (21, 27), (22, 29), (23, 31), (25, 35), (27, 39), (29, 41) };
        var scores = states.Select(s => One(E("cut57", "{2}{G}", 3, .57), Pool(s.Item1, s.Item2)).Score0To50!.Value).ToArray();
        Assert.All(scores.Zip(scores.Skip(1)), p => Assert.InRange(Math.Abs(p.Second - p.First), 0, 3));
    }

    [Fact] public void ACardKeptByTheTopEndCapIsExplainedAccurately()
    {
        // Expected: in a deck over its top-end cap, a 3-drop cannot be swapped for a 6-drop; the reason names the
        // composition limits generically instead of claiming a creature/early-play floor.
        var three = Score([E("three", "{2}{G}", 3), E("six-x", "{4}{G}{G}", 6)], TopHeavy())[0];
        Assert.DoesNotContain(three.DeckImpact.Reasons, r => r.Contains("creature/early-play floor.", StringComparison.Ordinal));
        if (three.DeckImpact.Reasons.Any(r => r.StartsWith("+ Kept in", StringComparison.Ordinal)))
            Assert.Contains(three.DeckImpact.Reasons, r => r.Contains("top-end cap", StringComparison.Ordinal));
    }

    [Fact] public void SupplyProfileFallsBackToALabelledProvisionalDefault()
    {
        // Expected: FRA uses its calibrated profile; any other set uses the same values as an explicitly provisional
        // default, so FRA scores are unchanged by the refactor and other sets are labelled honestly.
        var fra = FuturePickSupplyProfile.For("fra"); var other = FuturePickSupplyProfile.For("WOE");
        Assert.True(fra.IsSetCalibrated); Assert.False(other.IsSetCalibrated);
        Assert.Contains("provisional", other.Provenance, StringComparison.Ordinal);
        Assert.Equal(fra with { Name = other.Name, Provenance = other.Provenance, IsSetCalibrated = false }, other);
        Assert.Same(FuturePickSupplyProfile.ProvisionalDefault, new ContextualPickScoreConfiguration().SupplyProfile);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ContextualPickScoreConfiguration(supplyProfile: fra with { ExpectedPlayableShare = 0 }));
        var score = One(E("filler-56", "{2}{G}", 3), Pool(22, 30, .58));
        Assert.Contains("provisional", score.DeckImpact.SupplyProfile, StringComparison.Ordinal);
    }
}
