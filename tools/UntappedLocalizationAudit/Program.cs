using System.Diagnostics;
using System.Text.Json;
using static UntappedLocalizationAudit.Native;

namespace UntappedLocalizationAudit;

internal static class Program
{
    public static readonly string PlayerLogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "AppData", "LocalLow", "Wizards Of The Coast", "MTGA", "Player.log");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static int Main(string[] args)
    {
        SetProcessDpiAwarenessContext(-4); // PER_MONITOR_AWARE_V2: every rectangle below is in physical pixels.
        if (args.Length == 0) { Console.Error.WriteLine("commands: topology | uia | shot | watch | handles | modules  [--out dir]"); return 1; }
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < args.Length; i++)
            if (args[i].StartsWith("--")) options[args[i][2..]] = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[++i] : "true";
        var outDir = options.GetValueOrDefault("out") ?? Path.Combine("artifacts", "untapped-audit", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(outDir);
        var tag = options.GetValueOrDefault("tag") ?? "";
        switch (args[0])
        {
            case "topology":
            {
                var result = Topology.Run();
                Save(outDir, $"topology{tag}.json", result);
                Console.WriteLine(JsonSerializer.Serialize(result, Json));
                break;
            }
            case "uia":
            {
                var max = int.Parse(options.GetValueOrDefault("max") ?? "4000");
                var budget = TimeSpan.FromSeconds(double.Parse(options.GetValueOrDefault("seconds") ?? "30"));
                var targets = options.TryGetValue("hwnd", out var hwnd)
                    ? [(nint)Convert.ToInt64(hwnd.Replace("0x", ""), 16)]
                    : Topology.TopLevel().Where(h =>
                    {
                        GetWindowThreadProcessId(h, out var pid);
                        var name = Topology.ProcessName(pid);
                        return IsWindowVisible(h) && (Topology.IsUntapped(name) || Topology.IsArena(name));
                    }).ToList();
                foreach (var h in targets)
                {
                    var dump = options.ContainsKey("slow") ? Uia.Dump(h, max, budget) : Uia.DumpCached(h);
                    Save(outDir, $"uia-{Hex(h)}{tag}.json", dump);
                    Console.WriteLine(JsonSerializer.Serialize(dump, Json));
                }
                break;
            }
            case "shot":
            {
                Rect32 rect;
                if (options.TryGetValue("rect", out var spec))
                {
                    var p = spec.Split(',').Select(int.Parse).ToArray();
                    rect = new Rect32(p[0], p[1], p[2], p[3]);
                }
                else rect = Topology.WindowRect(Topology.ArenaWindow());
                var file = Path.Combine(outDir, options.GetValueOrDefault("file") ?? $"shot-{DateTime.Now:HHmmss-fff}.png");
                var clock = Stopwatch.StartNew();
                Shot.Capture(rect, file);
                Console.WriteLine($"{DateTime.Now:o} captured {rect} -> {file} in {clock.ElapsedMilliseconds} ms");
                break;
            }
            case "watch":
                Watch.Run(double.Parse(options.GetValueOrDefault("seconds") ?? "30"), int.Parse(options.GetValueOrDefault("interval") ?? "2"),
                    outDir, int.Parse(options.GetValueOrDefault("shots") ?? "0"), double.Parse(options.GetValueOrDefault("scale") ?? "0.5"));
                Console.WriteLine($"watch written to {outDir}");
                break;
            case "trigger":
                Trigger.Run(outDir, double.Parse(options.GetValueOrDefault("seconds") ?? "900"),
                    options.GetValueOrDefault("offsets") ?? "0,50,100,200,400,800,1500,3000", options.ContainsKey("uia"),
                    double.Parse(options.GetValueOrDefault("after") ?? "5"));
                Console.WriteLine($"trigger written to {outDir}");
                break;
            case "session":
                Session.Run(outDir, double.Parse(options.GetValueOrDefault("wait") ?? "1800"), double.Parse(options.GetValueOrDefault("max") ?? "120"),
                    int.Parse(options.GetValueOrDefault("frame") ?? "50"), double.Parse(options.GetValueOrDefault("scale") ?? "0.5"),
                    double.Parse(options.GetValueOrDefault("after") ?? "4"));
                Console.WriteLine($"session written to {outDir}");
                break;
            case "geom":
                Geom.Run(outDir, double.Parse(options.GetValueOrDefault("max") ?? "3600"));
                Console.WriteLine($"geom written to {outDir}");
                break;
            case "handles":
            {
                var result = Handles.Run(options.ContainsKey("files"));
                Save(outDir, $"handles{tag}.json", result);
                Console.WriteLine(JsonSerializer.Serialize(result, Json));
                break;
            }
            case "arena-modules":
            {
                // Read-only module inventory of MTGA (same as Task Manager / the existing DraftTG audit): looks for foreign DLLs only.
                var arena = Process.GetProcessesByName("MTGA").Single();
                var modules = arena.Modules.Cast<ProcessModule>().Select(m => m.FileName ?? m.ModuleName).ToList();
                var arenaDir = Path.GetDirectoryName(arena.MainModule?.FileName ?? "") ?? "";
                var foreign = modules.Where(m => !m.StartsWith(@"C:\Windows", StringComparison.OrdinalIgnoreCase)
                                                 && !m.StartsWith(arenaDir, StringComparison.OrdinalIgnoreCase)).ToList();
                var result = new
                {
                    TimestampLocal = DateTime.Now.ToString("o"), Pid = arena.Id, ModuleCount = modules.Count, ArenaDirectory = arenaDir,
                    NonWindowsNonArena = foreign,
                    UntappedModules = modules.Where(m => m.Contains("untapped", StringComparison.OrdinalIgnoreCase)).ToList(),
                };
                Save(outDir, $"arena-modules{tag}.json", result);
                Console.WriteLine(JsonSerializer.Serialize(result, Json));
                break;
            }
            case "modules":
            {
                var result = Process.GetProcesses().Where(p => Topology.IsUntapped(p.ProcessName)).OrderBy(p => p.Id).Select(p =>
                {
                    try
                    {
                        var modules = p.Modules.Cast<ProcessModule>().Select(m => m.FileName ?? m.ModuleName).ToList();
                        return (object)new
                        {
                            Pid = p.Id, ModuleCount = modules.Count,
                            NonSystem = modules.Where(m => !m.StartsWith(@"C:\Windows", StringComparison.OrdinalIgnoreCase)).ToList(),
                            System = modules.Where(m => m.StartsWith(@"C:\Windows", StringComparison.OrdinalIgnoreCase)).Select(Path.GetFileName).Order().ToList(),
                        };
                    }
                    catch (Exception ex) { return new { Pid = p.Id, Error = ex.Message }; }
                }).ToList();
                Save(outDir, $"modules{tag}.json", result);
                Console.WriteLine(JsonSerializer.Serialize(result, Json));
                break;
            }
            default: Console.Error.WriteLine($"unknown command {args[0]}"); return 1;
        }
        return 0;
    }

    private static void Save(string dir, string name, object value) =>
        File.WriteAllText(Path.Combine(dir, name), JsonSerializer.Serialize(value, Json));
}
