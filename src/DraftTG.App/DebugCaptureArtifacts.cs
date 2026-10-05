using System.Security.Cryptography;
using DraftTG.App.Platform;
using SkiaSharp;

namespace DraftTG.App;

internal static class DebugCaptureArtifacts
{
    public static string CreatePath(string template, PackFrameContext context, long captureRequest)
    {
        var position = context.Request.Pack.Position;
        var name = $"{Path.GetFileNameWithoutExtension(template)}-P{position.Pack.Value}P{position.Pick.Value}"
            + $"-gen{context.Request.PackGeneration}-cap{captureRequest}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss-fff}Z-{Guid.NewGuid():N}.png";
        return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(template))!, name);
    }

    public static async Task SaveAsync(string path, ArenaRegionFrame frame, string diagnostics, Action verifyCurrent,
        CancellationToken token)
    {
        var log = Path.ChangeExtension(path, ".txt");
        var temporaryImage = path + ".tmp"; var temporaryLog = log + ".tmp";
        try
        {
            await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var image = SKImage.FromBitmap(frame.Image);
                using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
                var bytes = encoded.ToArray();
                File.WriteAllBytes(temporaryImage, bytes);
                File.WriteAllText(temporaryLog, diagnostics + $"PNG SHA256: {Convert.ToHexString(SHA256.HashData(bytes))}\n"
                    + $"Debug capture: {path}\nDiagnostic log: {log}\n");
            }, token);
            token.ThrowIfCancellationRequested(); verifyCurrent();
            // Commit on the session's UI continuation: no await between generation check and paired publication.
            File.Move(temporaryImage, path); File.Move(temporaryLog, log);
            RetainRecentPairs(path);
        }
        catch
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(log)) File.Delete(log);
            throw;
        }
        finally
        {
            if (File.Exists(temporaryImage)) File.Delete(temporaryImage);
            if (File.Exists(temporaryLog)) File.Delete(temporaryLog);
        }
    }
    public static Task SaveFailureAsync(string log, string diagnostics, CancellationToken token) => Task.Run(() =>
    {
        Directory.CreateDirectory(Path.GetDirectoryName(log)!);
        File.WriteAllText(log, diagnostics + $"Diagnostic log: {log}\n");
        RetainRecentPairs(log);
    }, token);

    private static void RetainRecentPairs(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        var filename = Path.GetFileName(path);
        var prefix = filename[..filename.IndexOf("-P", StringComparison.Ordinal)];
        // Only this debug command's generated top-level pairs; no recursive deletion or generic legacy files.
        var pairs = Directory.EnumerateFiles(directory, prefix + "-P*")
            .Where(f => Path.GetExtension(f) is ".png" or ".txt")
            .GroupBy(f => Path.GetFileNameWithoutExtension(f))
            .OrderByDescending(g => g.Max(File.GetLastWriteTimeUtc)).Skip(20);
        foreach (var pair in pairs) foreach (var file in pair) File.Delete(file);
    }
}
