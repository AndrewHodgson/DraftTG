using System.Collections;

namespace DraftTG.ArenaIntegration;

public sealed record ArenaCardIdentifier
{
    public int Value { get; }

    private ArenaCardIdentifier(int value) => Value = value;

    public static bool TryCreate(int value, out ArenaCardIdentifier? identifier)
    {
        if (value <= 0)
        {
            identifier = null;
            return false;
        }
        identifier = new ArenaCardIdentifier(value);
        return true;
    }

    public static ArenaCardIdentifier Create(int value) =>
        TryCreate(value, out var identifier)
            ? identifier!
            : throw new ArgumentOutOfRangeException(nameof(value), value, "An Arena card identifier must be positive.");
}

public sealed record ArenaDraftIdentifier
{
    public string Value { get; }

    private ArenaDraftIdentifier(string value) => Value = value;

    public static bool TryCreate(string? value, out ArenaDraftIdentifier? identifier)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            identifier = null;
            return false;
        }
        identifier = new ArenaDraftIdentifier(value);
        return true;
    }

    public static ArenaDraftIdentifier Create(string value) =>
        TryCreate(value, out var identifier)
            ? identifier!
            : throw new ArgumentException("An Arena draft identifier must contain non-whitespace text.", nameof(value));
}

public sealed record ArenaDraftCoordinate
{
    private ArenaDraftCoordinate(int pack, int pick) => (Pack, Pick) = (pack, pick);

    public int Pack { get; }
    public int Pick { get; }

    public static bool TryCreate(int pack, int pick, out ArenaDraftCoordinate? coordinate)
    {
        if (pack <= 0 || pick <= 0)
        {
            coordinate = null;
            return false;
        }
        coordinate = new ArenaDraftCoordinate(pack, pick);
        return true;
    }

    public static ArenaDraftCoordinate Create(int pack, int pick) =>
        TryCreate(pack, pick, out var coordinate)
            ? coordinate!
            : throw new ArgumentOutOfRangeException(nameof(pack), $"Arena coordinates must be positive; received {pack}/{pick}.");
}

public enum ArenaDraftModeKind
{
    Premier,
    Traditional,
    Quick,
    PickTwo,
    Unknown
}

public sealed record ArenaDraftMode
{
    private ArenaDraftMode(ArenaDraftModeKind kind, string? unknownEventName = null) =>
        (Kind, UnknownEventName) = (kind, unknownEventName);

    public ArenaDraftModeKind Kind { get; }
    public string? UnknownEventName { get; }

    public static ArenaDraftMode Premier { get; } = new(ArenaDraftModeKind.Premier);
    public static ArenaDraftMode Traditional { get; } = new(ArenaDraftModeKind.Traditional);
    public static ArenaDraftMode Quick { get; } = new(ArenaDraftModeKind.Quick);
    public static ArenaDraftMode PickTwo { get; } = new(ArenaDraftModeKind.PickTwo);
    public static ArenaDraftMode Unknown(string eventName) =>
        new(ArenaDraftModeKind.Unknown, eventName);
}

public sealed class ArenaCardIdentifierList
    : IReadOnlyList<ArenaCardIdentifier>, IEquatable<ArenaCardIdentifierList>
{
    private readonly ArenaCardIdentifier[] _items;

    public ArenaCardIdentifierList(IEnumerable<ArenaCardIdentifier> items) =>
        _items = items.ToArray();

    public ArenaCardIdentifier this[int index] => _items[index];
    public int Count => _items.Length;
    public IEnumerator<ArenaCardIdentifier> GetEnumerator() =>
        ((IEnumerable<ArenaCardIdentifier>)_items).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool Equals(ArenaCardIdentifierList? other) =>
        other is not null && _items.SequenceEqual(other._items);
    public override bool Equals(object? obj) => Equals(obj as ArenaCardIdentifierList);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in _items) hash.Add(item);
        return hash.ToHashCode();
    }
}

public sealed record ArenaDraftStart(
    string EventName,
    ArenaDraftMode Mode,
    ArenaDraftIdentifier? DraftIdentifier = null)
{
    /// <summary>Deduplicates an explicit entry request; never promoted to a draft identifier.</summary>
    public string? EntryRequestIdentifier { get; init; }
}

public sealed record ArenaDraftPackPresentation(
    ArenaDraftIdentifier? DraftIdentifier,
    ArenaDraftCoordinate Coordinate,
    ArenaCardIdentifierList CardIdentifiers);

public sealed record ArenaDraftPickSubmission(
    ArenaDraftIdentifier? DraftIdentifier,
    ArenaDraftCoordinate Coordinate,
    ArenaCardIdentifierList CardIdentifiers);

public sealed record ArenaDraftCompletion(
    string? EventName = null,
    ArenaDraftIdentifier? DraftIdentifier = null)
{
    public ArenaDraftCompletionOrigin Origin { get; init; }
    public ArenaDraftMode? Mode { get; init; }
    /// <summary>Only explicit Quick Draft PickedCards, never a deck or generic CardPool.</summary>
    public ArenaCardIdentifierList? FinalPickedCards { get; init; }
}

public enum ArenaDraftCompletionOrigin { DraftProtocol, DeckSelection }

/// <summary>Quick Draft pool membership, not a chronological sequence of picks.</summary>
public sealed record ArenaPickedCardsSnapshot(
    ArenaDraftIdentifier? DraftIdentifier,
    ArenaDraftCoordinate? CurrentCoordinate,
    ArenaCardIdentifierList RawCardIdentifiers)
{
    public ArenaCardMultiset Cards => new(RawCardIdentifiers);
}

public abstract record ArenaDraftLogEvent
{
    private ArenaDraftLogEvent() { }

    public sealed record SourceReset : ArenaDraftLogEvent;
    public sealed record DraftStarted(ArenaDraftStart Start) : ArenaDraftLogEvent;
    public sealed record PackPresented(ArenaDraftPackPresentation Pack) : ArenaDraftLogEvent;
    public sealed record PickSubmitted(ArenaDraftPickSubmission Pick) : ArenaDraftLogEvent;
    public sealed record PickedCardsObserved(ArenaPickedCardsSnapshot Pool) : ArenaDraftLogEvent;
    public sealed record DraftCompleted(ArenaDraftCompletion Completion) : ArenaDraftLogEvent;
}
