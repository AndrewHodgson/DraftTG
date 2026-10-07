using System.Diagnostics;
using System.Drawing.Imaging;
using System.Text.Json;
using static UntappedLocalizationAudit.Native;

namespace UntappedLocalizationAudit;

/// <summary>
/// Passive geometry recorder for user-performed resolution changes. Every time the USER brings Arena to the
/// foreground it saves full-resolution captures of Arena's window rectangle: two "arena-only" frames taken before
/// Untapped raises its overlay (it needs >= 68 ms), and two "overlay" frames afterwards, each labelled with the overlay's
/// Z order/topmost state at capture time, plus a topology snapshot. Client-size changes are logged. Stop with
/// &lt;out&gt;/STOP. Never focuses, clicks, moves or resizes anything.
/// </summary>
internal static class Geom
{
    private static readonly string UntappedLog = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "untapped-companion", "log.log");

    public static void Run(string outDir, double maxSeconds)
    {
        timeBeginPeriod(1);
        try { RunCore(outDir, maxSeconds); }
        finally { timeEndPeriod(1); }
    }

    private static (int Za, int Zo, bool Top) Z(nint arena, nint overlay)
    {
        var z = Topology.TopLevel();
        return (z.IndexOf(arena), z.IndexOf(overlay), (GetWindowLongPtr(overlay, GWL_EXSTYLE).ToInt64() & 8) != 0);
    }

    private static void RunCore(string outDir, double maxSeconds)
    {
        var arena = Topology.ArenaWindow();
        var overlay = Topology.TopLevel().FirstOrDefault(h => Text(h, true) == "Untapped.gg Overlay");
        var stop = Path.Combine(outDir, "STOP");
        using var log = new StreamWriter(Path.Combine(outDir, "geom-events.log")) { AutoFlush = true };
        long untappedPos = new FileInfo(UntappedLog).Length;
        log.WriteLine($"# start {DateTime.Now:o} arena={Hex(arena)} overlay={Hex(overlay)}");
        var clock = Stopwatch.StartNew();
        var wasForeground = GetForegroundWindow() == arena;
        Rect32? lastClient = null; var episode = 0; double lastTail = 0;
        while (clock.Elapsed.TotalSeconds < maxSeconds && !File.Exists(stop))
        {
            var t = clock.Elapsed.TotalMilliseconds;
            var client = Topology.Client(arena);
            if (client != lastClient)
            {
                log.WriteLine($"{DateTime.Now:HH:mm:ss.fff} t={t,10:F1} CLIENT {client} window={Topology.WindowRect(arena)} dpi={GetDpiForWindow(arena)} overlay={Topology.WindowRect(overlay)}");
                lastClient = client;
            }
            if (t - lastTail > 50)
            {
                using var s = new FileStream(UntappedLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (s.Length > untappedPos)
                {
                    s.Seek(untappedPos, SeekOrigin.Begin);
                    var text = new StreamReader(s).ReadToEnd();
                    untappedPos = s.Length;
                    foreach (var line in text.Split('\n').Where(l => l.Contains("Resolution") || l.Contains("Overlay") || l.Contains("Scry") || l.Contains("scene") || l.Contains("Draft")))
                        log.WriteLine($"{DateTime.Now:HH:mm:ss.fff} t={t,10:F1} [UNTAPPED] {(line.Length > 300 ? line[..300] : line.TrimEnd('\r'))}");
                }
                lastTail = t;
            }
            var isForeground = GetForegroundWindow() == arena;
            if (isForeground && !wasForeground)
            {
                episode++;
                var start = clock.Elapsed.TotalMilliseconds;
                var pending = new List<(string Name, System.Drawing.Bitmap Bitmap)>();
                foreach (var (offset, label) in new[] { (0.0, "arenaonly-a"), (30.0, "arenaonly-b"), (700.0, "overlay-a"), (2500.0, "overlay-b") })
                {
                    while (clock.Elapsed.TotalMilliseconds - start < offset) Thread.Sleep(1);
                    var (za, zo, top) = Z(arena, overlay);          // state immediately before the grab
                    var c = Topology.Client(arena)!;
                    var rect = Topology.WindowRect(arena);
                    var grabbedAt = clock.Elapsed.TotalMilliseconds - start;
                    var grab = Shot.Grab(rect);                      // ~20 ms; PNG encoding is deferred
                    var (za2, zo2, top2) = Z(arena, overlay);       // and immediately after
                    var name = $"e{episode:00}-{label}-{c.W}x{c.H}.png";
                    pending.Add((name, grab));
                    log.WriteLine($"{DateTime.Now:HH:mm:ss.fff} t={clock.Elapsed.TotalMilliseconds,10:F1} CAPTURE {name} at+{grabbedAt:F0}ms window={rect} client={c} overlay={Topology.WindowRect(overlay)} " +
                                  $"before(zA={za},zO={zo},top={top}) after(zA={za2},zO={zo2},top={top2}) fg={(GetForegroundWindow() == arena ? "MTGA" : "other")}");
                }
                foreach (var (name, bmp) in pending) { bmp.Save(Path.Combine(outDir, name), ImageFormat.Png); bmp.Dispose(); }
                File.WriteAllText(Path.Combine(outDir, $"e{episode:00}-topology.json"), JsonSerializer.Serialize(Topology.Run(), new JsonSerializerOptions { WriteIndented = true }));
                isForeground = GetForegroundWindow() == arena;
            }
            wasForeground = isForeground;
            Thread.Sleep(4);
        }
        log.WriteLine($"# end {DateTime.Now:o} episodes={episode}");
    }
}
