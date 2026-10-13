namespace DraftTG.ArenaIntegration.Memory.Windows;

internal sealed record MonoField(ulong Owner, int Offset, bool IsStatic);
internal sealed record MonoListState(ulong List, ulong Items, int Count, int Version);

/// <summary>Passive metadata subset. It does not invoke or initialize any Mono class/method.</summary>
internal sealed class ArenaMonoRuntime(IRemoteMemory memory, ulong monoBase)
{
    private readonly Dictionary<(ulong, string), MonoField> fieldCache = [];
    private readonly Dictionary<string, ulong> images = [];
    internal ulong RootDomain { get; private set; }

    internal void Initialize(ArenaBuildFingerprint build)
    {
        if (!ArenaMemoryCompatibility.IsSupported(build))
            throw new ArenaMemoryException(ArenaMemoryStatus.UnsupportedBuild, "Unknown fingerprint; no runtime reads attempted.");
        RootDomain = Require(memory.Ptr(monoBase + MonoLayout.RootDomainRva), "Mono root domain");
        var id = memory.I32(RootDomain + MonoLayout.DomainId);
        if (id is < 0 or > 32) Fail("Mono root domain ID outside bounds.");
        var node = memory.Ptr(monoBase + MonoLayout.LoadedAssembliesRva);
        var seen = new HashSet<ulong>();
        while (node != 0)
        {
            if (seen.Count >= 1024 || !seen.Add(node)) Fail("Loaded-assembly chain exceeds bounds or cycles.");
            var assembly = Require(memory.Ptr(node), "Loaded assembly");
            var image = Require(memory.Ptr(assembly + MonoLayout.AssemblyImage), "Assembly image");
            var name = Path.GetFileNameWithoutExtension(memory.CString(Require(memory.Ptr(image + MonoLayout.ImageName), "Image name"), 1024));
            if (build.Assemblies.Any(a => a.Name == name))
            {
                if (!images.TryAdd(name, image)) Fail($"Multiple loaded images named {name}.");
                var guid = memory.CString(Require(memory.Ptr(image + MonoLayout.ImageGuid), "Loaded image MVID"), 64);
                if (!Guid.TryParse(guid, out var mvid) || mvid != build.Assemblies.Single(a => a.Name == name).Mvid)
                    Fail($"Loaded {name} MVID does not match fingerprinted file.");
            }
            node = memory.Ptr(node + 8); // GSList: data pointer, next pointer (public Mono assembly.c).
        }
        foreach (var a in build.Assemblies) if (!images.ContainsKey(a.Name)) Fail($"Required loaded image {a.Name} unavailable.");
    }

    internal ulong FindClass(string assembly, string ns, string name, int token)
    {
        if (!images.TryGetValue(assembly, out var image)) Fail($"Missing image {assembly}.");
        var hash = image + MonoLayout.ImageClassCache;
        var size = RemoteMemory.Count(memory.I32(hash + MonoLayout.HashSize), 8192, "Class hash bucket count");
        var entries = RemoteMemory.Count(memory.I32(hash + MonoLayout.HashEntries), 65536, "Class hash entries");
        var buckets = Require(memory.Ptr(hash + MonoLayout.HashBuckets), "Class cache buckets");
        var seen = new HashSet<ulong>();
        ulong found = 0;
        for (var i = 0; i < size; i++)
        {
            var klass = memory.Ptr(buckets + (ulong)i * 8);
            while (klass != 0)
            {
                if (seen.Count >= entries || !seen.Add(klass)) Fail("Class cache cycle/duplicate/inconsistent count.");
                var kind = memory.U8(klass + MonoLayout.ClassKind);
                if (kind is not (1 or 2) || memory.Ptr(klass + MonoLayout.ClassImage) != image) Fail("Unexpected class-cache entry.");
                if (memory.I32(klass + MonoLayout.ClassToken) == token && ClassName(klass) == name && ClassNamespace(klass) == ns)
                { if (found != 0) Fail("Ambiguous class metadata."); found = klass; }
                klass = memory.Ptr(klass + MonoLayout.ClassNextCache);
            }
        }
        if (seen.Count != entries || memory.I32(hash + MonoLayout.HashEntries) != entries || memory.Ptr(hash + MonoLayout.HashBuckets) != buckets
            || memory.I32(hash + MonoLayout.HashSize) != size) Fail("Class cache changed during passive enumeration.");
        return Require(found, $"Loaded class {assembly}:{ns}.{name}");
    }

