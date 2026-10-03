using System.Text.Json.Serialization;

namespace DraftTG.Data;

public sealed record ScryfallCardDataCacheMetadata(
    [property: JsonPropertyName("dataset_type")] string DatasetType,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
    [property: JsonPropertyName("last_checked_at")] DateTimeOffset LastCheckedAt,
    [property: JsonPropertyName("cache_format_version")] int CacheFormatVersion);

public enum ScryfallCardDataLoadSource
{
    Downloaded,
    Cache,
    CacheAfterRefreshFailure
}

public sealed record ScryfallCardDataRefreshDiagnostic(
    string Message,
    ScryfallCardDataErrorKind? ErrorKind = null);

public sealed record ScryfallCardDataLoadResult(
    ScryfallCardCatalogData CardData,
    ScryfallCardDataLoadSource Source,
    DateTimeOffset? DatasetUpdatedAt,
    bool RefreshAttempted,
    ScryfallCardDataRefreshDiagnostic? RefreshDiagnostic = null);

public interface IScryfallCardDataProvider
{
    Task<ScryfallCardDataLoadResult> LoadAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default);
}
