using System.Diagnostics;

namespace DraftTG.ArenaIntegration.Memory.Windows;

/// <summary>Debug-only entry point. Unknown fingerprints stop before Mono/object memory traversal.</summary>
public sealed class ArenaDraftMemoryReader
{
    public ArenaDraftMemorySnapshot ReadOnce(int? requestedProcessId = null)
    {
        var started = Stopwatch.StartNew();
        var timestamp = DateTimeOffset.UtcNow;
        ArenaProcessReader? process = null;
        ArenaBuildFingerprint? build = null;
        string? profile = null;
        int? pid = requestedProcessId;
        var active = false;
        try
        {
            if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
                return Result(ArenaMemoryStatus.UnsupportedPlatform, "Requires 64-bit Windows.");
            var candidates = ArenaProcessDiscovery.GetCandidates();
            try
            {
                if (candidates.Length == 0) return Result(ArenaMemoryStatus.NotRunning, "MTGA not running.");
                if (pid.HasValue && !candidates.Any(p => p.Id == pid.Value)) return Result(ArenaMemoryStatus.NotRunning, "Requested PID is not a running MTGA process.");
                if (!pid.HasValue && candidates.Length != 1) return Result(ArenaMemoryStatus.ObjectGraphIncomplete, "Multiple MTGA processes; choose --pid explicitly.");
                pid ??= candidates.Single().Id;
            }
            finally { foreach (var p in candidates) p.Dispose(); }
            process = new(pid.Value);
            build = ArenaMemoryCompatibility.Inspect(process.Architecture, process.Modules);
            if (!ArenaMemoryCompatibility.IsSupported(build)) return Result(ArenaMemoryStatus.UnsupportedBuild, "Unknown fingerprint; no Mono/object traversal attempted.");
            profile = ArenaMemoryCompatibility.ProfileId;
            var mono = ArenaProcessReader.Module(process.Modules, "mono-2.0-bdwgc.dll");
            var runtime = new ArenaMonoRuntime(process, mono.BaseAddress);
            runtime.Initialize(build);
            var exe = ArenaProcessReader.Module(process.Modules, "MTGA.exe");
            var graph = new ArenaDraftObjectGraph(process, runtime, Path.Combine(Path.GetDirectoryName(exe.Path)!, "MTGA_Data", "Managed"));
            var snapshot = graph.Read(() => active = true);
            active = true;
            return new(ArenaMemoryStatus.Success, "Complete ordered offered list; passive consistency checks passed.", pid,
                build, profile, timestamp, true, snapshot.ViewMode, Array.AsReadOnly(snapshot.GrpIds), snapshot.Identity, started.Elapsed, process.BytesRead);
        }
        catch (ArenaMemoryException ex) { return Result(ex.Status, ex.Message); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException or System.ComponentModel.Win32Exception or ArgumentException or OverflowException or System.Text.DecoderFallbackException)
        { return Result(profile is null && process is not null ? ArenaMemoryStatus.UnsupportedBuild : ArenaMemoryStatus.TransientReadFailure, $"{ex.GetType().Name}: {ex.Message}"); }
        finally { process?.Dispose(); }

        ArenaDraftMemorySnapshot Result(ArenaMemoryStatus status, string detail) =>
            new(status, detail, pid, build, profile, timestamp, active, null, Array.Empty<uint>(), null, started.Elapsed, process?.BytesRead ?? 0);
    }
}
