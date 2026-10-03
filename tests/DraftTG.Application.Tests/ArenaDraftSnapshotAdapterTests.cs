using DraftTG.ArenaIntegration;
using DraftTG.Domain;

namespace DraftTG.Application.Tests;

public sealed class ArenaDraftSnapshotAdapterTests
{
    private static readonly ArenaCardResolver Resolver = ArenaCardResolverTests.CreateResolver(
        (101, "A"), (102, "B"), (103, "C"), (104, "D"), (105, "E"), (106, "F"),
        (201, "X1"), (201, "X2"), (202, "Y1"), (202, "Y2"));

    private readonly ArenaDraftSnapshotAdapter _adapter = new(Resolver);

    [Fact]
    public void IdleStateIsUnavailable()
    {
        var result = _adapter.Convert(State(ArenaDraftSessionStatus.Idle));

        AssertUnavailable(result, DraftSnapshotAvailability.Idle);
    }

    [Fact]
    public void CompletedStateIsUnavailable()
    {
        var result = _adapter.Convert(State(ArenaDraftSessionStatus.Completed));

        AssertUnavailable(result, DraftSnapshotAvailability.Completed);
    }

    [Fact]
    public void ActiveStateWithoutCurrentPackIsUnavailable()
    {
        var result = _adapter.Convert(State(ArenaDraftSessionStatus.Active));

        AssertUnavailable(result, DraftSnapshotAvailability.NoCurrentPack);
    }

    [Theory]
    [InlineData(ArenaDraftModeKind.Premier, DraftFormat.BestOfOne)]
    [InlineData(ArenaDraftModeKind.Quick, DraftFormat.BestOfOne)]
    [InlineData(ArenaDraftModeKind.Traditional, DraftFormat.BestOfThree)]
    public void SupportedModeMapsToDomainFormat(
        ArenaDraftModeKind modeKind,
        DraftFormat expectedFormat)
    {
        var result = _adapter.Convert(ActiveState(Mode(modeKind), Pack(1, 1, 101)));

        Assert.Equal(DraftSnapshotAvailability.Ready, result.Availability);
        Assert.Equal(expectedFormat, Assert.IsType<DraftSnapshot>(result.Snapshot).Format);
    }

    [Theory]
    [InlineData(ArenaDraftModeKind.PickTwo)]
    [InlineData(ArenaDraftModeKind.Unknown)]
    public void UnsupportedModeDoesNotGuessDomainFormat(ArenaDraftModeKind modeKind)
    {
        var result = _adapter.Convert(ActiveState(Mode(modeKind), Pack(1, 1, 101)));

        AssertUnavailable(result, DraftSnapshotAvailability.UnsupportedDraftMode);
    }

    [Fact]
    public void CurrentPackPreservesCardOrder()
    {
        var result = _adapter.Convert(ActiveState(ArenaDraftMode.Premier, Pack(1, 1, 101, 102, 103)));

        Assert.Equal(
            ["A", "B", "C"],
            result.Snapshot!.CurrentPack.AvailableCardIdentifiers.Select(identifier => identifier.Value));
    }

    [Fact]
    public void CurrentPackPreservesDuplicateCards()
    {
        var result = _adapter.Convert(ActiveState(ArenaDraftMode.Premier, Pack(1, 1, 101, 101)));

        Assert.Equal(
            ["A", "A"],
            result.Snapshot!.CurrentPack.AvailableCardIdentifiers.Select(identifier => identifier.Value));
    }

    [Fact]
    public void HistoryIsResolvedInCoordinateOrder()
    {
        var result = _adapter.Convert(ActiveState(
            ArenaDraftMode.Premier,
            Pack(1, 3, 103),
            Pick(1, 1, 101),
            Pick(1, 2, 102)));

        Assert.Equal(
            ["A", "B"],
            result.Snapshot!.History.Picks.Select(pick => pick.SelectedCardIdentifier.Value));
        Assert.Equal([1, 2], result.Snapshot.History.Picks.Select(pick => pick.Position.Pick.Value));
    }

    [Fact]
    public void CoordinateGapsArePreservedWithoutSynthesis()
    {
        var result = _adapter.Convert(ActiveState(
            ArenaDraftMode.Premier,
            Pack(1, 4, 103),
            Pick(1, 1, 101),
            Pick(1, 3, 102)));

        Assert.Equal([1, 3], result.Snapshot!.History.Picks.Select(pick => pick.Position.Pick.Value));
    }