    internal string ClassName(ulong klass) => memory.CString(Require(memory.Ptr(klass + MonoLayout.ClassName), "Class name"));
    internal string ClassNamespace(ulong klass) => memory.CString(Require(memory.Ptr(klass + MonoLayout.ClassNamespace), "Class namespace"));
    internal ulong ObjectClass(ulong obj)
    {
        if ((obj & 7) != 0) Fail("Object pointer is not aligned.");
        var vt = Require(memory.Ptr(Require(obj, "Object")), "Object vtable");
        var klass = Require(memory.Ptr(vt), "Object class");
        if (((vt | klass) & 7) != 0) Fail("Vtable/class pointer is not aligned.");
        return klass;
    }
    internal void Expect(ulong obj, string assembly, string ns, string name)
    {
        var klass = ObjectClass(obj);
        if (ClassName(klass) != name || ClassNamespace(klass) != ns || memory.Ptr(klass + MonoLayout.ClassImage) != images[assembly])
            throw new ArenaMemoryException(ArenaMemoryStatus.ObjectGraphIncomplete, $"Expected {assembly}:{ns}.{name} object.");
    }
    internal MonoField Field(ulong klass, string name)
    {
        if (fieldCache.TryGetValue((klass, name), out var cached)) return cached;
        var seen = new HashSet<ulong>();
        for (var current = klass; current != 0; current = memory.Ptr(current + MonoLayout.ClassParent))
        {
            if (seen.Count >= 32 || !seen.Add(current)) Fail("Invalid class inheritance chain.");
            if ((memory.U32(current + MonoLayout.ClassMetadataFlags) & 0x180000) != 0x80000)
                Fail("Field metadata is uninitialized or a dynamic type; passive reader will not initialize it.");
            var definition = current;
            var kind = memory.U8(current + MonoLayout.ClassKind);
            if (kind == 3) definition = Require(memory.Ptr(Require(memory.Ptr(current + MonoLayout.ClassGenericClass), "Generic class")), "Generic definition");
            else if (kind is not (1 or 2)) Fail("Unsupported Mono class kind for field lookup.");
            var count = RemoteMemory.Count(memory.I32(definition + MonoLayout.ClassFieldCount), 1024, "Field count");
            if (count == 0) continue;
            var fields = Require(memory.Ptr(current + MonoLayout.ClassFields), "Initialized field table");
            for (var i = 0; i < count; i++)
            {
                var f = fields + (ulong)i * MonoLayout.FieldStride;
                if (memory.CString(Require(memory.Ptr(f + MonoLayout.FieldName), "Field name")) != name) continue;
                if (memory.Ptr(f + MonoLayout.FieldParent) != current) Fail("Field owner mismatch.");
                var offset = memory.I32(f + MonoLayout.FieldOffset);
                var type = Require(memory.Ptr(f), "Field MonoType");
                var isStatic = (memory.U16(type + 8) & 0x10) != 0;
                if (offset < 0 || offset > 1_048_576 || (!isStatic && (offset < (int)MonoLayout.ObjectHeader || offset >= memory.I32(current + MonoLayout.ClassSize))))
                    Fail($"Uninitialized/invalid field offset for {name}.");
                var result = new MonoField(current, offset, isStatic);
                fieldCache[(klass, name)] = result;
                return result;
            }
        }
        Fail($"Required field {name} unavailable (no remote initialization attempted).");
        return null!;
    }
    internal ulong Address(ulong obj, string field)
    {
        var f = Field(ObjectClass(obj), field);
        if (f.IsStatic) Fail($"{field} is unexpectedly static.");
        return obj + (ulong)f.Offset;
    }
    internal ulong Reference(ulong obj, string field) => memory.Ptr(Address(obj, field));
    internal ulong StaticReference(ulong klass, string name)
    {
        var f = Field(klass, name);
        if (!f.IsStatic) Fail($"{name} is not static.");
        var info = Require(memory.Ptr(klass + MonoLayout.ClassRuntimeInfo), "Class runtime info");
        var domain = memory.I32(RootDomain + MonoLayout.DomainId);
        if (memory.U16(info) < domain) Fail("Root-domain vtable unavailable.");
        var vt = Require(memory.Ptr(info + MonoLayout.RuntimeInfoVtables + (ulong)domain * 8), "Initialized class vtable");
        if (memory.Ptr(vt) != klass || (memory.U8(vt + MonoLayout.VtableFlags) & 4) == 0) Fail("Static data vtable mismatch/unavailable.");
        var slots = RemoteMemory.Count(memory.I32(klass + MonoLayout.ClassVtableSize), 4096, "Vtable slots");
        var data = Require(memory.Ptr(vt + MonoLayout.VtableSlots + (ulong)slots * 8), "Static data");
        return memory.Ptr(data + (ulong)f.Offset);
    }

