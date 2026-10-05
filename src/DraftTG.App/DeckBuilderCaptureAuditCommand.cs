using System.Globalization;
using System.Text.Json;
using DraftTG.Application;
using DraftTG.ArenaIntegration;
using DraftTG.App.Platform;
using SkiaSharp;

namespace DraftTG.App;

/// <summary>Explicit developer capture only. No input, deck edits, scene navigation, matcher or default image persistence.</summary>
internal static class DeckBuilderCaptureAuditCommand
{
    public static int Run(string[] args) => RunAsync(args).GetAwaiter().GetResult();
    private static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length is not (5 or 7) || args[1] != "--log" || args[3] != "--output" || args.Length == 7 && args[5] != "--region")
                throw new ArgumentException("Usage: --capture-deck-builder --log <Player.log> --output <directory> [--region x,y,width,height]");
            var region = new NormalizedDraftRegion(0, .13, 1, .83);
            if (args.Length == 7)
            {
                var numbers = args[6].Split(',').Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
                if (numbers.Length != 4) throw new ArgumentException("Region requires four normalized numbers.");
                region = new(numbers[0], numbers[1], numbers[2], numbers[3]);
            }
            if (!region.IsValid) throw new ArgumentException("Invalid bounded Arena region.");
            var sceneBefore = Scene(args[2]);
            if (sceneBefore.Name != "DeckBuilder") throw new InvalidOperationException($"Latest Arena scene is {sceneBefore.Name ?? "unknown"}; open the deck builder manually.");
            using var coordinator = new ArenaCaptureCoordinator(new WindowsGraphicsCaptureFrameCapture(), new(TimeSpan.FromSeconds(5)));
            var geometry = coordinator.Observe();
            using var frame = await coordinator.AcquireAsync(region);
            if (Scene(args[2]) != sceneBefore || !ArenaWindowGeometry.SameCaptureGeometry(geometry, coordinator.Observe()))
                throw new InvalidOperationException("Arena scene/window changed during capture; frame discarded.");
            var directory = Path.GetFullPath(args[4]); Directory.CreateDirectory(directory);
            var filename = $"deck-builder-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss-fff}Z";
            var png = Path.Combine(directory, filename + ".png");
            using var image = SKImage.FromBitmap(frame.Image); using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            await File.WriteAllBytesAsync(png, encoded.ToArray());
            await File.WriteAllTextAsync(Path.Combine(directory, filename + ".json"), JsonSerializer.Serialize(new
            {
                frame.Backend, frame.CapturedAt, frame.CaptureDuration, Region = region,
                Width = frame.Image.Width, Height = frame.Image.Height, Scene = sceneBefore.Name,
                SceneRecord = sceneBefore.Ordinal, Diagnostics = coordinator.DiagnosticText,
                CurrentEditorCountsConfirmed = false, InputAutomation = false
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Explicit Arena-only cropped capture: {png}; {frame.Image.Width}x{frame.Image.Height}; {frame.Backend}");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine($"{error.GetType().Name}: {error.Message}"); return 1; }
    }

    private static (string? Name, int Ordinal) Scene(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(file); var parser = new ArenaDeckLogParser();
        string? scene = null; var ordinal = 0; var last = 0;
        while (reader.ReadLine() is { } line)
        {
            ordinal++;
            if (!line.Contains("Client.SceneChange ", StringComparison.Ordinal)) continue;
            scene = parser.Parse(new ArenaLogSourceEvent.Line(line)).OfType<ArenaDeckLogEvent.SceneChanged>().LastOrDefault()?.Scene;
            last = ordinal; // A malformed later scene cannot leave an earlier DeckBuilder certification in place.
        }
        return (scene, last);
    }
}
