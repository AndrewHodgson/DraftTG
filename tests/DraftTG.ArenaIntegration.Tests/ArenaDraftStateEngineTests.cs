using DraftTG.ArenaIntegration;

namespace DraftTG.ArenaIntegration.Tests;

public sealed class ArenaDraftStateEngineTests
{
    [Fact]
    public void InitialStateIsIdleAndEmpty()
    {
        var snapshot = new ArenaDraftStateEngine().Current;
        Assert.Equal(ArenaDraftSessionStatus.Idle, snapshot.Status);
        Assert.Null(snapshot.DraftIdentifier);
        Assert.Null(snapshot.Mode);
        Assert.Null(snapshot.EventName);
        Assert.Null(snapshot.CurrentPack);
        Assert.Empty(snapshot.CompletedPicks);
        Assert.False(snapshot.IsCompleted);
    }

    [Fact]
    public void SourceResetClearsActiveStateAndDeduplicationFacts()
    {
        var engine = new ArenaDraftStateEngine();
        engine.Apply(Start("draft-a"));
        engine.Apply(Pack(1, 1, [101, 102], "draft-a"));
        engine.Apply(Pick(1, 1, [101], "draft-a"));

        var update = engine.Apply(new ArenaDraftLogEvent.SourceReset());

        Assert.True(update.Changed);
        Assert.Equal(ArenaDraftSessionStatus.Idle, update.Snapshot.Status);
        Assert.Null(update.Snapshot.DraftIdentifier);
        Assert.Null(update.Snapshot.Mode);
        Assert.Null(update.Snapshot.CurrentPack);
        Assert.Empty(update.Snapshot.CompletedPicks);

        // The formerly used coordinate can now hold unrelated data without conflict.
        engine.Apply(Pick(1, 1, [999], "draft-b"));
        Assert.Equal([999], Values(Assert.Single(engine.Current.CompletedPicks).CardIdentifiers));
    }

    [Fact]
    public void SourceResetWhileIdleIsIdempotent()
    {
        var engine = new ArenaDraftStateEngine();
        Assert.False(engine.Apply(new ArenaDraftLogEvent.SourceReset()).Changed);
        Assert.False(engine.Apply(new ArenaDraftLogEvent.SourceReset()).Changed);
    }

    [Fact]
    public void ExplicitStartCapturesSessionIdentity()
    {
        var update = new ArenaDraftStateEngine().Apply(
            Start("draft-a", ArenaDraftMode.Premier, "PremierDraft_TST"));

        Assert.True(update.Changed);
        Assert.Equal(ArenaDraftSessionStatus.Active, update.Snapshot.Status);
        Assert.Equal("draft-a", update.Snapshot.DraftIdentifier?.Value);
        Assert.Equal(ArenaDraftMode.Premier, update.Snapshot.Mode);
        Assert.Equal("PremierDraft_TST", update.Snapshot.EventName);
    }

    [Fact]
    public void DuplicateStartDoesNotDestroyReconstructedState()
    {
        var engine = new ArenaDraftStateEngine();
        var start = Start("draft-a");
        engine.Apply(start);
        engine.Apply(Pack(1, 1, [101, 102], "draft-a"));
        engine.Apply(Pick(1, 1, [101], "draft-a"));

        var update = engine.Apply(start);

        Assert.False(update.Changed);
        Assert.Single(update.Snapshot.CompletedPicks);
        Assert.Null(update.Snapshot.CurrentPack);
    }

    [Fact]
    public void ClearlyDifferentExplicitStartCreatesFreshSession()
    {
        var engine = new ArenaDraftStateEngine();
        engine.Apply(Start("draft-a"));
        engine.Apply(Pick(1, 1, [101], "draft-a"));

        var update = engine.Apply(Start("draft-b", ArenaDraftMode.Traditional, "TradDraft_TST"));

        Assert.True(update.Changed);
        Assert.Equal("draft-b", update.Snapshot.DraftIdentifier?.Value);
        Assert.Equal(ArenaDraftMode.Traditional, update.Snapshot.Mode);
        Assert.Empty(update.Snapshot.CompletedPicks);
        Assert.Null(update.Snapshot.CurrentPack);
    }

    [Fact]
    public void DifferentExplicitIdentityWithoutDraftIdsCreatesFreshSession()
    {
        var engine = new ArenaDraftStateEngine();
        engine.Apply(Start(mode: ArenaDraftMode.Premier, eventName: "PremierDraft_ONE"));
        engine.Apply(Pick(1, 1, [101]));

        engine.Apply(Start(mode: ArenaDraftMode.Quick, eventName: "QuickDraft_TWO"));

        Assert.Equal(ArenaDraftMode.Quick, engine.Current.Mode);
        Assert.Empty(engine.Current.CompletedPicks);
    }

