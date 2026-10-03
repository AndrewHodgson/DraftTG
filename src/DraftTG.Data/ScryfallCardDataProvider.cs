using System.Text.Json;

namespace DraftTG.Data;

/// <summary>Loads, refreshes, validates, and safely installs the local Scryfall cache.</summary>
public sealed class ScryfallCardDataProvider : IScryfallCardDataProvider
{
    public const string BulkFileName = "scryfall-default-cards.jsonl.gz";
    public const string MetadataFileName = "metadata.json";
    public static readonly TimeSpan DefaultRefreshInterval = TimeSpan.FromDays(7);

    private const string DatasetType = "default_cards";
    private const int CacheFormatVersion = 1;
    private readonly IScryfallBulkDataClient _bulkDataClient;
    private readonly IScryfallCardDataFileLoader _fileLoader;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _refreshInterval;
    private readonly string _cacheDirectory;
    private readonly string _bulkFilePath;
    private readonly string _metadataFilePath;
    private readonly SemaphoreSlim _loadGate = new(1, 1);

    public ScryfallCardDataProvider(
        IScryfallBulkDataClient bulkDataClient,
        IScryfallCardDataFileLoader fileLoader,
        IApplicationDataPathProvider pathProvider,
        TimeProvider? timeProvider = null,
        TimeSpan? refreshInterval = null)
    {
        _bulkDataClient = bulkDataClient ?? throw new ArgumentNullException(nameof(bulkDataClient));
        _fileLoader = fileLoader ?? throw new ArgumentNullException(nameof(fileLoader));
        ArgumentNullException.ThrowIfNull(pathProvider);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _refreshInterval = refreshInterval ?? DefaultRefreshInterval;
        if (_refreshInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(refreshInterval),
                "The refresh interval must be positive.");
        }

