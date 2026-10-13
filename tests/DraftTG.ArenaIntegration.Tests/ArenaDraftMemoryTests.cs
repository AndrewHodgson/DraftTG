using System.Buffers.Binary;
using System.Text;
using DraftTG.ArenaIntegration.Memory;
using DraftTG.ArenaIntegration.Memory.Windows;

namespace DraftTG.ArenaIntegration.Tests;

public sealed class ArenaDraftMemoryTests
{
    [Fact]
    public void ProcessRightsAreExactlyQueryAndRead()
    {
        Assert.Equal(0x410u, ArenaProcessReader.AccessRights);
        Assert.Equal(0u, ArenaProcessReader.AccessRights & (0x20u | 0x8u | 0x2u));
    }

    [Fact]
    public void ReadBoundsRejectOverflowLowAddressesAndUnboundedLengths()
    {
        RemoteMemory.Validate(0x10000, 65536);
        foreach (var (address, size) in new (ulong, int)[] { (0, 8), (ulong.MaxValue, 8), (RemoteMemory.MaxAddress, 8), (0x10000, -1), (0x10000, 65537) })
            Assert.Equal(ArenaMemoryStatus.TransientReadFailure, Assert.Throws<ArenaMemoryException>(() => RemoteMemory.Validate(address, size)).Status);
        Assert.Throws<ArenaMemoryException>(() => RemoteMemory.Count(-1, 32, "pack"));
        Assert.Throws<ArenaMemoryException>(() => RemoteMemory.Count(33, 32, "pack"));
    }

    [Fact]
    public void ModuleLookupIsCaseInsensitiveAndRejectsMissingOrDuplicateModules()
    {
        var module = new ArenaRemoteModule("mono-2.0-bdwgc.dll", 0x100000, 0x800000);
        Assert.Same(module, ArenaProcessReader.Module([module], "MONO-2.0-BDWGC.DLL"));
        Assert.Throws<ArenaMemoryException>(() => ArenaProcessReader.Module([], module.Name));
        Assert.Throws<ArenaMemoryException>(() => ArenaProcessReader.Module([module, module], module.Name));
    }

    [Fact]
    public void EveryFingerprintComponentIsRequiredAndUnknownBuildPerformsZeroRuntimeReads()
    {
        var supported = ArenaMemoryCompatibility.Supported;
        Assert.True(ArenaMemoryCompatibility.IsSupported(supported));
        var changedAssembly = supported.Assemblies.Select((a, i) => i == 0 ? a with { Mvid = Guid.Empty } : a).ToArray();
        var changedHash = supported.Assemblies.Select((a, i) => i == 0 ? a with { Sha256 = "unknown" } : a).ToArray();
        foreach (var changed in new[] { supported with { Architecture = "x86" }, supported with { UnityVersion = "unknown" },
            supported with { ExecutableSha256 = "unknown" }, supported with { UnityPlayerSha256 = "unknown" }, supported with { MonoSha256 = "unknown" },
            supported with { Assemblies = changedAssembly }, supported with { Assemblies = changedHash }, supported with { Assemblies = [] } })
        {
            var memory = new FixtureMemory();
            var runtime = new ArenaMonoRuntime(memory, 0x1000000);
            Assert.Equal(ArenaMemoryStatus.UnsupportedBuild, Assert.Throws<ArenaMemoryException>(() => runtime.Initialize(changed)).Status);
            Assert.Equal(0, memory.Reads);
        }
    }

    [Fact]
    public void LoadedMvidMismatchDeclinesBeforeObjectTraversal()
    {
        var fixture = new MonoFixture(initialize: false);
        fixture.Memory.CString(fixture.GuidAddresses[0], Guid.Empty.ToString());
        Assert.Equal(ArenaMemoryStatus.MetadataUnavailable,
            Assert.Throws<ArenaMemoryException>(() => fixture.Runtime.Initialize(ArenaMemoryCompatibility.Supported)).Status);
        var initialized = new MonoFixture();
        initialized.Memory.U64(initialized.MonoBase + MonoLayout.RootDomainRva, 0x200100);
        Assert.True(Assert.Throws<ArenaMemoryException>(() => initialized.Runtime.CheckRootDomain()).ResetConnection);
    }

