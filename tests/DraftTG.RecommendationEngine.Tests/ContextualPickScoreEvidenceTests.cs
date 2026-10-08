using DraftTG.Domain;

namespace DraftTG.RecommendationEngine.Tests;

/// <summary>Phase 3: provenance of the quality component. Expected behaviour is stated before each assertion.</summary>
public sealed class ContextualPickScoreEvidenceTests
{
    private static readonly SetArchetypeProfile Profile = new("WOE", "https://example.test/roles",
        new[] { "WU", "UB", "BR", "RG", "GW", "WB", "UR", "BG", "RW", "GU" }
            .Select(p => new ArchetypeDefinition("WOE", ArchetypeColorPair.Create(p), p, "Fixture")));
    private static readonly LimitedCardStatisticsCatalog Environment = new([new(CardIdentifier.Create("environment"), 1000000, GameInHandWinRate: .56)]);

    private static Card C(string id, string cost, int mv, CardRarity rarity = CardRarity.Common)
    {
        var colors = cost.Where("WUBRG".Contains).Distinct().Select(c => (MagicColor)"WUBRG".IndexOf(c));
        return new(CardIdentifier.Create(id), id, new(colors), rarity, CardSetCode.Create("WOE"), CollectorNumber.Create(id))
        { GameplayMetadata = new(mv, cost, "Creature") };
    }
    private static Card[] DeepRedGreen() => Enumerable.Range(0, 14).Select(i => C($"g{i}", i % 3 == 0 ? "{1}{G}" : "{2}{G}", i % 3 == 0 ? 2 : 3))
        .Concat(Enumerable.Range(0, 13).Select(i => C($"r{i}", i % 3 == 0 ? "{1}{R}" : "{3}{R}", i % 3 == 0 ? 2 : 4)))
        .Concat(Enumerable.Range(0, 8).Select(i => C($"w{i}", "{2}{W}", 3))).ToArray();

    /// <summary>Pool cards are measured at 57%; pack cards get the given row (null = no provider row at all).</summary>
    private static ContextualPickScoreResult Score(IReadOnlyList<(Card Card, LimitedCardStatistics? Row)> pack, Card[] pool,
        LimitedCardStatisticsCatalog? environment = null)
    {
        var picks = pool.Length;
        var snapshot = new DraftSnapshot(new(new(PackNumber.Create(picks / 14 + 1), PickNumber.Create(picks % 14 + 1)), pack.Select(p => p.Card.Identifier)),
            new(pool.Select((c, i) => new DraftPick(new(PackNumber.Create(i / 14 + 1), PickNumber.Create(i % 14 + 1)), c.Identifier))), DraftFormat.BestOfOne);
        var catalog = new CardCatalog(pool.Concat(pack.Select(p => p.Card)).DistinctBy(c => c.Identifier));
        var stats = new LimitedCardStatisticsCatalog(pool.DistinctBy(c => c.Identifier).Select(c => new LimitedCardStatistics(c.Identifier, 10000, GameInHandWinRate: .57))
            .Concat(pack.Where(p => p.Row is not null).Select(p => p.Row!)));
        environment ??= Environment;
        var statistical = new StatisticalRecommendationEngine().Recommend(snapshot.CurrentPack, stats, environment);
        var colors = new ContextualRecommendationEngine().Recommend(snapshot, catalog, statistical);
        var lane = new LaneContextualRecommendationEngine().Recommend(snapshot, colors, DraftPackObservationHistory.Empty, LimitedStatisticsFormat.PremierDraft);
        var archetype = new ArchetypeRecommendationEngine().Recommend(lane, Profile, new("WOE", LimitedStatisticsFormat.PremierDraft));
        return new ContextualPickScoreEngine().Recommend(snapshot, catalog, archetype, stats, environment);
    }

