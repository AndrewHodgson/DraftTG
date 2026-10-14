using System.Text.Json;
using DraftTG.Application;
using DraftTG.ArenaIntegration.Memory;
using DraftTG.ArenaIntegration.Memory.Windows;
using DraftTG.App.Platform;

namespace DraftTG.App;

internal static class ArenaUiLocalizationDebugCommand
{
    internal static object Snapshot(ArenaDraftMemorySnapshot read, ArenaWindowGeometry? window,
        CaptureFreeUiStability stability, long packGeneration, MemoryCardName[]? names = null, CaptureFreeUiStability? deckStability = null,
        MemoryCardName[]? deckNames = null)
    {
        var layout = stability.IsStable && read.Status == ArenaMemoryStatus.Success && window is not null
            ? CaptureFreeDraftLayout.Map(window.Width, window.Height, read.OrderedOfferedGrpIds.Count,
                read.ViewMode, window.IsWindowed, read.Ui?.PackLayout) : null;
        var deckGate=deckStability??stability;
        var deckLayout=deckGate.IsStable&&window is not null?CaptureFreeDeckLayout.Map(read.Ui?.DeckView,window.Width,window.Height,read.ViewMode,window.IsWindowed):null;
        return new
        {
            ArenaBuild = read.Build, Window = window is null ? null : new { Handle = window.Handle.ToInt64(),
                window.ProcessId, window.X, window.Y, window.Width, window.Height, window.Scaling, window.IsWindowed,
                window.IsVisible, window.IsMinimized }, Dpi = window?.Scaling * 96,
            DraftPack = new { read.Status, read.Detail, read.DraftSessionId, read.PackNumber, read.PickNumber,
                read.ViewMode, OrderedGrpIds = read.OrderedOfferedGrpIds, read.PrintingIdentities, Names = names, PackGeneration = packGeneration },
            DeckView = new { DeckGeneration = deckGate.Generation, Stable = deckGate.IsStable, Memory = read.Ui?.DeckView,
                ViewportRect=deckLayout?.Viewport,ContentRect=deckLayout?.Content,ScrollPixels=deckLayout?.ScrollPixels,
                ScrollFromNormalizedPixels=deckLayout?.NormalizedScrollPixels,ProjectedRows=deckLayout?.Rows,Decline=deckLayout?.Decline,
                Rows = deckLayout?.Rows.Select(e => new { e.CollectionIndex, e.VisualIndex, e.GrpId, e.Quantity, e.Holder,
                    LogicalIdentity=deckNames?.FirstOrDefault(n=>n.GrpId==e.GrpId)?.LogicalIdentity,
                    Rect=e.Rectangle, e.VisibleRectangle, e.Visible, e.Culled, e.Clipped, Generation=deckGate.Generation, e.Source }).ToArray() },
            Scroll = new { MainNormalized = read.Ui?.DeckView?.MainScroll, SideNormalized = read.Ui?.DeckView?.SideScroll, PixelOffset = deckLayout?.ScrollPixels },
            Geometry = new { stability.Generation, stability.IsStable, PackLayout = read.Ui?.PackLayout,
                Profile = layout?.Profile, Rectangles = layout?.Rectangles },
            UnsupportedReasons = new[] { read.Status == ArenaMemoryStatus.Success ? null : read.Detail,
                layout?.Decline, read.Ui?.UnsupportedReason, read.Ui?.DeckView?.UnsupportedReason,
                stability.IsStable ? null : "Awaiting two stable UI/window reads" }.Where(x => x is not null).ToArray(),
            Timings = new { FirstAttach = read.IsFirstAttach, ReadMs = read.Duration.TotalMilliseconds,
                TraversalMs = read.TraversalDuration.TotalMilliseconds, UiReadMs = read.Ui?.ReadMilliseconds,
                DeckReadMs = read.Ui?.DeckReadMilliseconds, DeckIdentityMs=read.Ui?.DeckIdentityMilliseconds, DeckGeometryMs=read.Ui?.DeckGeometryMilliseconds },
            ScreenCaptureInitialized = false
        };
    }
    internal static string? Key(ArenaDraftMemorySnapshot read, ArenaWindowGeometry? window) =>
        read.ProcessInstance is null || window is null ? null : JsonSerializer.Serialize(new
        {
            read.ProcessInstance, read.Status, read.Identity, read.ViewMode, read.OrderedOfferedGrpIds, read.PrintingIdentities,
            read.Ui?.PackLayout, Handle = window.Handle.ToInt64(), window.ProcessId,
            window.Width, window.Height, window.Scaling, window.IsVisible, window.IsMinimized, window.IsWindowed
        });
    internal static string? DeckKey(ArenaDraftMemorySnapshot read,ArenaWindowGeometry? window)=>window is null||read.ProcessInstance is null?null:
        JsonSerializer.Serialize(new {read.ProcessInstance,read.ViewMode,read.Ui?.DeckView,Handle=window.Handle.ToInt64(),window.Width,window.Height,window.Scaling,window.IsVisible,window.IsMinimized});
    internal static int Run(string[] args)
    {
        try { return RunAsync(CaptureFreeOptions.Parse(args)).GetAwaiter().GetResult(); }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
    }
    private static async Task<int> RunAsync(CaptureFreeOptions options)
    {
        CaptureFreeSafety.EnableForProcess();
        using var token = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; token.Cancel(); };
        Console.CancelKeyPress += cancel;
        using var ledger = new CaptureFreeEvidenceLedger(options.Ledger, "UiLocalizationDebug");
        var stability = new CaptureFreeUiStability();var deckStability=new CaptureFreeUiStability(); string? last = null;
        try
        {
            MemoryCardNames names;
            try { names = await MemoryCardNames.LoadAsync(token.Token); }
            catch (Exception ex) when (ex is not OperationCanceledException) { names = new(null, null); }
            using var reader = new ArenaDraftMemoryReader(names.PrintingReconciler);
            using var watcher = new ArenaDraftMemoryWatcher(reader:()=>reader.ReadCurrent());
            var clock = System.Diagnostics.Stopwatch.StartNew();
            do
            {
                var read = reader.ReadCurrent(); watcher.Observe(read); ledger.Poll(read);
                var window = OperatingSystem.IsWindows() ? ArenaWindowInspector.InspectWindow().Window : null;
                var key = Key(read, window); stability.Observe(key, read.ReadTimestamp);
                var deckKey=DeckKey(read,window);deckStability.Observe(deckKey,read.ReadTimestamp);
                var eventKey = key + ":" + stability.IsStable + ":"+deckKey+":"+deckStability.IsStable+$":{window?.X}:{window?.Y}";
                if (eventKey != last)
                {
                    last = eventKey;
                    var data = Snapshot(read, window, stability, watcher.Generation, names.Resolve(read),deckStability,
                        names.ResolveIds(read.Ui?.DeckView?.Entries.Select(e=>e.GrpId)??[]));
                    ledger.Record("UiLocalization", data);
                    Console.WriteLine(JsonSerializer.Serialize(data));
                }
                await Task.Delay(100, token.Token);
            } while (clock.Elapsed < TimeSpan.FromSeconds(options.Seconds ?? 2));
            return 0;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return 0; }
        finally { Console.CancelKeyPress -= cancel; }
    }
}
