using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace DraftTG.LocalizationAudit;

internal sealed record ModuleInfo(string Name, string Path, string BaseAddress, int Bytes, string? Version);
internal sealed record FieldInfo(string Name, string Type, bool Static);
internal sealed record TypeInfo(string Name, string BaseType, IReadOnlyList<FieldInfo> Fields, IReadOnlyList<string> Properties, IReadOnlyList<string> GeometryMethods);
internal sealed record AssemblyInfo(string Name, string Sha256, string Mvid, IReadOnlyList<TypeInfo> Types, bool Truncated);

internal static class RuntimeAudit
{
    private static readonly Regex Interesting = new("Card(View|Viewer|Widget|Display|Tile|Visual|Model|Data|PrintingData|PrintingRecord|Collection)|MetaCardHolder|Draft.*(View|Controller|Widget|Display|Panel)|Deck.*(View|Controller|Panel)|Pool.*(View|Display)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    public static object Read(Process process)
    {
        var modules = process.Modules.Cast<ProcessModule>().Select(m => new ModuleInfo(m.ModuleName, m.FileName,
            WindowAudit.Hex(m.BaseAddress), m.ModuleMemorySize, m.FileVersionInfo.FileVersion)).ToArray();
        var root = Path.GetDirectoryName(process.MainModule!.FileName)!;
        var managed = Path.Combine(root, "MTGA_Data", "Managed");
        var files = new[] { "Core.dll", "Assembly-CSharp.dll", "SharedClientCore.dll", "UnityEngine.CoreModule.dll" };
        return new { Runtime = modules.Any(m => m.Name.StartsWith("mono-", StringComparison.OrdinalIgnoreCase)) ? "Mono (loaded native module)"
            : modules.Any(m => m.Name == "GameAssembly.dll") ? "IL2CPP candidate (requires metadata confirmation)" : "Unknown",
            GameVersion = FileVersionInfo.GetVersionInfo(process.MainModule.FileName).FileVersion,
            UnityVersion = modules.FirstOrDefault(m => m.Name == "UnityPlayer.dll")?.Version,
            Modules = modules, ManagedAssemblies = files.Where(f => File.Exists(Path.Combine(managed,f)))
                .Select(f => Metadata(Path.Combine(managed,f))).ToArray(),
            MemoryLayoutWarning = "Metadata field signatures are not live instance offsets. No game assembly is loaded or executed by this tool." };
    }
    private static AssemblyInfo Metadata(string path)
    {
        using var stream = File.OpenRead(path); using var pe = new PEReader(stream); var reader = pe.GetMetadataReader();
        var provider = new TypeNames(); var types = new List<TypeInfo>(); var truncated = false;
        foreach (var handle in reader.TypeDefinitions)
        {
            var def = reader.GetTypeDefinition(handle); var name = provider.GetTypeFromDefinition(reader, handle, 0);
            if (name.Contains('<')) continue; // Skip generated closures/state machines; retain authored structural metadata.
            if (!Interesting.IsMatch(name) && name is not ("UnityEngine.Object" or "UnityEngine.Transform" or "UnityEngine.RectTransform" or "UnityEngine.Camera")) continue;
            if (types.Count >= 800) { truncated = true; break; }
            var fields = def.GetFields().Select(h => reader.GetFieldDefinition(h)).Select(f => new FieldInfo(reader.GetString(f.Name),
                f.DecodeSignature(provider, (object?)null), (f.Attributes & System.Reflection.FieldAttributes.Static) != 0)).ToArray();
            var properties = def.GetProperties().Select(h => reader.GetString(reader.GetPropertyDefinition(h).Name)).ToArray();
            var methods = def.GetMethods().Select(h => reader.GetString(reader.GetMethodDefinition(h).Name))
                .Where(m => m.Contains("Rect",StringComparison.OrdinalIgnoreCase) || m.Contains("Corner",StringComparison.OrdinalIgnoreCase)
                    || m.Contains("Screen",StringComparison.OrdinalIgnoreCase) || m.Contains("Position",StringComparison.OrdinalIgnoreCase)).Distinct().ToArray();
            types.Add(new(name, provider.Entity(reader, def.BaseType), fields, properties, methods));
        }
        stream.Position=0;
        return new(Path.GetFileName(path), Convert.ToHexString(SHA256.HashData(stream)), reader.GetGuid(reader.GetModuleDefinition().Mvid).ToString(), types, truncated);
    }
    private sealed class TypeNames : ISignatureTypeProvider<string, object?>
    {
        public string Entity(MetadataReader r, EntityHandle h) => h.IsNil ? "" : h.Kind switch
        { HandleKind.TypeDefinition => GetTypeFromDefinition(r,(TypeDefinitionHandle)h,0), HandleKind.TypeReference => GetTypeFromReference(r,(TypeReferenceHandle)h,0),
            HandleKind.TypeSpecification => GetTypeFromSpecification(r,null,(TypeSpecificationHandle)h,0), _ => "" };
        public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte k) { var t=r.GetTypeDefinition(h); return Join(r.GetString(t.Namespace),r.GetString(t.Name)); }
        public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte k) { var t=r.GetTypeReference(h); return Join(r.GetString(t.Namespace),r.GetString(t.Name)); }
        public string GetTypeFromSpecification(MetadataReader r, object? c, TypeSpecificationHandle h, byte k) => r.GetTypeSpecification(h).DecodeSignature(this,c);
        private static string Join(string ns,string n) => ns.Length==0 ? n : ns+"."+n;
        public string GetArrayType(string t,ArrayShape s) => t+"["+new string(',',s.Rank-1)+"]";
        public string GetByReferenceType(string t)=>t+"&";
        public string GetFunctionPointerType(MethodSignature<string> s)=>"function pointer";
        public string GetGenericInstantiation(string t,ImmutableArray<string> a)=>t+"<"+string.Join(",",a)+">";
        public string GetGenericMethodParameter(object? c,int i)=>"!!"+i;
        public string GetGenericTypeParameter(object? c,int i)=>"!"+i;
        public string GetModifiedType(string m,string t,bool required)=>t;
        public string GetPinnedType(string t)=>t;
        public string GetPointerType(string t)=>t+"*";
        public string GetPrimitiveType(PrimitiveTypeCode c)=>c.ToString();
        public string GetSZArrayType(string t)=>t+"[]";
    }
}