        _cacheDirectory = Path.Combine(pathProvider.GetApplicationDataDirectory(), "card-data");
        _bulkFilePath = Path.Combine(_cacheDirectory, BulkFileName);
        _metadataFilePath = Path.Combine(_cacheDirectory, MetadataFileName);
    }

    public async Task<ScryfallCardDataLoadResult> LoadAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        await _loadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await LoadCoreAsync(forceRefresh, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _loadGate.Release();
        }
    }

    private async Task<ScryfallCardDataLoadResult> LoadCoreAsync(
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(_cacheDirectory);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new ScryfallCardDataException(
                ScryfallCardDataErrorKind.CacheInstallationFailed,
                "DraftTG could not create its card-data cache directory.",
                error);
        }

        var cachedData = await TryLoadCachedDataAsync(cancellationToken).ConfigureAwait(false);
        var cachedMetadata = cachedData is null
            ? null
            : await TryReadMetadataAsync(cancellationToken).ConfigureAwait(false);
        var now = _timeProvider.GetUtcNow();
        var refreshDue = forceRefresh
            || cachedData is null
            || cachedMetadata is null
            || now - cachedMetadata.LastCheckedAt >= _refreshInterval;

        if (!refreshDue)
        {
            return new ScryfallCardDataLoadResult(
                cachedData!,
                ScryfallCardDataLoadSource.Cache,
                cachedMetadata!.UpdatedAt,
                RefreshAttempted: false);
        }

        try
        {
            var remoteMetadata = await _bulkDataClient
                .GetDefaultCardsMetadataAsync(cancellationToken)
                .ConfigureAwait(false);

            if (cachedData is not null
                && cachedMetadata is not null
                && remoteMetadata.UpdatedAt <= cachedMetadata.UpdatedAt)
            {
                var checkedMetadata = cachedMetadata with { LastCheckedAt = now };
                await WriteMetadataAtomicallyAsync(checkedMetadata, cancellationToken)
                    .ConfigureAwait(false);
                return new ScryfallCardDataLoadResult(
                    cachedData,
                    ScryfallCardDataLoadSource.Cache,
                    cachedMetadata.UpdatedAt,
                    RefreshAttempted: true);
            }

            return await DownloadValidateAndInstallAsync(
                remoteMetadata,
                now,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (cachedData is not null)
        {
            return new ScryfallCardDataLoadResult(
                cachedData,
                ScryfallCardDataLoadSource.CacheAfterRefreshFailure,
                cachedMetadata?.UpdatedAt,
                RefreshAttempted: true,
                new ScryfallCardDataRefreshDiagnostic(
                    error.Message,
                    (error as ScryfallCardDataException)?.Kind));
        }
    }

    private async Task<ScryfallCardDataLoadResult> DownloadValidateAndInstallAsync(
        ScryfallBulkDataMetadata remoteMetadata,
        DateTimeOffset checkedAt,
        CancellationToken cancellationToken)
    {
        var temporaryBulkPath = Path.Combine(
            _cacheDirectory,
            $"{BulkFileName}.{Guid.NewGuid():N}.download");
        try
        {
            await _bulkDataClient.DownloadAsync(
                remoteMetadata.JsonlDownloadUri,
                temporaryBulkPath,
                cancellationToken).ConfigureAwait(false);
            var cardData = await _fileLoader
                .LoadAsync(temporaryBulkPath, cancellationToken)
                .ConfigureAwait(false);
            var metadata = new ScryfallCardDataCacheMetadata(
                DatasetType,
                remoteMetadata.UpdatedAt,
                checkedAt,
                CacheFormatVersion);

            await InstallAsync(temporaryBulkPath, metadata, cancellationToken)
                .ConfigureAwait(false);
            return new ScryfallCardDataLoadResult(
                cardData,
                ScryfallCardDataLoadSource.Downloaded,
                remoteMetadata.UpdatedAt,
                RefreshAttempted: true);
        }
        finally
        {
            TryDelete(temporaryBulkPath);
        }
    }

    private async Task InstallAsync(
        string temporaryBulkPath,
        ScryfallCardDataCacheMetadata metadata,
        CancellationToken cancellationToken)
    {
        var temporaryMetadataPath = Path.Combine(
            _cacheDirectory,
            $"{MetadataFileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            await WriteMetadataAsync(temporaryMetadataPath, metadata, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryBulkPath, _bulkFilePath, overwrite: true);
            File.Move(temporaryMetadataPath, _metadataFilePath, overwrite: true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new ScryfallCardDataException(
                ScryfallCardDataErrorKind.CacheInstallationFailed,
                "DraftTG could not install the validated Scryfall card-data cache.",
                error);
        }
        finally
        {
            TryDelete(temporaryMetadataPath);
        }
    }

    private async Task<ScryfallCardCatalogData?> TryLoadCachedDataAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_bulkFilePath)) return null;
        try
        {
            return await _fileLoader.LoadAsync(_bulkFilePath, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (error is ScryfallCardDataException
            or IOException
            or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task<ScryfallCardDataCacheMetadata?> TryReadMetadataAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_metadataFilePath)) return null;
        try
        {
            await using var stream = new FileStream(
                _metadataFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var metadata = await JsonSerializer.DeserializeAsync<ScryfallCardDataCacheMetadata>(
                stream,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return IsValidMetadata(metadata) ? metadata : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (error is JsonException
            or IOException
            or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task WriteMetadataAtomicallyAsync(
        ScryfallCardDataCacheMetadata metadata,
        CancellationToken cancellationToken)
    {
        var temporaryPath = Path.Combine(
            _cacheDirectory,
            $"{MetadataFileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            await WriteMetadataAsync(temporaryPath, metadata, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, _metadataFilePath, overwrite: true);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static async Task WriteMetadataAsync(
        string path,
        ScryfallCardDataCacheMetadata metadata,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await JsonSerializer.SerializeAsync(stream, metadata, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static bool IsValidMetadata(ScryfallCardDataCacheMetadata? metadata) =>
        metadata is
        {
            DatasetType: DatasetType,
            CacheFormatVersion: CacheFormatVersion
        }
        && metadata.UpdatedAt != default
        && metadata.LastCheckedAt != default;

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup must not hide the primary download/import result.
        }
    }
}
