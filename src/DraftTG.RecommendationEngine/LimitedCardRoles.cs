using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using DraftTG.Domain;

namespace DraftTG.RecommendationEngine;

[Flags]
public enum LimitedCardRole
{
    None = 0, Creature = 1, EarlyPlay = 2, Land = 4, DirectManaSource = 8,
    HardRemoval = 16, ConditionalRemoval = 32, CombatTrick = 64, CardAdvantage = 128,
    Ramp = 256, SynergyEnabler = 512, SynergyPayoff = 1024, Finisher = 2048,
    Defensive = 4096, Aggressive = 8192, Fixing = 16384
}

/// <summary>
/// Roles by evidence source. Reliable roles come from structured metadata; rule roles from a small set of
/// high-confidence Oracle patterns; curated roles from explicit profiles/overrides. Removed roles are explicit
/// override removals and always win. Only <see cref="Roles"/> is consumed by scoring.
/// </summary>
public sealed record LimitedCardRoleProfile(LimitedCardRole ReliableRoles, LimitedCardRole CuratedRoles,
    bool HasStructuredMetadata, string Source)
{
    public LimitedCardRole RuleRoles { get; init; }
    public LimitedCardRole RemovedRoles { get; init; }
    public LimitedCardRole Roles => (ReliableRoles | RuleRoles | CuratedRoles) & ~RemovedRoles;
    /// <summary>Hard removal counts 1, conditional removal .5, otherwise 0 (precision-first interaction count).</summary>
    public double RemovalWeight => (Roles & LimitedCardRole.HardRemoval) != 0 ? 1 : (Roles & LimitedCardRole.ConditionalRemoval) != 0 ? .5 : 0;
    public string SourceOf(LimitedCardRole role) => (RemovedRoles & role) != 0 ? "removed by override"
        : (CuratedRoles & role) != 0 ? "explicit override" : (RuleRoles & role) != 0 ? "high-confidence Oracle rule"
        : (ReliableRoles & role) != 0 ? "structured" : "not assigned";
}

/// <summary>Optional exact-set/card profiles that add curated roles; no Oracle keyword heuristics or shipped manual database.</summary>
public interface ILimitedCardRoleProfiles
{
    LimitedCardRole? Find(CardSetCode set, CardIdentifier card);
}

/// <summary>Explicit per-printing role corrections. Lives in data/configuration, never in generic engine code.</summary>
public sealed record LimitedCardRoleOverride(LimitedCardRole Add, LimitedCardRole Remove);
public interface ILimitedCardRoleOverrides
{
    LimitedCardRoleOverride? Find(Card card);
}
/// <summary>Overrides keyed by exact CardIdentifier, or by set + collector number for a printing.</summary>
public sealed class LimitedCardRoleOverrideTable(IReadOnlyDictionary<string, LimitedCardRoleOverride> overrides) : ILimitedCardRoleOverrides
{
    public static string PrintingKey(string set, string collectorNumber) => $"{set.ToUpperInvariant()}#{collectorNumber}";
    public LimitedCardRoleOverride? Find(Card card) => overrides.GetValueOrDefault(card.Identifier.Value)
        ?? overrides.GetValueOrDefault(PrintingKey(card.SetCode.Value, card.CollectorNumber.Value));
}

public sealed partial class LimitedCardRoleClassifier(ILimitedCardRoleProfiles? profiles = null, ILimitedCardRoleOverrides? overrides = null)
{
    // Card-intrinsic roles are parsed once per immutable Card instance, not per candidate per pick.
    private static readonly ConditionalWeakTable<Card, Tuple<LimitedCardRole, LimitedCardRole>> Intrinsic = new();

    public LimitedCardRoleProfile Classify(Card? card)
    {
        if (card is null) return new(LimitedCardRole.None, LimitedCardRole.None, false, "Unknown card metadata.");
        var (reliable, rules) = Intrinsic.GetValue(card, static c => Tuple.Create(StructuredRoles(c), OracleRoles(c))).ToValueTuple();
        var curated = profiles?.Find(card.SetCode, card.Identifier) ?? LimitedCardRole.None;
        var correction = overrides?.Find(card);
        curated |= correction?.Add ?? LimitedCardRole.None;
        return new(reliable, curated, card.GameplayMetadata.CardTypes.IsKnown,
            curated == LimitedCardRole.None && correction is null ? "Structured metadata plus high-confidence Oracle rules."
                : "Structured metadata, high-confidence Oracle rules and explicit overrides.")
        { RuleRoles = rules, RemovedRoles = correction?.Remove ?? LimitedCardRole.None };
    }

    private static LimitedCardRole StructuredRoles(Card card)
    {
        var metadata = card.GameplayMetadata;
        var roles = LimitedCardRole.None;
        if (metadata.IsCreature) roles |= LimitedCardRole.Creature;
        if (metadata.IsLand) roles |= LimitedCardRole.Land;
        if (metadata.IsNonlandSpell && metadata.ManaValue is <= 2) roles |= LimitedCardRole.EarlyPlay;
        var produced = metadata.Faces.Count == 0 ? metadata.ProducedMana : null;
        if (produced?.Count > 0) roles |= LimitedCardRole.DirectManaSource;
        // Fixing: a land whose structured produced_mana has two or more colours. Nonland producers are not classified:
        // the FRA audit found conditional/restricted mana (e.g. planeswalker-only) and high-cost producers, so Ramp and
        // nonland fixing stay deferred rather than guessed.
        if (metadata.IsLand && produced?.Where(kind => kind != ManaKind.Colorless).Distinct().Count() >= 2) roles |= LimitedCardRole.Fixing;
        return roles;
    }