    [Fact] public void QualityEvidenceDistinguishesDirectMissingRowRowWithoutGihAndNoBaseline()
    {
        // Expected: direct only with a usable GIH; a row with ALSA but no GIH, or a zero sample, is estimated; no baseline is unavailable.
        Card a = C("direct", "{2}{G}", 3), b = C("no-row", "{2}{G}", 3), c = C("no-gih", "{2}{G}", 3), d = C("zero-sample", "{2}{G}", 3);
        var pack = new (Card, LimitedCardStatistics?)[] { (a, new(a.Identifier, 4000, GameInHandWinRate: .6)), (b, null),
            (c, new(c.Identifier, 400, AverageLastSeenAt: 6.1)), (d, new(d.Identifier, 0, GameInHandWinRate: .6)) };
        var result = Score(pack, []);
        Assert.Equal([PickScoreQualityEvidence.DirectCardStatistics, PickScoreQualityEvidence.NoProviderRow,
            PickScoreQualityEvidence.ProviderRowWithoutUsableGih, PickScoreQualityEvidence.ProviderRowWithoutUsableGih], result.Cards.Select(s => s.QualityEvidence));
        Assert.Equal([false, true, true, true], result.Cards.Select(s => s.IsQualityEstimated));
        Assert.All(Score(pack, [], new()).Cards, s => Assert.Equal(PickScoreQualityEvidence.NoEnvironmentBaseline, s.QualityEvidence));
    }

    [Fact] public void EstimatedQualityStillUsesRealDraftContext()
    {
        // Expected: EST means only Q is the neutral prior. Late in committed red-green, an estimated on-colour card still
        // gets real deck fit and outranks an estimated off-colour card; neither is pinned to a flat 25.
        Card on = C("estimated-on", "{2}{G}", 3), off = C("estimated-off", "{2}{U}", 3);
        var result = Score([(on, null), (off, null)], DeepRedGreen());
        Assert.All(result.Cards, s => { Assert.True(s.IsQualityEstimated); Assert.Equal(.5, s.QualityComponent); });
        Assert.True(result.Cards[0].DeckNeedComponent > new ContextualPickScoreConfiguration().DeckFitFloor); Assert.Equal(new ContextualPickScoreConfiguration().DeckFitFloor, result.Cards[1].DeckNeedComponent);
        Assert.True(result.Cards[0].Score0To50 > result.Cards[1].Score0To50);
        Assert.NotEqual(25, result.Cards[1].Score0To50);
        Assert.Contains(result.Cards[0].Reasons, r => r.Contains("no 17Lands row", StringComparison.Ordinal));
    }

    [Fact] public void PrintedRarityChangesNeitherEvidenceNorScore()
    {
        // Expected: rarity is printed metadata only; it never alters provenance or the score, estimated or direct.
        ContextualPickScore[] Run(CardRarity rarity)
        {
            Card measured = C("measured", "{2}{G}", 3, rarity), missing = C("missing", "{2}{G}", 3, rarity);
            return Score([(measured, new(measured.Identifier, 4000, GameInHandWinRate: .6)), (missing, null)], DeepRedGreen()).Cards.ToArray();
        }
        var common = Run(CardRarity.Common); var mythic = Run(CardRarity.Mythic);
        Assert.Equal(common.Select(s => (s.QualityEvidence, s.ContextualValue)), mythic.Select(s => (s.QualityEvidence, s.ContextualValue)));
        Assert.Equal([PickScoreQualityEvidence.DirectCardStatistics, PickScoreQualityEvidence.NoProviderRow], common.Select(s => s.QualityEvidence));
    }

    [Fact] public void EstimatedScoresStayWithinTheDisplayRangeAtEveryStage()
    {
        // Expected: the neutral prior plus any context keeps the displayed score inside 0–50 from P1P1 to the last pick.
        var pool = DeepRedGreen().Concat(Enumerable.Range(0, 7).Select(i => C($"x{i}", "{2}{B}", 3))).ToArray();
        for (var picks = 0; picks <= 41; picks += 3)
        {
            var result = Score([(C("est-on", "{2}{R}", 3), null), (C("est-off", "{2}{U}", 3), null)], pool[..picks]);
            Assert.All(result.Cards, s => Assert.InRange(s.Score0To50!.Value, 0, 50));
        }
    }
}
