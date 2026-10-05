using DraftTG.Domain;
using static DraftTG.RecommendationEngine.Tests.ColorCommitmentTests;

namespace DraftTG.RecommendationEngine.Tests;

public sealed class ArchetypeRecommendationTests
{
    private static readonly Card Black = Card("black", MagicColor.Black);
    private static readonly Card Green = Card("green", MagicColor.Green);
    private static readonly Card Blue = Card("blue", MagicColor.Blue);
    private static readonly Card SecondGreen = Card("green-2", MagicColor.Green);
    private static readonly SetArchetypeProfile Profile = new("WOE", "https://example.test/design",
        new[] { "WU", "UB", "BR", "RG", "GW", "WB", "UR", "BG", "RW", "GU" }
            .Select(p => new ArchetypeDefinition("WOE", ArchetypeColorPair.Create(p), p, "Description", "Story")));
    private static Card[] Balanced => Enumerable.Repeat(Black, 7).Concat(Enumerable.Repeat(Green, 7)).ToArray();
    private static ArchetypeContextProfile Analyze(Card[] pool, ColorCommitmentConfiguration? config = null) =>
        ArchetypeContextProfile.Analyze(Profile, ColorCommitmentProfile.From(History(pool), new([Black, Green, Blue]), config));
    private static LaneDraftRecommendation Lane(Card[] pack, double?[] rates, Card[]? pool = null)
    {
        var snapshot = Snapshot(pool ?? Balanced, pack);
        var catalog = new CardCatalog((pool ?? Balanced).Concat(pack).DistinctBy(c => c.Identifier));
        var rows = new LimitedCardStatisticsCatalog(pack.Select((c, i) => new LimitedCardStatistics(c.Identifier,
            GameInHandWinRate: rates[i], GameInHandGameCount: 5000)).DistinctBy(r => r.CardIdentifier));
        var stats = new StatisticalRecommendationEngine().Recommend(snapshot.CurrentPack, rows,
            new([new(CardIdentifier.Create("env"), GameInHandWinRate: .55, GameInHandGameCount: 10000)]));
        var colors = new ContextualRecommendationEngine().Recommend(snapshot, catalog, stats);
        return new LaneContextualRecommendationEngine().Recommend(snapshot, colors, DraftPackObservationHistory.Empty, LimitedStatisticsFormat.QuickDraft);
    }
    private static ArchetypePairStatistics Pair(LaneDraftRecommendation lane, double?[] rates, ArchetypeColorPair? pair = null) =>
        new(new("WOE", LimitedStatisticsFormat.QuickDraft), pair ?? ArchetypeColorPair.Create("BG"),
            new(lane.Cards.Select((c, i) => new LimitedCardStatistics(c.CardIdentifier, GameInHandWinRate: rates[i], GameInHandGameCount: 10000))
                .DistinctBy(r => r.CardIdentifier)), [new(.56, 10000)]);
    private static ArchetypeRecommendationResult Recommend(LaneDraftRecommendation lane, ArchetypePairStatistics? pair = null, SetArchetypeProfile? set = null) =>
        new ArchetypeRecommendationEngine().Recommend(lane, set ?? Profile, new("WOE", LimitedStatisticsFormat.QuickDraft), pair);

    [Fact] public void EmptyPoolHasNoActiveArchetype()
    { var profile = Analyze([]); Assert.Null(profile.Active); Assert.All(profile.Candidates, c => Assert.Equal(0, c.Confidence)); }

    [Fact] public void OneColorEarlyOrLateDoesNotChooseAnArbitrarySecondColor()
    {
        foreach (var picks in new[] { 1, 14 })
        {
            var profile = Analyze(Enumerable.Repeat(Green, picks).ToArray()); Assert.Null(profile.Active);
            Assert.Equal(4, profile.Candidates.Count(c => c.Confidence == profile.Candidates[0].Confidence)); Assert.Equal(0, profile.Lead);
        }
    }

