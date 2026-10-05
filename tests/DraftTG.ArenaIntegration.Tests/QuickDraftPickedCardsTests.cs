namespace DraftTG.ArenaIntegration.Tests;

public sealed class QuickDraftPickedCardsTests
{
    [Fact]
    public void LiveCandyTrailInsertionPreservesKnownP1P3AndConvergesWithExplicitPick()
    {
        var lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "quick-draft-picked-cards-live.jsonl"));
        foreach (var includeExplicitRequest in new[] { false, true })
        {
            var engine = new ArenaDraftStateEngine();
            // Exact-coordinate precondition supplied separately; the first live pool cannot prove P1P3.
            engine.Apply(Pick(1, 3, 86908, draftId: null));
            var parser = new ArenaDraftLogParser();
            foreach (var line in lines.Where((_, index) => includeExplicitRequest || index != 1))
                foreach (var fact in parser.Parse(new ArenaLogSourceEvent.Line(line))) engine.Apply(fact);

            var state = engine.Current;
            Assert.Equal(86908, Assert.Single(state.CompletedPicks.Single(p => p.Coordinate == Coordinate(1, 3)).CardIdentifiers).Value);
            Assert.Equal(86975, Assert.Single(state.CompletedPicks.Single(p => p.Coordinate == Coordinate(1, 10)).CardIdentifiers).Value);
            Assert.Equal(2, state.CompletedPicks.Count);
            Assert.Equal(10, state.DraftedPool.Count);
            Assert.Equal(8, state.UnqualifiedHistoryCardCount);
            Assert.Equal(Coordinate(1, 11), state.CurrentPack!.Coordinate);
            Assert.Null(state.PickedCardsDiagnostic);
        }
    }

    [Theory]
    [InlineData(new int[] { 3, 1, 2 })]
    [InlineData(new int[] { 2, 3, 1 })]
    [InlineData(new int[] { 3, 2, 1 })]
    public void ReorderingAloneProducesNoSemanticChange(int[] permutation)
    {
        var engine = QuickEngine();
        engine.Apply(Pick(1, 1, 1));
        engine.Apply(Pool(1, 4, [1, 2, 3]));
        var before = engine.Current;
        var replay = engine.Apply(Pool(1, 4, permutation));
        Assert.False(replay.Changed);
        Assert.Equal(before, replay.Snapshot);
        Assert.Single(replay.Snapshot.CompletedPicks);
    }

    [Theory]
    [InlineData(new int[] { 1, 1, 2 }, new int[] { 2, 1, 3, 1 }, 3)]
    [InlineData(new int[] { 1, 2 }, new int[] { 1, 1, 2 }, 1)]
    public void DuplicateOccurrencesAreCountedWhenInferringOneAddedCard(int[] previous, int[] current, int added)
    {
        var engine = QuickEngine();
        engine.Apply(Pool(1, previous.Length + 1, previous));
        var result = engine.Apply(Pool(1, current.Length + 1, current)).Snapshot;
        var pick = Assert.Single(result.CompletedPicks);
        Assert.Equal(Coordinate(1, previous.Length + 1), pick.Coordinate);
        Assert.Equal(added, Assert.Single(pick.CardIdentifiers).Value);
        Assert.Equal(current.Length, result.DraftedPool.Count);
        Assert.Equal(current.Count(c => c == 1), result.DraftedPool.CountOf(ArenaCardIdentifier.Create(1)));
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    public void PackBoundaryInfersAtPreviousCoordinate(int previousPack, int nextPack)
    {
        var engine = QuickEngine();
        var previous = Enumerable.Range(1, (previousPack - 1) * 14 + 13).ToArray();
        engine.Apply(Pool(previousPack, 14, previous));
        var current = previous.Prepend(999).ToArray();
        var state = engine.Apply(Pool(nextPack, 1, current)).Snapshot;
        var pick = Assert.Single(state.CompletedPicks);
        Assert.Equal(Coordinate(previousPack, 14), pick.Coordinate);
        Assert.Equal(999, Assert.Single(pick.CardIdentifiers).Value);
        Assert.False(ArenaQuickDraftCoordinates.IsNext(Coordinate(3, 14), Coordinate(4, 1)));
    }

    [Fact]
    public void MissedUpdatesPreservePoolAndExactHistoryThenResumeInference()
    {
        var engine = QuickEngine();
        engine.Apply(Pick(1, 3, 3));
        engine.Apply(Pool(1, 6, [1, 2, 3, 4, 5]));
        var gap = engine.Apply(Pool(1, 9, [8, 3, 7, 2, 6, 1, 5, 4])).Snapshot;
        Assert.Equal(8, gap.DraftedPool.Count);
        Assert.Single(gap.CompletedPicks);
        Assert.Equal(7, gap.UnqualifiedHistoryCardCount);
        var resumed = engine.Apply(Pool(1, 10, [8, 9, 3, 7, 2, 6, 1, 5, 4])).Snapshot;
        Assert.Equal([3, 9], resumed.CompletedPicks.Select(p => p.Coordinate.Pick));
        Assert.Equal(9, Assert.Single(resumed.CompletedPicks[1].CardIdentifiers).Value);
        Assert.Equal(9, resumed.DraftedPool.Count);
        Assert.Equal(7, resumed.UnqualifiedHistoryCardCount);
    }

    [Fact]
    public void MidDraftPoolAndExplicitDuplicateCopyAreMergedWithoutDoubleCounting()
    {
        var engine = QuickEngine();
        engine.Apply(Pick(1, 1, 1));
        var first = engine.Apply(Pool(1, 4, [1, 2, 1])).Snapshot;
        Assert.Single(first.CompletedPicks);
        Assert.Equal(2, first.UnqualifiedHistoryCardCount);
        var pending = engine.Apply(Pick(1, 4, 1)).Snapshot;
        Assert.Equal(4, pending.DraftedPool.Count);
        Assert.Equal(3, pending.DraftedPool.CountOf(ArenaCardIdentifier.Create(1)));
        var confirmed = engine.Apply(Pool(1, 5, [1, 1, 2, 1])).Snapshot;
        Assert.Equal(4, confirmed.DraftedPool.Count);
        Assert.Equal(2, confirmed.CompletedPicks.Count);
        engine.Apply(Pick(1, 2, 1)); // late exact history for an already covered occurrence
        Assert.Equal(4, engine.Current.DraftedPool.Count);
        Assert.Equal(1, engine.Current.UnqualifiedHistoryCardCount);
        engine.Apply(Pick(1, 3, 99)); // late exact evidence inconsistent with the snapshot
        Assert.Equal(ArenaPickedCardsDiagnosticKind.ExactHistoryMismatch, engine.Current.PickedCardsDiagnostic!.Kind);
        Assert.Equal(1, engine.Current.DraftedPool.CountOf(ArenaCardIdentifier.Create(99)));
        Assert.Equal(4, engine.Current.CompletedPicks.Count);
    }

    [Fact]
    public void InferredPickAndLaterExplicitPickAreIdempotentButDifferentCardStillConflicts()
    {
        var engine = QuickEngine();
        engine.Apply(Pool(1, 3, [1, 2]));
        engine.Apply(Pool(1, 4, [86908, 2, 1]));
        Assert.False(engine.Apply(Pick(1, 3, 86908)).Changed);
        var before = engine.Current;
        var conflict = Assert.Throws<ArenaDraftStateConflictException>(() => engine.Apply(Pick(1, 3, 86975)));
        Assert.Equal(ArenaDraftStateConflictKind.PickSubmission, conflict.Kind);
        Assert.Equal(Coordinate(1, 3), conflict.Coordinate);
        Assert.Equal(before, engine.Current);
    }

    [Fact]
    public void InconsistentSnapshotsAreDiagnosedWithoutRewritingPoolOrHistory()
    {
        foreach (var (cards, expected) in new[]
        {
            (new[] { 1, 2 }, ArenaPickedCardsDiagnosticKind.UnexpectedCardCount),
            (new[] { 1, 2, 4 }, ArenaPickedCardsDiagnosticKind.RemovedCards)
        })
        {
            var engine = QuickEngine();
            engine.Apply(Pool(1, 4, [1, 2, 3]));
            var rejected = engine.Apply(Pool(1, 4, cards)).Snapshot;
            Assert.Equal(expected, rejected.PickedCardsDiagnostic!.Kind);
            Assert.Equal([1, 2, 3], rejected.DraftedPool.Occurrences.Select(c => c.Value));
            Assert.Empty(rejected.CompletedPicks);
        }
        var mismatch = QuickEngine();
        mismatch.Apply(Pick(1, 1, 99));
        mismatch.Apply(Pool(1, 3, [1, 2]));
        Assert.Equal(ArenaPickedCardsDiagnosticKind.ExactHistoryMismatch, mismatch.Current.PickedCardsDiagnostic!.Kind);
        Assert.Equal(99, Assert.Single(mismatch.Current.DraftedPool.Occurrences).Value);
        var backward = QuickEngine();
        backward.Apply(Pool(1, 4, [1, 2, 3]));
        backward.Apply(Pool(1, 3, [1, 2]));
        Assert.Equal(ArenaPickedCardsDiagnosticKind.BackwardCoordinate, backward.Current.PickedCardsDiagnostic!.Kind);
        Assert.Equal(3, backward.Current.DraftedPool.Count);
    }

    [Fact]
    public void SourceReplayNewSessionAndCompletionKeepPoolsDeterministic()
    {
        var engine = QuickEngine();
        var facts = new[] { Pool(1, 3, [1, 2]), Pool(1, 4, [3, 2, 1]) };
        foreach (var fact in facts) engine.Apply(fact);
        var before = engine.Current;
        engine.Apply(new ArenaDraftLogEvent.SourceReset());
        engine.Apply(new ArenaDraftLogEvent.DraftStarted(new("QuickDraft_TST", ArenaDraftMode.Quick, ArenaDraftIdentifier.Create("draft-a"))));
        foreach (var fact in facts) engine.Apply(fact);
        Assert.Equal(before, engine.Current);

        var parser = new ArenaDraftLogParser();
        foreach (var fact in parser.Parse(new ArenaLogSourceEvent.Line(
            """{"method":"BotDraftDraftStatus","response":{"EventName":"QuickDraft_TST","DraftId":"draft-a","DraftStatus":"Completed","PickedCards":[3,1,2]}}""")))
            engine.Apply(fact);
        Assert.True(engine.Current.IsCompleted);
        Assert.Equal(3, engine.Current.DraftedPool.Count);
        Assert.Single(engine.Current.CompletedPicks);
        Assert.Null(engine.Current.PickedCardsDiagnostic);

        engine.Apply(Pool(1, 1, [], "draft-b"));
        Assert.Equal(ArenaDraftSessionStatus.Active, engine.Current.Status);
        Assert.Empty(engine.Current.CompletedPicks);
        Assert.Equal(0, engine.Current.DraftedPool.Count);
    }

    private static ArenaDraftStateEngine QuickEngine()
    {
        var engine = new ArenaDraftStateEngine();
        engine.Apply(new ArenaDraftLogEvent.DraftStarted(new("QuickDraft_TST", ArenaDraftMode.Quick, ArenaDraftIdentifier.Create("draft-a"))));
        return engine;
    }
    private static ArenaDraftCoordinate Coordinate(int pack, int pick) => ArenaDraftCoordinate.Create(pack, pick);
    private static ArenaDraftLogEvent Pool(int pack, int pick, int[] cards, string draftId = "draft-a") =>
        new ArenaDraftLogEvent.PickedCardsObserved(new(ArenaDraftIdentifier.Create(draftId), Coordinate(pack, pick),
            new(cards.Select(ArenaCardIdentifier.Create))));
    private static ArenaDraftLogEvent Pick(int pack, int pick, int card, string? draftId = "draft-a") =>
        new ArenaDraftLogEvent.PickSubmitted(new(draftId is null ? null : ArenaDraftIdentifier.Create(draftId),
            Coordinate(pack, pick), new([ArenaCardIdentifier.Create(card)])));
}
