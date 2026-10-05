using DraftTG.Domain;
using static DraftTG.RecommendationEngine.Tests.ColorCommitmentTests;

namespace DraftTG.RecommendationEngine.Tests;

public sealed class LaneDetectionTests
{
    private static readonly Card Green = Card("signal-green", MagicColor.Green);
    private static readonly Card White = Card("signal-white", MagicColor.White);
    private static DraftPosition At(int pack, int pick) => new(PackNumber.Create(pack), PickNumber.Create(pick));

    private static DraftPackObservationHistory Observe(DraftPackObservationHistory history, DraftPosition position,
        Card[] cards, double? value = 0.60, double? alsa = 3, double? baseline = 0.55)
    {
        var pack = new DraftPack(position, cards.Select(c => c.Identifier));
        var statistics = new LimitedCardStatisticsCatalog(cards.DistinctBy(c => c.Identifier).Select(c =>
            new LimitedCardStatistics(c.Identifier, GameInHandWinRate: value is { } score && baseline is { } b ? 2 * score - b : null,
                GameInHandGameCount: 500, AverageLastSeenAt: alsa)));
        var environment = new LimitedCardStatisticsCatalog(baseline is { } rate
            ? [new(CardIdentifier.Create("environment"), GameInHandWinRate: rate, GameInHandGameCount: 10000)] : []);
        var statistical = new StatisticalRecommendationEngine().Recommend(pack, statistics, environment);
        return history.Observe(pack, new(cards.DistinctBy(c => c.Identifier)), null, statistical, statistics);
    }
    private static DraftPackObservationHistory Signals(int count)
    {
        var history = DraftPackObservationHistory.Empty;
        for (var i = 0; i < count; i++) history = Observe(history, At(1, 8 + i), [Green]);
        return history;
    }
    private static LaneOpennessProfile Profile(DraftPackObservationHistory history, int pack = 1, int pick = 14,
        LaneDetectionConfiguration? configuration = null) => LaneOpennessProfile.Analyze(history, At(pack, pick), configuration);

    [Fact]
    public void LatenessAndAdjustedQualityMustBothBePositive()
    {
        foreach (var (alsa, value, expected) in new[]
        { (3.0, 0.60, 1.0), (7.0, 0.60, 1.0 / 3), (8.0, 0.60, 0.0), (9.0, 0.60, 0.0),
          (3.0, 0.57, 0.5), (3.0, 0.55, 0.0), (3.0, 0.54, 0.0) })
        {
            var profile = Profile(Observe(DraftPackObservationHistory.Empty, At(1, 8), [Green], value, alsa));
            Assert.Equal(expected, profile.For(MagicColor.Green).Evidence, 12);
            Assert.Equal(expected > 0 ? 1 : 0, profile.EligibleObservationCount);
        }
    }

    [Fact]
    public void InclusivePositionRampStartsWeakAtFourAndReachesFullAtEight()
    {
        foreach (var (pick, expected) in new[] { (1, 0.0), (2, 0.0), (3, 0.0), (4, 0.2), (6, 0.6), (8, 1.0), (10, 1.0) })
        {
            var profile = Profile(Observe(DraftPackObservationHistory.Empty, At(1, pick), [Green], alsa: 0.5));
            Assert.Equal(expected, profile.For(MagicColor.Green).Evidence, 12);
        }
    }

    [Fact]
    public void ColoredEvidenceIsFractionalWhileColorlessContributesNothing()
    {
        foreach (var colors in new[] { Array.Empty<MagicColor>(), new[] { MagicColor.Green },
            new[] { MagicColor.Green, MagicColor.White }, new[] { MagicColor.Green, MagicColor.White, MagicColor.Blue } })
        {
            var card = Card("multi", colors);
            var profile = Profile(Observe(DraftPackObservationHistory.Empty, At(1, 8), [card]));
            Assert.Equal(colors.Length == 0 ? 0 : 1, profile.Colors.Sum(c => c.Evidence), 12);
            foreach (var color in colors) Assert.Equal(1.0 / colors.Length, profile.For(color).Evidence, 12);
        }
    }

