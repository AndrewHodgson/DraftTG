using System.Buffers.Binary;
using System.Text;

namespace DraftTG.ArenaIntegration.Memory.Windows;

internal sealed class ArenaMemoryException(ArenaMemoryStatus status, string message) : Exception(message)
{
    public ArenaMemoryStatus Status { get; } = status;
    public bool ResetConnection { get; init; }
}

internal interface IRemoteMemory
{
    byte[] Read(ulong address, int length);
}

internal static class RemoteMemory
{
    internal const int MaxRead = 65536;
    internal const ulong MaxAddress = 0x00007FFFFFFFFFFF;
    internal static void Validate(ulong address, int length)
    {
        if (length < 0 || length > MaxRead || address < 0x10000 || address > MaxAddress
            || (ulong)length > MaxAddress - address)
            throw new ArenaMemoryException(ArenaMemoryStatus.TransientReadFailure, "Read outside bounded user address space.");
    }
    internal static ulong Ptr(this IRemoteMemory m, ulong address)
    {
        var result = BinaryPrimitives.ReadUInt64LittleEndian(m.Read(address, 8));
        if (result != 0 && (result < 0x10000 || result > MaxAddress))
            throw new ArenaMemoryException(ArenaMemoryStatus.ObjectGraphIncomplete, "Invalid remote pointer.");
        return result;
    }
    internal static int I32(this IRemoteMemory m, ulong address) => BinaryPrimitives.ReadInt32LittleEndian(m.Read(address, 4));
    internal static uint U32(this IRemoteMemory m, ulong address) => BinaryPrimitives.ReadUInt32LittleEndian(m.Read(address, 4));
    internal static ushort U16(this IRemoteMemory m, ulong address) => BinaryPrimitives.ReadUInt16LittleEndian(m.Read(address, 2));
    internal static byte U8(this IRemoteMemory m, ulong address) => m.Read(address, 1)[0];
    internal static bool Bool(this IRemoteMemory m, ulong address) => m.U8(address) switch
    { 0 => false, 1 => true, _ => throw new ArenaMemoryException(ArenaMemoryStatus.InvalidPackState, "Invalid boolean value.") };
    internal static int Count(int count, int max, string name)
    {
        if (count < 0 || count > max) throw new ArenaMemoryException(ArenaMemoryStatus.InvalidPackState, $"{name} outside 0..{max}: {count}.");
        return count;
    }
    internal static string CString(this IRemoteMemory m, ulong address, int max = 512)
    {
        if (max < 1 || max > 4096) throw new ArgumentOutOfRangeException(nameof(max));
        // Byte reads avoid crossing into a page past the terminator. Only narrow metadata names use this.
        var bytes = new List<byte>();
        for (var i = 0; i < max; i++)
        {
            var b = m.U8(address + (ulong)i);
            if (b == 0) return new UTF8Encoding(false, true).GetString(bytes.ToArray());
            bytes.Add(b);
        }
        throw new ArenaMemoryException(ArenaMemoryStatus.MetadataUnavailable, "Unterminated metadata string.");
    }
    internal static string ManagedString(this IRemoteMemory m, ulong address, int max = 4096)
    {
        if (max < 0 || max > 4096) throw new ArgumentOutOfRangeException(nameof(max));
        var length = Count(m.I32(address + MonoLayout.StringLength), max, "Managed string length");
        return new UnicodeEncoding(false, false, true).GetString(m.Read(address + MonoLayout.StringData, checked(length * 2)));
    }
}
