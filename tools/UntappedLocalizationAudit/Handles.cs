using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using static UntappedLocalizationAudit.Native;

namespace UntappedLocalizationAudit;

internal sealed record HandleEntry(ulong Object, uint Pid, ulong Value, uint Access, ushort Type);

/// <summary>
/// Which kernel objects do Untapped processes hold? Uses the system handle table (SystemExtendedHandleInformation)
/// and, to identify a target, duplicates a handle into THIS process and asks only for its process id / file path.
/// Never reads or writes the memory of Untapped or Arena, never injects, never changes either process.
/// </summary>
internal static class Handles
{
    private const uint PROCESS_DUP_HANDLE = 0x40, PROCESS_QUERY_LIMITED_INFORMATION = 0x1000, DUPLICATE_SAME_ACCESS = 2;

    // Access masks that commonly belong to synchronous pipes whose name query can block; never queried by name.
    private static readonly HashSet<uint> RiskyFileAccess = [0x0012019F, 0x001A019F, 0x00120189, 0x0016019F];

    public static List<HandleEntry> Snapshot()
    {
        var size = 1 << 22;
        while (true)
        {
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                var status = NtQuerySystemInformation(64, buffer, size, out var needed);
                if (status == unchecked((int)0xC0000004)) { size = Math.Max(size * 2, needed + (1 << 20)); continue; }
                if (status != 0) throw new InvalidOperationException($"NtQuerySystemInformation status 0x{status:X8}");
                var count = Marshal.ReadInt64(buffer);
                var list = new List<HandleEntry>((int)count);
                for (long i = 0; i < count; i++)
                {
                    var p = buffer + 16 + (nint)(i * 40);
                    list.Add(new HandleEntry((ulong)Marshal.ReadInt64(p), (uint)Marshal.ReadInt64(p + 8), (ulong)Marshal.ReadInt64(p + 16),
                        (uint)Marshal.ReadInt32(p + 24), (ushort)Marshal.ReadInt16(p + 30)));
                }
                return list;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
    }

    public static string ProcessAccess(uint a)
    {
        if ((a & 0x1FFFFF) == 0x1FFFFF) return "PROCESS_ALL_ACCESS";
        var flags = new List<string>();
        void F(uint bit, string name) { if ((a & bit) == bit) flags.Add(name); }
        F(0x0001, "TERMINATE"); F(0x0002, "CREATE_THREAD"); F(0x0008, "VM_OPERATION"); F(0x0010, "VM_READ"); F(0x0020, "VM_WRITE");
        F(0x0040, "DUP_HANDLE"); F(0x0080, "CREATE_PROCESS"); F(0x0100, "SET_QUOTA"); F(0x0200, "SET_INFORMATION");
        F(0x0400, "QUERY_INFORMATION"); F(0x0800, "SUSPEND_RESUME"); F(0x1000, "QUERY_LIMITED_INFORMATION");
        F(0x10000, "DELETE"); F(0x20000, "READ_CONTROL"); F(0x40000, "WRITE_DAC"); F(0x80000, "WRITE_OWNER"); F(0x100000, "SYNCHRONIZE");
        return string.Join('|', flags);
    }

    private static string? FilePath(nint source, ulong value)
    {
        if (!DuplicateHandle(source, (nint)value, GetCurrentProcess(), out var dup, 0, false, DUPLICATE_SAME_ACCESS)) return null;
        string? result = null;
        var worker = new Thread(() =>
        {
            if (GetFileType(dup) != 1) return; // FILE_TYPE_DISK only.
            var builder = new StringBuilder(1024);
            if (GetFinalPathNameByHandle(dup, builder, (uint)builder.Capacity, 0) > 0) result = builder.ToString();
        }) { IsBackground = true };
        worker.Start();
        if (!worker.Join(300)) return "<timeout>"; // Leak the duplicate rather than closing it under a blocked query.
        CloseHandle(dup);
        return result;
    }

    public static object Run(bool includeFiles)
    {
        var self = (uint)Environment.ProcessId;
        var arenaPid = (uint)(Process.GetProcessesByName("MTGA").FirstOrDefault()?.Id ?? 0);
        var untapped = Process.GetProcesses().Where(p => Topology.IsUntapped(p.ProcessName)).Select(p => (uint)p.Id).OrderBy(p => p).ToList();

        // Reference handles owned by THIS process reveal the Process/File object-type indexes for this OS build.
        var refProcess = arenaPid != 0 ? OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, arenaPid) : 0;
        using var refFile = File.Open(Program.PlayerLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var refFileValue = (ulong)refFile.SafeFileHandle.DangerousGetHandle();
        var watch = Stopwatch.StartNew();
        var snapshot = Snapshot();
        var snapshotMs = watch.ElapsedMilliseconds;
        var ownProcess = snapshot.FirstOrDefault(e => e.Pid == self && e.Value == (ulong)refProcess);
        var ownFile = snapshot.FirstOrDefault(e => e.Pid == self && e.Value == refFileValue);
        if (ownProcess is null || ownFile is null) throw new InvalidOperationException("Reference handles not found in the system handle table.");
        var arenaObject = ownProcess.Object;

        var perProcess = new List<object>();
        foreach (var pid in untapped)
        {
            var source = OpenProcess(PROCESS_DUP_HANDLE, false, pid);
            var dupError = source == 0 ? Marshal.GetLastWin32Error() : 0;
            var entries = snapshot.Where(e => e.Pid == pid).ToList();
            var processHandles = new List<object>();
            foreach (var e in entries.Where(e => e.Type == ownProcess.Type))
            {
                uint target = 0; var how = "unresolved";
                if (arenaObject != 0 && e.Object == arenaObject) { target = arenaPid; how = "kernel-object-address"; }
                else if (source != 0)
                {
                    if (DuplicateHandle(source, (nint)e.Value, GetCurrentProcess(), out var dup, 0, false, DUPLICATE_SAME_ACCESS))
                    { target = GetProcessId(dup); CloseHandle(dup); how = "duplicate(same access)"; }
                    if (target == 0 && DuplicateHandle(source, (nint)e.Value, GetCurrentProcess(), out var dup2, PROCESS_QUERY_LIMITED_INFORMATION, false, 0))
                    { target = GetProcessId(dup2); CloseHandle(dup2); how = "duplicate(query-limited)"; }
                }
                processHandles.Add(new
                {
                    Handle = $"0x{e.Value:X}", Access = $"0x{e.Access:X}", AccessFlags = ProcessAccess(e.Access),
                    TargetPid = target, TargetName = target == 0 ? "?" : target == pid ? "(self)" : Topology.ProcessName(target),
                    TargetIsArena = target != 0 && target == arenaPid, VmReadGranted = (e.Access & 0x10) != 0, How = how,
                });
            }
            var files = new List<object>();
            var skippedRisky = 0;
            if (includeFiles && source != 0)
                foreach (var e in entries.Where(e => e.Type == ownFile.Type))
                {
                    if (RiskyFileAccess.Contains(e.Access)) { skippedRisky++; continue; }
                    var path = FilePath(source, e.Value);
                    if (path is not null) files.Add(new { Handle = $"0x{e.Value:X}", Access = $"0x{e.Access:X}", Path = path });
                }
            if (source != 0) CloseHandle(source);
            perProcess.Add(new
            {
                Pid = pid, HandleCount = entries.Count, DuplicateAccessError = dupError,
                TypeHistogram = entries.GroupBy(e => e.Type).OrderBy(g => g.Key).ToDictionary(g => $"type{g.Key}", g => g.Count()),
                ProcessHandles = processHandles, DiskFiles = files, RiskyFileHandlesNotQueried = skippedRisky,
            });
        }

        // Every holder of the Arena process object, system-wide, when kernel object addresses are exposed to this caller.
        var arenaHolders = arenaObject == 0 ? null : snapshot.Where(e => e.Object == arenaObject && e.Pid != self)
            .Select(e => new { e.Pid, Name = Topology.ProcessName(e.Pid), Access = $"0x{e.Access:X}", AccessFlags = ProcessAccess(e.Access) }).ToList();
        if (refProcess != 0) CloseHandle(refProcess);
        return new
        {
            TimestampLocal = DateTime.Now.ToString("o"), ArenaPid = arenaPid, SystemHandleCount = snapshot.Count, SnapshotMs = snapshotMs,
            ProcessTypeIndex = ownProcess.Type, FileTypeIndex = ownFile.Type,
            KernelObjectAddressesExposed = arenaObject != 0, ArenaProcessObjectHoldersSystemWide = arenaHolders,
            UntappedProcesses = perProcess,
        };
    }
}