    [Fact] public void BalancedLaterPoolActivatesBgAndCanChangeAfterLaterPicks()
    {
        Assert.Equal("BG", Analyze(Balanced).Active!.Definition.Pair.Code);
        var changed = Analyze(Enumerable.Repeat(Blue, 7).Concat(Enumerable.Repeat(Green, 7)).ToArray());
        Assert.Equal("UG", changed.Active!.Definition.Pair.Code); Assert.Equal("GU", changed.Active.Definition.Pair.DisplayCode);
    }

    [Fact] public void NearlyEqualTopPairsStayUnsettledEvenWithStrongConfidence()
    {
        var profile = Analyze(Enumerable.Repeat(Black, 5).Concat(Enumerable.Repeat(Green, 5)).Concat(Enumerable.Repeat(Blue, 4)).ToArray());
        Assert.True(profile.Candidates[0].Confidence > .35); Assert.True(profile.Lead < .10); Assert.Null(profile.Active);
    }

    [Fact] public void ExactCoverageBalanceFitAndProgressRegression()
    {
        var pool = Enumerable.Repeat(Black, 3).Concat(Enumerable.Repeat(Green, 3)).ToArray();
        var evidence = Analyze(pool, new(fullCommitmentAfterPicks: 12)).Candidates.Single(c => c.Definition.Pair.Code == "BG");
        Assert.Equal(1, evidence.Coverage); Assert.Equal(1, evidence.Balance); Assert.Equal(1, evidence.PairPoolFit);
        Assert.Equal(.5, evidence.ProgressFactor); Assert.Equal(.5, evidence.Confidence);
    }

    [Fact] public void PairShrinkageExactRegression() => Assert.Equal(.59, ArchetypeMath.Shrink(.56, .62, 300, new()), 12);

    [Fact] public void RelativeLiftNormalizationAndBoundedAdjustmentExactRegression()
    {
        var config = new ArchetypeConfiguration(); var lift = ArchetypeMath.Lift(.57, .55, .60, .56);
        Assert.Equal(.02, lift, 12); Assert.Equal(.5, ArchetypeMath.Normalize(lift, config), 12);
        Assert.Equal(.0045, ArchetypeMath.Adjustment(ArchetypeMath.Normalize(lift, config), .60, config), 12);
        Assert.Equal(-1, ArchetypeMath.Normalize(-.5, config)); Assert.Equal(1, ArchetypeMath.Normalize(.5, config));
        Assert.Equal(.015, ArchetypeMath.Adjustment(1, 1, config));
    }

    [Fact] public void PairBaselineUsesSamplesAndIgnoresInvalidOrMissingRows()
    {
        Assert.Equal(.56, ArchetypeMath.Baseline([new(.5, 100), new(.6, 150), new(double.NaN, 1000), new(1.1, 500), new(.9, 0), new(null, 1)]));
        Assert.Null(ArchetypeMath.Baseline([new(null, 5), new(.5, 0)]));
    }

    [Fact] public void UnknownSetPreservesLaneValuesRanksAndPriorStageObjects()
    {
        var lane = Lane([Green, SecondGreen], [.58, .59]);
        var result = new ArchetypeRecommendationEngine().Recommend(lane, null, new("UNKNOWN", LimitedStatisticsFormat.QuickDraft), Pair(lane, [.9, .1]));
        Assert.Null(result.Profile.Active); Assert.Empty(result.Profile.Candidates);
        Assert.All(result.Cards, c => { Assert.Equal(0, c.Affinity.Adjustment); Assert.Equal(c.LaneRecommendation.ContextualValue, c.ContextualValue); });
        Assert.Equal(lane.Cards.Select(c => c.ContextualRank), result.Cards.Select(c => c.ContextualRank)); Assert.Same(lane, result.LaneRecommendation);
    }

    [Fact] public void EmpiricalAffinityCanChangeAClosePickWithoutChangingPreviousStages()
    {
        var lane = Lane([Green, SecondGreen], [.58, .586]); var original = lane.Cards.ToArray();
        var result = Recommend(lane, Pair(lane, [.62, .52]));
        Assert.Equal(1, lane.TopRecommendedPackIndex); Assert.Equal(0, result.TopRecommendedPackIndex);
        Assert.True(result.Cards[0].Affinity.Adjustment > 0); Assert.True(result.Cards[1].Affinity.Adjustment < 0);
        Assert.Equal(original, lane.Cards); Assert.All(result.Cards, c => Assert.Same(original[c.PackIndex], c.LaneRecommendation));
    }

