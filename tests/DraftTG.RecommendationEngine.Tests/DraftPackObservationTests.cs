using DraftTG.Domain;
using static DraftTG.RecommendationEngine.Tests.ColorCommitmentTests;

namespace DraftTG.RecommendationEngine.Tests;

public sealed class DraftPackObservationTests
{
    private static readonly Card Green = Card("green", MagicColor.Green);
    private static readonly Card Blue = Card("blue", MagicColor.Blue);
    private static readonly CardCatalog Catalog = new([Green, Blue]);

    [Fact]
    public void OrderedOccurrencesColorsAndBaselineAreCapturedWithoutChangingScores()
    {
        var snapshot = Snapshot([], Blue, Green, Blue);
        var statistical = new StatisticalRecommendationEngine().Recommend(snapshot.CurrentPack,
            new([new(Blue.Identifier, GameInHandWinRate: 0.6, GameInHandGameCount: 500),
                new(Green.Identifier, GameInHandWinRate: 0.58, GameInHandGameCount: 500)]));
        var history = DraftPackObservationHistory.Empty.Observe(snapshot.CurrentPack, Catalog, snapshot.History, statistical);
        var observation = Assert.Single(history.Observations);
        Assert.Equal(snapshot.CurrentPack.Position, observation.Position);
        Assert.Equal([Blue.Identifier, Green.Identifier, Blue.Identifier], observation.Cards.Select(c => c.CardIdentifier));
        Assert.Equal([0, 1, 2], observation.Cards.Select(c => c.PackIndex));
        Assert.Equal(Blue.Colors, observation.Cards[0].Colors);
        Assert.Equal(statistical.Cards[0].AdjustedValue, observation.Cards[0].Phase8AdjustedValue);
        Assert.Equal(0.6, observation.Cards[0].RawGamesInHandWinRate);
        Assert.Equal(500, observation.Cards[0].GamesInHandSampleCount);
        Assert.True(history.StartsAtBeginning);
        Assert.Null(history.CompletedPickCoverage);
        Assert.Null(observation.SelectedCardIdentifier);
    }

    [Fact]
    public void ReplayEnrichesExistingPackAndLaterPickCompletesItEvenWithoutNextPack()
    {
        var snapshot = Snapshot([], Green, Blue);
        var initial = DraftPackObservationHistory.Empty.Observe(snapshot.CurrentPack, Catalog, new());
        var statistical = new StatisticalRecommendationEngine().Recommend(snapshot.CurrentPack,
            new([new(Green.Identifier, GameInHandWinRate: 0.58, GameInHandGameCount: 500)]));
        var replay = initial.Observe(new(snapshot.CurrentPack.Position, snapshot.CurrentPack.AvailableCardIdentifiers), Catalog, new(), statistical);
        Assert.Single(replay.Observations);
        Assert.Null(initial.Observations[0].Cards[0].Phase8AdjustedValue); // earlier immutable snapshot
        Assert.NotNull(replay.Observations[0].Cards[0].Phase8AdjustedValue);
        var completed = replay.Observe(null, Catalog, History(Green));
        Assert.Equal(Green.Identifier, completed.Observations[0].SelectedCardIdentifier);
        Assert.Equal(0, completed.Observations[0].SelectedPackIndex);
        Assert.Equal(1, completed.CompletedPickCoverage);
        var repeated = completed.Observe(snapshot.CurrentPack, Catalog, History(Green));
        Assert.Single(repeated.Observations);
        Assert.Equal(completed.Observations[0].Cards, repeated.Observations[0].Cards);
        Assert.Equal(1, repeated.ObservedCompletedPickCount);
    }

    [Fact]
    public void IdenticalCardsAtDifferentCoordinatesAreDifferentObservedPacks()
    {
        var first = Snapshot([], Green, Blue);
        var second = Snapshot([Green], Green, Blue);
        var history = DraftPackObservationHistory.Empty.Observe(first.CurrentPack, Catalog, new())
            .Observe(second.CurrentPack, Catalog, second.History);
        Assert.Equal(2, history.Observations.Count);
        Assert.Equal(Green.Identifier, history.Observations[0].SelectedCardIdentifier);
        Assert.Null(history.Observations[1].SelectedCardIdentifier);
    }