    [Fact]
    public void RelativeSupportRegressionUsesFiveColorMeanAndSeparateConfidence()
    {
        var profile = Profile(Signals(2));
        Assert.Equal(2, profile.For(MagicColor.Green).Evidence);
        Assert.Equal(0.4, profile.MeanEvidence, 12);
        Assert.Equal(0.8, profile.For(MagicColor.Green).RawSupport, 12);
        Assert.All(profile.Colors.Where(c => c.Color != MagicColor.Green), c => Assert.Equal(-0.2, c.RawSupport, 12));
        Assert.Equal(0.5, profile.ObservationConfidence);
        Assert.Equal(0.4, profile.For(MagicColor.Green).EffectiveSupport, 12);
    }

    [Fact]
    public void ConfidenceCountsMeaningfulPacksInsteadOfCardsAndCapsAtOne()
    {
        foreach (var (count, expected) in new[] { (0, 0.0), (1, 0.25), (2, 0.5), (4, 1.0), (5, 1.0) })
            Assert.Equal(expected, Profile(Signals(count)).ObservationConfidence);
        var manyCards = Observe(DraftPackObservationHistory.Empty, At(1, 8), Enumerable.Repeat(Green, 10).ToArray());
        var profile = Profile(manyCards);
        Assert.Equal(10, profile.Signals.Count);
        Assert.Equal(1, profile.EligibleObservationCount);
        Assert.Equal(0.25, profile.ObservationConfidence);
        Assert.InRange(profile.For(MagicColor.Green).EffectiveSupport * 0.020, 0, 0.005);
    }

    [Fact]
    public void PackRecencyDecaysEvidenceWithoutDeletingOlderSignals()
    {
        var history = DraftPackObservationHistory.Empty;
        for (var pack = 1; pack <= 3; pack++) history = Observe(history, At(pack, 8), [Green]);
        var profile = Profile(history, pack: 3);
        Assert.Equal([0.25, 0.5, 1], profile.Signals.Select(s => s.PackRecencyWeight));
        Assert.Equal(1.75, profile.For(MagicColor.Green).Evidence, 12);
        Assert.Equal(3, profile.EligibleObservationCount);
    }

    [Fact]
    public void MissingOrInvalidInputsProduceNeutralSupportsWithoutManufacturedValues()
    {
        foreach (var (value, alsa, baseline) in new (double?, double?, double?)[]
        { (null, 3, 0.55), (0.60, null, 0.55), (0.60, 3, null), (0.60, double.NaN, 0.55),
          (0.60, double.PositiveInfinity, 0.55), (0.60, 0, 0.55), (double.NaN, 3, 0.55), (0.60, 3, double.NaN) })
        {
            var profile = Profile(Observe(DraftPackObservationHistory.Empty, At(1, 8), [Green], value, alsa, baseline));
            Assert.Empty(profile.Signals);
            Assert.Equal(0, profile.ObservationConfidence);
            Assert.All(profile.Colors, c => Assert.Equal(0, c.EffectiveSupport));
        }
    }

    [Fact]
    public void ConfigurationIsValidatedAndChangesTheFormulaRatherThanJustMetadata()
    {
        var history = Observe(DraftPackObservationHistory.Empty, At(1, 8), [Green], alsa: 5);
        var configuration = new LaneDetectionConfiguration(lateArrivalScale: 6, laneStrengthScale: 0.08,
            fullLaneConfidenceAfterObservations: 2, laneEvidenceScale: 4);
        var profile = Profile(history, configuration: configuration);
        Assert.Same(configuration, profile.Configuration);
        Assert.Equal(0.3125, profile.For(MagicColor.Green).Evidence, 12);
        Assert.Equal(0.5, profile.ObservationConfidence);
        foreach (var invalid in new Func<LaneDetectionConfiguration>[]
        { () => new(lateArrivalScale: 0), () => new(laneStrengthScale: double.NaN), () => new(laneEvidenceScale: -1),
          () => new(laneSignalStartPick: 0), () => new(laneSignalStartPick: 8, laneSignalFullPick: 4),
          () => new(fullLaneConfidenceAfterObservations: 0), () => new(previousPackEvidenceWeight: double.PositiveInfinity),
          () => new(maxHumanLaneAdjustment: -1), () => new(maxBotLaneAdjustment: 2) })
            Assert.Throws<ArgumentOutOfRangeException>(() => invalid());
    }

