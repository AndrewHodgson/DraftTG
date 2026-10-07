using DraftTG.Data;

namespace DraftTG.Application;

/// <summary>Secondary keys of the Phase 9E hypothesis family, in tools/ArenaSortOrderProbe enumeration order.</summary>
public enum ArenaSortKey { Color, LandLast, CreaturesFirst, Cmc, BasicLandsFirst, Title, Collector, GrpId }

/// <summary>
/// One candidate Arena display rule: <c>Order_MythicToCommon</c>, then 1–3 secondary keys, then GrpId.
/// NULL keys sort last. This is a hypothesis under test, never an established Arena rule.
/// </summary>
public sealed class ArenaDisplayOrderRule : IComparer<ArenaCardSortKeys>
{
    internal ArenaDisplayOrderRule(int index, IReadOnlyList<ArenaSortKey> secondaryKeys)
    {
        Index = index;
        SecondaryKeys = secondaryKeys;
        Name = "rarity>" + string.Join(">", secondaryKeys.Select(KeyName));
    }

    public int Index { get; }
    public IReadOnlyList<ArenaSortKey> SecondaryKeys { get; }
    public string Name { get; }
    public override string ToString() => Name;

    public int Compare(ArenaCardSortKeys? x, ArenaCardSortKeys? y)
    {
        ArgumentNullException.ThrowIfNull(x); ArgumentNullException.ThrowIfNull(y);
        var result = Nullable(x.MythicToCommon, y.MythicToCommon);
        foreach (var key in SecondaryKeys)
        {
            if (result != 0) return result;
            result = key switch
            {
                ArenaSortKey.Color => Nullable(x.ColorOrder, y.ColorOrder),
                ArenaSortKey.LandLast => Nullable(x.LandLast, y.LandLast),
                ArenaSortKey.CreaturesFirst => Nullable(x.CreaturesFirst, y.CreaturesFirst),
                ArenaSortKey.Cmc => Nullable(x.CmcWithXLast, y.CmcWithXLast),
                ArenaSortKey.BasicLandsFirst => Nullable(x.BasicLandsFirst, y.BasicLandsFirst),
                ArenaSortKey.Title => x.Title is null || y.Title is null
                    ? (x.Title is null).CompareTo(y.Title is null) : string.CompareOrdinal(x.Title, y.Title),
                ArenaSortKey.Collector => Collector(x).CompareTo(Collector(y)),
                ArenaSortKey.GrpId => x.GrpId.CompareTo(y.GrpId),
                _ => throw new ArgumentOutOfRangeException(nameof(key))
            };
        }
        return result != 0 ? result : x.GrpId.CompareTo(y.GrpId);
    }

    private static int Nullable(int? x, int? y) =>
        x is null || y is null ? (x is null).CompareTo(y is null) : x.Value.CompareTo(y.Value);

