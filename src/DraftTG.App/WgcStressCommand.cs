using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using DraftTG.Application;
using DraftTG.App.Platform;

namespace DraftTG.App;

internal sealed record WgcStressCycle(int Cycle, bool Success, int Width, int Height, string Diagnostics,
    int Handles, long PrivateBytes, long ManagedBytes, int NativeOwners, int Threads, int ThreadPoolWorkers);
internal sealed record WgcStressResult(int Requested, int Completed, int Succeeded, bool ResourcesBounded, IReadOnlyList<WgcStressCycle> Cycles);

/// <summary>Explicit local developer probe. Frames are discarded, with no matcher, references or image upload/save.</summary>
internal static class WgcStressCommand
{
    public static int Run(string[] args) => RunAsync(args).GetAwaiter().GetResult();
    private static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length != 6 || args[1] != "--fixture" || args[2] != "--cycles" || args[4] != "--output"
                || !int.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out var cycles) || cycles is < 1 or > 1000)
                throw new ArgumentException("Usage: --stress-wgc --fixture --cycles <1-1000> --output <directory>");
#if WINDOWS
            using var fixture = new Platform.WgcStressFixture();
            var capture = new WindowsGraphicsCaptureFrameCapture(fixture.Inspect);
            var result = await RunCyclesAsync(capture, new(.1, .1, .8, .8), cycles, beforeCycle: fixture.Present);
            var directory = Path.GetFullPath(args[5]); Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "wgc-stress.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"WGC stress: {result.Succeeded}/{result.Completed} successful ({result.Requested} requested); bounded resources: {result.ResourcesBounded}");
            return result.Succeeded == result.Requested && result.ResourcesBounded ? 0 : 1;
#else
            throw new PlatformNotSupportedException("The native WGC stress fixture requires the Windows target.");
#endif
        }
        catch (Exception ex) { Console.Error.WriteLine(CaptureFailure.From("WGC stress harness", ex).Text); return 1; }
    }

    internal static async Task<WgcStressResult> RunCyclesAsync(IArenaRegionCapture capture, NormalizedDraftRegion region,
        int count, CancellationToken token = default, Action<int>? beforeCycle = null)
    {
        using var coordinator = new ArenaCaptureCoordinator(capture, new(TimeSpan.FromSeconds(3)));
        var results = new List<WgcStressCycle>(); var consecutiveFailures = 0;
        for (var i = 1; i <= count; i++)
        {
            token.ThrowIfCancellationRequested();
            var success = false; var width = 0; var height = 0;
            try
            {
                beforeCycle?.Invoke(i); coordinator.Restart();
                using var frame = await coordinator.AcquireAsync(region, token, packGeneration: i);
                width = frame.Image.Width; height = frame.Image.Height; success = width > 0 && height > 0;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception) { /* Focused stage/type/HRESULT/message are in the request diagnostics. */ }
            // Diagnostic harness only: settle WinRT projections/finalizers before periodic resource samples.
            if (i % 10 == 0)
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); GC.WaitForPendingFinalizers();
                await Task.Delay(30, token); // Native COM/driver release can finish asynchronously after managed finalization.
            }
            using var process = Process.GetCurrentProcess(); process.Refresh();
            var owners = capture is ICaptureLifecycleSource source ? source.Lifecycle.OwnedRequests : 0;
            results.Add(new(i, success, width, height, coordinator.DiagnosticText, process.HandleCount,
                process.PrivateMemorySize64, GC.GetTotalMemory(false), owners, process.Threads.Count, ThreadPool.ThreadCount));
            Console.WriteLine($"Cycle {i}: {(success ? $"{width}x{height}" : "failed")}; native owners {owners}; handles {process.HandleCount}");
            consecutiveFailures = success ? 0 : consecutiveFailures + 1;
            if (consecutiveFailures >= 5) break; // Stop an unusable desktop probe without a five-minute failure loop.
        }
        var periodic = results.Where(r => r.Cycle >= 10 && r.Cycle % 10 == 0).ToArray();
        var bounded = results.All(r => r.NativeOwners == 0) && (periodic.Length < 2
            || (periodic[^1].Handles - periodic[0].Handles <= 64
                && periodic[^1].PrivateBytes - periodic[0].PrivateBytes <= 64L * 1024 * 1024));
        return new(count, results.Count, results.Count(r => r.Success), bounded, results.AsReadOnly());
    }
}
