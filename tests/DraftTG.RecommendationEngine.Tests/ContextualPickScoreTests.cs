using DraftTG.Domain;

namespace DraftTG.RecommendationEngine.Tests;

public sealed class ContextualPickScoreTests
{
    private static Card Card(string id, string colors = "G", int mv = 3, string type = "Creature", IEnumerable<ManaKind>? production = null) =>
        new(CardIdentifier.Create(id), id, new(colors.Select(c => (MagicColor)"WUBRG".IndexOf(c))), CardRarity.Common,
            CardSetCode.Create("WOE"), CollectorNumber.Create(id))
        { GameplayMetadata = new(mv, type == "Land" ? "" : "{1}" + string.Concat(colors.Select(c => $"{{{c}}}")), type, producedMana: production) };
    private static readonly Card Green = Card("green"), Red = Card("red", "R");
    private static readonly LimitedCardStatisticsCatalog Environment = new([new(CardIdentifier.Create("environment"), 10000, GameInHandWinRate: .56)]);
    private static readonly SetArchetypeProfile Profile = new("WOE", "https://example.test/roles",
        new[] { "WU", "UB", "BR", "RG", "GW", "WB", "UR", "BG", "RW", "GU" }
            .Select(p => new ArchetypeDefinition("WOE", ArchetypeColorPair.Create(p), p, "Fixture")));
    private sealed record Input(DraftSnapshot Snapshot, CardCatalog Catalog, LimitedCardStatisticsCatalog Stats,
        LimitedCardStatisticsCatalog Environment, ArchetypeRecommendationResult Archetype)
    {
        public ContextualPickScoreResult Score(ContextualPickScoreEngine? engine = null) => (engine ?? new()).Recommend(Snapshot, Catalog, Archetype, Stats, Environment);
    }
    private static Card[] CommittedPool() => Enumerable.Repeat(Green, 18).Concat(Enumerable.Repeat(Red, 17)).ToArray();
    private static Input Fixture((Card Card, double? Rate, int Count)[] pack, Card[]? pool = null, int? completed = null,
        LimitedStatisticsFormat format = LimitedStatisticsFormat.PremierDraft, DraftPackObservationHistory? observations = null,
        ArchetypePairStatistics? pair = null, bool baseline = true)
    {
        pool ??= [];
        var picks = completed ?? pool.Length;
        var snapshot = new DraftSnapshot(new(new(PackNumber.Create(picks / 14 + 1), PickNumber.Create(picks % 14 + 1)), pack.Select(c => c.Card.Identifier)),
            ColorCommitmentTests.History(pool), DraftFormat.BestOfOne);
        var catalog = new CardCatalog(pool.Concat(pack.Select(p => p.Card)).DistinctBy(c => c.Identifier));
        var stats = new LimitedCardStatisticsCatalog(pack.Select(p => new LimitedCardStatistics(p.Card.Identifier, p.Count, GameInHandWinRate: p.Rate, AverageLastSeenAt: 3))
            .Concat(pool.Select(c => new LimitedCardStatistics(c.Identifier, 10000, GameInHandWinRate: .56))).DistinctBy(r => r.CardIdentifier));
        var environment = baseline ? Environment : new LimitedCardStatisticsCatalog();
        var statistical = new StatisticalRecommendationEngine().Recommend(snapshot.CurrentPack, stats, environment);
        var colors = new ContextualRecommendationEngine().Recommend(snapshot, catalog, statistical);
        var lane = new LaneContextualRecommendationEngine().Recommend(snapshot, colors, observations ?? DraftPackObservationHistory.Empty, format);
        var archetype = new ArchetypeRecommendationEngine().Recommend(lane, Profile, new("WOE", format), pair);
        return new(snapshot, catalog, stats, environment, archetype);
    }
    private static ContextualPickScore[] Scores(Card candidate, double rate = .56, Card[]? pool = null, int? completed = null) =>
        Fixture([(candidate, rate, 10000), (Card("comparison", "R"), .55, 10000)], pool, completed).Score().Cards.ToArray();