    [Fact]
    public void FuturePacksAndFuturePicksDoNotAffectEarlierRecommendations()
    {
        var history = Observe(Signals(1), At(1, 12), [White]);
        history = Observe(history, At(2, 8), [White]);
        var profile = Profile(history, pick: 8);
        Assert.Single(profile.Signals);
        Assert.Equal(0, profile.For(MagicColor.White).Evidence);
        Assert.Equal(1, profile.For(MagicColor.Green).Evidence);
    }

    [Fact]
    public void LaneFitUsesMinimumRequiredColorSupportAndColorlessStaysNeutral()
    {
        var history = Observe(Signals(2), At(1, 10), [White]);
        var profile = Profile(history);
        Assert.Equal(profile.For(MagicColor.Green).EffectiveSupport, profile.Fit(Green.Colors));
        Assert.Equal(0, profile.Fit(ColorSet.Colorless));
        Assert.Equal(profile.For(MagicColor.White).EffectiveSupport, profile.Fit(new([MagicColor.Green, MagicColor.White])));
        Assert.True(profile.Fit(new([MagicColor.Green, MagicColor.White])) > 0);
        Assert.True(profile.Fit(new([MagicColor.Green, MagicColor.Blue])) < 0);
    }

    private static LaneDraftRecommendation Recommend(DraftPackObservationHistory observations, double blueValue = 0.608,
        LimitedStatisticsFormat? format = LimitedStatisticsFormat.QuickDraft, bool unscoredGreen = false)
    {
        var blue = Card("candidate-blue", MagicColor.Blue);
        var green = Card("candidate-green", MagicColor.Green);
        var colorless = Card("candidate-blank");
        var snapshot = new DraftSnapshot(new(At(1, 14), [blue.Identifier, green.Identifier, colorless.Identifier]), new(), DraftFormat.BestOfOne);
        var statistics = new LimitedCardStatisticsCatalog([
            new(blue.Identifier, GameInHandWinRate: 2 * blueValue - 0.55, GameInHandGameCount: 500),
            new(green.Identifier, GameInHandWinRate: unscoredGreen ? null : 0.65, GameInHandGameCount: 500),
            new(colorless.Identifier, GameInHandWinRate: 0.63, GameInHandGameCount: 500)]);
        var statistical = new StatisticalRecommendationEngine().Recommend(snapshot.CurrentPack, statistics,
            new([new(CardIdentifier.Create("environment"), GameInHandWinRate: 0.55, GameInHandGameCount: 10000)]));
        var pool = new ContextualRecommendationEngine().Recommend(snapshot, new([blue, green, colorless]), statistical);
        return new LaneContextualRecommendationEngine().Recommend(snapshot, pool, observations, format);
    }

    [Fact]
    public void RepeatedOpenSignalsCanReorderCloseCardsWithAllEarlierStagesRetained()
    {
        var result = Recommend(Signals(4));
        Assert.Equal(1, result.TopRecommendedPackIndex);
        Assert.Equal(0, result.StatisticalRecommendation.TopRecommendedPackIndex);
        Assert.Equal(0, result.PoolRecommendation.TopRecommendedPackIndex);
        Assert.Same(result.PoolRecommendation.Cards[1], result.Cards[1].PoolRecommendation);
        Assert.Equal(0.60, result.Cards[1].PoolRecommendation.ContextualValue!.Value, 12);
        Assert.Equal(0.61, result.Cards[1].ContextualValue!.Value, 12);
        Assert.Equal([0, 1, 2], result.Cards.Select(c => c.PackIndex));
    }

    [Fact]
    public void LargeGapAllowsPivotAndBotInfluenceIsHalfHumanWithUnknownModeNeutral()
    {
        Assert.Equal(0, Recommend(Signals(4), blueValue: 0.70, format: LimitedStatisticsFormat.PremierDraft).TopRecommendedPackIndex);
        foreach (var (format, maximum, top) in new (LimitedStatisticsFormat?, double, int)[]
        { (LimitedStatisticsFormat.PremierDraft, 0.020, 1), (LimitedStatisticsFormat.TraditionalDraft, 0.020, 1),
          (LimitedStatisticsFormat.QuickDraft, 0.010, 0), (null, 0, 0), ((LimitedStatisticsFormat)99, 0, 0) })
        {
            var result = Recommend(Signals(1), blueValue: 0.602, format: format);
            Assert.Equal(maximum, result.MaximumLaneAdjustment);
            Assert.Equal(top, result.TopRecommendedPackIndex);
            Assert.Equal(0.1 * maximum, result.Cards[1].LaneAdjustment, 12);
        }
    }

