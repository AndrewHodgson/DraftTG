using System.Text.RegularExpressions;
using DraftTG.ArenaIntegration;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;

namespace DraftTG.Application;

public static class DraftExpansionResolver
{
    public static string? Resolve(string? eventName, DraftSnapshot? snapshot, CardCatalog catalog)
    {
        var match = Regex.Match(eventName ?? string.Empty,
            @"^(?:QuickDraft|PremierDraft|TraditionalDraft|TradDraft)_([A-Z0-9]{2,8})(?:_[0-9]{8})?\z",
            RegexOptions.CultureInvariant);
        if (match.Success) return match.Groups[1].Value;
        // Fallback requires a nonempty pack, every identity resolved, and unanimous set codes.
        var cards = snapshot?.CurrentPack.AvailableCardIdentifiers.Select(catalog.Find).ToArray();
        if (cards is null || cards.Length == 0 || cards.Any(card => card is null)) return null;
        var codes = cards.Select(card => card!.SetCode.Value.ToUpperInvariant()).Distinct(StringComparer.Ordinal).ToArray();
        return codes.Length == 1 && Regex.IsMatch(codes[0], @"^[A-Z0-9]{2,8}\z") ? codes[0] : null;
    }

    public static LimitedStatisticsFormat? FormatFor(ArenaDraftModeKind? mode) => mode switch
    {
        ArenaDraftModeKind.Quick => LimitedStatisticsFormat.QuickDraft,
        ArenaDraftModeKind.Premier => LimitedStatisticsFormat.PremierDraft,
        ArenaDraftModeKind.Traditional => LimitedStatisticsFormat.TraditionalDraft,
        _ => null
    };
}
