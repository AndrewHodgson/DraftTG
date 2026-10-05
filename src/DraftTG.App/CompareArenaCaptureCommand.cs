using System.Globalization;
using System.Text.Json;
using DraftTG.Application;
using DraftTG.App.Platform;
using DraftTG.Data;

namespace DraftTG.App;

/// <summary>Explicit developer save only; sequential same-HWND acquisitions, not an atomic compositor snapshot.</summary>
internal static class CompareArenaCaptureCommand
{
    public static int Run(string[] args) => RunAsync(args).GetAwaiter().GetResult();
    private static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows capture comparison requires Windows.");
            if (args.Length != 7 || args[1] != "--manifest" || args[3] != "--output" || args[5] != "--pack-generation"
                || !long.TryParse(args[6], NumberStyles.None, CultureInfo.InvariantCulture, out var generation) || generation < 1)
                throw new ArgumentException("Usage: --compare-arena-capture --manifest <current-pack.json> --output <directory> --pack-generation <token>");
            var manifest = JsonSerializer.Deserialize<OfflineLocalizationManifest>(File.ReadAllText(args[2]),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("Empty pack manifest.");
            var request = OfflineArtworkCommand.CreateRequest(manifest) with { PackGeneration = generation };
            var snapshot = new PackFrameContext(request, DateTimeOffset.UtcNow);
            var output = Path.GetFullPath(args[4]);
            var settings = new OverlaySettingsFile(ApplicationDataPathProviderFactory.CreateDefault());
            var document = JsonSerializer.Deserialize<CalibrationDocument>(await settings.ReadAsync() ?? throw new InvalidDataException("No saved draft-region calibration."))
                ?? throw new InvalidDataException("Invalid calibration.");
            if (document.Version != 1 || !document.Screen.IsValid || !document.Region.IsValid)
                throw new InvalidDataException("Invalid calibration.");
            var window = new GdiBitBltFrameCapture().InspectWindow().Window ?? throw new InvalidOperationException("No Arena rendering HWND found.");
            var region = new NormalizedDraftRegion(
                (document.Screen.X + document.Region.X * document.Screen.Width - window.X) / window.Width,
                (document.Screen.Y + document.Region.Y * document.Screen.Height - window.Y) / window.Height,
                document.Region.Width * document.Screen.Width / window.Width, document.Region.Height * document.Screen.Height / window.Height);
            _ = ArenaDraftCrop.Calculate(window, region);
            var failures = 0;
            foreach (var (label, backend) in new (string, IArenaRegionCapture)[]
                { ("wgc", new WindowsGraphicsCaptureFrameCapture()), ("gdi", new GdiBitBltFrameCapture()) })
            {
                using var coordinator = new ArenaCaptureCoordinator(backend, new(TimeSpan.FromSeconds(5)));
                var path = DebugCaptureArtifacts.CreatePath(Path.Combine(output, label + ".png"), snapshot, 1);
                var candidates = $"Pack: P{manifest.PackNumber}P{manifest.PickNumber}\nExpected card count: {manifest.Cards.Length}\n"
                    + "Developer-supplied candidate snapshot; verify against live Arena state.\nCandidate cards (payload order):\n"
                    + string.Join("\n", manifest.Cards.Select(c => $"  {c.Name}; {c.Id}")) + "\n";
                try
                {
                    if (!ArenaWindowGeometry.SameCaptureGeometry(window, coordinator.Observe()))
                        throw new InvalidDataException("Arena HWND/geometry changed between backend comparisons.");
                    using var frame = await coordinator.AcquireAsync(region, packGeneration: generation);
                    var hashed = frame with { ImageHash = PackFrameSynchronizer.Hash(frame.Image) };
                    coordinator.SynchronizedFrame(hashed);
                    await DebugCaptureArtifacts.SaveAsync(path, hashed, coordinator.DiagnosticText + candidates
                        + $"Capture timestamp: {frame.CapturedAt:O}\nFrame HWND: 0x{frame.WindowHandle.ToInt64():X}\n"
                        + $"WGC presentation time (QPC): {frame.PresentationTime}\n",
                        () =>
                        {
                            if (!ArenaWindowGeometry.SameCaptureGeometry(window, backend.InspectWindow().Window))
                                throw new InvalidDataException("Arena changed before comparison save.");
                        }, default);
                    Console.WriteLine($"{label}: {path}; raw pixel hash {hashed.ImageHash}");
                }
                catch (Exception ex)
                {
                    failures++; coordinator.Fail("Comparison capture", ex);
                    await DebugCaptureArtifacts.SaveFailureAsync(Path.ChangeExtension(path, ".txt"), coordinator.DiagnosticText + candidates, default);
                    Console.Error.WriteLine($"{label}: {ex.GetType().Name}: {ex.Message}");
                }
            }
            return failures == 0 ? 0 : 1;
        }
        catch (Exception ex) { Console.Error.WriteLine($"{ex.GetType().Name}: {ex.Message}"); return 1; }
    }
    private sealed record CalibrationDocument(int Version, OverlayScreen Screen, NormalizedDraftRegion Region);
}
