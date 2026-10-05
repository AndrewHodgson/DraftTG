using System.Collections.Frozen;
using System.Globalization;
using System.Text.RegularExpressions;
using DraftTG.Domain;

namespace DraftTG.RecommendationEngine;

public sealed record ManaDemand(IReadOnlyDictionary<MagicColor, double> Colors, bool IsKnown, IReadOnlyList<string> UnrecognizedSymbols);

/// <summary>Colored-pip demand only. Primary face only; no double counting of combined displays and faces.</summary>
public static class DeckManaDemand
{
    public static ManaDemand For(Card card) => Parse(card.GameplayMetadata.Faces.FirstOrDefault()?.ManaCost
        ?? card.GameplayMetadata.ManaCost?.Split("//", StringSplitOptions.TrimEntries)[0]);

    public static ManaDemand Parse(string? cost)
    {
        var demand = Enum.GetValues<MagicColor>().ToDictionary(c => c, _ => 0d);
        var unknown = new List<string>();
        if (cost is null) return new(demand.ToFrozenDictionary(), false, Array.AsReadOnly(Array.Empty<string>()));
        var matches = Regex.Matches(cost, @"\{([^{}]+)\}");
        if (string.Concat(matches.Select(m => m.Value)) != cost) unknown.Add(cost);
        foreach (Match match in matches)
        {
            var parts = match.Groups[1].Value.Split('/');
            var colors = parts.Where(p => p.Length == 1 && "WUBRG".Contains(p, StringComparison.Ordinal))
                .Select(p => (MagicColor)"WUBRG".IndexOf(p, StringComparison.Ordinal)).Distinct().ToArray();
            if (parts.Any(p => !(p.Length == 1 && "WUBRGCPXYZS".Contains(p, StringComparison.Ordinal))
                && !int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out _))) unknown.Add(match.Value);
            foreach (var color in colors) demand[color] += 1d / colors.Length;
        }
        return new(demand.ToFrozenDictionary(), unknown.Count == 0, Array.AsReadOnly(unknown.ToArray()));
    }
}

public static class BaselineBasicLandAllocator
{
    public static IReadOnlyList<GeneratedBasicLandEntry> Allocate(ArchetypeColorPair pair,
        IReadOnlyDictionary<MagicColor, double> demand, int slots, int minimumPerUsedColor)
    {
        if (slots < 0 || minimumPerUsedColor < 0 || demand.Values.Any(v => !double.IsFinite(v) || v < 0))
            throw new ArgumentOutOfRangeException(nameof(slots));
        var used = pair.Colors.Colors.Where(c => demand.GetValueOrDefault(c) > 0).ToArray();
        // A generic-only deck still needs lands: use one canonical plan color without inventing pip demand.
        if (used.Length == 0) used = [pair.Colors.Colors[0]];
        var minimum = Math.Min(minimumPerUsedColor, slots / used.Length);
        var remaining = slots - minimum * used.Length;
        var total = used.Sum(c => demand.GetValueOrDefault(c));
        var quotas = used.ToDictionary(c => c, c => total > 0 ? remaining * demand.GetValueOrDefault(c) / total : (double)remaining / used.Length);
        var counts = used.ToDictionary(c => c, c => minimum + (int)Math.Floor(quotas[c]));
        foreach (var color in used.OrderByDescending(c => quotas[c] - Math.Floor(quotas[c])).ThenBy(c => c)
            .Take(slots - counts.Values.Sum())) counts[color]++;
        return Array.AsReadOnly(counts.Where(p => p.Value > 0).OrderBy(p => p.Key)
            .Select(p => new GeneratedBasicLandEntry((BasicLandType)(int)p.Key, p.Value)).ToArray());
    }
}