    [Fact]
    public void BoundedStringsUseCorrectEncodingsAndRejectTruncationOrInvalidLengths()
    {
        var memory = new FixtureMemory();
        memory.CString(0x10001, "Core"); // C-string pointers need not be eight-byte aligned.
        Assert.Equal("Core", memory.CString(0x10001, 5));
        Assert.Throws<ArenaMemoryException>(() => memory.CString(0x10001, 4));
        var text = "Arena Ω 🂡";
        memory.I32(0x20000 + MonoLayout.StringLength, text.Length);
        memory.Bytes(0x20000 + MonoLayout.StringData, Encoding.Unicode.GetBytes(text));
        Assert.Equal(text, memory.ManagedString(0x20000));
        memory.I32(0x20000 + MonoLayout.StringLength, -1);
        Assert.Throws<ArenaMemoryException>(() => memory.ManagedString(0x20000));
        memory.I32(0x20000 + MonoLayout.StringLength, 4097);
        Assert.Throws<ArenaMemoryException>(() => memory.ManagedString(0x20000));
        memory.I32(0x20000 + MonoLayout.StringLength, 1);
        memory.Bytes(0x20000 + MonoLayout.StringData, [0, 0xD8]);
        Assert.Throws<DecoderFallbackException>(() => memory.ManagedString(0x20000));
    }

    [Fact]
    public void GenericListFieldsAreDynamicallyResolvedRatherThanUsingGameOffsets()
    {
        var fixture = new MonoFixture();
        var state = fixture.Runtime.ListState(fixture.List, 32);
        Assert.Equal(2, state.Count);
        Assert.Equal(9, state.Version);
        Assert.Equal(new ulong[] { 0x700000, 0x700100 }, fixture.Runtime.References(state));
        Assert.Equal(24, fixture.Runtime.Field(fixture.ListClass, "_size").Offset);
        Assert.Throws<ArenaMemoryException>(() => fixture.Runtime.Field(fixture.ListClass, "missing"));
        fixture.Memory.I32(fixture.SizeField + MonoLayout.FieldOffset, -1);
        // A new runtime avoids the successful lookup's local metadata cache.
        var fresh = new ArenaMonoRuntime(fixture.Memory, fixture.MonoBase);
        fresh.Initialize(ArenaMemoryCompatibility.Supported);
        Assert.Throws<ArenaMemoryException>(() => fresh.Field(fixture.ListClass, "_size"));
    }

    [Fact]
    public void ArrayCapacityAndDuplicateObjectPointersFailClosed()
    {
        var fixture = new MonoFixture();
        fixture.Memory.U64(fixture.Array + MonoLayout.ArrayLength, 1);
        Assert.Throws<ArenaMemoryException>(() => fixture.Runtime.ListState(fixture.List, 32));
        fixture.Memory.U64(fixture.Array + MonoLayout.ArrayLength, 2);
        fixture.Memory.U64(fixture.Array + MonoLayout.ArrayData + 8, 0x700000);
        var state = fixture.Runtime.ListState(fixture.List, 32);
        Assert.Equal(ArenaMemoryStatus.InvalidPackState, Assert.Throws<ArenaMemoryException>(() => fixture.Runtime.References(state)).Status);
    }

    [Fact]
    public void ListVersionMutationDuringTraversalDiscardsAllResults()
    {
        var fixture = new MonoFixture();
        var state = fixture.Runtime.ListState(fixture.List, 32);
        fixture.Memory.BeforeRead = address =>
        {
            if (address == fixture.Array + MonoLayout.ArrayData) fixture.Memory.I32(fixture.List + 28, 10);
        };
        Assert.Equal(ArenaMemoryStatus.TransientReadFailure, Assert.Throws<ArenaMemoryException>(() => fixture.Runtime.References(state)).Status);
    }