    // ---- High-confidence removal rules (precision first; uncertain wording stays unclassified) ----
    // Faces a player can actually cast: every face of Adventure/Prepare/modal/split cards, the front only otherwise.
    private static IEnumerable<string> CastableText(Card card)
    {
        var metadata = card.GameplayMetadata;
        if (metadata.Faces.Count == 0) return [metadata.OracleText ?? ""];
        return metadata.Layout is CardLayout.Adventure or CardLayout.Prepare or CardLayout.ModalDoubleFaced or CardLayout.Split
            ? metadata.Faces.Select(face => face.OracleText ?? "") : [metadata.Faces[0].OracleText ?? metadata.OracleText ?? ""];
    }

    internal static LimitedCardRole OracleRoles(Card card)
    {
        var hard = false; var conditional = false;
        foreach (var raw in CastableText(card))
        {
            var text = raw.Replace('−', '-').ToLowerInvariant();
            var flicker = Flicker().IsMatch(text);
            foreach (var clause in Clauses().Split(text))
            {
                if (clause.Contains("you control", StringComparison.Ordinal) && !clause.Contains("you don't control", StringComparison.Ordinal)
                    && !clause.Contains("opponent", StringComparison.Ordinal)) continue;
                if (!flicker && DestroyOrExile().Match(clause) is { Success: true } removal)
                {
                    if (removal.Groups["q"].Value.Contains("non", StringComparison.Ordinal) && removal.Groups["q"].Value.Contains("creature", StringComparison.Ordinal)) continue;
                    if (Qualified(removal, clause)) conditional = true; else hard = true;
                }
                else if (Damage().Match(clause) is { Success: true } damage)
                {
                    var amount = damage.Groups["n"].Value == "x" ? int.MaxValue : int.Parse(damage.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture);
                    if (amount >= 3 && !Qualified(damage, clause)) hard = true; else if (amount >= 2) conditional = true;
                }
                else if (Shrink().Match(clause) is { Success: true } shrink)
                {
                    var toughness = shrink.Groups["t"].Value == "x" ? int.MaxValue : int.Parse(shrink.Groups["t"].Value, System.Globalization.CultureInfo.InvariantCulture);
                    if (toughness >= 3 && !Qualified(shrink, clause)) hard = true; else if (toughness >= 2) conditional = true;
                }
                else if (Bite().IsMatch(clause) && (clause.Contains("you don't control", StringComparison.Ordinal) || clause.Contains("opponent", StringComparison.Ordinal)))
                    conditional = true;
            }
            if (card.GameplayMetadata.CardTypes.Contains(CardType.Enchantment) && Pacifism().IsMatch(text)) conditional = true;
        }
        return (hard ? LimitedCardRole.HardRemoval : LimitedCardRole.None) | (conditional && !hard ? LimitedCardRole.ConditionalRemoval : LimitedCardRole.None);
    }

    // Any adjective before the noun (attacking, tapped, black or green...) or a trailing "with"/"that" restriction.
    private static bool Qualified(Match match, string clause)
    {
        if (match.Groups["q"].Value.Trim().Length > 0) return true;
        var rest = clause[(match.Index + match.Length)..];
        rest = Controller().Replace(rest, "").TrimStart();
        return rest.StartsWith("with ", StringComparison.Ordinal) || rest.StartsWith("that", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"[.;\n]|•")] private static partial Regex Clauses();
    [GeneratedRegex(@"return (it|that card|the exiled card|those cards|them)[^.]*to the battlefield")] private static partial Regex Flicker();
    [GeneratedRegex(@"\b(destroy|exile) (?:up to one )?target (?<q>(?:[a-z',/-]+ )*?)(?:creature|nonland permanent)\b(?! card)")] private static partial Regex DestroyOrExile();
    [GeneratedRegex(@"deals (?<n>\d+|x) damage to (?:any target|target (?<q>(?:[a-z',/-]+ )*?)creature\b)")] private static partial Regex Damage();
    [GeneratedRegex(@"target (?<q>(?:[a-z',/-]+ )*?)creature(?: an opponent controls| you don't control)? gets -(?<p>\d+|x)/-(?<t>\d+|x)\b")] private static partial Regex Shrink();
    [GeneratedRegex(@"deals damage equal to (?:its|that creature's) power to (?:target creature|any target)")] private static partial Regex Bite();
    [GeneratedRegex(@"enchanted creature can't attack or block")] private static partial Regex Pacifism();
    [GeneratedRegex(@"^(?:\s*(?:or planeswalker|or vehicle|or battle))?(?:\s*(?:an opponent controls|your opponents control|you don't control|that player controls))?")] private static partial Regex Controller();
}
