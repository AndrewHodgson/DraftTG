using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

// Static read-only inspection. Target assemblies are never loaded into the CLR or executed.
if (args.Length == 1 && args[0] == "self-test") { ReferenceTests.Run(); return; }
if (args.Length >= 2 && args[0] == "reference") { DraftSortReference.Replay(args[1]); return; }
if (args.Length < 2) throw new ArgumentException("inventory <managed directory> | types/dump/calls/resources <assembly> [regex] | field-data <assembly> <hex field token> <byte count> | reference <json> | self-test");
if (args[0] == "inventory")
{
    var entries = Directory.EnumerateFiles(args[1], "*.dll").Order().Select(path =>
    {
        using var stream = File.OpenRead(path); using var pe = new PEReader(stream);
        if (!pe.HasMetadata) return null;
        var reader = pe.GetMetadataReader(); var assembly = reader.GetAssemblyDefinition();
        var version = FileVersionInfo.GetVersionInfo(path);
        stream.Position = 0;
        return new { Path = Path.GetFullPath(path), Bytes = stream.Length, SHA256 = Convert.ToHexString(SHA256.HashData(stream)),
            Assembly = reader.GetString(assembly.Name), AssemblyVersion = assembly.Version.ToString(), version.FileVersion, version.ProductVersion,
            Mvid = reader.GetGuid(reader.GetModuleDefinition().Mvid), References = reader.AssemblyReferences.Select(h => reader.GetString(reader.GetAssemblyReference(h).Name)).ToArray() };
    }).Where(e => e is not null).ToArray();
    Console.WriteLine(JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
    return;
}
using var input = File.OpenRead(args[1]);
using var image = new PEReader(input);
var metadata = image.GetMetadataReader();
var names = new Names(metadata);
if (args[0] == "field-data")
{
    var handle = (FieldDefinitionHandle)MetadataTokens.Handle(Convert.ToInt32(args[2], 16));
    var field = metadata.GetFieldDefinition(handle);
    var rva = field.GetRelativeVirtualAddress();
    if (rva == 0) throw new ArgumentException("Field has no embedded RVA data.");
    var bytes = image.GetSectionData(rva).GetContent(0, int.Parse(args[3])).ToArray();
    Console.WriteLine($"{names.Entity(handle)} RVA=0x{rva:X} bytes={Convert.ToHexString(bytes)}");
    Console.WriteLine("Int32 little-endian: " + string.Join(",", Enumerable.Range(0, bytes.Length / 4).Select(i => BitConverter.ToInt32(bytes, i * 4))));
    return;
}
var pattern = new Regex(args.Length > 2 ? args[2] : ".*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
if (args[0] == "resources")
{
    foreach (var handle in metadata.ManifestResources)
    {
        var resource = metadata.GetManifestResource(handle);
        Console.WriteLine($"{metadata.GetString(resource.Name)} offset={resource.Offset} {resource.Attributes} implementation={resource.Implementation.Kind}");
    }
    return;
}
foreach (var typeHandle in metadata.TypeDefinitions)
{
    var type = metadata.GetTypeDefinition(typeHandle); var typeName = names.Type(typeHandle);
    if (args[0] == "types")
    {
        if (pattern.IsMatch(typeName)) Console.WriteLine($"0x{MetadataTokens.GetToken(typeHandle):X8} {typeName} : {names.Entity(type.BaseType)}");
        continue;
    }
    var selectedType = pattern.IsMatch(typeName);
    if (args[0] == "dump" && selectedType)
    {
        Console.WriteLine($"TYPE 0x{MetadataTokens.GetToken(typeHandle):X8} {typeName} {type.Attributes} : {names.Entity(type.BaseType)}");
        foreach (var fieldHandle in type.GetFields())
        {
            var field = metadata.GetFieldDefinition(fieldHandle);
            var constant = field.GetDefaultValue();
            Console.WriteLine($" FIELD 0x{MetadataTokens.GetToken(fieldHandle):X8} {field.Attributes} {field.DecodeSignature(names, null)} {metadata.GetString(field.Name)}"
                + (constant.IsNil ? "" : " = " + names.Constant(constant))
                + " attributes=" + string.Join(",", field.GetCustomAttributes().Select(h => names.Entity(metadata.GetCustomAttribute(h).Constructor))));
        }
    }
    foreach (var methodHandle in type.GetMethods())
    {
        var method = metadata.GetMethodDefinition(methodHandle); var methodName = names.Method(methodHandle);
        if (args[0] == "dump" && !selectedType && !pattern.IsMatch(methodName)) continue;
        if (args[0] == "dump")
        {
            var signature = method.DecodeSignature(names, null);
            Console.WriteLine($"METHOD 0x{MetadataTokens.GetToken(methodHandle):X8} {methodName} {method.Attributes} -> {signature.ReturnType} ({string.Join(", ", signature.ParameterTypes)}) RVA=0x{method.RelativeVirtualAddress:X}");
        }
        if (method.RelativeVirtualAddress == 0) continue;
        var body = image.GetMethodBody(method.RelativeVirtualAddress);
        if (args[0] == "dump") Console.WriteLine($" maxstack={body.MaxStack} initlocals={body.LocalVariablesInitialized} locals={names.Entity(body.LocalSignature)}");
        foreach (var instruction in Il.Decode(body.GetILBytes()!, names.Token))
        {
            var line = $"IL_{instruction.Offset:X4}: {instruction.OpCode,-12} {instruction.Operand}";
            if (args[0] == "dump") Console.WriteLine(" " + line);
            else if (args[0] == "calls" && pattern.IsMatch(instruction.Operand)) Console.WriteLine($"{methodName} {line}");
        }
        if (args[0] == "dump") foreach (var region in body.ExceptionRegions)
            Console.WriteLine($" EH {region.Kind} try=IL_{region.TryOffset:X4}+{region.TryLength} handler=IL_{region.HandlerOffset:X4}+{region.HandlerLength} catch={names.Entity(region.CatchType)}");
    }
}

internal sealed record Instruction(int Offset, string OpCode, string Operand);
internal static class Il
{
    private static readonly Dictionary<short, OpCode> Codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)!).ToDictionary(c => c.Value);
    public static IEnumerable<Instruction> Decode(byte[] bytes, Func<int, string> token)
    {
        var offset = 0;
        while (offset < bytes.Length)
        {
            var start = offset; short value = bytes[offset++];
            if (value == 0xFE) value = unchecked((short)(0xFE00 | bytes[offset++]));
            if (!Codes.TryGetValue(value, out var opcode)) throw new BadImageFormatException($"Unknown opcode at {start:X4}");
            string operand;
            switch (opcode.OperandType)
            {
                case OperandType.InlineNone: operand = ""; break;
                case OperandType.ShortInlineI: operand = ((sbyte)bytes[offset++]).ToString(); break;
                case OperandType.ShortInlineVar: operand = bytes[offset++].ToString(); break;
                case OperandType.InlineVar: operand = BitConverter.ToUInt16(bytes, offset).ToString(); offset += 2; break;
                case OperandType.InlineI: operand = BitConverter.ToInt32(bytes, offset).ToString(); offset += 4; break;
                case OperandType.InlineI8: operand = BitConverter.ToInt64(bytes, offset).ToString(); offset += 8; break;
                case OperandType.ShortInlineR: operand = BitConverter.ToSingle(bytes, offset).ToString(System.Globalization.CultureInfo.InvariantCulture); offset += 4; break;
                case OperandType.InlineR: operand = BitConverter.ToDouble(bytes, offset).ToString(System.Globalization.CultureInfo.InvariantCulture); offset += 8; break;
                case OperandType.ShortInlineBrTarget:
                    var shortDelta = (sbyte)bytes[offset++]; operand = $"IL_{offset + shortDelta:X4}"; break;
                case OperandType.InlineBrTarget:
                    var delta = BitConverter.ToInt32(bytes, offset); offset += 4; operand = $"IL_{offset + delta:X4}"; break;
                case OperandType.InlineSwitch:
                    var count = BitConverter.ToInt32(bytes, offset); offset += 4;
                    var end = checked(offset + count * 4);
                    operand = string.Join(",", Enumerable.Range(0, count).Select(i => $"IL_{end + BitConverter.ToInt32(bytes, offset + i * 4):X4}")); offset = end; break;
                default: operand = token(BitConverter.ToInt32(bytes, offset)); offset += 4; break;
            }
            yield return new(start, opcode.Name!, operand);
        }
    }
}