    [Fact]
    public void CompleteSnapshotRequiresEveryOccurrenceAndAllowsDuplicateCardIdentities()
    {
        ArenaDraftSnapshotRules.RequireComplete(2, new uint[] { 106401, 106401 });
        foreach (var (expected, cards) in new (int, uint[])[] { (0, []), (2, [106401]), (2, [106401, 0]), (1, [uint.MaxValue]), (33, new uint[33]) })
            Assert.Equal(ArenaMemoryStatus.InvalidPackState, Assert.Throws<ArenaMemoryException>(() => ArenaDraftSnapshotRules.RequireComplete(expected, cards)).Status);
    }

    [Theory]
    [InlineData("list")]
    [InlineData("column")]
    public void CliFormatsOrderedGrpIdsWithoutReordering(string view)
    {
        var snapshot = Snapshot(ArenaMemoryStatus.Success) with { IsDraftSceneActive = true, ViewMode = view, OrderedOfferedGrpIds = new uint[] { 106451, 106292 } };
        var output = ArenaDraftMemoryFormatter.Format(snapshot);
        Assert.Contains("Draft scene: active", output);
        Assert.Contains($"View: {view}", output);
        Assert.Contains("Pack count: 2", output);
        Assert.True(output.IndexOf("0  GrpId=106451", StringComparison.Ordinal) < output.IndexOf("1  GrpId=106292", StringComparison.Ordinal));
    }

    [Fact]
    public void CliFailureFormattingNeverPrintsPartialCardsAsAnOfferedPack()
    {
        foreach (var status in new[] { ArenaMemoryStatus.NotRunning, ArenaMemoryStatus.UnsupportedBuild, ArenaMemoryStatus.DraftSceneNotActive, ArenaMemoryStatus.TransientReadFailure })
        {
            var output = ArenaDraftMemoryFormatter.Format(Snapshot(status) with { OrderedOfferedGrpIds = new uint[] { 1 } });
            Assert.DoesNotContain("Ordered offered cards:", output);
            Assert.DoesNotContain("Pack count:", output);
        }
        Assert.Contains("MTGA not running", ArenaDraftMemoryFormatter.Format(Snapshot(ArenaMemoryStatus.NotRunning)));
        Assert.Contains("unsupported build", ArenaDraftMemoryFormatter.Format(Snapshot(ArenaMemoryStatus.UnsupportedBuild)));
        Assert.Contains("Draft scene: inactive", ArenaDraftMemoryFormatter.Format(Snapshot(ArenaMemoryStatus.DraftSceneNotActive)));
    }

    private static ArenaDraftMemorySnapshot Snapshot(ArenaMemoryStatus status) =>
        new(status, "fixture", 123, null, null, DateTimeOffset.UnixEpoch, false, null, [], null, TimeSpan.Zero, 0);

    private sealed class FixtureMemory : IRemoteMemory
    {
        private readonly Dictionary<ulong, byte> bytes = [];
        internal int Reads;
        internal Action<ulong>? BeforeRead;
        public byte[] Read(ulong address, int length)
        {
            RemoteMemory.Validate(address, length);
            Reads++;
            BeforeRead?.Invoke(address);
            return Enumerable.Range(0, length).Select(i => bytes.GetValueOrDefault(address + (ulong)i)).ToArray();
        }
        internal void Bytes(ulong address, byte[] value) { for (var i = 0; i < value.Length; i++) bytes[address + (ulong)i] = value[i]; }
        internal void U64(ulong address, ulong value) { var b = new byte[8]; BinaryPrimitives.WriteUInt64LittleEndian(b, value); Bytes(address, b); }
        internal void I32(ulong address, int value) { var b = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(b, value); Bytes(address, b); }
        internal void Byte(ulong address, byte value) => bytes[address] = value;
        internal void CString(ulong address, string value) => Bytes(address, Encoding.UTF8.GetBytes(value + '\0'));
    }