    internal MonoListState ListState(ulong list, int max)
    {
        var klass = ObjectClass(list);
        if (ClassName(klass) != "List`1" || ClassNamespace(klass) != "System.Collections.Generic" || memory.Ptr(klass + MonoLayout.ClassImage) != images["mscorlib"])
            Fail("Expected framework List<T>.");
        var count = RemoteMemory.Count(memory.I32(Address(list, "_size")), max, "List count");
        var items = Require(Reference(list, "_items"), "List backing array");
        if (ArrayLength(items, 8192) < count) Fail("List count exceeds array capacity.");
        return new(list, items, count, memory.I32(Address(list, "_version")));
    }
    internal int ArrayLength(ulong array, int max)
    {
        var klass = ObjectClass(array);
        if (memory.U8(klass + MonoLayout.ClassKind) != 5 || memory.U8(klass + MonoLayout.ClassRank) != 1 || memory.Ptr(array + MonoLayout.ArrayBounds) != 0)
            Fail("Expected one-dimensional zero-based vector.");
        var length = memory.PtrValue(array + MonoLayout.ArrayLength);
        if (length > (ulong)max) Fail("Array length outside bounds.");
        return (int)length;
    }
    internal ulong[] References(MonoListState state)
    {
        var values = new ulong[state.Count];
        var seen = new HashSet<ulong>();
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = Require(memory.Ptr(state.Items + MonoLayout.ArrayData + (ulong)i * 8), "List entry");
            if (!seen.Add(values[i])) throw new ArenaMemoryException(ArenaMemoryStatus.InvalidPackState, "Duplicate object pointer in offered-card list.");
        }
        CheckList(state);
        return values;
    }
    internal void CheckList(MonoListState state)
    {
        if (ListState(state.List, 8192) != state)
            throw new ArenaMemoryException(ArenaMemoryStatus.TransientReadFailure, "List identity/count/version changed during traversal.");
    }

    internal static ulong Require(ulong pointer, string name)
    {
        if (pointer == 0) Fail($"{name} unavailable.");
        return pointer;
    }
    private static void Fail(string reason) => throw new ArenaMemoryException(ArenaMemoryStatus.MetadataUnavailable, reason);
}

internal static class RemoteValue
{
    // Lengths are native unsigned integers, not addresses; do not apply pointer alignment validation.
    internal static ulong PtrValue(this IRemoteMemory memory, ulong address) => System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(memory.Read(address, 8));
}