    // Same as the probe: concatenated ASCII digits of CollectorNumber; none sorts last.
    private static long Collector(ArenaCardSortKeys card)
    {
        var digits = new string((card.CollectorNumber ?? "").Where(char.IsAsciiDigit).Take(15).ToArray());
        return digits.Length == 0 ? 1_000_000 : long.Parse(digits, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string KeyName(ArenaSortKey key) => key switch
    {
        ArenaSortKey.Color => "color", ArenaSortKey.LandLast => "landLast", ArenaSortKey.CreaturesFirst => "creaturesFirst",
        ArenaSortKey.Cmc => "cmc", ArenaSortKey.BasicLandsFirst => "basicLandsFirst", ArenaSortKey.Title => "title",
        ArenaSortKey.Collector => "collector", _ => "grpId"
    };
}

/// <summary>Rules that induce exactly the same total order over the class universe.</summary>
public sealed record ArenaDisplayOrderRuleClass(IReadOnlyList<ArenaDisplayOrderRule> Rules)
{
    public ArenaDisplayOrderRule Representative => Rules[0];
}

public sealed record ArenaDisplayOrderEvaluation(
    int HypothesisCount,
    int ObservationCount,
    int CardCount,
    IReadOnlyList<ArenaDisplayOrderRule> Survivors,
    IReadOnlyList<ArenaDisplayOrderRuleClass> Classes,
    IReadOnlyList<string> ContradictingObservations,
    int DiscriminatingObservations,
    int UnusableObservations,
    int ClassUniverseSize)
{
    public bool IsFamilyRefuted => ObservationCount > 0 && Survivors.Count == 0;
    public int Contradictions => ContradictingObservations.Count;
}

public enum ArenaDisplayOrderPredictionStatus { Unavailable, Unanimous, Discriminating }
public enum ArenaDisplayOrderAgreement { Unavailable, Agrees, Undetermined, Disagrees }

public sealed record ArenaPredictedOrder(IReadOnlyList<int> Order, int RuleCount);

public sealed record ArenaDisplayOrderPrediction(ArenaDisplayOrderPredictionStatus Status,
    IReadOnlyList<ArenaPredictedOrder> Orders, int SurvivingRules, string? Reason = null)
{
    public static ArenaDisplayOrderPrediction Unavailable(string reason, int survivingRules = 0) => new(ArenaDisplayOrderPredictionStatus.Unavailable, [], survivingRules, reason);

    /// <summary>Agrees: one predicted order and it matches. Undetermined: several, one matches. Disagrees: none matches.</summary>
    public ArenaDisplayOrderAgreement CompareWith(IReadOnlyList<int> actual)
    {
        if (Status == ArenaDisplayOrderPredictionStatus.Unavailable) return ArenaDisplayOrderAgreement.Unavailable;
        if (!Orders.Any(o => o.Order.SequenceEqual(actual))) return ArenaDisplayOrderAgreement.Disagrees;
        return Status == ArenaDisplayOrderPredictionStatus.Unanimous ? ArenaDisplayOrderAgreement.Agrees : ArenaDisplayOrderAgreement.Undetermined;
    }
}

/// <summary>
/// Pure, deterministic and platform-neutral (no capture, UI, Scryfall or file access). Observational only in
/// Phase 9E.1: no production placement path consumes <see cref="Predict"/>.
/// </summary>
public static class ArenaDisplayOrderModel
{
    public const int MaximumSecondaryKeys = 3;

    /// <summary>Rarity first, then every ordered choice of 1–3 of the 8 secondary keys: 8 + 56 + 336 = 400 rules.</summary>
    public static IReadOnlyList<ArenaDisplayOrderRule> Hypotheses { get; } = CreateHypotheses();

    public static IReadOnlyList<int>? Sort(ArenaDisplayOrderRule rule, IReadOnlyList<int> pack, IReadOnlyDictionary<int, ArenaCardSortKeys> keys)
    {
        if (pack.Any(id => !keys.ContainsKey(id))) return null;
        return pack.OrderBy(id => keys[id], rule).ToArray(); // Stable; identical GrpIds stay together.
    }

    public static ArenaDisplayOrderEvaluation Evaluate(IReadOnlyList<ArenaDisplayOrderObservation> observations,
        IReadOnlyDictionary<int, ArenaCardSortKeys>? classUniverse = null)
    {
        var survivors = Hypotheses.ToList();
        var contradictions = new List<string>();
        int discriminating = 0, unusable = 0, cards = 0, usable = 0;
        var observed = new Dictionary<int, ArenaCardSortKeys>();
        foreach (var observation in observations)
        {
            var keys = observation.KeyMap();
            if (observation.LogOrder.Any(id => !keys.ContainsKey(id))) { unusable++; continue; }
            usable++; cards += observation.LogOrder.Count;
            foreach (var pair in keys) observed.TryAdd(pair.Key, pair.Value);
            var predictions = survivors.Select(rule => (Rule: rule, Order: Sort(rule, observation.LogOrder, keys)!)).ToArray();
            if (predictions.Select(p => string.Join(",", p.Order)).Distinct(StringComparer.Ordinal).Count() > 1) discriminating++;
            var consistent = predictions.Where(p => p.Order.SequenceEqual(observation.VisualOrder)).Select(p => p.Rule).ToList();
            if (consistent.Count == 0) contradictions.Add(observation.ObservationId);
            survivors = consistent;
        }
        var universe = new Dictionary<int, ArenaCardSortKeys>(observed);
        if (classUniverse is not null) foreach (var pair in classUniverse) universe.TryAdd(pair.Key, pair.Value);
        // No observed cards means no functional grouping can be claimed yet.
        return new(Hypotheses.Count, usable, cards, survivors.AsReadOnly(), universe.Count == 0 ? [] : Classes(survivors, universe),
            contradictions.AsReadOnly(), discriminating, unusable, universe.Count);
    }

    /// <summary>Groups rules by the total order they induce over the universe; first-seen hypothesis order is kept.</summary>
    public static IReadOnlyList<ArenaDisplayOrderRuleClass> Classes(IReadOnlyList<ArenaDisplayOrderRule> rules,
        IReadOnlyDictionary<int, ArenaCardSortKeys> universe)
    {
        var ids = universe.Keys.Order().ToArray();
        return rules.GroupBy(rule => string.Join(",", Sort(rule, ids, universe)!), StringComparer.Ordinal)
            .Select(group => new ArenaDisplayOrderRuleClass(group.ToArray())).ToArray();
    }

    public static ArenaDisplayOrderPrediction Predict(IReadOnlyList<int> pack, IReadOnlyDictionary<int, ArenaCardSortKeys> keys,
        IReadOnlyList<ArenaDisplayOrderRule> survivors)
    {
        if (pack.Count == 0) return ArenaDisplayOrderPrediction.Unavailable("Empty pack.", survivors.Count);
        if (survivors.Count == 0) return ArenaDisplayOrderPrediction.Unavailable("No surviving order rule; the hypothesis family is refuted.");
        var missing = pack.Where(id => !keys.ContainsKey(id)).Distinct().ToArray();
        if (missing.Length > 0) return ArenaDisplayOrderPrediction.Unavailable($"Missing Arena sort keys for {missing.Length} card(s).", survivors.Count);
        var orders = survivors.Select(rule => Sort(rule, pack, keys)!)
            .GroupBy(order => string.Join(",", order), StringComparer.Ordinal)
            .Select(group => new ArenaPredictedOrder(group.First(), group.Count()))
            .OrderByDescending(order => order.RuleCount).ToArray(); // OrderBy is stable: ties keep first-seen rule order.
        return new(orders.Length == 1 ? ArenaDisplayOrderPredictionStatus.Unanimous : ArenaDisplayOrderPredictionStatus.Discriminating,
            orders, survivors.Count);
    }

    private static IReadOnlyList<ArenaDisplayOrderRule> CreateHypotheses()
    {
        var keys = Enum.GetValues<ArenaSortKey>();
        var rules = new List<ArenaDisplayOrderRule>();
        for (var length = 1; length <= MaximumSecondaryKeys; length++)
            foreach (var permutation in Permutations(keys, length))
                rules.Add(new(rules.Count, permutation));
        return rules.AsReadOnly();
    }

    // Lexicographic by index, identical to Python itertools.permutations.
    private static IEnumerable<ArenaSortKey[]> Permutations(ArenaSortKey[] items, int length)
    {
        if (length == 0) { yield return []; yield break; }
        for (var i = 0; i < items.Length; i++)
            foreach (var tail in Permutations(items.Where((_, j) => j != i).ToArray(), length - 1))
                yield return [items[i], .. tail];
    }
}
