using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text;
using static UntappedLocalizationAudit.Native;

namespace UntappedLocalizationAudit;

/// <summary>
/// Passive recorder for user-performed experiments (move, hover, pick). Starts when the USER brings Arena to the
/// foreground and stops a few seconds after Arena loses it. Records at ~4 ms: Arena window/client rect, overlay rect,
/// z-order, topmost, foreground, cursor. Saves JPEG frames of Arena's window rect at a fixed cadence. Tails Untapped's
/// log and Player.log, timestamping new matching lines on arrival. Never focuses, clicks, moves or resizes anything.
/// </summary>
internal static class Session
{
    private static readonly string UntappedLog = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "untapped-companion", "log.log");
    private static readonly string[] UntappedKeys = ["Scry", "Draft", "Pack", "registerDraft", "sceneChange", "Overlay", "Resolution", "Window"];
    private static readonly string[] ArenaKeys = ["BotDraft", "Draft", "DraftPack", "SceneChange", "EventPlayerDraftMakePick", "PickNext"];

    public static void Run(string outDir, double waitSeconds, double maxSeconds, int frameMs, double scale, double afterLossSeconds)
    {
        timeBeginPeriod(1);
        try { RunCore(outDir, waitSeconds, maxSeconds, frameMs, scale, afterLossSeconds); }
        finally { timeEndPeriod(1); }
    }

    private sealed class Tail(string path, string[] keys, string tag)
    {
        private long _pos = new FileInfo(path).Length;
        public IEnumerable<string> Poll()
        {
            using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (s.Length < _pos) _pos = 0;
            if (s.Length == _pos) yield break;
            s.Seek(_pos, SeekOrigin.Begin);
            var buffer = new byte[s.Length - _pos];
            var read = s.Read(buffer, 0, buffer.Length);
            var text = Encoding.UTF8.GetString(buffer, 0, read);
            var cut = text.LastIndexOf('\n');
            if (cut < 0) yield break;               // wait for a complete line
            _pos += Encoding.UTF8.GetByteCount(text[..(cut + 1)]);
            foreach (var line in text[..cut].Split('\n'))
                if (keys.Any(k => line.Contains(k, StringComparison.Ordinal)))
                    yield return $"{tag} {(line.Length > 400 ? line[..400] + "…" : line.TrimEnd('\r'))}";
        }
    }

    private static void RunCore(string outDir, double waitSeconds, double maxSeconds, int frameMs, double scale, double afterLossSeconds)
    {
        var arena = Topology.ArenaWindow();
        var overlay = Topology.TopLevel().FirstOrDefault(h => Text(h, true) == "Untapped.gg Overlay");
        var framesDir = Path.Combine(outDir, "frames"); Directory.CreateDirectory(framesDir);
        using var log = new StreamWriter(Path.Combine(outDir, "session-events.log")) { AutoFlush = true };
        var tails = new[] { new Tail(UntappedLog, UntappedKeys, "[UNTAPPED]"), new Tail(Program.PlayerLogPath, ArenaKeys, "[PLAYERLOG]") };
        var jpeg = ImageCodecInfo.GetImageEncoders().First(e => e.FormatID == ImageFormat.Jpeg.Guid);
        var quality = new EncoderParameters(1) { Param = { [0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 90L) } };
        var queue = new BlockingCollection<(string Name, Bitmap Bitmap)>(64);
        var writer = new Thread(() => { foreach (var (n, b) in queue.GetConsumingEnumerable()) { b.Save(Path.Combine(framesDir, n), jpeg, quality); b.Dispose(); } }) { IsBackground = true };
        writer.Start();
        log.WriteLine($"# start {DateTime.Now:o} arena={Hex(arena)} overlay={Hex(overlay)} frameMs={frameMs} scale={scale}");
        var clock = Stopwatch.StartNew();
        double lastTail = 0, lastActivity = -1e9; string? last = null; var frames = 0; var dropped = 0;
        var running = true; var active = false; long activeUntilTicks = 0;
        var stopFile = Path.Combine(outDir, "STOP");
        var capture = new Thread(() =>
        {
            double next = 0;
            while (Volatile.Read(ref running))
            {
                var t = clock.Elapsed.TotalMilliseconds;
                if (!Volatile.Read(ref active) || t < next) { Thread.Sleep(1); continue; }
                next = t + frameMs;
                var rect = Topology.WindowRect(arena);
                if (rect.W <= 0) continue;
                using var full = Shot.Grab(rect);
                var small = new Bitmap((int)(rect.W * scale), (int)(rect.H * scale), PixelFormat.Format24bppRgb);
                using (var g = Graphics.FromImage(small)) { g.InterpolationMode = InterpolationMode.HighQualityBilinear; g.DrawImage(full, 0, 0, small.Width, small.Height); }
                var name = $"f{t:0000000}.jpg";
                if (queue.TryAdd((name, small))) { Interlocked.Increment(ref frames); GetCursorPos(out var fc); lock (log) log.WriteLine($"{DateTime.Now:HH:mm:ss.fff} t={t,10:F1} FRAME {name} arena={rect} grabDoneT={clock.Elapsed.TotalMilliseconds:F1} cursorScreen=({fc.X},{fc.Y})"); }
                else { small.Dispose(); Interlocked.Increment(ref dropped); }
            }
        }) { IsBackground = true };
        capture.Start();
        Rect32? prevArena = null; (int X, int Y)? prevCursor = null;
        // Runs until the STOP file appears or maxSeconds elapse. Frames are taken only while Arena is foreground AND
        // something is happening: cursor inside Arena's window, Arena's rectangle changed, or a new log event (each keeps
        // capture alive for 1.5 s; log events for 6 s).
        while (clock.Elapsed.TotalSeconds < maxSeconds && !File.Exists(stopFile))
        {
            var t = clock.Elapsed.TotalMilliseconds;
            var fg = GetForegroundWindow();
            var z = Topology.TopLevel();
            GetWindowThreadProcessId(fg, out var fgPid);
            GetCursorPos(out var cursor);
            var arenaRect = Topology.WindowRect(arena);
            var client = Topology.Client(arena);
            var ex = GetWindowLongPtr(overlay, GWL_EXSTYLE).ToInt64();
            var state = $"fg={Topology.ProcessName(fgPid)} zA={z.IndexOf(arena)} zO={z.IndexOf(overlay)} top={(ex & 8) != 0} vis={IsWindowVisible(overlay)} " +
                        $"arena={arenaRect} client={client} ov={Topology.WindowRect(overlay)}";
            var cur = client is null ? "" : $" cursorClient=({cursor.X - client.X},{cursor.Y - client.Y})";
            if (state != last) { lock (log) log.WriteLine($"{DateTime.Now:HH:mm:ss.fff} t={t,10:F1} {state}{cur}"); last = state; }
            var inWindow = cursor.X >= arenaRect.X && cursor.X < arenaRect.X + arenaRect.W && cursor.Y >= arenaRect.Y && cursor.Y < arenaRect.Y + arenaRect.H;
            var moved = prevCursor is not null && prevCursor != (cursor.X, cursor.Y);
            if ((inWindow && moved) || (prevArena is not null && prevArena != arenaRect)) lastActivity = Math.Max(lastActivity, t);
            prevArena = arenaRect; prevCursor = (cursor.X, cursor.Y);
            if (t - lastTail >= 20)
            {
                foreach (var tail in tails)
                    foreach (var line in tail.Poll())
                    {
                        lock (log) log.WriteLine($"{DateTime.Now:HH:mm:ss.fff} t={t,10:F1} {line}");
                        lastActivity = Math.Max(lastActivity, t + 4500);
                    }
                lastTail = t;
            }
            Volatile.Write(ref active, fg == arena && t - lastActivity < 1500);
            Thread.Sleep(3);
        }
        Volatile.Write(ref running, false); capture.Join(5000);
        queue.CompleteAdding(); writer.Join(30000);
        log.WriteLine($"# end {DateTime.Now:o} frames={frames} dropped={dropped} stopFile={File.Exists(stopFile)}");
    }
}