    [Fact] public void WeightsUseSmoothStageAnchorsAndAlwaysSumToOne()
    {
        var config = new ContextualPickScoreConfiguration();
        Assert.Equal(config.EarlyWeights, config.WeightsAt(4d / 42));
        Assert.Equal(config.MiddleWeights, config.WeightsAt(21d / 42));
        Assert.Equal(config.LateWeights, config.WeightsAt(35d / 42));
        for (var i = 0; i <= 420; i++)
        {
            var w = config.WeightsAt(i / 420d);
            Assert.Equal(1, w.Quality + w.Lane + w.Archetype + w.DeckNeed, 12);
            var next = config.WeightsAt((i + .001) / 420d);
            Assert.InRange(Math.Abs(w.DeckNeed - next.DeckNeed), 0, .00001);
        }
    }
    [Fact] public void NormalizationHasExplicitNeutralEndpointsAndClamps()
    {
        // Phase 2: bounds are approached softly rather than clipped, so extreme inputs stay ordered.
        var c = new ContextualPickScoreConfiguration();
        Assert.Equal(.5, c.NormalizeQuality(.56, .56));
        Assert.InRange(c.NormalizeQuality(.1, .56), 0, .001); Assert.InRange(c.NormalizeQuality(.99, .56), .999, 1);
        Assert.True(c.NormalizeQuality(.99, .56) > c.NormalizeQuality(.80, .56)); Assert.True(c.NormalizeQuality(.1, .56) < c.NormalizeQuality(.3, .56));
        Assert.InRange(c.Calibrate(0), 0, .01); Assert.InRange(c.Calibrate(1), .99, 1); Assert.True(c.Calibrate(1) < 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ContextualPickScoreConfiguration(qualityRelativeSpan: double.NaN));
    }
    [Fact] public void EarlyCalibrationDistinguishesBombStrongPlayableAndWeak()
    {
        Assert.InRange(Scores(Card("bomb"), .66)[0].Score0To50!.Value, 45, 50);
        Assert.InRange(Scores(Card("strong"), .61)[0].Score0To50!.Value, 35, 41);
        Assert.InRange(Scores(Card("average"), .56)[0].Score0To50!.Value, 20, 30);
        Assert.InRange(Scores(Card("weak"), .50)[0].Score0To50!.Value, 0, 12);
    }
    [Fact] public void AbsoluteScoreDoesNotDependOnRankOrPackStrengthDistribution()
    {
        var same = Card("same");
        var low = Fixture([(same, .57, 10000), (Card("other"), .51, 10000)]).Score();
        var high = Fixture([(same, .57, 10000), (Card("other"), .66, 10000)]).Score();
        Assert.Equal(low.Cards[0].Score0To50, high.Cards[0].Score0To50);
        Assert.Equal(1, low.Cards[0].CurrentPackRank); Assert.Equal(2, high.Cards[0].CurrentPackRank);
        Assert.All(low.Cards, c => Assert.True(c.Score0To50 < 30));
        var bombs = Fixture([(same, .66, 10000), (Card("other"), .67, 10000)]).Score();
        Assert.All(bombs.Cards, c => Assert.True(c.Score0To50 >= 45));
    }
    [Fact] public void QualityExactlyReusesPhaseEightAdjustedValue()
    {
        var f = Fixture([(Card("a"), .61, 8000), (Card("b"), .55, 1000)]);
        var score = f.Score().Cards[0]; var phase8 = f.Archetype.Cards[0].StatisticalRecommendation;
        Assert.Equal(new ContextualPickScoreConfiguration().NormalizeQuality(phase8.AdjustedValue!.Value, .56), score.QualityComponent);
        Assert.Equal(phase8.DataWeight, score.StatisticalDataWeight);
        var rarityVariant = new CardCatalog(f.Catalog.Cards.Select(c => c with { Rarity = CardRarity.Mythic }));
        Assert.Equal(score.Score0To50, new ContextualPickScoreEngine().Recommend(f.Snapshot, rarityVariant, f.Archetype, f.Stats, f.Environment).Cards[0].Score0To50);
    }
    [Fact] public void LowSamplePremiumStatsAreShrunkWithoutSecondConfidenceMultiplier()
    {
        var low = Fixture([(Card("a"), .65, 40), (Card("b"), .55, 10000)]).Score().Cards[0];
        var high = Fixture([(Card("a"), .65, 8000), (Card("b"), .55, 10000)]).Score().Cards[0];
        Assert.True(high.Score0To50 > low.Score0To50); Assert.InRange(low.Score0To50!.Value, 25, 30);
    }
    [Fact] public void LateOffColorBombIsDiscountedAndDoesNotMakeDeck()
    {
        var result = Scores(Card("blue-bomb", "U"), .66, CommittedPool())[0];
        // Phase 4: an off-colour card keeps only the sideboard/future floor (below neutral), not a maindeck value.
        Assert.False(result.DeckImpact.MakesProjectedDeck); Assert.Equal(new ContextualPickScoreConfiguration().DeckFitFloor, result.DeckNeedComponent);
        Assert.InRange(result.Score0To50!.Value, 10, 25); Assert.True(result.ColorFit < 0);
        Assert.Contains(result.Reasons, r => r.Contains("Outside your established colors"));
    }
    [Fact] public void EarlySpeculativeBombKeepsFlexibilityAndSkipsAllDeckOptimization()
    {
        var f = Fixture([(Card("blue", "U", 6), .66, 10000), (Card("ordinary"), .56, 10000)], [Green, Red], 2);
        var result = f.Score(); Assert.Equal(0, result.Metrics.OptimizerCalls);
        Assert.Equal(0, result.Cards[0].Weights.DeckNeed); Assert.InRange(result.Cards[0].Score0To50!.Value, 45, 50);
    }
    [Fact] public void LaneReusesPositiveLateEvidenceWithHalfStrengthForQuickDraft()
    {
        var history = DraftPackObservationHistory.Empty;
        for (var pick = 8; pick <= 11; pick++)
        {
            var pack = new DraftPack(new(PackNumber.Create(1), PickNumber.Create(pick)), [Green.Identifier]);
            var stats = new LimitedCardStatisticsCatalog([new(Green.Identifier, 10000, GameInHandWinRate: .66, AverageLastSeenAt: 3)]);
            history = history.Observe(pack, new([Green]), null, new StatisticalRecommendationEngine().Recommend(pack, stats, Environment), stats);
        }
        var human = Fixture([(Green, .56, 10000), (Card("white", "W"), .56, 10000)], completed: 11, observations: history).Score();
        var bot = Fixture([(Green, .56, 10000), (Card("white", "W"), .56, 10000)], completed: 11, observations: history, format: LimitedStatisticsFormat.QuickDraft).Score();
        Assert.True(human.Cards[0].LaneComponent > .5);
        Assert.Equal((human.Cards[0].LaneComponent - .5) / 2, bot.Cards[0].LaneComponent - .5, 12);
        Assert.True(human.Cards[0].Score0To50 > bot.Cards[0].Score0To50);
    }
    [Fact] public void PairPerformanceBoostsAndReducesArchetypeUsingExistingLift()
    {
        var a = Card("lift"); var b = Card("negative", "R");
        var pair = new ArchetypePairStatistics(new("WOE", LimitedStatisticsFormat.PremierDraft), ArchetypeColorPair.Create("RG"),
            new([new(a.Identifier, 10000, GameInHandWinRate: .62), new(b.Identifier, 10000, GameInHandWinRate: .52)]), [new(.56, 10000)]);
        var result = Fixture([(a, .56, 10000), (b, .56, 10000)], CommittedPool(), pair: pair).Score();
        Assert.True(result.Cards[0].ArchetypeComponent > .5); Assert.True(result.Cards[1].ArchetypeComponent < .5);
        Assert.True(result.Cards[0].Score0To50 > result.Cards[1].Score0To50);
    }
    [Fact] public void UnsettledOrMissingExactPairEvidenceStaysNeutral()
    {
        var a = Card("lift");
        var mismatched = new ArchetypePairStatistics(new("WOE", LimitedStatisticsFormat.QuickDraft), ArchetypeColorPair.Create("RG"),
            new([new(a.Identifier, 10000, GameInHandWinRate: .70)]), [new(.56, 10000)]);
        Assert.Equal(.5, Fixture([(a, .56, 10000), (Red, .56, 10000)], pair: mismatched).Score().Cards[0].ArchetypeComponent);
        Assert.Equal(.5, Fixture([(a, .56, 10000), (Red, .56, 10000)], CommittedPool(), pair: mismatched).Score().Cards[0].ArchetypeComponent);
    }
    [Fact] public void NeededTwoDropImprovesEarlyPlayFloorWithBoundedBonus()
    {
        var result = Scores(Card("two-drop", mv: 2), pool: CommittedPool())[0];
        Assert.True(result.DeckImpact.EarlyPlayDelta > 0); Assert.Equal(1, result.DeckImpact.StructuralGain);
        Assert.Contains(result.Reasons, r => r.Contains("missing early play")); Assert.InRange(result.Score0To50!.Value, 25, 35);
    }
    [Fact] public void CrowdedTopEndRestrainsAverageSixDropRelativeToNeededEarlyPlay()
    {
        var high = Card("high", mv: 6); var low = Card("low", "R", 3);
        var pool = Enumerable.Repeat(high, 24).Concat(Enumerable.Repeat(low, 11)).ToArray();
        var result = Scores(Card("another-high", mv: 6), .56, pool)[0];
        var early = Scores(Card("needed-early", mv: 2), .56, pool)[0];
        // Phase 4: excess top-end is now an explicit redundancy (negative structure), not merely an absent bonus.
        Assert.True(result.DeckImpact.StructuralGain < 0); Assert.InRange(result.DeckNeedComponent, 0, .55);
        Assert.True(early.DeckNeedComponent > result.DeckNeedComponent); Assert.True(early.Score0To50 > result.Score0To50);
    }
    [Fact] public void CreatureCandidateHelpsExistingCreatureShortfall()
    {
        var spell = Card("spell", "G", 3, "Sorcery");
        var result = Scores(Card("needed-creature", "G", 3), pool: Enumerable.Repeat(spell, 20).Concat(Enumerable.Repeat(Red, 15)).ToArray())[0];
        Assert.True(result.DeckImpact.CreatureCountDelta > 0); Assert.True(result.DeckImpact.StructuralGain > 0);
        Assert.Contains(result.Reasons, r => r.Contains("creature"));
    }
    [Fact] public void CreatureHeavyDeckDoesNotGiveBlanketCreatureBonus()
    {
        var result = Scores(Card("another-creature"), .60, CommittedPool())[0];
        Assert.True(result.DeckImpact.MakesProjectedDeck); Assert.Equal(0, result.DeckImpact.StructuralGain);
    }
    [Fact] public void BetterCandidateMakesDeckAndReportsReplacementAndCommonQualityDelta()
    {
        var result = Scores(Card("upgrade"), .63, CommittedPool())[0];
        Assert.True(result.DeckImpact.MakesProjectedDeck); Assert.NotNull(result.DeckImpact.ReplacesCard);
        Assert.True(result.DeckImpact.CommonDeckQualityDelta > 0); Assert.True(result.DeckNeedComponent > .65);
        var black = Card("black", "B");
        var pivotPool = Enumerable.Repeat(Green, 12).Concat(Enumerable.Repeat(Red, 11)).Concat(Enumerable.Repeat(black, 10)).ToArray();
        var pivot = Scores(Card("black-upgrade", "B"), .66, pivotPool)[0];
        Assert.True(pivot.DeckImpact.ColorPairChanged); Assert.Equal("RG", pivot.DeckImpact.BeforePair!.Code);
        Assert.Equal("BG", pivot.DeckImpact.AfterPair!.Code); Assert.True(pivot.DeckImpact.MakesProjectedDeck);
    }
    [Fact] public void RedundantInferiorCandidateHasNoMarginalMembershipValue()
    {
        // Graded deck fit: about 5.7 pp below the cut line is negligible, but no longer an exact step to zero.
        var result = Scores(Card("redundant"), .50, CommittedPool())[0];
        // Phase 4: a clearly excluded card sits at the sideboard floor rather than exactly zero.
        Assert.False(result.DeckImpact.MakesProjectedDeck); Assert.InRange(result.DeckNeedComponent, new ContextualPickScoreConfiguration().DeckFitFloor, new ContextualPickScoreConfiguration().DeckFitFloor + .02);
        Assert.True(result.DeckImpact.CutlineMargin < -.05);
    }
    [Fact] public void FixingOnlyReceivesValueForMeasuredSourceImprovement()
    {
        var result = Fixture([(Card("fixing", "", 0, "Land", [ManaKind.Red, ManaKind.Green]), .56, 10000),
            (Card("comparison", "R"), .55, 10000)], Enumerable.Repeat(Green, 12).Concat(Enumerable.Repeat(Red, 11)).ToArray(), completed: 35).Score().Cards[0];
        Assert.True(result.DeckImpact.MakesProjectedDeck, "Included: " + result.DeckImpact);
        Assert.True(result.DeckImpact.ManaBaseDelta > 0, "Mana: " + result.DeckImpact);
        Assert.InRange(result.DeckNeedComponent, .5, .9);
        var useless = Scores(Card("wrong-fixing", "", 0, "Land", [ManaKind.Blue, ManaKind.White]), pool: CommittedPool())[0];
        Assert.Equal(0, useless.DeckImpact.ManaBaseDelta); Assert.True(useless.DeckNeedComponent <= .1);
    }
    [Fact] public void DuplicateOccurrencesKeepIdentityAndReuseCounterfactualResult()
    {
        var card = Card("duplicate");
        var unique = Fixture([(card, .61, 10000)], CommittedPool()).Score();
        var duplicate = Fixture([(card, .61, 10000), (card, .61, 10000)], CommittedPool()).Score();
        Assert.Equal([0, 1], duplicate.Cards.Select(c => c.PackIndex));
        Assert.Equal(duplicate.Cards[0].Score0To50, duplicate.Cards[1].Score0To50);
        Assert.Same(duplicate.Cards[0].DeckImpact, duplicate.Cards[1].DeckImpact);
        Assert.Equal(unique.Metrics.OptimizerCalls, duplicate.Metrics.OptimizerCalls);
    }
    [Fact] public void CounterfactualsAreDeterministicAndNeverMutateRealPool()
    {
        var f = Fixture([(Card("upgrade"), .61, 10000), (Card("outside", "U"), .64, 10000)], CommittedPool());
        var ids = f.Snapshot.DraftedPool.CardIdentifiers.ToArray(); var first = f.Score(); var second = f.Score();
        Assert.Equal(ids, f.Snapshot.DraftedPool.CardIdentifiers);
        Assert.Equal(first.Cards.Select(c => c.Score0To50), second.Cards.Select(c => c.Score0To50));
        Assert.Equal(first.Cards.Select(c => c.DeckImpact), second.Cards.Select(c => c.DeckImpact), new ImpactComparer());
        Assert.InRange(first.Metrics.OptimizerCalls, 1, 30);
        var generic = Card("generic", "", 2);
        var shared = Fixture([(Card("candidate", "", 2), .61, 10000)], Enumerable.Repeat(generic, 35).ToArray());
        var input = new DeckBuildInput(new(shared.Snapshot.DraftedPool, DraftPoolCompleteness.Complete), shared.Catalog, shared.Stats, shared.Environment);
        var commitment = shared.Archetype.LaneRecommendation.PoolRecommendation.Profile;
        var cached = new CandidateDeckImpactEvaluator(); var uncached = new CandidateDeckImpactEvaluator(reuseEquivalentPairProjections: false);
        Assert.Equal(cached.Evaluate(shared.Snapshot, input, commitment), uncached.Evaluate(shared.Snapshot, input, commitment), new ImpactComparer());
        Assert.Equal(2, cached.OptimizerCalls); Assert.Equal(20, uncached.OptimizerCalls);
    }
    private sealed class ImpactComparer : IEqualityComparer<CandidateDeckImpact>
    {
        public bool Equals(CandidateDeckImpact? a, CandidateDeckImpact? b) => a is not null && b is not null
            && a with { Reasons = b.Reasons } == b && a.Reasons.SequenceEqual(b.Reasons);
        public int GetHashCode(CandidateDeckImpact obj) => obj.GetHashCode();
    }
    [Fact] public void MissingStatsUseExplicitNeutralEstimateAndNeverInventRawGih()
    {
        var f = Fixture([(Card("unknown"), null, 0), (Card("known"), .56, 10000)]);
        var result = f.Score().Cards[0]; Assert.Equal(PickScoreAvailability.EstimatedMissingStatistics, result.Availability);
        Assert.Equal(.5, result.QualityComponent); Assert.Equal(25, result.Score0To50);
        Assert.Null(f.Archetype.Cards[0].StatisticalRecommendation.RawGamesInHandWinRate);
    }
    [Fact] public void NoBaselineMeansUnavailableScoreAndNoProjectionWork()
    {
        var result = Fixture([(Card("unknown"), null, 0), (Card("known"), .56, 10000)], CommittedPool(), baseline: false).Score();
        Assert.All(result.Cards, c => { Assert.Null(c.Score0To50); Assert.Equal(PickScoreAvailability.NoEnvironmentBaseline, c.Availability); });
        Assert.Equal(0, result.Metrics.OptimizerCalls);
    }
    private sealed class Profiles : ILimitedCardRoleProfiles
    { public LimitedCardRole? Find(CardSetCode set, CardIdentifier card) => set.Value == "WOE" && card.Value == "curated" ? LimitedCardRole.HardRemoval : null; }
    [Fact] public void ReliableRolesAndOptionalSetProfilesStaySeparateFromSemanticGuessing()
    {
        var classifier = new LimitedCardRoleClassifier(new Profiles());
        var roles = classifier.Classify(Card("curated", mv: 2));
        Assert.Equal(LimitedCardRole.Creature | LimitedCardRole.EarlyPlay, roles.ReliableRoles);
        Assert.Equal(LimitedCardRole.HardRemoval, roles.CuratedRoles);
        Assert.Equal(LimitedCardRole.None, classifier.Classify(Card("unknown", type: "Instant")).CuratedRoles);
    }
    [Fact] public void MissingMetadataUsesExplicitColorFallbackAndPartialCoordinateStage()
    {
        var unknown = Green with { Identifier = CardIdentifier.Create("unknown"), GameplayMetadata = CardGameplayMetadata.Unknown };
        var result = Scores(unknown, .61, [Green], 35)[0];
        Assert.False(result.DeckImpact.IsAvailable); Assert.Equal(35d / 42, result.DraftProgress);
        Assert.Contains(result.Reasons, r => r.Contains("missing pool cards"));
    }
    [Fact] public void ScoresAreFiniteBoundedMonotoneAndContributionsMatchActualCalibration()
    {
        var previous = -1;
        foreach (var rate in new[] { .1, .45, .52, .56, .60, .66, .95 })
        {
            var score = Scores(Card("candidate"), rate)[0];
            Assert.InRange(score.Score0To50!.Value, 0, 50); Assert.True(score.Score0To50 >= previous); previous = score.Score0To50.Value;
            Assert.InRange(score.ContextualValue!.Value, 0, 1);
            Assert.Equal(score.Score0To50, (int)Math.Round(Math.Clamp(25 + score.Contributions.Sum(c => c.PointsFromNeutral), 0, 50), MidpointRounding.AwayFromZero));
        }
        var flexible = Card("flexible", "U");
        var a = Scores(flexible, .61, [Green, Red, Green, Red])[0];
        var b = Scores(flexible, .61, [Green, Red, Green, Red, Card("unrelated", "")])[0];
        Assert.InRange(Math.Abs(a.Score0To50!.Value - b.Score0To50!.Value), 0, 2);
    }
    [Fact] public void WrongOccurrenceIdentityAndCancelledProjectionFailBeforeReturningScores()
    {
        var f = Fixture([(Card("a"), .56, 10000), (Card("b"), .56, 10000)], CommittedPool());
        var other = f.Snapshot with { CurrentPack = new(f.Snapshot.CurrentPack.Position, f.Snapshot.CurrentPack.AvailableCardIdentifiers.Reverse()) };
        Assert.Throws<ArgumentException>(() => new ContextualPickScoreEngine().Recommend(other, f.Catalog, f.Archetype, f.Stats, f.Environment));
        Assert.Throws<OperationCanceledException>(() => new ContextualPickScoreEngine().Recommend(f.Snapshot, f.Catalog, f.Archetype, f.Stats, f.Environment, token: new(true)));
    }
}
