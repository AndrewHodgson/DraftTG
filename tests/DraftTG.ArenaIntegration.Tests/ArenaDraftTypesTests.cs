using DraftTG.ArenaIntegration;

namespace DraftTG.ArenaIntegration.Tests;

public sealed class ArenaDraftTypesTests
{
    [Fact]
    public void PositiveArenaCardIdentifiersAreAcceptedWithValueEquality()
    {
        var first = ArenaCardIdentifier.Create(104_894);
        var same = ArenaCardIdentifier.Create(104_894);
        var different = ArenaCardIdentifier.Create(104_895);
        Assert.Equal(104_894, first.Value);
        Assert.Equal(first, same);
        Assert.NotEqual(first, different);
        Assert.Equal(2, new HashSet<ArenaCardIdentifier> { first, same, different }.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveArenaCardIdentifiersAreRejected(int value) =>
        Assert.False(ArenaCardIdentifier.TryCreate(value, out _));

    [Fact]
    public void ArenaDraftIdentifiersValidateAndPreserveText()
    {
        const string value = "  Draft-AbC-123  ";
        Assert.Equal(value, ArenaDraftIdentifier.Create(value).Value);
        Assert.False(ArenaDraftIdentifier.TryCreate("", out _));
        Assert.False(ArenaDraftIdentifier.TryCreate(" \t\n", out _));
    }

    [Fact]
    public void ArenaCoordinatesRequirePositiveValuesWithoutMaximums()
    {
        Assert.False(ArenaDraftCoordinate.TryCreate(0, 1, out _));
        Assert.False(ArenaDraftCoordinate.TryCreate(1, 0, out _));
        var coordinate = ArenaDraftCoordinate.Create(4, 20);
        Assert.Equal(4, coordinate.Pack);
        Assert.Equal(20, coordinate.Pick);
    }
}
