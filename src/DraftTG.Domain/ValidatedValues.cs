namespace DraftTG.Domain;

public sealed record CardIdentifier
{
    public string Value { get; }

    private CardIdentifier(string value) => Value = value;

    public static bool TryCreate(string? value, out CardIdentifier? identifier)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            identifier = null;
            return false;
        }

        identifier = new CardIdentifier(value);
        return true;
    }

    public static CardIdentifier Create(string value) =>
        TryCreate(value, out var identifier)
            ? identifier!
            : throw new ArgumentException("A card identifier must contain non-whitespace text.", nameof(value));

    public override string ToString() => Value;
}

public sealed record CardSetCode
{
    public string Value { get; }

    private CardSetCode(string value) => Value = value;

    public static bool TryCreate(string? value, out CardSetCode? setCode)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            setCode = null;
            return false;
        }

        setCode = new CardSetCode(value);
        return true;
    }

    public static CardSetCode Create(string value) =>
        TryCreate(value, out var setCode)
            ? setCode!
            : throw new ArgumentException("A set code must contain non-whitespace text.", nameof(value));

    public override string ToString() => Value;
}

public sealed record CollectorNumber
{
    public string Value { get; }

    private CollectorNumber(string value) => Value = value;

    public static bool TryCreate(string? value, out CollectorNumber? collectorNumber)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            collectorNumber = null;
            return false;
        }

        collectorNumber = new CollectorNumber(value);
        return true;
    }

    public static CollectorNumber Create(string value) =>
        TryCreate(value, out var collectorNumber)
            ? collectorNumber!
            : throw new ArgumentException("A collector number must contain non-whitespace text.", nameof(value));

    public override string ToString() => Value;
}

public sealed record PackNumber : IComparable<PackNumber>
{
    public int Value { get; }

    private PackNumber(int value) => Value = value;

    public static bool TryCreate(int value, out PackNumber? number)
    {
        if (value is < 1 or > 3)
        {
            number = null;
            return false;
        }

        number = new PackNumber(value);
        return true;
    }

    public static PackNumber Create(int value) =>
        TryCreate(value, out var number)
            ? number!
            : throw new ArgumentOutOfRangeException(nameof(value), value, "A pack number must be between 1 and 3.");

    public int CompareTo(PackNumber? other) => other is null ? 1 : Value.CompareTo(other.Value);
}

public sealed record PickNumber : IComparable<PickNumber>
{
    public int Value { get; }

    private PickNumber(int value) => Value = value;

    public static bool TryCreate(int value, out PickNumber? number)
    {
        if (value is < 1 or > 14)
        {
            number = null;
            return false;
        }

        number = new PickNumber(value);
        return true;
    }

    public static PickNumber Create(int value) =>
        TryCreate(value, out var number)
            ? number!
            : throw new ArgumentOutOfRangeException(nameof(value), value, "A pick number must be between 1 and 14.");

    public int CompareTo(PickNumber? other) => other is null ? 1 : Value.CompareTo(other.Value);
}
