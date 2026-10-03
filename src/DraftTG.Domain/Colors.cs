namespace DraftTG.Domain;

public enum MagicColor : byte
{
    White = 0,
    Blue = 1,
    Black = 2,
    Red = 3,
    Green = 4
}

public readonly record struct ColorSet
{
    private const byte ValidBits = 0b1_1111;
    private readonly byte _bits;

    public static ColorSet Colorless => default;

    public ColorSet(IEnumerable<MagicColor> colors)
    {
        ArgumentNullException.ThrowIfNull(colors);
        byte bits = 0;
        foreach (var color in colors)
        {
            var value = (byte)color;
            if (value > (byte)MagicColor.Green)
            {
                throw new ArgumentOutOfRangeException(nameof(colors), color, "Unknown Magic color.");
            }
            bits |= (byte)(1 << value);
        }
        _bits = (byte)(bits & ValidBits);
    }

    public IReadOnlyList<MagicColor> Colors =>
        Enum.GetValues<MagicColor>().Where(Contains).ToArray();

    public bool IsColorless => _bits == 0;

    public int Count => System.Numerics.BitOperations.PopCount(_bits);

    public bool Contains(MagicColor color)
    {
        var value = (byte)color;
        return value <= (byte)MagicColor.Green && (_bits & (1 << value)) != 0;
    }
}
