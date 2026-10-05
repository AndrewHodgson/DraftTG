using System.Text.Json;
using DraftTG.Domain;

namespace DraftTG.Data;

/// <summary>Curated descriptions only. Add an embedded profile file, never card rating conditionals.</summary>
public sealed class SetArchetypeProfileCatalog : ISetArchetypeProfileCatalog
{
    private readonly IReadOnlyDictionary<string, SetArchetypeProfile> _profiles;
    public SetArchetypeProfileCatalog(IEnumerable<SetArchetypeProfile> profiles) =>
        _profiles = profiles.ToDictionary(p => p.SetCode, StringComparer.Ordinal);
    public static SetArchetypeProfileCatalog Default { get; } = LoadEmbedded();
    public SetArchetypeProfile? Find(string setCode) => _profiles.GetValueOrDefault(setCode);
    public static SetArchetypeProfile Parse(string json)
    {
        var document = JsonSerializer.Deserialize<ProfileDocument>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow })
            ?? throw new JsonException("Empty archetype profile.");
        if (document.Archetypes is null || document.Archetypes.Any(a => a is null) || document.Set is null || document.Source is null)
            throw new JsonException("Incomplete archetype profile.");
        return new(document.Set, document.Source, document.Archetypes.Select(a => new ArchetypeDefinition(document.Set,
            ArchetypeColorPair.Create(a.Colors), a.Name, a.Description, a.Story)));
    }
    private static SetArchetypeProfileCatalog LoadEmbedded()
    {
        var assembly = typeof(SetArchetypeProfileCatalog).Assembly;
        return new(assembly.GetManifestResourceNames().Where(n => n.Contains(".Archetypes.", StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal).Select(n =>
            {
                using var reader = new StreamReader(assembly.GetManifestResourceStream(n)!);
                return Parse(reader.ReadToEnd());
            }));
    }
    private sealed record ProfileDocument(string Set, string Source, ArchetypeDocument[] Archetypes);
    private sealed record ArchetypeDocument(string Colors, string Name, string Description, string? Story);
}
