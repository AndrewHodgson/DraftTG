using DraftTG.Application;
using DraftTG.ArenaIntegration.Memory;
using DraftTG.App.Platform;

namespace DraftTG.App;

internal static class ArenaMemoryWatchCommand
{
    internal static int Run(string[] args)
    {
        try { return RunAsync(CaptureFreeOptions.Parse(args)).GetAwaiter().GetResult(); }
        catch (Exception ex) { Console.Error.WriteLine("Memory watcher stopped: " + ex.Message); return 1; }
    }
    private static async Task<int> RunAsync(CaptureFreeOptions options)
    {
        CaptureFreeSafety.EnableForProcess();
        using var lifetime = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; lifetime.Cancel(); };
        Console.CancelKeyPress += cancel;
        using var ledger = new CaptureFreeEvidenceLedger(options.Ledger, "Console");
        MemoryCardNames names;
        try { names = await MemoryCardNames.LoadAsync(lifetime.Token); }
        catch (Exception ex) when (ex is not OperationCanceledException) { Console.WriteLine("Catalog unavailable: " + ex.Message); names = new(null, null); }
        using var watcher = new ArenaDraftMemoryWatcher(printingReconciler:names.PrintingReconciler);
        watcher.PollObserved += ledger.Poll;
        try
        {
            Console.WriteLine("Capture-free memory watcher: ENABLED\nWGC: DISABLED\nGDI fallback: DISABLED\nLive evidence: " + options.Ledger);
            await foreach (var update in watcher.WatchAsync(options.Seconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null, lifetime.Token))
            {
                var snapshot = update.CurrentPack ?? update.CurrentRead;
                var resolved = names.Resolve(snapshot);
                var window = OperatingSystem.IsWindows() ? ArenaWindowInspector.InspectWindow().Window : null;
                ledger.Memory(update, resolved, null, window);
                Console.WriteLine($"{update.Transition}; Memory generation: {update.Generation}; Stable read: {(update.CurrentPack is not null ? "yes" : "no")}; "
                    + $"P{snapshot.PackNumber}P{snapshot.PickNumber}; View: {snapshot.ViewMode}; Cards: {update.CurrentPack?.OrderedOfferedGrpIds.Count ?? 0}; {snapshot.Detail}");
                if (update.CurrentPack is not null)
                {
                    MemoryCardNames.Print(resolved);
                    var layout = window is null ? null : CaptureFreeDraftLayout.Map(window.Width, window.Height, snapshot.OrderedOfferedGrpIds.Count, snapshot.ViewMode, window.IsWindowed);
                    Console.WriteLine($"Client: {window?.Width}x{window?.Height}; DPI: {window?.Scaling * 96}; Layout: {layout?.Profile ?? layout?.Decline ?? "Window unavailable"}");
                }
            }
            return 0;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return 0; }
        finally { Console.CancelKeyPress -= cancel; watcher.PollObserved -= ledger.Poll; }
    }
}
