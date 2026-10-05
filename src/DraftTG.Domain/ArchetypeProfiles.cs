using System.Text.RegularExpressions;

namespace DraftTG.Domain;

/// <summary>Two distinct colors, normalized in WUBRG order. Display guild order is independent.</summary>
public sealed record ArchetypeColorPair
{
    private ArchetypeColorPair(ColorSet colors) => Colors = colors;
    public ColorSet Colors { get; }
    public string Code => string.Concat(Colors.Colors.Select(c => "WUBRG"[(int)c]));
    public string DisplayCode => Code switch { "WG" => "GW", "WR" => "RW", "UG" => "GU", _ => Code };
    public static ArchetypeColorPair Create(string symbols)
    {
        if (symbols is null || symbols.Length != 2 || symbols[0] == symbols[1]
            || symbols.Any(c => !"WUBRG".Contains(c)))
            throw new ArgumentException("A pair must contain exactly two distinct WUBRG colors.", nameof(symbols));
        return new(new ColorSet(symbols.Select(c => (MagicColor)"WUBRG".IndexOf(c))));
    }
    public bool Includes(ColorSet colors) => colors.Count is 1 or 2 && colors.Colors.All(Colors.Contains);
    public override string ToString() => Code;
}

public sealed record ArchetypeDefinition
{
    public ArchetypeDefinition(string setCode, ArchetypeColorPair pair, string name, string description, string? story = null)
    {
        if (!Regex.IsMatch(setCode ?? "", "^[A-Z0-9]{2,8}\\z", RegexOptions.CultureInvariant))
            throw new ArgumentException("Profile set code must be exact uppercase alphanumeric.", nameof(setCode));
        ArgumentNullException.ThrowIfNull(pair);
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(description)
            || (story is not null && string.IsNullOrWhiteSpace(story)))
            throw new ArgumentException("Archetype name and description, and any optional story, must be nonempty.");
        SetCode = setCode!; Pair = pair; Name = name; Description = description; Story = story;
    }
    public string SetCode { get; }
    public ArchetypeColorPair Pair { get; }
    public string Name { get; }
    public string Description { get; }
    public string? Story { get; }
}

public sealed class SetArchetypeProfile
{
    public SetArchetypeProfile(string setCode, string source, IEnumerable<ArchetypeDefinition> archetypes)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme != "https")
            throw new ArgumentException("A profile requires an HTTPS source.", nameof(source));
        var copy = archetypes.ToArray();
        if (copy.Length == 0 || copy.Any(a => a.SetCode != setCode) || copy.Select(a => a.Pair).Distinct().Count() != copy.Length)
            throw new ArgumentException("Profile archetypes must have one exact set code and unique pairs.", nameof(archetypes));
        SetCode = setCode; Source = source; Archetypes = Array.AsReadOnly(copy.OrderBy(a => a.Pair.Code, StringComparer.Ordinal).ToArray());
    }
    public string SetCode { get; }
    public string Source { get; }
    public IReadOnlyList<ArchetypeDefinition> Archetypes { get; }
}

public interface ISetArchetypeProfileCatalog { SetArchetypeProfile? Find(string setCode); }
