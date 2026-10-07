using System.Collections.Concurrent;
using System.Diagnostics;
using static UntappedLocalizationAudit.Native;

namespace UntappedLocalizationAudit;

/// <summary>
/// Passive high-frequency sampler: Arena window/client rectangles, every Untapped top-level window rectangle/visibility,
/// Player.log length, and per-process CPU time. Only change events are written. Optional periodic composited crops.
/// </summary>
internal static class Watch
{
    public static void Run(double seconds, int intervalMs, string outDir, int shotEveryMs, double shotScale)
    {
        timeBeginPeriod(1);
        try { RunCore(seconds, intervalMs, outDir, shotEveryMs, shotScale); }
        finally { timeEndPeriod(1); }
    }

    private static List<nint> UntappedWindows() => Topology.TopLevel().Where(h =>
    {
        GetWindowThreadProcessId(h, out var pid);
        return Topology.IsUntapped(Topology.ProcessName(pid));
    }).ToList();

    private static void RunCore(double seconds, int intervalMs, string outDir, int shotEveryMs, double shotScale)
    {
        var arena = Topology.ArenaWindow();
        using var log = new FileStream(Program.PlayerLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var processes = Process.GetProcesses().Where(p => Topology.IsUntapped(p.ProcessName) || Topology.IsArena(p.ProcessName)).ToList();
        var lastCpu = processes.ToDictionary(p => p.Id, p => p.TotalProcessorTime);
        using var events = new StreamWriter(Path.Combine(outDir, "watch-events.log"));
        using var cpu = new StreamWriter(Path.Combine(outDir, "watch-cpu.tsv"));
        cpu.WriteLine("t_ms\t" + string.Join('\t', processes.Select(p => $"{p.ProcessName}:{p.Id}")));
        var windows = UntappedWindows();
        var clock = Stopwatch.StartNew();
        var lastWindowScan = 0.0; var lastCpuMs = 0.0; string? last = null; var samples = 0;
        var shotQueue = new BlockingCollection<(double T, Rect32 R)>(4);
        var shooter = shotEveryMs > 0 ? new Thread(() =>
        {
            foreach (var (t, r) in shotQueue.GetConsumingEnumerable())
                try { ShotScaled(r, Path.Combine(outDir, $"shot-{t:000000}.png"), shotScale); } catch { }
        }) { IsBackground = true } : null;
        shooter?.Start();
        var lastShot = -1e9;
        events.WriteLine($"# start {DateTime.Now:o} arena={Hex(arena)} untappedWindows={string.Join(',', windows.Select(Hex))}");
        while (clock.Elapsed.TotalSeconds < seconds)
        {
            var t = clock.Elapsed.TotalMilliseconds;
            if (t - lastWindowScan > 100) { windows = UntappedWindows(); lastWindowScan = t; }
            var arenaRect = Topology.WindowRect(arena);
            var parts = new List<string> { $"arena win={arenaRect} client={Topology.Client(arena)} vis={IsWindowVisible(arena)} min={IsIconic(arena)}" };
            foreach (var h in windows)
            {
                DwmGetWindowAttribute(h, DWMWA_CLOAKED, out int cloaked, 4);
                parts.Add($"{Hex(h)} {Topology.WindowRect(h)} vis={IsWindowVisible(h)} cloak={cloaked}");
            }
            parts.Add($"log={log.Length}");
            var state = string.Join(" | ", parts);
            samples++;
            if (state != last) { events.WriteLine($"{DateTime.Now:HH:mm:ss.fff} t={t,10:F1} {state}"); last = state; }
            if (shooter is not null && t - lastShot >= shotEveryMs && arenaRect.W > 0) { shotQueue.TryAdd((t, arenaRect)); lastShot = t; }
            if (t - lastCpuMs >= 100)
            {
                var row = new List<string> { t.ToString("F0") };
                foreach (var p in processes)
                {
                    try { p.Refresh(); var now = p.TotalProcessorTime; row.Add(((now - lastCpu[p.Id]).TotalMilliseconds / (t - lastCpuMs) * 100).ToString("F0")); lastCpu[p.Id] = now; }
                    catch { row.Add("x"); }
                }
                cpu.WriteLine(string.Join('\t', row));
                lastCpuMs = t;
            }
            Thread.Sleep(intervalMs);
        }
        shotQueue.CompleteAdding();
        shooter?.Join(5000);
        events.WriteLine($"# end {DateTime.Now:o} samples={samples} meanIntervalMs={clock.Elapsed.TotalMilliseconds / Math.Max(1, samples):F2}");
    }

    private static void ShotScaled(Rect32 r, string path, double scale)
    {
        using var full = Shot.Grab(r);
        if (scale >= 0.999) { full.Save(path, System.Drawing.Imaging.ImageFormat.Png); return; }
        using var small = new System.Drawing.Bitmap(full, new System.Drawing.Size((int)(r.W * scale), (int)(r.H * scale)));
        small.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }
}
