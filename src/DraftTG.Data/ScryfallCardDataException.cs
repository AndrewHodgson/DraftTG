namespace DraftTG.Data;

public enum ScryfallCardDataErrorKind
{
    MetadataRequestFailed,
    InvalidBulkMetadata,
    DefaultCardsNotFound,
    InvalidDownloadUri,
    BulkDownloadFailed,
    InvalidGzip,
    MalformedJsonLine,
    CatalogImportFailed,
    EmptyBulkData,
    CacheInstallationFailed
}

public sealed class ScryfallCardDataException : Exception
{
    public ScryfallCardDataException(
        ScryfallCardDataErrorKind kind,
        string message,
        Exception? innerException = null,
        long? lineNumber = null)
        : base(message, innerException)
    {
        Kind = kind;
        LineNumber = lineNumber;
    }

    public ScryfallCardDataErrorKind Kind { get; }
    public long? LineNumber { get; }
}