    [Fact]
    public void OrderedPackIdentityIsPartOfKey()
    {
        var first = Snapshot([], Green, Blue);
        var history = DraftPackObservationHistory.Empty.Observe(first.CurrentPack, Catalog, new())
            .Observe(new(first.CurrentPack.Position, [Blue.Identifier, Green.Identifier]), Catalog, new());
        Assert.Equal(2, history.Observations.Count);
    }

    [Fact]
    public void MidDraftStartupNeverInventsEarlierPacksAndCoverageCountsOnlySeenCompletedPicks()
    {
        var pool = Enumerable.Repeat(Green, 17).ToArray();
        var snapshot = Snapshot(pool, Green, Blue); // P2P4
        var history = DraftPackObservationHistory.Empty.Observe(snapshot.CurrentPack, Catalog, snapshot.History);
        Assert.Single(history.Observations);
        Assert.False(history.StartsAtBeginning);
        Assert.Equal(Position(17), history.FirstObservedPosition);
        Assert.Equal(17, history.KnownCompletedPickCount);
        Assert.Equal(0, history.ObservedCompletedPickCount);
        Assert.Equal(0, history.CompletedPickCoverage);
        var next = Snapshot(pool.Append(Blue).ToArray(), Green);
        history = history.Observe(next.CurrentPack, Catalog, next.History);
        Assert.Equal(2, history.Observations.Count);
        Assert.Equal(1, history.ObservedCompletedPickCount);
        Assert.Equal(1.0 / 18, history.CompletedPickCoverage!.Value, 12);
    }

    [Fact]
    public void DuplicateSelectionKeepsKnownCardWithoutInventingOccurrenceIndex()
    {
        var snapshot = Snapshot([], Green, Blue, Green);
        var history = DraftPackObservationHistory.Empty.Observe(snapshot.CurrentPack, Catalog, new())
            .Observe(null, Catalog, History(Green));
        var observation = Assert.Single(history.Observations);
        Assert.Equal(Green.Identifier, observation.SelectedCardIdentifier);
        Assert.Null(observation.SelectedPackIndex);
        Assert.Equal([0, 2], observation.MatchingSelectedPackIndexes);
    }

    [Fact]
    public void UnavailableStatisticsAndMetadataStayAbsent()
    {
        var snapshot = Snapshot([], Green);
        var observation = Assert.Single(DraftPackObservationHistory.Empty.Observe(snapshot.CurrentPack, new(), new()).Observations);
        Assert.Null(observation.Cards[0].Colors);
        Assert.Null(observation.Cards[0].Phase8AdjustedValue);
        Assert.Null(observation.Cards[0].RawGamesInHandWinRate);
        Assert.Null(observation.Cards[0].GamesInHandSampleCount);
    }

    [Fact]
    public void MismatchedStatisticsCannotBeRecordedAgainstWrongPack()
    {
        var snapshot = Snapshot([], Green);
        var wrong = new StatisticalRecommendationEngine().Recommend(Snapshot([], Blue).CurrentPack, new());
        Assert.Throws<ArgumentException>(() => DraftPackObservationHistory.Empty.Observe(snapshot.CurrentPack, Catalog, new(), wrong));
    }

    [Fact]
    public void UnresolvedLaterHistoryDoesNotEraseKnownSelectionOrCoverage()
    {
        var snapshot = Snapshot([], Green, Blue);
        var completed = DraftPackObservationHistory.Empty.Observe(snapshot.CurrentPack, Catalog, new())
            .Observe(null, Catalog, History(Green));
        var unavailable = completed.Observe(null, Catalog, null);
        Assert.Equal(Green.Identifier, unavailable.Observations[0].SelectedCardIdentifier);
        Assert.Equal(1, unavailable.KnownCompletedPickCount);
        Assert.Equal(1, unavailable.CompletedPickCoverage);
    }
}
