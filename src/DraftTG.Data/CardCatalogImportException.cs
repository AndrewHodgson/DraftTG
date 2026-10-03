using DraftTG.Domain;

namespace DraftTG.Data;

public enum CardCatalogImportErrorKind
{
    MalformedJson,
    InvalidCardIdentifier,
    InvalidSetCode,
    InvalidCollectorNumber,
    UnsupportedColorCode,
    UnsupportedRarity,
    DuplicateCardIdentifier,
    InvalidArenaIdentifier,
    DuplicateArenaIdentifier
}

public sealed class CardCatalogImportException : Exception
{
    public CardCatalogImportException(
        CardCatalogImportErrorKind kind,
        string detail,
        Exception? innerException = null,
        CardIdentifier? cardIdentifier = null,
        int? arenaIdentifier = null,
        CardIdentifier? existingCardIdentifier = null,
        CardIdentifier? incomingCardIdentifier = null)
        : base($"{kind}: {detail}", innerException)
    {
        Kind = kind;
        Detail = detail;
        CardIdentifier = cardIdentifier;
        ArenaIdentifier = arenaIdentifier;
        ExistingCardIdentifier = existingCardIdentifier;
        IncomingCardIdentifier = incomingCardIdentifier;
    }

    public CardCatalogImportErrorKind Kind { get; }
    public string Detail { get; }
    public CardIdentifier? CardIdentifier { get; }
    public int? ArenaIdentifier { get; }
    public CardIdentifier? ExistingCardIdentifier { get; }
    public CardIdentifier? IncomingCardIdentifier { get; }
}
