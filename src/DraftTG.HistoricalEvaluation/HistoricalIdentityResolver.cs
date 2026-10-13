using System.Text;
using System.Text.RegularExpressions;
using DraftTG.Domain;

namespace DraftTG.HistoricalEvaluation;

/// <summary>Printing resolution is intentionally separate from provider statistical-name resolution.</summary>
public sealed class HistoricalIdentityResolver
{
    private readonly CardCatalog _catalog;
    private readonly Dictionary<string, Card[]> _full;
    private readonly Dictionary<string, Card[]> _front;
    public HistoricalIdentityResolver(CardCatalog catalog)
    {
        _catalog = catalog;
        _full = catalog.Cards.GroupBy(c => Normalize(c.Name)).ToDictionary(g => g.Key, g => g.OrderBy(c => c.Identifier.Value, StringComparer.Ordinal).ToArray());
        _front = catalog.Cards.Where(c => c.GameplayMetadata.Faces.Count > 1)
            .GroupBy(c => Normalize(c.GameplayMetadata.Faces[0].Name ?? c.Name.Split("//")[0]))
            .ToDictionary(g => g.Key, g => g.OrderBy(c => c.Identifier.Value, StringComparer.Ordinal).ToArray());
    }
    public static string Normalize(string name) => Regex.Replace(name.Normalize(NormalizationForm.FormKC)
        .Replace('’', '\'').Trim(), @"\s*//\s*", " // ").ToUpperInvariant();
    public HistoricalCardIdentity Resolve(string source, string set, IEnumerable<string>? bonusSets = null)
    {
        var allowed = new HashSet<string>((bonusSets ?? []).Append(set), StringComparer.OrdinalIgnoreCase);
        var explicitCard = _catalog.Find(CardIdentifier.Create(source));
        if (explicitCard is not null)
            return allowed.Contains(explicitCard.SetCode.Value)
                ? Resolved(source, explicitCard, IdentityStatus.ExplicitIdentifier, "Explicit canonical ID in declared set/sheet scope.")
                : Missing(source, "Explicit ID belongs to an undeclared printing set.");
        var key = Normalize(source);
        var exact = _full.GetValueOrDefault(key, []).Where(c => c.SetCode.Value.Equals(set, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (exact.Length > 0) return Choose(source, exact, IdentityStatus.ExactSet, "Exact set + normalized full name.");
        var front = _front.GetValueOrDefault(key, []).Where(c => c.SetCode.Value.Equals(set, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (front.Length > 0) return Choose(source, front, IdentityStatus.FrontFace, "Exact set + declared multiface front name.");
        var sheets = _full.GetValueOrDefault(key, []).Concat(_front.GetValueOrDefault(key, []))
            .Where(c => allowed.Contains(c.SetCode.Value)).DistinctBy(c => c.Identifier).OrderBy(c => c.Identifier.Value, StringComparer.Ordinal).ToArray();
        return sheets.Length > 0 ? Choose(source, sheets, IdentityStatus.ExplicitBonusSheet, "Only explicitly declared bonus-sheet sets searched.")
            : Missing(source, "No exact printing in declared set/sheet scope; cross-set reprint mapping prohibited.");
    }
    private static HistoricalCardIdentity Choose(string source, Card[] cards, IdentityStatus kind, string rule) => cards.Length == 1
        ? Resolved(source, cards[0], kind, rule)
        : new(source, null, null, IdentityStatus.Ambiguous, rule + " Multiple printings remain ambiguous.", cards.Select(c => c.Identifier.Value).ToArray());
    private static HistoricalCardIdentity Resolved(string source, Card card, IdentityStatus kind, string rule)
        => new(source, card.Identifier.Value, card.SetCode.Value, kind, rule, [card.Identifier.Value]);
    private static HistoricalCardIdentity Missing(string source, string rule) => new(source, null, null, IdentityStatus.Unresolved, rule, []);
}
