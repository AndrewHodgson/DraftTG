using System.IO.Compression;
using System.Text;
using System.Text.Json;
using DraftTG.Data;

namespace DraftTG.Data.Tests;

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"DraftTG-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }
    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
    }
}

internal static class CardDataTestFiles
{
    internal static string Card(
        string id,
        int? arenaId = null,
        string rarity = "common") => JsonSerializer.Serialize(new
    {
        id,
        arena_id = arenaId,
        name = $"Card {id}",
        colors = new[] { "U" },
        rarity,
        set = "tst",
        collector_number = id
    });

    internal static byte[] Gzip(params string[] lines)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        using (var writer = new StreamWriter(gzip, new UTF8Encoding(false)))
        {
            foreach (var line in lines) writer.WriteLine(line);
        }
        return output.ToArray();
    }

    internal static async Task WriteCacheAsync(
        string applicationDataDirectory,
        byte[] bulkData,
        DateTimeOffset updatedAt,
        DateTimeOffset lastCheckedAt)
    {
        var cacheDirectory = System.IO.Path.Combine(applicationDataDirectory, "card-data");
        Directory.CreateDirectory(cacheDirectory);
        await System.IO.File.WriteAllBytesAsync(
            System.IO.Path.Combine(cacheDirectory, ScryfallCardDataProvider.BulkFileName),
            bulkData);
        var metadata = new ScryfallCardDataCacheMetadata(
            "default_cards",
            updatedAt,
            lastCheckedAt,
            1);
        await System.IO.File.WriteAllTextAsync(
            System.IO.Path.Combine(cacheDirectory, ScryfallCardDataProvider.MetadataFileName),
            JsonSerializer.Serialize(metadata));
    }
}

internal sealed class FixedApplicationDataPathProvider(string path)
    : IApplicationDataPathProvider
{
    public string GetApplicationDataDirectory() => path;
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