    [Fact]
    public void UnresolvedCurrentPackCardPreventsPartialSnapshot()
    {
        var result = _adapter.Convert(ActiveState(ArenaDraftMode.Premier, Pack(1, 1, 101, 999)));

        AssertUnavailable(result, DraftSnapshotAvailability.UnresolvedArenaCard, 999);
    }

    [Fact]
    public void UnresolvedHistoryCardPreventsPartialSnapshot()
    {
        var result = _adapter.Convert(ActiveState(
            ArenaDraftMode.Premier,
            Pack(1, 2, 101),
            Pick(1, 1, 999)));

        AssertUnavailable(result, DraftSnapshotAvailability.UnresolvedArenaCard, 999);
    }

    [Fact]
    public void SeveralUnresolvedCardsAreDeduplicatedInDeterministicOrder()
    {
        var result = _adapter.Convert(ActiveState(
            ArenaDraftMode.Premier,
            Pack(1, 3, 999, 998, 999),
            Pick(1, 1, 997),
            Pick(1, 2, 998)));

        AssertUnavailable(result, DraftSnapshotAvailability.UnresolvedArenaCard, 999, 998, 997);
    }

    [Fact]
    public void AmbiguousCurrentPackCardPreventsGuessedSnapshot()
    {
        var result = _adapter.Convert(ActiveState(
            ArenaDraftMode.Premier,
            Pack(1, 1, 101, 201)));

        AssertAmbiguous(result, [201]);
    }

    [Fact]
    public void AmbiguousHistoricalCardPreventsGuessedSnapshot()
    {
        var result = _adapter.Convert(ActiveState(
            ArenaDraftMode.Premier,
            Pack(1, 2, 101),
            Pick(1, 1, 201)));

        AssertAmbiguous(result, [201]);
    }

    [Fact]
    public void SeveralAmbiguousCardsAreDeduplicatedInFirstSeenOrder()
    {
        var result = _adapter.Convert(ActiveState(
            ArenaDraftMode.Premier,
            Pack(1, 3, 202, 201, 202),
            Pick(1, 1, 201)));

        AssertAmbiguous(result, [202, 201]);
    }

    [Fact]
    public void AmbiguityTakesPrecedenceWhileMissingIdentifiersRemainReported()
    {
        var result = _adapter.Convert(ActiveState(
            ArenaDraftMode.Premier,
            Pack(1, 2, 999, 201)));

        AssertAmbiguous(result, [201], [999]);
    }

    [Fact]
    public void MultiCardHistoricalPickIsUnsupported()
    {
        var result = _adapter.Convert(ActiveState(
            ArenaDraftMode.Premier,
            Pack(1, 2, 103),
            Pick(1, 1, 101, 102)));

        AssertUnavailable(result, DraftSnapshotAvailability.UnsupportedMultiCardPick);
    }

    [Fact]
    public void OutOfRangeCurrentPackCoordinateIsUnsupported()
    {
        var result = _adapter.Convert(ActiveState(ArenaDraftMode.Premier, Pack(4, 1, 101)));

        AssertUnavailable(result, DraftSnapshotAvailability.UnsupportedCoordinate);
    }

    [Fact]
    public void OutOfRangeHistoricalCoordinateIsUnsupported()
    {
        var result = _adapter.Convert(ActiveState(
            ArenaDraftMode.Premier,
            Pack(1, 1, 101),
            Pick(1, 15, 102)));

        AssertUnavailable(result, DraftSnapshotAvailability.UnsupportedCoordinate);
    }

    [Fact]
    public void CompleteActiveDraftConvertsToDomainSnapshot()
    {
        var result = _adapter.Convert(ActiveState(
            ArenaDraftMode.Premier,
            Pack(1, 3, 103, 104, 105, 106),
            Pick(1, 1, 101),
            Pick(1, 2, 102)));

        var snapshot = Assert.IsType<DraftSnapshot>(result.Snapshot);
        Assert.Equal(DraftSnapshotAvailability.Ready, result.Availability);
        Assert.Empty(result.UnresolvedArenaCards);
        Assert.Empty(result.AmbiguousArenaCards);
        Assert.Equal(DraftFormat.BestOfOne, snapshot.Format);
        Assert.Equal(1, snapshot.CurrentPack.Position.Pack.Value);
        Assert.Equal(3, snapshot.CurrentPack.Position.Pick.Value);
        Assert.Equal(["C", "D", "E", "F"],
            snapshot.CurrentPack.AvailableCardIdentifiers.Select(identifier => identifier.Value));
        Assert.Equal(["A", "B"],
            snapshot.History.Picks.Select(pick => pick.SelectedCardIdentifier.Value));
    }