    [Fact]
    public void PackCanInferAnActiveSession()
    {
        var snapshot = new ArenaDraftStateEngine().Apply(Pack(1, 1, [101, 102])).Snapshot;
        Assert.Equal(ArenaDraftSessionStatus.Active, snapshot.Status);
        Assert.NotNull(snapshot.CurrentPack);
        Assert.Null(snapshot.Mode);
        Assert.Null(snapshot.EventName);
    }

    [Fact]
    public void PickCanInferAnActiveSession()
    {
        var snapshot = new ArenaDraftStateEngine().Apply(Pick(1, 3, [101])).Snapshot;
        Assert.Equal(ArenaDraftSessionStatus.Active, snapshot.Status);
        Assert.Single(snapshot.CompletedPicks);
        Assert.Null(snapshot.CurrentPack);
    }

    [Fact]
    public void LaterFactAdoptsMissingDraftIdentifier()
    {
        var engine = new ArenaDraftStateEngine();
        engine.Apply(Pack(1, 1, [101]));
        engine.Apply(Pick(1, 1, [101], "adopted-draft"));
        Assert.Equal("adopted-draft", engine.Current.DraftIdentifier?.Value);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ConflictingDraftIdentifierStartsFreshInferredSession(bool usePack)
    {
        var engine = new ArenaDraftStateEngine();
        engine.Apply(Start("draft-a"));
        engine.Apply(Pick(1, 1, [101], "draft-a"));

        var update = usePack
            ? engine.Apply(Pack(1, 2, [201, 202], "draft-b"))
            : engine.Apply(Pick(1, 2, [201], "draft-b"));

        Assert.True(update.Changed);
        Assert.Equal(ArenaDraftSessionStatus.Active, update.Snapshot.Status);
        Assert.Equal("draft-b", update.Snapshot.DraftIdentifier?.Value);
        Assert.Null(update.Snapshot.Mode);
        Assert.Null(update.Snapshot.EventName);
        Assert.DoesNotContain(update.Snapshot.CompletedPicks, pick =>
            pick.CardIdentifiers.Any(identifier => identifier.Value == 101));
    }

    [Fact]
    public void CurrentPackPreservesOrderingAndDuplicates()
    {
        var pack = new ArenaDraftStateEngine()
            .Apply(Pack(1, 4, [101, 202, 101], "draft-a"))
            .Snapshot.CurrentPack!;

        Assert.Equal(ArenaDraftCoordinate.Create(1, 4), pack.Coordinate);
        Assert.Equal("draft-a", pack.DraftIdentifier?.Value);
        Assert.Equal([101, 202, 101], Values(pack.CardIdentifiers));
    }

    [Fact]
    public void DuplicatePackIsNoChange()
    {
        var engine = new ArenaDraftStateEngine();
        var pack = Pack(1, 1, [101, 102], "draft-a");
        Assert.True(engine.Apply(pack).Changed);
        Assert.False(engine.Apply(Pack(1, 1, [101, 102], "draft-a")).Changed);
        Assert.Equal([101, 102], Values(engine.Current.CurrentPack!.CardIdentifiers));
    }

    [Fact]
    public void ConflictingPackAtSameCoordinateThrowsWithoutOverwritingState()
    {
        var engine = new ArenaDraftStateEngine();
        engine.Apply(Pack(1, 1, [101, 102], "draft-a"));

        var error = Assert.Throws<ArenaDraftStateConflictException>(
            () => engine.Apply(Pack(1, 1, [101, 999], "draft-a")));

        Assert.Equal(ArenaDraftStateConflictKind.PackPresentation, error.Kind);
        Assert.Equal(ArenaDraftCoordinate.Create(1, 1), error.Coordinate);
        Assert.Equal([101, 102], Values(error.ExistingCardIdentifiers));
        Assert.Equal([101, 999], Values(error.IncomingCardIdentifiers));
        Assert.Equal([101, 102], Values(engine.Current.CurrentPack!.CardIdentifiers));
    }

    [Fact]
    public void PickSubmissionStoresOneCompletedPick()
    {
        var record = Assert.Single(
            new ArenaDraftStateEngine().Apply(Pick(1, 3, [123], "draft-a")).Snapshot.CompletedPicks);
        Assert.Equal(ArenaDraftCoordinate.Create(1, 3), record.Coordinate);
        Assert.Equal("draft-a", record.DraftIdentifier?.Value);
        Assert.Equal([123], Values(record.CardIdentifiers));
    }

    [Fact]
    public void MultiCardPickRemainsOneOrderedRecord()
    {
        var picks = new ArenaDraftStateEngine()
            .Apply(Pick(1, 3, [123, 456, 123]))
            .Snapshot.CompletedPicks;
        Assert.Single(picks);
        Assert.Equal([123, 456, 123], Values(picks[0].CardIdentifiers));
    }

    [Fact]
    public void DuplicatePickIsStoredOnceAndReportsNoChange()
    {
        var engine = new ArenaDraftStateEngine();
        var pick = Pick(1, 3, [123], "draft-a");
        engine.Apply(pick);
        var duplicate = engine.Apply(Pick(1, 3, [123], "draft-a"));
        Assert.False(duplicate.Changed);
        Assert.Single(duplicate.Snapshot.CompletedPicks);
    }

    [Fact]
    public void ConflictingPickAtSameCoordinateThrowsWithoutChangingHistory()
    {
        var engine = new ArenaDraftStateEngine();
        engine.Apply(Pick(1, 3, [123], "draft-a"));

        var error = Assert.Throws<ArenaDraftStateConflictException>(
            () => engine.Apply(Pick(1, 3, [456], "draft-a")));

        Assert.Equal(ArenaDraftStateConflictKind.PickSubmission, error.Kind);
        Assert.Equal([123], Values(error.ExistingCardIdentifiers));
        Assert.Equal([456], Values(error.IncomingCardIdentifiers));
        Assert.Equal([123], Values(Assert.Single(engine.Current.CompletedPicks).CardIdentifiers));
    }

    [Fact]
    public void MatchingPickClearsCurrentPack()
    {
        var engine = new ArenaDraftStateEngine();
        engine.Apply(Pack(1, 4, [101, 102]));
        engine.Apply(Pick(1, 4, [101]));
        Assert.Null(engine.Current.CurrentPack);
    }

    [Fact]
    public void UnrelatedPickDoesNotClearCurrentPack()
    {
        var engine = new ArenaDraftStateEngine();
        engine.Apply(Pack(1, 4, [101, 102]));
        engine.Apply(Pick(1, 3, [999]));
        Assert.Equal(ArenaDraftCoordinate.Create(1, 4), engine.Current.CurrentPack?.Coordinate);
    }

    [Fact]
    public void PickWithoutPackIsAccepted()
    {
        var engine = new ArenaDraftStateEngine();
        engine.Apply(Pick(2, 5, [222]));
        Assert.Equal(ArenaDraftCoordinate.Create(2, 5), Assert.Single(engine.Current.CompletedPicks).Coordinate);
    }

    [Fact]
    public void PackAfterRecordedPickDoesNotBecomeActionable()
    {
        var engine = new ArenaDraftStateEngine();
        engine.Apply(Pick(1, 3, [123]));
        var update = engine.Apply(Pack(1, 3, [123, 456]));
        Assert.False(update.Changed);
        Assert.Null(update.Snapshot.CurrentPack);
    }

    [Fact]
    public void CoordinateGapsArePreservedWithoutSynthesis()
    {
        var engine = new ArenaDraftStateEngine();
        engine.Apply(Pick(1, 1, [101]));
        engine.Apply(Pick(1, 3, [103]));
        engine.Apply(Pick(1, 7, [107]));
        Assert.Equal([1, 3, 7], engine.Current.CompletedPicks.Select(record => record.Coordinate.Pick));
    }

    [Fact]
    public void PicksAreExposedInCoordinateOrderRatherThanArrivalOrder()
    {
        var engine = new ArenaDraftStateEngine();
        engine.Apply(Pick(2, 1, [201]));
        engine.Apply(Pick(1, 7, [107]));
        engine.Apply(Pick(1, 1, [101]));

        Assert.Equal(
            [(1, 1), (1, 7), (2, 1)],
            engine.Current.CompletedPicks.Select(record =>
                (record.Coordinate.Pack, record.Coordinate.Pick)));
    }

    [Fact]
    public void HumanSingleCardSequenceProducesCoherentState()
    {
        var engine = new ArenaDraftStateEngine();
        engine.Apply(Start("human", ArenaDraftMode.Premier, "PremierDraft_TST"));
        engine.Apply(Pack(1, 1, [101, 102], "human"));
        engine.Apply(Pick(1, 1, [101], "human"));
        engine.Apply(Pack(1, 2, [102, 103], "human"));
        engine.Apply(Pick(1, 2, [102], "human"));

        Assert.Equal(ArenaDraftSessionStatus.Active, engine.Current.Status);
        Assert.Equal(ArenaDraftMode.Premier, engine.Current.Mode);
        Assert.Equal([101, 102], engine.Current.CompletedPicks.SelectMany(record => Values(record.CardIdentifiers)));
        Assert.Null(engine.Current.CurrentPack);
    }

    [Theory]
    [MemberData(nameof(ModeCases))]
    public void ArenaModesRemainArenaNative(ArenaDraftMode mode, string eventName)
    {
        var snapshot = new ArenaDraftStateEngine().Apply(Start(mode: mode, eventName: eventName)).Snapshot;
        Assert.Equal(mode, snapshot.Mode);
        Assert.Equal(eventName, snapshot.EventName);
        Assert.Equal(ArenaDraftSessionStatus.Active, snapshot.Status);
    }

    [Fact]
    public void QuickStateConsumesAlreadyNormalizedCoordinates()
    {
        var engine = new ArenaDraftStateEngine();
        engine.Apply(Start("quick", ArenaDraftMode.Quick, "QuickDraft_TST"));
        engine.Apply(Pack(2, 7, [501, 502], "quick"));
        engine.Apply(Pick(2, 7, [501], "quick"));
        Assert.Equal(ArenaDraftCoordinate.Create(2, 7), Assert.Single(engine.Current.CompletedPicks).Coordinate);
    }

    [Fact]
    public void CompletionRetainsIdentityAndHistoryWhileClearingPack()
    {
        var engine = new ArenaDraftStateEngine();
        engine.Apply(Start("draft-a", ArenaDraftMode.Premier, "PremierDraft_TST"));
        engine.Apply(Pick(1, 1, [101], "draft-a"));
        engine.Apply(Pack(1, 2, [102, 103], "draft-a"));

        var update = engine.Apply(Complete("draft-a", "PremierDraft_TST"));

        Assert.True(update.Changed);
        Assert.Equal(ArenaDraftSessionStatus.Completed, update.Snapshot.Status);
        Assert.True(update.Snapshot.IsCompleted);
        Assert.Equal("draft-a", update.Snapshot.DraftIdentifier?.Value);
        Assert.Equal(ArenaDraftMode.Premier, update.Snapshot.Mode);
        Assert.Equal("PremierDraft_TST", update.Snapshot.EventName);
        Assert.Single(update.Snapshot.CompletedPicks);
        Assert.Null(update.Snapshot.CurrentPack);
    }

    [Fact]
    public void DuplicateCompletionIsIdempotent()
    {
        var engine = new ArenaDraftStateEngine();
        engine.Apply(Start("draft-a"));
        var completion = Complete("draft-a", "PremierDraft_TST");
        Assert.True(engine.Apply(completion).Changed);
        Assert.False(engine.Apply(completion).Changed);
    }

    [Fact]
    public void SameSessionReplayAfterCompletionDoesNotReopenOrLoseHistory()
    {
        var engine = new ArenaDraftStateEngine();
        var start = Start("draft-a");
        var pack = Pack(1, 1, [101, 102], "draft-a");
        var pick = Pick(1, 1, [101], "draft-a");
        var completion = Complete("draft-a", "PremierDraft_TST");
        engine.Apply(start);
        engine.Apply(pack);
        engine.Apply(pick);
        engine.Apply(completion);

        Assert.False(engine.Apply(start).Changed);
        Assert.False(engine.Apply(pack).Changed);
        Assert.False(engine.Apply(pick).Changed);
        Assert.False(engine.Apply(completion).Changed);
        Assert.Equal(ArenaDraftSessionStatus.Completed, engine.Current.Status);
        Assert.Single(engine.Current.CompletedPicks);
        Assert.Null(engine.Current.CurrentPack);
    }

    [Fact]
    public void LateSameSessionFactsCanEnrichHistoryWithoutReopeningCompletion()
    {
        var engine = new ArenaDraftStateEngine();
        engine.Apply(Start("draft-a"));
        engine.Apply(Pick(1, 1, [101], "draft-a"));
        engine.Apply(Complete("draft-a"));

        var latePick = engine.Apply(Pick(1, 2, [102], "draft-a"));
        var latePack = engine.Apply(Pack(1, 3, [103, 104], "draft-a"));

        Assert.True(latePick.Changed);
        Assert.False(latePack.Changed);
        Assert.Equal(ArenaDraftSessionStatus.Completed, engine.Current.Status);
        Assert.Equal(2, engine.Current.CompletedPicks.Count);
        Assert.Null(engine.Current.CurrentPack);
    }

    [Fact]
    public void NewIdentifierAfterCompletionStartsFreshActiveSession()
    {
        var engine = new ArenaDraftStateEngine();
        engine.Apply(Start("draft-a"));
        engine.Apply(Pick(1, 1, [101], "draft-a"));
        engine.Apply(Complete("draft-a"));

        engine.Apply(Pack(1, 1, [201, 202], "draft-b"));

        Assert.Equal(ArenaDraftSessionStatus.Active, engine.Current.Status);
        Assert.Equal("draft-b", engine.Current.DraftIdentifier?.Value);
        Assert.Empty(engine.Current.CompletedPicks);
        Assert.Equal([201, 202], Values(engine.Current.CurrentPack!.CardIdentifiers));
    }

    [Fact]
    public void PreviouslyReturnedSnapshotDoesNotMutate()
    {
        var engine = new ArenaDraftStateEngine();
        engine.Apply(Pick(1, 1, [101]));
        var previous = engine.Current;
        engine.Apply(Pick(1, 2, [102]));

        Assert.Single(previous.CompletedPicks);
        Assert.Equal(2, engine.Current.CompletedPicks.Count);
    }

    [Fact]
    public void ExposedCollectionsCannotMutateEngineState()
    {
        var sourceCards = new[] { ArenaCardIdentifier.Create(101), ArenaCardIdentifier.Create(102) };
        var cards = new ArenaCardIdentifierList(sourceCards);
        var engine = new ArenaDraftStateEngine();
        engine.Apply(new ArenaDraftLogEvent.PackPresented(
            new ArenaDraftPackPresentation(null, ArenaDraftCoordinate.Create(1, 1), cards)));
        sourceCards[0] = ArenaCardIdentifier.Create(999);

        var snapshot = engine.Current;
        Assert.False((object)snapshot.CompletedPicks is IList<ArenaDraftPickRecord>);
        Assert.False((object)snapshot.CurrentPack!.CardIdentifiers is IList<ArenaCardIdentifier>);
        Assert.Equal([101, 102], Values(snapshot.CurrentPack.CardIdentifiers));
    }

    public static TheoryData<ArenaDraftMode, string> ModeCases => new()
    {
        { ArenaDraftMode.Traditional, "TradDraft_TST" },
        { ArenaDraftMode.PickTwo, "PickTwoDraft_TST" },
        { ArenaDraftMode.Unknown("MysteryDraft_TST"), "MysteryDraft_TST" }
    };

    private static ArenaDraftLogEvent Start(
        string? draftIdentifier = null,
        ArenaDraftMode? mode = null,
        string eventName = "PremierDraft_TST") =>
        new ArenaDraftLogEvent.DraftStarted(
            new ArenaDraftStart(
                eventName,
                mode ?? ArenaDraftMode.Premier,
                draftIdentifier is null ? null : ArenaDraftIdentifier.Create(draftIdentifier)));

    private static ArenaDraftLogEvent Pack(
        int pack,
        int pick,
        int[] cardIdentifiers,
        string? draftIdentifier = null) =>
        new ArenaDraftLogEvent.PackPresented(
            new ArenaDraftPackPresentation(
                draftIdentifier is null ? null : ArenaDraftIdentifier.Create(draftIdentifier),
                ArenaDraftCoordinate.Create(pack, pick),
                Identifiers(cardIdentifiers)));

    private static ArenaDraftLogEvent Pick(
        int pack,
        int pick,
        int[] cardIdentifiers,
        string? draftIdentifier = null) =>
        new ArenaDraftLogEvent.PickSubmitted(
            new ArenaDraftPickSubmission(
                draftIdentifier is null ? null : ArenaDraftIdentifier.Create(draftIdentifier),
                ArenaDraftCoordinate.Create(pack, pick),
                Identifiers(cardIdentifiers)));

    private static ArenaDraftLogEvent Complete(
        string? draftIdentifier = null,
        string? eventName = null) =>
        new ArenaDraftLogEvent.DraftCompleted(
            new ArenaDraftCompletion(
                eventName,
                draftIdentifier is null ? null : ArenaDraftIdentifier.Create(draftIdentifier)));

    private static ArenaCardIdentifierList Identifiers(IEnumerable<int> values) =>
        new(values.Select(ArenaCardIdentifier.Create));

    private static int[] Values(IEnumerable<ArenaCardIdentifier> identifiers) =>
        identifiers.Select(identifier => identifier.Value).ToArray();
}
