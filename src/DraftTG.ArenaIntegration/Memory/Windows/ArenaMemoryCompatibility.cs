using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace DraftTG.ArenaIntegration.Memory.Windows;

internal static class ArenaMemoryCompatibility
{
    internal const string ProfileId = "unity-mono-x64-69dd7ca1-arena-2026-10-13-v1";
    internal static ArenaBuildFingerprint Supported { get; } = new("x64", "6000.3.14.14060607",
        "E22C729EEF75383888CB3D26A8CB4C2372BC6ACDAE7D7D9215C5020BA257FA37",
        "BBDF3E73522223EA45C3A54049B716F9DA3E632994636734C5D53326AA2C36D0",
        "69DD7CA153D9BD718ED6176C1E6FF065721A9408964384A421602F7D5527A294",
        [new("Core", "947441EECC9A9231503AF434AD7970E6AF0BC2C56148D56991BBA5B6C76458E6", new("304dcc50-070c-4610-9196-b6124619943f")),
         new("SharedClientCore", "FF02A80282F36C45D6A639C6DC5013EFF16406BCE6EB0EF9985B59E1C3244B33", new("5028378c-e315-416c-92ef-a565601d5b8d")),
         new("Assembly-CSharp", "9B155EAAF58561B739AF6AC175E4CF3E59094E901D256B880912306F6ED4BE42", new("dfb5e0ff-0b6c-4d9f-aa38-10f3cbf5ce2e")),
         new("mscorlib", "6C76E06E6D44CF0F39BF59E6A90770968B70093AD4C1572DFBC5E46E431C5FBF", new("41d29b35-2f6a-475a-b1bf-7c6628b82790"))]);

    internal static bool IsSupported(ArenaBuildFingerprint f) =>
        f.Architecture == Supported.Architecture && f.UnityVersion == Supported.UnityVersion
        && f.ExecutableSha256 == Supported.ExecutableSha256 && f.UnityPlayerSha256 == Supported.UnityPlayerSha256
        && f.MonoSha256 == Supported.MonoSha256 && f.Assemblies.Count == Supported.Assemblies.Count
        && Supported.Assemblies.All(s => f.Assemblies.Count(a => a == s) == 1);

    // This boundary inspects local files/module identity only. It never traverses target memory.
    internal static ArenaBuildFingerprint Inspect(string architecture, IReadOnlyList<ArenaRemoteModule> modules)
    {
        var exe = ArenaProcessReader.Module(modules, "MTGA.exe");
        var player = ArenaProcessReader.Module(modules, "UnityPlayer.dll");
        var mono = ArenaProcessReader.Module(modules, "mono-2.0-bdwgc.dll");
        var managed = Path.Combine(Path.GetDirectoryName(exe.Path)!, "MTGA_Data", "Managed");
        var identities = new List<ArenaManagedAssemblyIdentity>();
        foreach (var name in Supported.Assemblies.Select(a => a.Name))
        {
            var path = Path.Combine(managed, name + ".dll");
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata) throw new ArenaMemoryException(ArenaMemoryStatus.UnsupportedBuild, $"{name} has no managed metadata.");
            var metadata = pe.GetMetadataReader();
            if (metadata.GetString(metadata.GetAssemblyDefinition().Name) != name)
                throw new ArenaMemoryException(ArenaMemoryStatus.UnsupportedBuild, "Unexpected managed assembly name.");
            identities.Add(new(name, Hash(path), metadata.GetGuid(metadata.GetModuleDefinition().Mvid)));
        }
        return new(architecture, FileVersionInfo.GetVersionInfo(player.Path).FileVersion ?? "unknown",
            Hash(exe.Path), Hash(player.Path), Hash(mono.Path), identities);
    }
    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}

/// <summary>
/// Only for the exact Mono SHA256 above. Public Unity Mono layouts corroborated by passive
/// disassembly of this installed DLL's getters/lookup paths; see the Phase 9F.1 offset table.
/// Game and framework managed field offsets are resolved at runtime, never stored here.
/// </summary>
internal static class MonoLayout
{
    internal const ulong RootDomainRva = 0x73FF80, LoadedAssembliesRva = 0x73FF90;
    internal const ulong AssemblyImage = 0x60, ImageName = 0x30, ImageGuid = 0x58, ImageClassCache = 0x4D0;
    internal const ulong HashSize = 0x18, HashEntries = 0x1C, HashBuckets = 0x20;
    internal const ulong ClassNextCache = 0x108, ClassRank = 0x1A, ClassKind = 0x1B, ClassSize = 0x1C, ClassMetadataFlags = 0x28,
        ClassParent = 0x30, ClassImage = 0x40, ClassName = 0x48, ClassNamespace = 0x50, ClassToken = 0x58,
        ClassVtableSize = 0x5C, ClassFields = 0x98, ClassRuntimeInfo = 0xD0, ClassGenericClass = 0xF0, ClassFieldCount = 0x100;
    internal const ulong FieldStride = 0x20, FieldName = 8, FieldParent = 0x10, FieldOffset = 0x18;
    internal const ulong DomainId = 0x94, RuntimeInfoVtables = 8, VtableFlags = 0x30, VtableSlots = 0x48;
    internal const ulong ObjectHeader = 0x10, ArrayBounds = 0x10, ArrayLength = 0x18, ArrayData = 0x20,
        StringLength = 0x10, StringData = 0x14;
}
