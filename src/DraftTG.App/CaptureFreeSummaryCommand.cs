using System.Text;
using System.Text.Json;

namespace DraftTG.App;

internal static class CaptureFreeSummaryCommand
{
    internal static int Run(string[] args)
    {
        try
        {
            var ledger = CaptureFreeOptions.DefaultLedger; string? report = null;
            for (var i = 1; i < args.Length; i++)
                if (args[i] == "--ledger" && i + 1 < args.Length) ledger = args[++i];
                else if (args[i] == "--report" && i + 1 < args.Length) report = args[++i];
                else throw new ArgumentException("Usage: --capture-free-draft-summary [--ledger <path>] [--report <markdown path>]");
            var rows = File.Exists(ledger) ? File.ReadLines(ledger).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l =>
            { using var d = JsonDocument.Parse(l); return d.RootElement.Clone(); }).ToArray() : [];
            var text = Summarize(rows);
            Console.WriteLine(text);
            if (report is not null) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report))!); File.WriteAllText(report, text); }
            return 0;
        }
        catch (Exception ex) when (ex is IOException or JsonException or ArgumentException)
        { Console.Error.WriteLine("Capture-free summary declined: " + ex.Message); return 1; }
    }
    internal static string Summarize(IReadOnlyList<JsonElement> rows)
    {
        var runs = rows.Where(r => Kind(r) == "RunStarted" && S(D(r), "Source") == "NativeWindowsMemory"
            && B(D(r), "CaptureBlocked")).Select(r => S(r, "RunId")).ToHashSet();
        var live = rows.Where(r => runs.Contains(S(r, "RunId"))).ToArray();
        var allStable = live.Where(r => Kind(r) == "Memory" && IsCoherent(D(r))).Select(D).ToArray();
        var stable = allStable.GroupBy(d => S(d, "SemanticPackKey")).Where(g => g.Key is not null).Select(g => g.Last()).ToArray();
        var drafts = stable.Select(d => S(d, "DraftId")).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().Count();
        var clients = allStable.Where(d => d.TryGetProperty("Client", out var c) && c.ValueKind == JsonValueKind.Object)
            .Select(d => $"{I(d.GetProperty("Client"), "Width")}x{I(d.GetProperty("Client"), "Height")}").Distinct().Count();
        var ends = live.Where(r => Kind(r) == "RunEnded").Select(D).ToArray();
        var capture = live.Any(r => B(D(r), "ScreenCaptureInitialized"));
        var attempts = ends.Sum(e => I(e, "RejectedCaptureAttempts"));
        var reviews = live.Where(r => Kind(r) == "ManualReview").Select(D).ToArray();
        var incorrect = reviews.Count(r => r.TryGetProperty("OrderCorrect", out var o) && o.ValueKind == JsonValueKind.False
            || r.TryGetProperty("PlacementCorrect", out var p) && p.ValueKind == JsonValueKind.False);
        var placements = live.Where(r => Kind(r) == "Placement" && B(D(r), "Placed")).Select(D).ToArray();
        var reviewed = placements.All(p => reviews.Any(r => S(r, "SemanticPackKey") == S(p, "SemanticPackKey")
            && S(r, "Profile") == S(p, "Profile") && S(r, "GeometryKey") == S(p, "GeometryKey")
            && B(r, "OrderCorrect") && B(r, "PlacementCorrect")));
        var staleRestored = live.Count(r => Kind(r) == "StaleBadgeRestored");
        var tornDisplayed = live.Count(r => Kind(r) == "TornPackDisplayed");
        var completed = runs.Count > 0 && runs.All(id => live.Any(r => Kind(r) == "RunEnded" && S(r, "RunId") == id));
        var gate = stable.Length >= 30 && drafts >= 2 && clients >= 2 && incorrect == 0 && !capture && attempts == 0
            && placements.Length > 0 && reviewed && staleRestored == 0 && tornDisplayed == 0 && completed;
        var output = new StringBuilder("# Phase 9F.2 live capture-free summary\n\n");
        output.AppendLine($"Native live runs: {runs.Count}; completed: {completed}");
        output.AppendLine($"Stable coherent observations / distinct semantic packs: {stable.Length}; identified drafts: {drafts}; client sizes: {clients}");
        foreach (var property in new[] { "DraftId", "RuntimeProfile", "CardCount", "ViewMode", "Reconciliation" })
            output.AppendLine(property + ": " + string.Join(", ", stable.GroupBy(d => S(d, property) ?? "unknown").Select(g => $"{g.Key}={g.Count()}")));
        output.AppendLine("Clients / DPI: " + string.Join(", ", allStable.Where(d => d.TryGetProperty("Client", out var c) && c.ValueKind == JsonValueKind.Object)
            .GroupBy(d => d.GetProperty("Client").GetRawText()).Select(g => $"{g.Key} ({g.Count()})")));
        output.AppendLine($"Unresolved GrpIds in distinct packs: {stable.Sum(d => d.TryGetProperty("Names", out var n) ? n.EnumerateArray().Count(v => !B(v, "Resolved")) : I(d, "CardCount"))}");
        foreach (var kind in new[] { "StaleUiRejected", "Move", "Resize", "DPI", "WindowUnavailable", "PickAccepted", "SourceDiagnostic" })
            output.AppendLine($"{kind}: {live.Count(r => Kind(r) == kind)}");
        var memories = live.Where(r => Kind(r) == "Memory").Select(D).ToArray();
        output.AppendLine("Transitions: " + string.Join(", ", memories.GroupBy(d => S(d, "Transition")).Select(g => $"{g.Key}={g.Count()}")));
        output.AppendLine("Process instances: " + string.Join(", ", memories.Select(d => S(d, "ProcessInstance")).Where(p => p is not null).Distinct()));
        output.AppendLine("Reconciliation events: " + string.Join(", ", memories.GroupBy(d => S(d, "Reconciliation") ?? "not compared").Select(g => $"{g.Key}={g.Count()}")));
        var declines = live.Where(r => Kind(r) == "Placement" && !B(D(r), "Placed")).Select(D).ToArray();
        output.AppendLine("Placement declines: " + string.Join(", ", declines.GroupBy(d => S(d, "Reason")).Select(g => $"{g.Key}={g.Count()}")));
        output.AppendLine($"Manual reviews: {reviews.Length}; incorrect order/placement reports: {incorrect}; all used layouts reviewed: {reviewed}");
        output.AppendLine($"Stale badges restored: {staleRestored}; torn packs displayed: {tornDisplayed}");
        output.AppendLine($"Screen capture initialized: {(capture ? "YES" : runs.Count > 0 ? "NO" : "UNOBSERVED")}; blocked initialization attempts: {attempts}");
        var first = new List<double>(); var steady = new List<double>(); var traversal = new List<double>();
        var statuses = new Dictionary<string, int>();
        foreach (var data in live.Where(r => Kind(r) == "Performance").Select(D))
        {
            var ms = data.GetProperty("ReadMs").EnumerateArray().Select(v => v.GetDouble()).ToArray();
            var flags = data.GetProperty("FirstAttach").EnumerateArray().Select(v => v.GetBoolean()).ToArray();
            var traverse = data.GetProperty("TraversalMs").EnumerateArray().Select(v => v.GetDouble()).ToArray();
            if (ms.Length != flags.Length || ms.Length != traverse.Length) throw new ArgumentException("Inconsistent performance batch.");
            for (var i = 0; i < ms.Length; i++) (flags[i] ? first : steady).Add(ms[i]);
            for (var i = 0; i < ms.Length; i++) if (!flags[i]) traversal.Add(traverse[i]);
            foreach (var p in data.GetProperty("Statuses").EnumerateObject()) statuses[p.Name] = statuses.GetValueOrDefault(p.Name) + p.Value.GetInt32();
        }
        output.AppendLine("Read outcomes (poll counts): " + string.Join(", ", statuses.Select(p => $"{p.Key}={p.Value}")));
        output.AppendLine("First attach ms: " + Stats(first)); output.AppendLine("Steady-state read ms: " + Stats(steady));
        output.AppendLine("Steady-state traversal ms: " + Stats(traversal));
        output.AppendLine("Polling: 100 ms completion-based delay; no overlapping reads. CPU/allocations not isolated in this report.");
        output.AppendLine($"\nTechnical live gate: {(gate ? "PASS" : "NOT PASSED")}.");
        output.AppendLine("Requires >=30 distinct coherent packs, >=2 identified drafts, >=2 sizes, completed capture-blocked runs, supported placement reviews, and no incorrect/stale/torn/capture events. Restart and physical coverage still require review; summary is not production approval.");
        return output.ToString();
    }
    private static string Stats(List<double> values)
    { if (values.Count == 0) return "unobserved"; values.Sort(); return $"n={values.Count}; median={values[values.Count / 2]:F2}; p95={values[(int)Math.Ceiling(values.Count * .95) - 1]:F2}; max={values[^1]:F2}"; }
    private static JsonElement D(JsonElement r) => r.GetProperty("Data");
    private static bool IsCoherent(JsonElement d) => B(d, "Stable") && I(d, "Generation") > 0 && I(d, "CardCount") is >= 1 and <= 32
        && S(d, "Consistency") == "Full checks + two coherent reads" && S(d, "RuntimeProfile") is not null
        && d.TryGetProperty("OrderedGrpIds", out var ids) && ids.ValueKind == JsonValueKind.Array && ids.GetArrayLength() == I(d, "CardCount")
        && ids.EnumerateArray().All(v => v.ValueKind == JsonValueKind.Number && v.TryGetUInt32(out var id) && id is > 0 and <= int.MaxValue);
    private static string? Kind(JsonElement r) => S(r, "Kind");
    private static string? S(JsonElement r, string p) => r.ValueKind == JsonValueKind.Object && r.TryGetProperty(p, out var v) && v.ValueKind != JsonValueKind.Null ? v.ToString() : null;
    private static bool B(JsonElement r, string p) => r.ValueKind == JsonValueKind.Object && r.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.True;
    private static int I(JsonElement r, string p) => r.ValueKind == JsonValueKind.Object && r.TryGetProperty(p, out var v)
        && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var value) ? value : 0;
}
