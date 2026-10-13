using System.Text.Json;
using DraftTG.Application;
using DraftTG.ArenaIntegration;
using DraftTG.ArenaIntegration.Memory;
using DraftTG.Data;
using DraftTG.Domain;

namespace DraftTG.App;

internal sealed record MemoryCardName(int Index, uint GrpId, bool Resolved, string Name, string? Set, string? Collector, string? Reason);
internal sealed class MemoryCardNames(ArenaCardResolver? resolver, CardCatalog? catalog)
{
    internal MemoryCardName[] Resolve(ArenaDraftMemorySnapshot snapshot) => snapshot.OrderedOfferedGrpIds.Select((id, index) =>
    {
        var resolution = resolver?.Resolve(ArenaCardIdentifier.Create((int)id));
        var card = resolution?.CardIdentifier is { } key ? catalog?.Find(key) : null;
        return new MemoryCardName(index, id, card is not null, card?.Name ?? "<unresolved>", card?.SetCode.Value,
            card?.CollectorNumber.Value, card is not null ? null : resolution?.Diagnostic ?? resolution?.Status.ToString() ?? "Catalog not available");
    }).ToArray();
    internal static async Task<MemoryCardNames> LoadAsync(CancellationToken token)
    {
        using var http = new HttpClient();
        var provider = new ScryfallCardDataProvider(new ScryfallBulkDataClient(http), new ScryfallJsonlGzipCardDataLoader(),
            ApplicationDataPathProviderFactory.CreateDefault());
        var bootstrap = await new DraftTGCardDataBootstrapper(provider, ArenaDatabasePrintingIdentitySource.CreateDefault()).BootstrapAsync(cancellationToken: token);
        return new(bootstrap.ArenaCardResolver, bootstrap.Catalog);
    }
    internal static void Print(IEnumerable<MemoryCardName> names)
    {
        foreach (var n in names) Console.WriteLine($"  {n.Index}  GrpId={n.GrpId}  {n.Name}  {n.Set} {n.Collector}" + (n.Resolved ? "" : $" ({n.Reason})"));
    }
}

internal sealed record CaptureFreeOptions(string Ledger, bool Verbose, int? Seconds)
{
    internal static string DefaultLedger => Path.Combine(ApplicationDataPathProviderFactory.CreateDefault().GetApplicationDataDirectory(), "capture-free-draft", "phase9f2-live.jsonl");
    internal static CaptureFreeOptions Parse(string[] args)
    {
        var path = DefaultLedger; var verbose = false; int? seconds = null;
        for (var i = 1; i < args.Length; i++)
            switch (args[i])
            {
                case "--ledger" when i + 1 < args.Length: path = Path.GetFullPath(args[++i]); break;
                case "--verbose": verbose = true; break;
                case "--seconds" when i + 1 < args.Length && int.TryParse(args[++i], out var n) && n is > 0 and <= 86400: seconds = n; break;
                default: throw new ArgumentException("Options: [--ledger <path>] [--verbose] [--seconds <1..86400>]");
            }
        return new(path, verbose, seconds);
    }
}

/// <summary>Append-only live diagnostics. Records no pixels, dumps or raw remote pointers.</summary>
internal sealed class CaptureFreeEvidenceLedger : IDisposable
{
    private readonly object gate = new();
    private readonly StreamWriter writer;
    private readonly List<(double Ms, double Traversal, bool First, string Status)> polls = [];
    internal string RunId { get; } = Guid.NewGuid().ToString("N");
    internal CaptureFreeEvidenceLedger(string path, string mode, bool live = true)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        writer = new(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), new System.Text.UTF8Encoding(false)) { AutoFlush = true };
        Record("RunStarted", new { Mode = mode, Source = live ? "NativeWindowsMemory" : "Synthetic", CaptureBlocked = CaptureFreeSafety.Enabled,
            ScreenCaptureInitialized = false, Build = typeof(Program).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .Cast<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion });
    }
    internal void Record(string kind, object data)
    { lock (gate) writer.WriteLine(JsonSerializer.Serialize(new { Schema = 1, RunId, Kind = kind, Time = DateTimeOffset.UtcNow, Data = data })); }
    internal void Poll(ArenaDraftMemorySnapshot s)
    {
        lock (gate)
        {
            polls.Add((s.Duration.TotalMilliseconds, s.TraversalDuration.TotalMilliseconds, s.IsFirstAttach, s.Status.ToString()));
            if (polls.Count >= 50) FlushPolls();
        }
    }
    private void FlushPolls()
    {
        if (polls.Count == 0) return;
        Record("Performance", new { ReadMs = polls.Select(p => p.Ms).ToArray(), TraversalMs = polls.Select(p => p.Traversal).ToArray(),
            FirstAttach = polls.Select(p => p.First).ToArray(), Statuses = polls.GroupBy(p => p.Status).ToDictionary(g => g.Key, g => g.Count()) });
        polls.Clear();
    }
    internal void Memory(ArenaDraftMemoryUpdate update, MemoryCardName[] names, CaptureFreeReconciliation? reconciliation,
        Platform.ArenaWindowGeometry? window)
    {
        var s = update.CurrentPack ?? update.CurrentRead;
        Record("Memory", new { Stable = update.CurrentPack is not null, Transition = update.Transition.ToString(), update.Generation,
            update.SemanticPackKey, StateKey = ArenaDraftMemoryWatcher.StateKey(s), s.ProcessId, s.ProcessInstance,
            DraftId = s.DraftSessionId ?? s.CourseId ?? reconciliation?.State.DraftIdentifier?.Value,
            DraftIdSource = s.DraftSessionId is not null ? "Memory draft ID" : s.CourseId is not null ? "Memory course ID" : "Player.log or unavailable",
            s.EventName, s.PackNumber, s.PickNumber, CardCount = update.CurrentPack?.OrderedOfferedGrpIds.Count ?? 0,
            OrderedGrpIds = update.CurrentPack?.OrderedOfferedGrpIds ?? [], Names = names, s.ViewMode, s.RuntimeProfile, s.Build,
            SnapshotTime = s.ReadTimestamp, ReadMs = s.Duration.TotalMilliseconds, s.BytesRead,
            Consistency = update.CurrentPack is not null ? "Full checks + two coherent reads" : "Not current",
            Reconciliation = reconciliation?.Status, ReconciliationReason = reconciliation?.Reason,
            Reason = update.CurrentPack is null ? update.Transition + ": " + s.Detail : null,
            Client = window is null ? null : new { window.ProcessId, window.X, window.Y, window.Width, window.Height, DPI = window.Scaling * 96, window.IsWindowed } });
    }
    public void Dispose()
    {
        lock (gate)
        {
            FlushPolls();
            Record("RunEnded", new { ScreenCaptureInitialized = false, RejectedCaptureAttempts = CaptureFreeSafety.RejectedCaptureAttempts });
            writer.Dispose();
        }
    }
}
