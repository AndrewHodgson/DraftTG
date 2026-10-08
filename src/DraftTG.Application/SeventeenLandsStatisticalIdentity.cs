using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using DraftTG.Data;
using DraftTG.Domain;

namespace DraftTG.Application;

public enum StatisticalIdentityKind { ExactName, MultifaceFrontFace, NoProviderRow, ConflictingProviderRows, AmbiguousMultifaceAlias }

/// <summary>How one exact DraftTG printing was joined to a row of the already selected set/format 17Lands snapshot.</summary>
public sealed record StatisticalIdentityResolution(StatisticalIdentityKind Kind, string? ProviderName)
{
    public bool IsResolved => Kind is StatisticalIdentityKind.ExactName or StatisticalIdentityKind.MultifaceFrontFace;
    public string Description => Kind switch
    {
        StatisticalIdentityKind.ExactName => $"17Lands row \"{ProviderName}\" (exact card name).",
        StatisticalIdentityKind.MultifaceFrontFace => $"17Lands row \"{ProviderName}\" (front face of this multiface card; 17Lands names the drafted card by its front face).",
        StatisticalIdentityKind.ConflictingProviderRows => $"17Lands has conflicting rows named \"{ProviderName}\"; none attached.",
        StatisticalIdentityKind.AmbiguousMultifaceAlias => $"Front-face name \"{ProviderName}\" could denote more than one card; none attached.",
        _ => "No 17Lands row for this card in the selected set/format."
    };
}

/// <summary>
/// Statistical identity is separate from printing identity. A DraftTG <see cref="CardIdentifier"/> stays the exact
/// Arena/Scryfall printing; this only decides which provider row, if any, describes that card. Precedence:
/// (A) the exact canonical name; (B) only when no row has that name, the front-face name of a structured multiface
/// card whose layout makes the front face the drafted object (Adventure, Transform, modal DFC, Prepare). The alias is
/// rejected when any catalog card is canonically named it or when two different cards share that front face.
/// Back faces, split halves, case/punctuation changes and partial matches are never used.
/// </summary>
public static class SeventeenLandsStatisticalIdentity
{
    private static readonly ConditionalWeakTable<CardCatalog, FrozenDictionary<string, string[]>> FrontFaceOwners = new();

    /// <summary>The structured front-face name 17Lands uses for this card, if its layout permits one.</summary>
    public static string? FrontFaceAlias(Card card)
    {
        var metadata = card.GameplayMetadata;
        if (metadata.Layout is not (CardLayout.Adventure or CardLayout.Transform or CardLayout.ModalDoubleFaced or CardLayout.Prepare)
            || metadata.Faces.Count < 2 || metadata.Faces.Any(face => string.IsNullOrEmpty(face.Name))) return null;
        // The canonical name must be exactly the face names joined by Scryfall's separator: structure, not text guessing.
        if (!string.Equals(card.Name, string.Join(" // ", metadata.Faces.Select(face => face.Name)), StringComparison.Ordinal)) return null;
        return metadata.Faces[0].Name;
    }

    /// <summary>True when a provider row name structurally denotes this card (used as a presentation integrity check).</summary>
    public static bool Denotes(Card card, string providerName) =>
        string.Equals(card.Name, providerName, StringComparison.Ordinal)
        || string.Equals(FrontFaceAlias(card), providerName, StringComparison.Ordinal);

    internal static Resolver For(IReadOnlyDictionary<string, SeventeenLandsRating?> rowsByName, CardCatalog catalog) => new(rowsByName, catalog);

    internal sealed class Resolver(IReadOnlyDictionary<string, SeventeenLandsRating?> rowsByName, CardCatalog catalog)
    {
        // Built once per immutable catalog: front-face alias -> distinct canonical names of eligible multiface cards.
        private readonly FrozenDictionary<string, string[]> _owners = FrontFaceOwners.GetValue(catalog, static c => c.Cards
            .Select(card => (Alias: FrontFaceAlias(card), card.Name)).Where(p => p.Alias is not null)
            .GroupBy(p => p.Alias!, StringComparer.Ordinal)
            .ToFrozenDictionary(g => g.Key, g => g.Select(p => p.Name).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal));

        public (StatisticalIdentityResolution Resolution, SeventeenLandsRating? Row) Resolve(Card card)
        {
            // (A) Exact canonical name: unchanged behaviour, and it is never overridden by an alias.
            if (rowsByName.TryGetValue(card.Name, out var exact))
                return exact is null ? (new(StatisticalIdentityKind.ConflictingProviderRows, card.Name), null)
                    : (new(StatisticalIdentityKind.ExactName, card.Name), exact);
            // (B) Structured front-face alias, only when the exact name has no row.
            if (FrontFaceAlias(card) is not { } alias || !rowsByName.TryGetValue(alias, out var row))
                return (new(StatisticalIdentityKind.NoProviderRow, null), null);
            if (row is null) return (new(StatisticalIdentityKind.ConflictingProviderRows, alias), null);
            if (!AliasDenotesOnly(alias, card.Name)) return (new(StatisticalIdentityKind.AmbiguousMultifaceAlias, alias), null);
            return (new(StatisticalIdentityKind.MultifaceFrontFace, alias), row);
        }

        /// <summary>Printings a provider row name denotes: exact canonical matches, else the single card it is a safe alias of.</summary>
        public IReadOnlyList<Card> CardsFor(string providerName)
        {
            var exact = catalog.FindByExactName(providerName);
            if (exact.Count > 0) return exact;
            return _owners.TryGetValue(providerName, out var owners) && owners.Length == 1 && AliasDenotesOnly(providerName, owners[0])
                ? catalog.FindByExactName(owners[0]).Where(card => FrontFaceAlias(card) == providerName).ToArray() : [];
        }

        // No card is canonically named the alias, and every multiface card with this front face is the same card.
        private bool AliasDenotesOnly(string alias, string canonicalName) =>
            catalog.FindByExactName(alias).Count == 0
            && _owners.TryGetValue(alias, out var owners) && owners.Length == 1 && string.Equals(owners[0], canonicalName, StringComparison.Ordinal);
    }
}
