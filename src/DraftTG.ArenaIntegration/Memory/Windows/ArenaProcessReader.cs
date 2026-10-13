using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace DraftTG.ArenaIntegration.Memory.Windows;

internal sealed record ArenaRemoteModule(string Path, ulong BaseAddress, uint Size)
{
    internal string Name => System.IO.Path.GetFileName(Path);
}

internal sealed class ArenaProcessReader : IRemoteMemory, IDisposable
{
    // No write, operation, debug, injection, or thread capability is requested.
    internal const uint AccessRights = 0x0400 | 0x0010;
    private readonly SafeProcessHandle handle;
    private readonly Stopwatch budget = Stopwatch.StartNew();
    internal long BytesRead { get; private set; }
    internal string Architecture { get; }
    internal IReadOnlyList<ArenaRemoteModule> Modules { get; }

    internal ArenaProcessReader(int pid)
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
            throw new ArenaMemoryException(ArenaMemoryStatus.UnsupportedPlatform, "Requires a 64-bit Windows reader.");
        handle = OpenProcess(AccessRights, false, pid);
        if (handle.IsInvalid) { handle.Dispose(); throw Failure("OpenProcess"); }
        try
        {
            if (!IsWow64Process2(handle, out var processMachine, out var nativeMachine)) throw Failure("IsWow64Process2");
            Architecture = processMachine == 0 && nativeMachine == 0x8664 ? "x64" : $"machine-{processMachine:X4}-{nativeMachine:X4}";
            Modules = EnumerateModules();
        }
        catch { handle.Dispose(); throw; }
    }

    internal static ArenaRemoteModule Module(IReadOnlyList<ArenaRemoteModule> modules, string name)
    {
        var matches = modules.Where(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
        return matches.Length == 1 ? matches[0] : throw new ArenaMemoryException(ArenaMemoryStatus.MetadataUnavailable, $"Expected one loaded module {name}; found {matches.Length}.");
    }

    private IReadOnlyList<ArenaRemoteModule> EnumerateModules()
    {
        var modules = new nint[2048];
        if (!EnumProcessModulesEx(handle, modules, (uint)(modules.Length * 8), out var needed, 3)) throw Failure("EnumProcessModulesEx");
        if (needed == 0 || needed % 8 != 0 || needed > modules.Length * 8)
            throw new ArenaMemoryException(ArenaMemoryStatus.MetadataUnavailable, "Module enumeration exceeded bounds or changed.");
        var result = new List<ArenaRemoteModule>();
        foreach (var module in modules.Take((int)needed / 8))
        {
            var name = new StringBuilder(32768);
            var chars = GetModuleFileNameEx(handle, module, name, name.Capacity);
            if (chars == 0 || chars >= name.Capacity - 1 || !GetModuleInformation(handle, module, out var info, (uint)Marshal.SizeOf<ModuleInfo>()))
                throw Failure("Module metadata");
            result.Add(new(name.ToString(), (ulong)info.BaseAddress, info.Size));
        }
        return result;
    }

    public byte[] Read(ulong address, int length)
    {
        RemoteMemory.Validate(address, length);
        if (budget.Elapsed > TimeSpan.FromSeconds(15) || BytesRead + length > 16 * 1024 * 1024)
            throw new ArenaMemoryException(ArenaMemoryStatus.TransientReadFailure, "Passive read time/byte budget exceeded.");
        if (length == 0) return [];
        var remaining = (ulong)length;
        var cursor = address;
        while (remaining > 0)
        {
            if (VirtualQueryEx(handle, (nint)cursor, out var region, (nuint)Marshal.SizeOf<MemoryInfo>()) == 0) throw Failure("VirtualQueryEx");
            var readable = region.State == 0x1000 && (region.Protect & 0x100) == 0
                && (region.Protect & 0xFF) is 0x02 or 0x04 or 0x08 or 0x20 or 0x40 or 0x80;
            if (!readable || cursor < (ulong)region.BaseAddress || (ulong)region.RegionSize <= cursor - (ulong)region.BaseAddress)
                throw new ArenaMemoryException(ArenaMemoryStatus.TransientReadFailure, "Remote region is not committed readable memory.");
            var consumed = Math.Min(remaining, (ulong)region.RegionSize - (cursor - (ulong)region.BaseAddress));
            cursor += consumed; remaining -= consumed;
        }
        var data = new byte[length];
        if (!ReadProcessMemory(handle, (nint)address, data, (nuint)length, out var read) || read != (nuint)length) throw Failure("ReadProcessMemory (no partial read accepted)");
        BytesRead += length;
        return data;
    }

    private static ArenaMemoryException Failure(string operation)
    {
        var code = Marshal.GetLastWin32Error();
        return new(code == 5 ? ArenaMemoryStatus.AccessDenied : ArenaMemoryStatus.TransientReadFailure,
            $"{operation}: Win32 {code} ({new Win32Exception(code).Message}).");
    }
    public void Dispose() => handle.Dispose();

    [StructLayout(LayoutKind.Sequential)] private struct ModuleInfo { public nint BaseAddress; public uint Size; public nint EntryPoint; }
    [StructLayout(LayoutKind.Sequential)] private struct MemoryInfo
    { public nint BaseAddress, AllocationBase; public uint AllocationProtect; public ushort PartitionId; public nuint RegionSize; public uint State, Protect, Type; }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool IsWow64Process2(SafeProcessHandle process, out ushort machine, out ushort native);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReadProcessMemory(SafeProcessHandle process, nint address, [Out] byte[] buffer, nuint size, out nuint read);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nuint VirtualQueryEx(SafeProcessHandle process, nint address, out MemoryInfo info, nuint length);
    [DllImport("psapi.dll", SetLastError = true)] private static extern bool EnumProcessModulesEx(SafeProcessHandle process, [Out] nint[] modules, uint bytes, out uint needed, uint filter);
    [DllImport("psapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint GetModuleFileNameEx(SafeProcessHandle process, nint module, StringBuilder name, int length);
    [DllImport("psapi.dll", SetLastError = true)] private static extern bool GetModuleInformation(SafeProcessHandle process, nint module, out ModuleInfo info, uint size);
}