    // Only fingerprinted bootstrap images + one generic List/array + three field descriptors, not an MTGA mock.
    private sealed class MonoFixture
    {
        internal FixtureMemory Memory { get; } = new();
        internal ArenaMonoRuntime Runtime { get; }
        internal ulong MonoBase = 0x1000000, List = 0x500000, Array = 0x510000, ListClass = 0x400000, SizeField = 0x430020;
        internal ulong[] GuidAddresses = new ulong[ArenaMemoryCompatibility.Supported.Assemblies.Count];
        internal MonoFixture(bool initialize = true)
        {
            Memory.U64(MonoBase + MonoLayout.RootDomainRva, 0x200000);
            Memory.I32(0x200000 + MonoLayout.DomainId, 0);
            Memory.U64(MonoBase + MonoLayout.LoadedAssembliesRva, 0x300000);
            ulong frameworkImage = 0;
            for (var i = 0; i < ArenaMemoryCompatibility.Supported.Assemblies.Count; i++)
            {
                var assembly = ArenaMemoryCompatibility.Supported.Assemblies[i];
                var node = 0x300000ul + (ulong)i * 0x100;
                var a = node + 0x1000; var image = node + 0x2000;
                Memory.U64(node, a); Memory.U64(node + 8, i < ArenaMemoryCompatibility.Supported.Assemblies.Count - 1 ? node + 0x100 : 0);
                Memory.U64(a + MonoLayout.AssemblyImage, image);
                Memory.U64(image + MonoLayout.ImageName, node + 0x3000);
                Memory.CString(node + 0x3000, assembly.Name);
                GuidAddresses[i] = node + 0x4000;
                Memory.U64(image + MonoLayout.ImageGuid, GuidAddresses[i]); Memory.CString(GuidAddresses[i], assembly.Mvid.ToString());
                if (assembly.Name == "mscorlib") frameworkImage = image;
            }
            Memory.U64(List, 0x410000); Memory.U64(0x410000, ListClass);
            Memory.Byte(ListClass + MonoLayout.ClassKind, 3); Memory.I32(ListClass + MonoLayout.ClassSize, 40);
            Memory.I32(ListClass + MonoLayout.ClassMetadataFlags, 0x80000);
            Memory.U64(ListClass + MonoLayout.ClassName, 0x450001); Memory.CString(0x450001, "List`1");
            Memory.U64(ListClass + MonoLayout.ClassNamespace, 0x450101); Memory.CString(0x450101, "System.Collections.Generic");
            Memory.U64(ListClass + MonoLayout.ClassImage, frameworkImage);
            Memory.U64(ListClass + MonoLayout.ClassGenericClass, 0x420000); Memory.U64(0x420000, 0x421000);
            Memory.I32(0x421000 + MonoLayout.ClassFieldCount, 3);
            Memory.U64(ListClass + MonoLayout.ClassFields, 0x430000);
            var names = new[] { "_items", "_size", "_version" }; var offsets = new[] { 16, 24, 28 };
            for (var i = 0; i < 3; i++)
            {
                var field = 0x430000ul + (ulong)i * MonoLayout.FieldStride;
                Memory.U64(field, 0x460000 + (ulong)i * 0x100);
                Memory.U64(field + MonoLayout.FieldName, 0x470001 + (ulong)i * 0x100);
                Memory.CString(0x470001 + (ulong)i * 0x100, names[i]);
                Memory.U64(field + MonoLayout.FieldParent, ListClass); Memory.I32(field + MonoLayout.FieldOffset, offsets[i]);
            }
            Memory.U64(List + 16, Array); Memory.I32(List + 24, 2); Memory.I32(List + 28, 9);
            Memory.U64(Array, 0x520000); Memory.U64(0x520000, 0x530000);
            Memory.Byte(0x530000 + MonoLayout.ClassKind, 5); Memory.Byte(0x530000 + MonoLayout.ClassRank, 1);
            Memory.U64(Array + MonoLayout.ArrayLength, 2);
            Memory.U64(Array + MonoLayout.ArrayData, 0x700000); Memory.U64(Array + MonoLayout.ArrayData + 8, 0x700100);
            Runtime = new(Memory, MonoBase);
            if (initialize) Runtime.Initialize(ArenaMemoryCompatibility.Supported);
        }
    }
}
