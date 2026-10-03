using DraftTG.Domain;

namespace DraftTG.Domain.Tests;

public sealed class DraftModelTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void PackNumbersAcceptOneThroughThree(int value) =>
        Assert.Equal(value, PackNumber.Create(value).Value);

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(4)]
    public void PackNumbersRejectValuesOutsideOneThroughThree(int value) =>
        Assert.False(PackNumber.TryCreate(value, out _));

    [Theory]
    [InlineData(1)]
    [InlineData(14)]
    public void PickNumbersAcceptValidBoundaries(int value) =>
        Assert.Equal(value, PickNumber.Create(value).Value);

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(15)]
    public void PickNumbersRejectValuesOutsideOneThroughFourteen(int value) =>
        Assert.False(PickNumber.TryCreate(value, out _));

    [Fact]
    public void PositionAndPackPreserveValuesOrderAndDuplicates()
    {
        var position = DomainTestSupport.Position(2, 7);
        var duplicate = CardIdentifier.Create("duplicate");
        var other = CardIdentifier.Create("other");
        var pack = new DraftPack(position, [duplicate, other, duplicate]);

        Assert.Equal(2, position.Pack.Value);
        Assert.Equal(7, position.Pick.Value);
        Assert.Equal([duplicate, other, duplicate], pack.AvailableCardIdentifiers);
        Assert.Equal(pack, new DraftPack(position, [duplicate, other, duplicate]));
    }

    [Fact]
    public void HistoryPreservesOrderDuplicatesAndMembership()
    {
        var duplicate = CardIdentifier.Create("same-card");
        var absent = CardIdentifier.Create("absent");
        var first = new DraftPick(DomainTestSupport.Position(1, 1), duplicate);
        var second = new DraftPick(DomainTestSupport.Position(1, 2), duplicate);
        var history = new DraftHistory([first, second]);

        Assert.Equal([first, second], history.Picks);
        Assert.Equal([duplicate, duplicate], history.SelectedCardIdentifiers);
        Assert.Equal(2, history.Count);
        Assert.True(history.Contains(duplicate));
        Assert.False(history.Contains(absent));
    }

    [Fact]
    public void SnapshotStoresImmutableCopiesAndUsesValueEquality()
    {
        var firstPick = new DraftPick(
            DomainTestSupport.Position(1, 1), CardIdentifier.Create("first"));
        var source = new List<DraftPick> { firstPick };
        var pack = new DraftPack(
            DomainTestSupport.Position(1, 2), [CardIdentifier.Create("available")]);
        var snapshot = new DraftSnapshot(pack, new DraftHistory(source), DraftFormat.BestOfOne);

        source.Add(new DraftPick(
            DomainTestSupport.Position(1, 2), CardIdentifier.Create("second")));

        Assert.Single(snapshot.History.Picks);
        Assert.Equal(
            snapshot,
            new DraftSnapshot(
                new DraftPack(pack.Position, pack.AvailableCardIdentifiers),
                new DraftHistory([firstPick]),
                DraftFormat.BestOfOne));
    }
}