internal sealed class Names(MetadataReader reader) : ISignatureTypeProvider<string, object?>
{
    public string Token(int value)
    {
        if ((value & unchecked((int)0xFF000000)) == 0x70000000) return JsonSerializer.Serialize(reader.GetUserString(MetadataTokens.UserStringHandle(value & 0xFFFFFF)));
        return $"0x{value:X8} " + Entity(MetadataTokens.EntityHandle(value));
    }
    public string Entity(EntityHandle handle) => handle.IsNil ? "nil" : handle.Kind switch
    {
        HandleKind.TypeDefinition => Type((TypeDefinitionHandle)handle),
        HandleKind.TypeReference => Reference((TypeReferenceHandle)handle),
        HandleKind.TypeSpecification => reader.GetTypeSpecification((TypeSpecificationHandle)handle).DecodeSignature(this, null),
        HandleKind.MethodDefinition => Method((MethodDefinitionHandle)handle),
        HandleKind.FieldDefinition => Field((FieldDefinitionHandle)handle),
        HandleKind.MemberReference => Member((MemberReferenceHandle)handle),
        HandleKind.MethodSpecification => Specification((MethodSpecificationHandle)handle),
        HandleKind.AssemblyReference => "[" + reader.GetString(reader.GetAssemblyReference((AssemblyReferenceHandle)handle).Name) + "]",
        HandleKind.StandaloneSignature => "signature:0x" + MetadataTokens.GetToken(handle).ToString("X8"),
        _ => handle.Kind.ToString()
    };
    public string Type(TypeDefinitionHandle handle)
    {
        var type = reader.GetTypeDefinition(handle); var parent = type.GetDeclaringType();
        return parent.IsNil ? Join(reader.GetString(type.Namespace), reader.GetString(type.Name)) : Type(parent) + "/" + reader.GetString(type.Name);
    }
    private string Reference(TypeReferenceHandle handle)
    {
        var type = reader.GetTypeReference(handle);
        return (type.ResolutionScope.Kind == HandleKind.TypeReference ? Entity((EntityHandle)type.ResolutionScope) + "/" : Entity((EntityHandle)type.ResolutionScope))
            + Join(reader.GetString(type.Namespace), reader.GetString(type.Name));
    }
    public string Method(MethodDefinitionHandle handle)
    { var method = reader.GetMethodDefinition(handle); return Type(method.GetDeclaringType()) + "::" + reader.GetString(method.Name); }
    private string Field(FieldDefinitionHandle handle)
    { var field = reader.GetFieldDefinition(handle); return Type(field.GetDeclaringType()) + "::" + reader.GetString(field.Name); }
    private string Member(MemberReferenceHandle handle)
    {
        var member = reader.GetMemberReference(handle);
        return Entity(member.Parent) + "::" + reader.GetString(member.Name);
    }
    private string Specification(MethodSpecificationHandle handle)
    { var method = reader.GetMethodSpecification(handle); return Entity(method.Method) + "<" + string.Join(",", method.DecodeSignature(this, null)) + ">"; }
    public string Constant(ConstantHandle handle)
    {
        var value = reader.GetConstant(handle); var blob = reader.GetBlobReader(value.Value);
        return value.TypeCode switch
        { ConstantTypeCode.Int32 => blob.ReadInt32().ToString(), ConstantTypeCode.UInt32 => blob.ReadUInt32().ToString(),
            ConstantTypeCode.Boolean => blob.ReadBoolean().ToString(), ConstantTypeCode.String => blob.ReadUTF16(blob.Length), _ => value.TypeCode.ToString() };
    }
    private static string Join(string ns, string name) => ns.Length == 0 ? name : ns + "." + name;
    public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[" + new string(',', shape.Rank - 1) + "]";
    public string GetByReferenceType(string elementType) => elementType + "&";
    public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr(" + string.Join(",", signature.ParameterTypes) + ")";
    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType + "<" + string.Join(",", typeArguments) + ">";
    public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;
    public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
    public string GetPinnedType(string elementType) => elementType + " pinned";
    public string GetPointerType(string elementType) => elementType + "*";
    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
    public string GetSZArrayType(string elementType) => elementType + "[]";
    public string GetTypeFromDefinition(MetadataReader metadataReader, TypeDefinitionHandle handle, byte rawTypeKind) => Type(handle);
    public string GetTypeFromReference(MetadataReader metadataReader, TypeReferenceHandle handle, byte rawTypeKind) => Reference(handle);
    public string GetTypeFromSpecification(MetadataReader metadataReader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) => reader.GetTypeSpecification(handle).DecodeSignature(this, null);
}
