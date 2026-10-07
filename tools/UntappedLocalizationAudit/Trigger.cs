using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Text.Json;
using static UntappedLocalizationAudit.Native;

namespace UntappedLocalizationAudit;

/// <summary>
/// Passive: waits until the USER brings Arena to the foreground, then records overlay z-order/visibility/geometry
/// at ~5 ms resolution and takes timed composited captures (and optionally one cached UIA dump) while Arena is active.
/// Never focuses, clicks, moves or resizes anything.
/// </summary>
internal static class Trigger
{
    public static void Run(string outDir, double waitSeconds, string offsetsMs, bool uia, double afterLossSeconds)
    {
        timeBeginPeriod(1);
        try { RunCore(outDir, waitSeconds, offsetsMs.Split(',').Select(double.Parse).ToList(), uia, afterLossSeconds); }
        finally { timeEndPeriod(1); }
    }

    private static nint Overlay() => Topology.TopLevel().FirstOrDefault(h =>
    {
        GetWindowThreadProcessId(h, out var pid);
        return Topology.IsUntapped(Topology.ProcessName(pid)) && Text(h, true) == "Untapped.gg Overlay";
    });

    private static void RunCore(string outDir, double waitSeconds, List<double> offsets, bool uia, double afterLossSeconds)
    {
        var arena = Topology.ArenaWindow();
        var overlay = Overlay();
        using var log = new StreamWriter(Path.Combine(outDir, "trigger-events.log")) { AutoFlush = true };
        log.WriteLine($"# start {DateTime.Now:o} arena={Hex(arena)} overlay={Hex(overlay)} offsetsMs={string.Join(',', offsets)}");
        var shots = new BlockingCollection<(string Name, System.Drawing.Bitmap Bitmap)>(32);
        var writer = new Thread(() => { foreach (var (name, bmp) in shots.GetConsumingEnumerable()) { bmp.Save(Path.Combine(outDir, name), ImageFormat.Png); bmp.Dispose(); } }) { IsBackground = true };
        writer.Start();
        var clock = Stopwatch.StartNew();
        double? fgSince = null, lostAt = null;
        var next = 0; string? last = null; Thread? uiaThread = null;
        while (clock.Elapsed.TotalSeconds < waitSeconds)
        {
            var t = clock.Elapsed.TotalMilliseconds;
            var fg = GetForegroundWindow();
            GetWindowThreadProcessId(fg, out var fgPid);
            var z = Topology.TopLevel();
            int za = z.IndexOf(arena), zo = z.IndexOf(overlay);
            var ex = GetWindowLongPtr(overlay, GWL_EXSTYLE).ToInt64();
            var state = $"fg={Hex(fg)}({Topology.ProcessName(fgPid)}) zArena={za} zOverlay={zo} overlayAbove={(zo >= 0 && zo < za)} " +
                        $"ovVis={IsWindowVisible(overlay)} ovTopmost={(ex & 8) != 0} ovEx={ExStyleFlags(ex)} ov={Topology.WindowRect(overlay)} client={Topology.Client(arena)}";
            if (state != last) { log.WriteLine($"{DateTime.Now:HH:mm:ss.fff} t={t,10:F1} {state}"); last = state; }
            if (fg == arena && lostAt is null) fgSince ??= t;
            if (fgSince is not null && fg != arena && lostAt is null) { lostAt = t; log.WriteLine($"{DateTime.Now:HH:mm:ss.fff} t={t,10:F1} ARENA LOST FOREGROUND after {t - fgSince:F0} ms"); }
            if (fgSince is not null && lostAt is null && next < offsets.Count && t - fgSince >= offsets[next])
            {
                var rect = Topology.WindowRect(arena);
                var grab = Stopwatch.StartNew();
                var bmp = Shot.Grab(rect);
                var name = $"fg-{offsets[next]:00000}ms.png";
                shots.Add((name, bmp));
                log.WriteLine($"{DateTime.Now:HH:mm:ss.fff} t={t,10:F1} CAPTURE {name} rect={rect} grabMs={grab.ElapsedMilliseconds} zArena={za} zOverlay={zo}");
                next++;
                if (next == offsets.Count && uia)
                {
                    uiaThread = new Thread(() =>
                    {
                        var dump = Uia.DumpCached(overlay);
                        File.WriteAllText(Path.Combine(outDir, "uia-overlay-foreground.json"), JsonSerializer.Serialize(dump, new JsonSerializerOptions { WriteIndented = true }));
                        log.WriteLine($"{DateTime.Now:HH:mm:ss.fff} UIA done");
                    }) { IsBackground = true };
                    uiaThread.Start();
                }
            }
            if (lostAt is not null && t - lostAt > afterLossSeconds * 1000 && (uiaThread is null || !uiaThread.IsAlive)) break;
            if (lostAt is not null && t - lostAt > (afterLossSeconds + 90) * 1000) { log.WriteLine("# UIA still running; giving up"); break; }
            Thread.Sleep(4);
        }
        shots.CompleteAdding();
        writer.Join(10000);
        log.WriteLine($"# end {DateTime.Now:o} fgSince={fgSince:F0} lostAt={lostAt:F0} captures={next}");
    }
}
