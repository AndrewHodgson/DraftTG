using System.Net;
using System.Text;
using System.Text.Json;
using DraftTG.Data;

namespace DraftTG.Data.Tests;

public sealed class ScryfallCardDataProviderTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-25T12:00:00Z");
    private static readonly DateTimeOffset OldVersion = DateTimeOffset.Parse("2026-09-01T00:00:00Z");
    private static readonly DateTimeOffset NewVersion = DateTimeOffset.Parse("2026-09-20T00:00:00Z");

    [Fact]
    public async Task NoCacheDownloadsValidatesAndInstallsBulkDataAndMetadata()
    {
        using var directory = new TemporaryDirectory();
        var downloaded = CardDataTestFiles.Gzip(
            CardDataTestFiles.Card("printing-a", 101),
            CardDataTestFiles.Card("printing-b"));
        var handler = RoutingHandler(NewVersion, downloaded);
        var provider = CreateProvider(directory.Path, handler);

        var result = await provider.LoadAsync();

        Assert.Equal(ScryfallCardDataLoadSource.Downloaded, result.Source);
        Assert.True(result.RefreshAttempted);
        Assert.Equal(NewVersion, result.DatasetUpdatedAt);
        Assert.Equal(2, result.CardData.Catalog.Count);
        Assert.Equal("printing-a", result.CardData.ArenaIdMappings[101].Value);
        Assert.Equal(2, handler.RequestCount);
        Assert.True(File.Exists(BulkPath(directory.Path)));
        Assert.True(File.Exists(MetadataPath(directory.Path)));
        var metadata = JsonSerializer.Deserialize<ScryfallCardDataCacheMetadata>(
            await File.ReadAllTextAsync(MetadataPath(directory.Path)));
        Assert.Equal(NewVersion, metadata!.UpdatedAt);
        Assert.Equal(Now, metadata.LastCheckedAt);
    }

    [Fact]
    public async Task FirstRunCollisionValidatesAndInstallsWithLocalizedAmbiguity()
    {
        using var directory = new TemporaryDirectory();
        var downloaded = CardDataTestFiles.Gzip(
            CardDataTestFiles.Card("printing-a", 101),
            CardDataTestFiles.Card("printing-b", 101),
            CardDataTestFiles.Card("printing-c", 202));
        var provider = CreateProvider(directory.Path, RoutingHandler(NewVersion, downloaded));

        var result = await provider.LoadAsync();

        Assert.Equal(ScryfallCardDataLoadSource.Downloaded, result.Source);
        Assert.Equal(3, result.CardData.Catalog.Count);
        Assert.Equal(
            ["printing-a", "printing-b"],
            result.CardData.AmbiguousArenaIds[101]
                .Select(identifier => identifier.Value));
        Assert.Equal("printing-c", result.CardData.ArenaIdMappings[202].Value);
        Assert.True(File.Exists(BulkPath(directory.Path)));
        Assert.True(File.Exists(MetadataPath(directory.Path)));
        AssertNoTemporaryFiles(directory.Path);
    }

    [Fact]
    public async Task FirstRunMetadataFailureWithoutCacheIsFocusedFailure()
    {
        using var directory = new TemporaryDirectory();
        var handler = new StubHttpMessageHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var error = await Assert.ThrowsAsync<ScryfallCardDataException>(() =>
            CreateProvider(directory.Path, handler).LoadAsync());

        Assert.Equal(ScryfallCardDataErrorKind.MetadataRequestFailed, error.Kind);
        Assert.False(File.Exists(BulkPath(directory.Path)));
    }

    [Fact]
    public async Task InvalidFirstRunImportInstallsNothingAndRemovesTemporaryFile()
    {
        using var directory = new TemporaryDirectory();
        var invalidData = CardDataTestFiles.Gzip(
            CardDataTestFiles.Card("unsupported", 101, "ultra"));
        var handler = RoutingHandler(NewVersion, invalidData);

        var error = await Assert.ThrowsAsync<ScryfallCardDataException>(() =>
            CreateProvider(directory.Path, handler).LoadAsync());

        Assert.Equal(ScryfallCardDataErrorKind.CatalogImportFailed, error.Kind);
        Assert.False(File.Exists(BulkPath(directory.Path)));
        Assert.False(File.Exists(MetadataPath(directory.Path)));
        AssertNoTemporaryFiles(directory.Path);
    }

    [Fact]
    public async Task ValidStaleCacheRemainsUsableWhenMetadataRequestFails()
    {
        using var directory = new TemporaryDirectory();
        await CardDataTestFiles.WriteCacheAsync(
            directory.Path,
            CardDataTestFiles.Gzip(CardDataTestFiles.Card("cached", 101)),
            OldVersion,
            Now - TimeSpan.FromDays(8));
        var handler = new StubHttpMessageHandler((_, _) =>
            throw new HttpRequestException("offline"));

        var result = await CreateProvider(directory.Path, handler).LoadAsync();

        Assert.Equal(ScryfallCardDataLoadSource.CacheAfterRefreshFailure, result.Source);
        Assert.NotNull(result.RefreshDiagnostic);
        Assert.Equal("cached", Assert.Single(result.CardData.Catalog.Cards).Identifier.Value);
    }

    [Fact]
    public async Task FreshCacheDoesNotMakeHttpRequest()
    {
        using var directory = new TemporaryDirectory();
        await CardDataTestFiles.WriteCacheAsync(
            directory.Path,
            CardDataTestFiles.Gzip(CardDataTestFiles.Card("cached", 101)),
            OldVersion,
            Now - TimeSpan.FromDays(1));
        var handler = new StubHttpMessageHandler((_, _) =>
            throw new InvalidOperationException("HTTP must not be used for a fresh cache."));

        var result = await CreateProvider(directory.Path, handler).LoadAsync();

        Assert.Equal(ScryfallCardDataLoadSource.Cache, result.Source);
        Assert.False(result.RefreshAttempted);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task ForcedRefreshChecksNetworkEvenWhenCacheIsFresh()
    {
        using var directory = new TemporaryDirectory();
        await CardDataTestFiles.WriteCacheAsync(
            directory.Path,
            CardDataTestFiles.Gzip(CardDataTestFiles.Card("cached", 101)),
            OldVersion,
            Now);
        var handler = RoutingHandler(OldVersion, bulkData: null);

        var result = await CreateProvider(directory.Path, handler).LoadAsync(forceRefresh: true);

        Assert.Equal(ScryfallCardDataLoadSource.Cache, result.Source);
        Assert.True(result.RefreshAttempted);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task DueRefreshWithSameVersionDoesNotDownloadBulkFile()
    {
        using var directory = new TemporaryDirectory();
        await CardDataTestFiles.WriteCacheAsync(
            directory.Path,
            CardDataTestFiles.Gzip(CardDataTestFiles.Card("cached", 101)),
            OldVersion,
            Now - TimeSpan.FromDays(8));
        var handler = RoutingHandler(OldVersion, bulkData: null);

        var result = await CreateProvider(directory.Path, handler).LoadAsync();

        Assert.Equal(ScryfallCardDataLoadSource.Cache, result.Source);
        Assert.True(result.RefreshAttempted);
        Assert.Equal(1, handler.RequestCount);
        var metadata = JsonSerializer.Deserialize<ScryfallCardDataCacheMetadata>(
            await File.ReadAllTextAsync(MetadataPath(directory.Path)));
        Assert.Equal(Now, metadata!.LastCheckedAt);
    }

    [Fact]
    public async Task NewRemoteVersionReplacesCacheOnlyAfterValidation()
    {
        using var directory = new TemporaryDirectory();
        await CardDataTestFiles.WriteCacheAsync(
            directory.Path,
            CardDataTestFiles.Gzip(CardDataTestFiles.Card("old", 101)),
            OldVersion,
            Now - TimeSpan.FromDays(8));
        var replacement = CardDataTestFiles.Gzip(CardDataTestFiles.Card("new", 202));
        var handler = RoutingHandler(NewVersion, replacement);

        var result = await CreateProvider(directory.Path, handler).LoadAsync();

        Assert.Equal(ScryfallCardDataLoadSource.Downloaded, result.Source);
        Assert.Equal("new", Assert.Single(result.CardData.Catalog.Cards).Identifier.Value);
        Assert.Equal("new", result.CardData.ArenaIdMappings[202].Value);
        var installed = await new ScryfallJsonlGzipCardDataLoader()
            .LoadAsync(BulkPath(directory.Path));
        Assert.Equal("new", Assert.Single(installed.Catalog.Cards).Identifier.Value);
    }

    [Fact]
    public async Task FailedReplacementDownloadRetainsGoodCache()
    {
        using var directory = new TemporaryDirectory();
        var original = CardDataTestFiles.Gzip(CardDataTestFiles.Card("old", 101));
        await CardDataTestFiles.WriteCacheAsync(
            directory.Path,
            original,
            OldVersion,
            Now - TimeSpan.FromDays(8));
        var handler = new StubHttpMessageHandler((request, _) =>
            request.RequestUri!.Host == "api.scryfall.com"
                ? MetadataResponse(NewVersion)
                : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var result = await CreateProvider(directory.Path, handler).LoadAsync();

        Assert.Equal(ScryfallCardDataLoadSource.CacheAfterRefreshFailure, result.Source);
        Assert.Equal(ScryfallCardDataErrorKind.BulkDownloadFailed,
            result.RefreshDiagnostic!.ErrorKind);
        Assert.Equal(original, await File.ReadAllBytesAsync(BulkPath(directory.Path)));
    }

    [Fact]
    public async Task InvalidReplacementRetainsCacheAndRemovesTemporaryFile()
    {
        using var directory = new TemporaryDirectory();
        var original = CardDataTestFiles.Gzip(CardDataTestFiles.Card("old", 101));
        await CardDataTestFiles.WriteCacheAsync(
            directory.Path,
            original,
            OldVersion,
            Now - TimeSpan.FromDays(8));
        var malformedReplacement = CardDataTestFiles.Gzip(
            CardDataTestFiles.Card("new", 202),
            "{not-json}");
        var handler = RoutingHandler(NewVersion, malformedReplacement);

        var result = await CreateProvider(directory.Path, handler).LoadAsync();

        Assert.Equal(ScryfallCardDataLoadSource.CacheAfterRefreshFailure, result.Source);
        Assert.Equal(ScryfallCardDataErrorKind.MalformedJsonLine,
            result.RefreshDiagnostic!.ErrorKind);
        Assert.Equal(original, await File.ReadAllBytesAsync(BulkPath(directory.Path)));
        AssertNoTemporaryFiles(directory.Path);
    }

    [Fact]
    public async Task CancellationDuringDownloadCleansTemporaryFileAndRetainsCache()
    {
        using var directory = new TemporaryDirectory();
        var original = CardDataTestFiles.Gzip(CardDataTestFiles.Card("old", 101));
        await CardDataTestFiles.WriteCacheAsync(
            directory.Path,
            original,
            OldVersion,
            Now - TimeSpan.FromDays(8));
        var client = new BlockingDownloadBulkClient(NewVersion);
        var provider = CreateProvider(directory.Path, client, new ScryfallJsonlGzipCardDataLoader());
        using var cancellation = new CancellationTokenSource();

        var loading = provider.LoadAsync(cancellationToken: cancellation.Token);
        await client.DownloadStarted.WaitAsync(TimeSpan.FromSeconds(5));
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loading);

        Assert.Equal(original, await File.ReadAllBytesAsync(BulkPath(directory.Path)));
        AssertNoTemporaryFiles(directory.Path);
    }

    [Fact]
    public async Task CancellationDuringImportCleansTemporaryFileAndRetainsCache()
    {
        using var directory = new TemporaryDirectory();
        var original = CardDataTestFiles.Gzip(CardDataTestFiles.Card("old", 101));
        await CardDataTestFiles.WriteCacheAsync(
            directory.Path,
            original,
            OldVersion,
            Now - TimeSpan.FromDays(8));
        var replacement = CardDataTestFiles.Gzip(CardDataTestFiles.Card("new", 202));
        var client = new ImmediateBulkClient(NewVersion, replacement);
        var loader = new BlockingReplacementLoader();
        var provider = CreateProvider(directory.Path, client, loader);
        using var cancellation = new CancellationTokenSource();

        var loading = provider.LoadAsync(cancellationToken: cancellation.Token);
        await loader.ReplacementImportStarted.WaitAsync(TimeSpan.FromSeconds(5));
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loading);

        Assert.Equal(original, await File.ReadAllBytesAsync(BulkPath(directory.Path)));
        AssertNoTemporaryFiles(directory.Path);
    }

    [Fact]
    public void WindowsPathUsesInjectedLocalApplicationDataBase()
    {
        var path = new WindowsApplicationDataPathProvider(Path.Combine("root", "LocalAppData"))
            .GetApplicationDataDirectory();

        Assert.Equal(Path.Combine("root", "LocalAppData", "DraftTG"), path);
    }

    [Fact]
    public void MacOSPathUsesInjectedHomeDirectory()
    {
        var path = new MacOSApplicationDataPathProvider(Path.Combine("root", "home"))
            .GetApplicationDataDirectory();

        Assert.Equal(
            Path.Combine("root", "home", "Library", "Application Support", "DraftTG"),
            path);
    }

    private static ScryfallCardDataProvider CreateProvider(
        string applicationDataPath,
        StubHttpMessageHandler handler) =>
        CreateProvider(
            applicationDataPath,
            new ScryfallBulkDataClient(new HttpClient(handler), "DraftTG.Tests/1.0"),
            new ScryfallJsonlGzipCardDataLoader());

    private static ScryfallCardDataProvider CreateProvider(
        string applicationDataPath,
        IScryfallBulkDataClient client,
        IScryfallCardDataFileLoader loader) =>
        new(
            client,
            loader,
            new FixedApplicationDataPathProvider(applicationDataPath),
            new FixedTimeProvider(Now));

    private static StubHttpMessageHandler RoutingHandler(
        DateTimeOffset version,
        byte[]? bulkData) =>
        new((request, _) =>
        {
            if (request.RequestUri!.Host == "api.scryfall.com")
            {
                return MetadataResponse(version);
            }
            if (bulkData is null)
            {
                throw new InvalidOperationException("The bulk file should not be downloaded.");
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bulkData)
            };
        });

    private static HttpResponseMessage MetadataResponse(DateTimeOffset version) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent($$"""
                {"data":[{"type":"default_cards","updated_at":"{{version:O}}","jsonl_download_uri":"https://data.scryfall.io/default.jsonl.gz"}]}
                """, Encoding.UTF8, "application/json")
        };

    private static string CacheDirectory(string root) => Path.Combine(root, "card-data");
    private static string BulkPath(string root) =>
        Path.Combine(CacheDirectory(root), ScryfallCardDataProvider.BulkFileName);
    private static string MetadataPath(string root) =>
        Path.Combine(CacheDirectory(root), ScryfallCardDataProvider.MetadataFileName);

    private static void AssertNoTemporaryFiles(string root) =>
        Assert.DoesNotContain(
            Directory.EnumerateFiles(CacheDirectory(root)),
            path => path.EndsWith(".download", StringComparison.Ordinal)
                || path.EndsWith(".tmp", StringComparison.Ordinal));

    private sealed class BlockingDownloadBulkClient(DateTimeOffset version)
        : IScryfallBulkDataClient
    {
        private readonly TaskCompletionSource _downloadStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task DownloadStarted => _downloadStarted.Task;

        public Task<ScryfallBulkDataMetadata> GetDefaultCardsMetadataAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ScryfallBulkDataMetadata(
                "default_cards",
                version,
                new Uri("https://data.scryfall.io/default.jsonl.gz")));

        public async Task DownloadAsync(
            Uri downloadUri,
            string destinationPath,
            CancellationToken cancellationToken = default)
        {
            await File.WriteAllBytesAsync(destinationPath, [1, 2, 3], cancellationToken);
            _downloadStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private sealed class ImmediateBulkClient(DateTimeOffset version, byte[] content)
        : IScryfallBulkDataClient
    {
        public Task<ScryfallBulkDataMetadata> GetDefaultCardsMetadataAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ScryfallBulkDataMetadata(
                "default_cards",
                version,
                new Uri("https://data.scryfall.io/default.jsonl.gz")));

        public Task DownloadAsync(
            Uri downloadUri,
            string destinationPath,
            CancellationToken cancellationToken = default) =>
            File.WriteAllBytesAsync(destinationPath, content, cancellationToken);
    }

    private sealed class BlockingReplacementLoader : IScryfallCardDataFileLoader
    {
        private readonly ScryfallJsonlGzipCardDataLoader _realLoader = new();
        private readonly TaskCompletionSource _replacementImportStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ReplacementImportStarted => _replacementImportStarted.Task;

        public async Task<ScryfallCardCatalogData> LoadAsync(
            string filePath,
            CancellationToken cancellationToken = default)
        {
            if (!filePath.EndsWith(".download", StringComparison.Ordinal))
            {
                return await _realLoader.LoadAsync(filePath, cancellationToken);
            }

            _replacementImportStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable after cancellation.");
        }
    }
}