    [Fact]
    public void ConversionIsDeterministicAndPure()
    {
        var state = ActiveState(
            ArenaDraftMode.Premier,
            Pack(1, 2, 103, 104),
            Pick(1, 1, 101));

        var first = _adapter.Convert(state);
        var second = _adapter.Convert(state);

        Assert.Equal(first, second);
    }

    [Fact]
    public void ConversionDoesNotMutateArenaState()
    {
        var state = ActiveState(
            ArenaDraftMode.Premier,
            Pack(1, 2, 103, 104),
            Pick(1, 1, 101));
        var expected = ActiveState(
            ArenaDraftMode.Premier,
            Pack(1, 2, 103, 104),
            Pick(1, 1, 101));

        _adapter.Convert(state);

        Assert.Equal(expected, state);
    }

    private static ArenaDraftStateSnapshot ActiveState(
        ArenaDraftMode mode,
        ArenaDraftPackState currentPack,
        params ArenaDraftPickRecord[] completedPicks) =>
        new(
            ArenaDraftSessionStatus.Active,
            ArenaDraftIdentifier.Create("draft-1"),
            mode,
            "SyntheticDraft",
            currentPack,
            new ArenaDraftPickRecordList(completedPicks));

    private static ArenaDraftStateSnapshot State(ArenaDraftSessionStatus status) =>
        new(status, null, null, null, null, new ArenaDraftPickRecordList([]));

    private static ArenaDraftPackState Pack(int pack, int pick, params int[] identifiers) =>
        new(
            ArenaDraftIdentifier.Create("draft-1"),
            ArenaDraftCoordinate.Create(pack, pick),
            new ArenaCardIdentifierList(identifiers.Select(ArenaCardIdentifier.Create)));

    private static ArenaDraftPickRecord Pick(int pack, int pick, params int[] identifiers) =>
        new(
            ArenaDraftIdentifier.Create("draft-1"),
            ArenaDraftCoordinate.Create(pack, pick),
            new ArenaCardIdentifierList(identifiers.Select(ArenaCardIdentifier.Create)));

    private static ArenaDraftMode Mode(ArenaDraftModeKind kind) => kind switch
    {
        ArenaDraftModeKind.Premier => ArenaDraftMode.Premier,
        ArenaDraftModeKind.Traditional => ArenaDraftMode.Traditional,
        ArenaDraftModeKind.Quick => ArenaDraftMode.Quick,
        ArenaDraftModeKind.PickTwo => ArenaDraftMode.PickTwo,
        ArenaDraftModeKind.Unknown => ArenaDraftMode.Unknown("SyntheticUnknown"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    private static void AssertUnavailable(
        ArenaDraftSnapshotResult result,
        DraftSnapshotAvailability availability,
        params int[] unresolvedArenaIdentifiers)
    {
        Assert.Equal(availability, result.Availability);
        Assert.Null(result.Snapshot);
        Assert.Equal(
            unresolvedArenaIdentifiers,
            result.UnresolvedArenaCards.Select(identifier => identifier.Value));
        Assert.Empty(result.AmbiguousArenaCards);
    }

    private static void AssertAmbiguous(
        ArenaDraftSnapshotResult result,
        int[] ambiguousArenaIdentifiers,
        int[]? unresolvedArenaIdentifiers = null)
    {
        Assert.Equal(DraftSnapshotAvailability.AmbiguousArenaCard, result.Availability);
        Assert.Null(result.Snapshot);
        Assert.Equal(
            ambiguousArenaIdentifiers,
            result.AmbiguousArenaCards.Select(identifier => identifier.Value));
        Assert.Equal(
            unresolvedArenaIdentifiers ?? [],
            result.UnresolvedArenaCards.Select(identifier => identifier.Value));
    }
}