    [Fact]
    public void UnscoredCardRemainsUnscoredAndColorlessReceivesNoLaneAdjustment()
    {
        var result = Recommend(Signals(4), unscoredGreen: true);
        Assert.True(result.Cards[1].LaneFit > 0);
        Assert.Null(result.Cards[1].ContextualValue);
        Assert.Null(result.Cards[1].ContextualRank);
        Assert.False(result.Cards[1].IsTopContextualCandidate);
        Assert.Equal(0, result.Cards[2].LaneAdjustment);
        Assert.Equal(result.Cards[2].PoolRecommendation.ContextualValue, result.Cards[2].ContextualValue);
    }

    [Fact]
    public void ClampedFinalTiesPreservePoolThenStatisticalSampleAndSlotTieRules()
    {
        var a = Card("a", MagicColor.Green);
        var b = Card("b", MagicColor.Green);
        var snapshot = new DraftSnapshot(new(At(1, 14), [a.Identifier, b.Identifier, a.Identifier]), new(), DraftFormat.BestOfOne);
        var stats = new StatisticalRecommendationEngine().Recommend(snapshot.CurrentPack,
            new([new(a.Identifier, GameInHandWinRate: 0.99, GameInHandGameCount: 500),
                new(b.Identifier, GameInHandWinRate: 1, GameInHandGameCount: 500)]),
            new([new(CardIdentifier.Create("environment"), GameInHandWinRate: 1, GameInHandGameCount: 10000)]));
        var pool = new ContextualRecommendationEngine().Recommend(snapshot, new([a, b]), stats);
        var result = new LaneContextualRecommendationEngine().Recommend(snapshot, pool, Signals(4), LimitedStatisticsFormat.PremierDraft);
        Assert.All(result.Cards, c => Assert.Equal(1, c.ContextualValue));
        Assert.Equal([2, 1, 3], result.Cards.Select(c => c.ContextualRank));
        Assert.Equal(snapshot.CurrentPack.AvailableCardIdentifiers, result.Cards.Select(c => c.CardIdentifier));
    }

    [Fact]
    public void CurrentPackSuppliesSignalBeforePickingAndReplayCannotDuplicateIt()
    {
        var observed = Observe(DraftPackObservationHistory.Empty, At(1, 8), [Green]);
        var replay = Observe(observed, At(1, 8), [Green]);
        Assert.Single(replay.Observations);
        Assert.Equal(Profile(observed).Colors, Profile(replay).Colors);
        Assert.Equal(0.25, Profile(replay).ObservationConfidence);
        Assert.Null(replay.Observations[0].SelectedCardIdentifier);
        Assert.Equal(3, replay.Observations[0].Cards[0].AverageLastSeenAt);
        Assert.Equal(0.55, replay.Observations[0].EnvironmentBaseline);
    }

    [Fact]
    public void SelectingSignalCardCompletesSameObservationWithoutRemovingOrDoublingEvidence()
    {
        var observed = Observe(DraftPackObservationHistory.Empty, At(1, 8), [Green]);
        var picked = observed.Observe(null, new([Green]), new([new(At(1, 8), Green.Identifier)]));
        Assert.Single(picked.Observations);
        Assert.Equal(Green.Identifier, picked.Observations[0].SelectedCardIdentifier);
        Assert.Equal(Profile(observed).Colors, Profile(picked).Colors);
        Assert.Equal(1, Profile(picked).EligibleObservationCount);
    }

    [Fact]
    public void MidDraftCoverageIsPartialAndOnlyRecordedSignalsContribute()
    {
        var observed = Observe(DraftPackObservationHistory.Empty, At(2, 4), [Green], alsa: 1);
        observed = observed.Observe(null, new([Green]), History(Enumerable.Repeat(Green, 17).ToArray()));
        var profile = Profile(observed, pack: 2);
        Assert.Single(observed.Observations);
        Assert.Equal(At(2, 4), observed.FirstObservedPosition);
        Assert.False(observed.StartsAtBeginning);
        Assert.Equal(0, observed.CompletedPickCoverage);
        Assert.Equal(0.25, profile.ObservationConfidence);
        Assert.Equal(0.2, profile.For(MagicColor.Green).Evidence, 12);
    }
}
