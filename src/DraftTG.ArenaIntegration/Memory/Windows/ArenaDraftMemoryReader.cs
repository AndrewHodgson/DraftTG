using System.Diagnostics;

namespace DraftTG.ArenaIntegration.Memory.Windows;

/// <summary>ReadOnce disposes; ReadCurrent caches process-lifetime metadata for a serial watcher.</summary>
public sealed class ArenaDraftMemoryReader(Func<uint,uint,ArenaOfferedCardIdentity>? printingReconciler = null) : IDisposable
{
    private ArenaProcessReader? process;
    private ArenaBuildFingerprint? build;
    private ArenaDraftObjectGraph? graph;
    private string? instance;
    private bool disposed;
    public ArenaDraftMemorySnapshot ReadOnce(int? requestedProcessId = null)
    {
        using var oneShot = new ArenaDraftMemoryReader(printingReconciler);
        return oneShot.ReadCurrent(requestedProcessId);
    }
    public ArenaDraftMemorySnapshot ReadCurrent(int? requestedProcessId = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var started = Stopwatch.StartNew();
        var timestamp = DateTimeOffset.UtcNow;
        int? pid = requestedProcessId;
        var active = false;
        var firstAttach = false;
        ArenaUiMemorySnapshot? ui = null;
        Stopwatch? traversal = null;
        try
        {
            if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
                return Result(ArenaMemoryStatus.UnsupportedPlatform, "Requires 64-bit Windows.");
            var candidates = ArenaProcessDiscovery.GetCandidates();
            string nextInstance;
            try
            {
                if (candidates.Length == 0) { Reset(); return Result(ArenaMemoryStatus.NotRunning, "MTGA not running."); }
                if (pid.HasValue && !candidates.Any(p => p.Id == pid)) { Reset(); return Result(ArenaMemoryStatus.NotRunning, "Requested PID is not running MTGA."); }
                if (!pid.HasValue && candidates.Length != 1) { Reset(); return Result(ArenaMemoryStatus.ObjectGraphIncomplete, "Multiple MTGA processes; choose --pid explicitly."); }
                var selected = candidates.Single(p => !pid.HasValue || p.Id == pid);
                pid = selected.Id;
                nextInstance = $"{pid}:{selected.StartTime.ToUniversalTime().Ticks}";
            }
            finally { foreach (var p in candidates) p.Dispose(); }
            if (instance != nextInstance)
            {
                Reset(); instance = nextInstance; firstAttach = true;
                process = new(pid!.Value);
                build = ArenaMemoryCompatibility.Inspect(process.Architecture, process.Modules);
                if (ArenaMemoryCompatibility.IsSupported(build))
                {
                    var mono = ArenaProcessReader.Module(process.Modules, "mono-2.0-bdwgc.dll");
                    var runtime = new ArenaMonoRuntime(process, mono.BaseAddress);
                    runtime.Initialize(build);
                    var exe = ArenaProcessReader.Module(process.Modules, "MTGA.exe");
                    graph = new(process, runtime, Path.Combine(Path.GetDirectoryName(exe.Path)!, "MTGA_Data", "Managed"), ArenaProcessReader.Module(process.Modules,"UnityPlayer.dll").BaseAddress,printingReconciler);
                }
            }
            if (process is null || build is null) { Reset(); return Result(ArenaMemoryStatus.TransientReadFailure, "Attachment incomplete."); }
            if (!ArenaMemoryCompatibility.IsSupported(build))
                return Result(ArenaMemoryStatus.UnsupportedBuild, "Unknown fingerprint; no Mono/object traversal attempted.");
            if (!firstAttach) process.BeginSnapshot();
            traversal = Stopwatch.StartNew();
            var snapshot = graph!.Read(() => active = true, observed => ui = observed);
            return new(ArenaMemoryStatus.Success, "Complete pack; scene/list/occurrence/logical-count/pod checks passed.", pid,
                build, ArenaMemoryCompatibility.ProfileFor(build), timestamp, true, snapshot.ViewMode,
                Array.AsReadOnly(snapshot.GrpIds), snapshot.Identity, started.Elapsed, process.BytesRead)
            { ProcessInstance = instance, DraftSessionId = snapshot.DraftId, EventName = snapshot.EventName, DraftKind = snapshot.DraftKind,
                CourseId = snapshot.CourseId,
                PackNumber = snapshot.Pack, PickNumber = snapshot.Pick, IsFirstAttach = firstAttach, TraversalDuration = traversal.Elapsed, Ui = snapshot.Ui, PrintingIdentities=snapshot.Printings };
        }
        catch (ArenaMemoryException ex)
        {
            var result = Result(ex.Status, ex.Message);
            if (ex.ResetConnection || graph is null && ex.Status != ArenaMemoryStatus.UnsupportedBuild) Reset();
            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException
            or System.ComponentModel.Win32Exception or ArgumentException or InvalidOperationException or OverflowException)
        {
            var result = Result(graph is null && process is not null ? ArenaMemoryStatus.UnsupportedBuild : ArenaMemoryStatus.TransientReadFailure,
                $"{ex.GetType().Name}: {ex.Message}");
            Reset(); return result;
        }
        ArenaDraftMemorySnapshot Result(ArenaMemoryStatus status, string detail) =>
            new(status, detail, pid, build, graph is null ? null : ArenaMemoryCompatibility.ProfileId, timestamp, active,
                null, Array.Empty<uint>(), null, started.Elapsed, process?.BytesRead ?? 0)
            { ProcessInstance = instance, IsFirstAttach = firstAttach, Ui = ui, TraversalDuration = traversal?.Elapsed ?? TimeSpan.Zero };
    }
    private void Reset() { process?.Dispose(); process = null; build = null; graph = null; instance = null; }
    public void Dispose() { if (disposed) return; disposed = true; Reset(); }
}