    [Fact] public void LargeLaneValueGapSurvivesMaximumOpposingAffinity()
    {
        var lane = Lane([Green, SecondGreen], [.58, .70]); var result = Recommend(lane, Pair(lane, [.9, .1]));
        Assert.Equal(1, result.TopRecommendedPackIndex); Assert.All(result.Cards, c => Assert.InRange(c.Affinity.Adjustment, -.015, .015));
    }

    [Fact] public void NegativeAffinityCanMoveDownAnOtherwiseCloseOnPairCard()
    {
        var lane = Lane([Green, SecondGreen], [.586, .58]); var result = Recommend(lane, Pair(lane, [.48, .58]));
        Assert.Equal(0, lane.TopRecommendedPackIndex); Assert.Equal(1, result.TopRecommendedPackIndex); Assert.True(result.Cards[0].Affinity.Adjustment < 0);
    }

    [Fact] public void MissingInvalidIneligibleAndUnscoredCardsRemainNeutral()
    {
        Card[] cards = [Green, SecondGreen, Blue, Card("blank"), Card("three", MagicColor.Black, MagicColor.Green, MagicColor.Blue)];
        var lane = Lane(cards, [.58, null, .58, .58, .58]); var result = Recommend(lane, Pair(lane, [null, .9, .9, .9, .9]));
        Assert.All(result.Cards, c => Assert.Equal(0, c.Affinity.Adjustment)); Assert.Null(result.Cards[1].ContextualValue); Assert.Null(result.Cards[1].ContextualRank);
        Assert.Equal(.9, result.Cards[1].Affinity.PairRawGih); Assert.Equal(10000, result.Cards[1].Affinity.PairSample);
        Assert.All(result.Cards.Skip(2), c => Assert.False(c.Affinity.Eligible));
        foreach (var rates in new double?[][] { [double.NaN, .58], [1.1, .58], [null, .58] })
        { var missingLane = Lane([Green, SecondGreen], [.58, .58]); Assert.Equal(0, Recommend(missingLane, Pair(missingLane, rates)).Cards[0].Affinity.Adjustment); }
        var noBaseline = new ArchetypePairStatistics(new("WOE", LimitedStatisticsFormat.QuickDraft), ArchetypeColorPair.Create("BG"), Pair(lane, [.8, .8, .8, .8, .8]).Catalog, []);
        var neutral = Recommend(lane, noBaseline);
        Assert.All(neutral.Cards, c => Assert.Equal(0, c.Affinity.Adjustment));
        Assert.Contains("Pair baseline unavailable", neutral.Cards[0].Affinity.Diagnostic);
        Assert.Equal(.8, neutral.Cards[0].Affinity.PairRawGih);
    }

    [Fact] public void WrongPairOrFormatCannotApplyAndDuplicateOccurrencesRemainDistinct()
    {
        var lane = Lane([Green, SecondGreen, Green], [.58, .58, .58]);
        var wrongPair = Pair(lane, [.9, .1, .9], ArchetypeColorPair.Create("GU"));
        var wrongFormat = new ArchetypePairStatistics(new("WOE", LimitedStatisticsFormat.PremierDraft), ArchetypeColorPair.Create("BG"), wrongPair.Catalog, [new(.56, 100)]);
        foreach (var data in new[] { wrongPair, wrongFormat })
        {
            var result = Recommend(lane, data); Assert.Null(result.PairStatistics); Assert.All(result.Cards, c => Assert.Equal(0, c.Affinity.Adjustment));
            Assert.Equal([1, 2, 3], result.Cards.Select(c => c.ContextualRank)); Assert.Equal([0, 1, 2], result.Cards.Select(c => c.PackIndex));
            Assert.Single(result.Cards, c => c.IsTopContextualCandidate);
        }
    }
}
