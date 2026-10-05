using System.Diagnostics;
using System.Text.Json;

namespace DraftTG.LocalizationAudit;

internal static class Program
{
    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length != 3 || args[1] != "--output" || args[0] is not ("snapshot" or "memory" or "duplicate"))
                throw new ArgumentException("Usage: snapshot|memory|duplicate --output <directory>");
            var output = Path.GetFullPath(args[2]); Directory.CreateDirectory(output);
            using var process = Process.GetProcessesByName("MTGA").SingleOrDefault()
                ?? throw new InvalidOperationException("Expected exactly one MTGA process.");
            var watch = Stopwatch.StartNew(); var start = DateTimeOffset.UtcNow;
            var before = SemanticSnapshot.Read();
            object result = args[0] switch
            {
                "snapshot" => new { Windows = WindowAudit.Read(process.Id), Runtime = RuntimeAudit.Read(process) },
                "memory" => before.Cards.Length is >= 1 and <= 14 ? MemoryAudit.Read(process, before.Cards)
                    : new { Success = false, Skipped = true, Reason = "No current pack resolved by the existing log parser/state engine; do not search with stale IDs." },
                "duplicate" => DesktopDuplicationAudit.Read(process.Id, output),
                _ => throw new UnreachableException()
            };
            var after = SemanticSnapshot.Read();
            var document = new { Command = args[0], StartedAt = start, FinishedAt = DateTimeOffset.UtcNow,
                DurationMs = watch.Elapsed.TotalMilliseconds, ProcessId = process.Id, ProcessSession = process.SessionId,
                InspectorSession = Process.GetCurrentProcess().SessionId, SemanticBefore = before, Result = result,
                SemanticAfter = after, SemanticUnchanged = before.SamePack(after) };
            var path = Path.Combine(output, args[0] + ".json");
            File.WriteAllText(path, JsonSerializer.Serialize(document, Json));
            Console.WriteLine($"{args[0]}: PID {process.Id}; {before.PackLabel}; {before.Cards.Length} candidates; {watch.Elapsed.TotalSeconds:F2}s; {path}");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine($"{ex.GetType().Name}; HRESULT 0x{ex.HResult:X8}; {ex.Message}"); return 1; }
    }
}
