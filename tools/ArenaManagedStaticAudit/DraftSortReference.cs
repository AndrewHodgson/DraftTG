using System.Globalization;
using System.Text.Json;
using DraftTG.Application;
using DraftTG.Data;

// Independent interoperability model of the inspected build. Not linked by the app.
// Host .NET collation is not a claim of identical Unity Mono collation for all Unicode.
internal sealed record ReferenceCard(int GrpId, int Rarity, bool IsLand, bool IsArtifact, int ColorFlags,
    int ColorIdentityFlags, string? LocalizedTitle, ArenaCardSortKeys HypothesisKeys);
internal sealed record ReferenceObservation(string Label, int[] Log, int[] Visual, ReferenceCard[] Cards);
internal sealed record ReferenceInput(string Culture, ReferenceObservation[] Observations, ArenaCardSortKeys[]? UniverseKeys = null);
internal static class DraftSortReference
{
    // Flags in increasing rank, derived from SharedClientCore GetColorSortOrderTable.
    private static readonly int[] FlagsByRank = [1, 2, 4, 8, 16, 3, 5, 6, 10, 12, 20, 24, 9, 17, 18,
        7, 14, 28, 25, 19, 13, 26, 21, 11, 22, 15, 30, 29, 27, 23, 31, 0];
    public static int ColorRank(ReferenceCard card) => ColorRank(card.IsLand || card.IsArtifact ? card.ColorIdentityFlags : card.ColorFlags);
    public static int ColorRank(int flags)
    {
        if (flags is < 0 or > 31) throw new ArgumentOutOfRangeException(nameof(flags));
        return Array.IndexOf(FlagsByRank, flags);
    }
    public static ReferenceCard[] Sort(IEnumerable<ReferenceCard> occurrences, IComparer<string?> titleComparer) =>
        occurrences.OrderBy(c => 5 - c.Rarity).ThenBy(c => c.IsLand ? 1 : 0)
            .ThenBy(ColorRank).ThenBy(c => c.LocalizedTitle, titleComparer).ToArray();

    public static void Replay(string path)
    {
        var input = JsonSerializer.Deserialize<ReferenceInput>(File.ReadAllText(path))!;
        var comparer = StringComparer.Create(CultureInfo.GetCultureInfo(input.Culture), false);
        var observations = input.Observations.Select(o =>
        {
            var cards = o.Cards.ToDictionary(c => c.GrpId);
            var keys = o.Cards.ToDictionary(c => c.GrpId, c => c.HypothesisKeys);
            var sorted = Sort(o.Log.Select(id => cards[id]), comparer);
            var order = sorted.Select(c => c.GrpId).ToArray();
            var matching = ArenaDisplayOrderModel.Hypotheses.Where(rule =>
                ArenaDisplayOrderModel.Sort(rule, o.Log, keys)!.SequenceEqual(order)).Select(r => r.Name).ToArray();
            var ties = sorted.Zip(sorted.Skip(1)).Count(p => p.First.Rarity == p.Second.Rarity
                && p.First.IsLand == p.Second.IsLand && ColorRank(p.First) == ColorRank(p.Second)
                && comparer.Compare(p.First.LocalizedTitle, p.Second.LocalizedTitle) == 0);
            var mismatches = o.Cards.Where(c => c.HypothesisKeys.MythicToCommon != 5 - c.Rarity
                || c.HypothesisKeys.LandLast != (c.IsLand ? 1 : 0) || c.HypothesisKeys.ColorOrder != ColorRank(c))
                .Select(c => c.GrpId).ToArray();
            return new { o.Label, CardCount = o.Log.Length, ExactMatch = order.SequenceEqual(o.Visual), Order = order,
                EqualAdjacentKeys = ties, DatabaseKeyMismatches = mismatches, MatchingHypothesisRules = matching };
        }).ToArray();
        var commonNames = observations.Select(o => o.MatchingHypothesisRules.AsEnumerable())
            .Aggregate((left, right) => left.Intersect(right)).ToArray();
        var commonRules = ArenaDisplayOrderModel.Hypotheses.Where(r => commonNames.Contains(r.Name)).ToArray();
        var universe = (input.UniverseKeys ?? input.Observations.SelectMany(o => o.Cards).Select(c => c.HypothesisKeys).ToArray())
            .GroupBy(k => k.GrpId).ToDictionary(g => g.Key, g => g.First());
        Console.WriteLine(JsonSerializer.Serialize(new { input.Culture, Observations = observations,
            CommonModelRuleCount = commonRules.Length, ClassUniverseSize = universe.Count,
            CommonModelClasses = ArenaDisplayOrderModel.Classes(commonRules, universe).Count,
            IlKeyCounterpartSurvives = commonNames.Contains("rarity>landLast>color>title"),
            Tested = observations.Length, ExactMatches = observations.Count(o => o.ExactMatch),
            Contradictions = observations.Where(o => !o.ExactMatch).Select(o => o.Label) }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
