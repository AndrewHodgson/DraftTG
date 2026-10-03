namespace DraftTG.ArenaIntegration;

public enum ArenaDraftLogParseErrorKind
{
    MalformedOuterJson,
    MalformedNestedJson,
    MissingRequiredField,
    InvalidArenaCardIdentifier,
    InvalidDraftIdentifier,
    InvalidCoordinate,
    MalformedCardList,
    UnsupportedPayloadShape
}

public sealed class ArenaDraftLogParseException : Exception
{
    public ArenaDraftLogParseException(
        ArenaDraftLogParseErrorKind kind,
        string detail,
        string? field = null,
        int? pack = null,
        int? pick = null,
        Exception? innerException = null)
        : base($"{kind}: {detail}", innerException)
    {
        Kind = kind;
        Detail = detail;
        Field = field;
        Pack = pack;
        Pick = pick;
    }

    public ArenaDraftLogParseErrorKind Kind { get; }
    public string Detail { get; }
    public string? Field { get; }
    public int? Pack { get; }
    public int? Pick { get; }
}
