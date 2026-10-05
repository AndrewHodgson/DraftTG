using DraftTG.ArenaIntegration;
using DraftTG.Domain;

namespace DraftTG.Application.Tests;

public sealed class DraftPoolAssemblyTests
{
    private static readonly ArenaDraftSnapshotAdapter Adapter = new(ArenaCardResolverTests.CreateResolver(
        (101, "A"), (102, "B"), (103, "C"), (104, "D"), (201, "X1"), (201, "X2")));
    private static ArenaCardIdentifierList Cards(params int[] ids) => new(ids.Select(ArenaCardIdentifier.Create));
    private static ArenaDraftPickRecord Pick(int pack, int pick, int card = 101) => new(null, ArenaDraftCoordinate.Create(pack, pick), Cards(card));
    private static ArenaDraftStateSnapshot Active(int pick, params ArenaDraftPickRecord[] history) => new(
        ArenaDraftSessionStatus.Active, ArenaDraftIdentifier.Create("draft-1"), ArenaDraftMode.Quick, "QuickDraft_WOE",
        new(null, ArenaDraftCoordinate.Create(1, pick), Cards(104)), new(history));

    [Fact]
    public void RecoveredInventoryIsAuthoritativeWithoutDoubleCountingExactHistory()
    {
        var state = Active(5, Pick(1, 1), Pick(1, 2, 102)) with
            { RecoveredPool = new(ArenaDraftCoordinate.Create(1, 5), new(Cards(101, 102, 103, 104))) };
        var result = Adapter.Convert(state);
        Assert.Equal(2, result.ResolvedHistory!.Count);
        var pool = result.DraftPool!;
        Assert.Equal(4, pool.TotalCardCount);
        Assert.Equal(4, pool.UniqueCardCount);
        Assert.All(pool.Entries, e => Assert.Equal(1, e.Count));
        Assert.Equal(DraftPoolCompleteness.Complete, pool.Completeness);
        // Duplicate counts also remain authoritative, independent of chronology coverage.
        state = state with { RecoveredPool = new(ArenaDraftCoordinate.Create(1, 5), new(Cards(101, 102, 102, 103))) };
        Assert.Equal(2, Adapter.Convert(state).DraftPool!.Entries.Single(e => e.CardIdentifier.Value == "B").Count);
    }

    [Fact]
    public void MidDraftHistoryOnlyInventoryIsPartialDespiteNoParserError()
    {
        var pool = Adapter.Convert(Active(8, Pick(1, 7))).DraftPool!;
        Assert.Equal(1, pool.TotalCardCount);
        Assert.Equal(DraftPoolCompleteness.Partial, pool.Completeness);
        Assert.Equal(DraftPoolCompleteness.Complete, Adapter.Convert(Active(3, Pick(1, 1), Pick(1, 2))).DraftPool!.Completeness);
    }

    [Fact]
    public void UnknownReliabilityAndUnresolvedIdentitiesAreExplicitWithoutHidingKnownCopies()
    {
        var state = Active(4) with { RecoveredPool = new(ArenaDraftCoordinate.Create(1, 4), new(Cards(101, 201, 999))) };
        var result = Adapter.Convert(state);
        Assert.Null(result.Snapshot); // Existing recommendation mapping still fails closed.
        Assert.Equal(3, result.DraftPool!.TotalCardCount);
        Assert.Equal(1, result.DraftPool.KnownCardCount);
        Assert.Equal(2, result.DraftPool.UnresolvedOccurrenceCount);
        Assert.Equal(DraftPoolCompleteness.Partial, result.DraftPool.Completeness);
        state = state with { PickedCardsDiagnostic = new(ArenaPickedCardsDiagnosticKind.ExactHistoryMismatch, "Contradictory snapshot ignored") };
        Assert.Equal(DraftPoolCompleteness.Unknown, Adapter.Convert(state).DraftPool!.Completeness);
    }

    [Fact]
    public void ExactSelectionsAfterRecoveredCoordinateExtendInventoryAndCompletionKeepsIt()
    {
        var state = Active(4, Pick(1, 3, 102)) with
            { RecoveredPool = new(ArenaDraftCoordinate.Create(1, 3), new(Cards(101, 101))) };
        var pool = Adapter.Convert(state).DraftPool!;
        Assert.Equal(3, pool.TotalCardCount);
        Assert.Equal(2, pool.Entries.Single(e => e.CardIdentifier.Value == "A").Count);
        Assert.Equal(DraftPoolCompleteness.Complete, pool.Completeness);
        var completed = Adapter.Convert(state with { Status = ArenaDraftSessionStatus.Completed, CurrentPack = null });
        Assert.Equal(3, completed.DraftPool!.TotalCardCount);
        Assert.Equal(DraftPoolCompleteness.Partial, completed.DraftPool.Completeness); // Ending does not prove a full final pool.
    }

    [Fact]
    public void FinalRecoveredPoolSurvivesCompletionAndEngineNewSessionClearsIt()
    {
        var engine = new ArenaDraftStateEngine();
        engine.Apply(new ArenaDraftLogEvent.DraftStarted(new("QuickDraft_WOE", ArenaDraftMode.Quick, ArenaDraftIdentifier.Create("draft-1"))));
        engine.Apply(new ArenaDraftLogEvent.PickedCardsObserved(new(ArenaDraftIdentifier.Create("draft-1"), null,
            new(Enumerable.Repeat(ArenaCardIdentifier.Create(101), 42)))));
        engine.Apply(new ArenaDraftLogEvent.DraftCompleted(new("QuickDraft_WOE", ArenaDraftIdentifier.Create("draft-1"))));
        var completed = Adapter.Convert(engine.Current);
        Assert.Null(completed.Snapshot);
        Assert.Equal(42, completed.DraftPool!.TotalCardCount);
        Assert.Equal(DraftPoolCompleteness.Complete, completed.DraftPool.Completeness);
        engine.Apply(new ArenaDraftLogEvent.DraftStarted(new("QuickDraft_WOE", ArenaDraftMode.Quick, ArenaDraftIdentifier.Create("draft-2"))));
        Assert.Equal(0, Adapter.Convert(engine.Current).DraftPool!.TotalCardCount);
        engine.Apply(new ArenaDraftLogEvent.SourceReset());
        Assert.Null(Adapter.Convert(engine.Current).DraftPool);
    }
}
