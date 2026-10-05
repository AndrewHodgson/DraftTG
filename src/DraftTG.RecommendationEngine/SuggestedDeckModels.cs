using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DraftTG.Domain;

namespace DraftTG.RecommendationEngine;

public sealed record SuggestedDeckConfiguration
{
    public SuggestedDeckConfiguration(int maximumSuggestedBuilds = 3, int maximumAdditionalPairDataRequests = 2)
    {
        if (maximumSuggestedBuilds is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(maximumSuggestedBuilds));
        if (maximumAdditionalPairDataRequests is < 0 or > 2) throw new ArgumentOutOfRangeException(nameof(maximumAdditionalPairDataRequests));
        MaximumSuggestedBuilds = maximumSuggestedBuilds;
        MaximumAdditionalPairDataRequests = maximumAdditionalPairDataRequests;
    }
    public int MaximumSuggestedBuilds { get; }
    // Alternatives need no new pair data under the existing baseline affinity rules. This is a hard future ceiling.
    public int MaximumAdditionalPairDataRequests { get; }
}

public readonly record struct SuggestedDeckRank
{
    public SuggestedDeckRank(int value)
    {
        if (value is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(value));
        Value = value;
    }
    public int Value { get; }
}

public readonly record struct SuggestedDeckId(string Value)
{
    internal static SuggestedDeckId For(string sessionIdentity, ArchetypeColorPair pair) =>
        new(SuggestedDeckIdentity.Hash($"{sessionIdentity}\0{pair.Code}"));
}

internal static class SuggestedDeckIdentity
{
    internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    internal static string Pool(DraftPoolSnapshot pool) => Hash(string.Join("\n", pool.Entries
        .OrderBy(e => e.CardIdentifier.Value, StringComparer.Ordinal)
        .Select(e => $"{e.CardIdentifier.Value.Length.ToString(CultureInfo.InvariantCulture)}:{e.CardIdentifier.Value}:{e.Count.ToString(CultureInfo.InvariantCulture)}"))
        + $"\n{pool.Completeness}:{pool.UnresolvedOccurrenceCount.ToString(CultureInfo.InvariantCulture)}");
}

/// <summary>Common overall values only; null quality means no common statistical basis, never an invented rate.</summary>
public sealed record SuggestedDeckComparison(double? CommonSpellQuality, double PairPoolFit,
    int MeasuredStatCards, int NeutralFallbackCards, int UnscoredCards, int CompositionRelaxations, double CombinedColorEvidence);

public sealed record SuggestedDeckCandidateDiagnostic(DeckPairEvidence Evidence, bool InitiallyViable,
    bool ValidCandidate, string Diagnostic);

public sealed class SuggestedDeckLandDifference
{
    internal SuggestedDeckLandDifference(IEnumerable<DeckCardEntry> added, IEnumerable<DeckCardEntry> removed,
        IEnumerable<GeneratedBasicLandEntry> basicsAdded, IEnumerable<GeneratedBasicLandEntry> basicsRemoved)
    {
        DraftedAdded = Array.AsReadOnly(added.ToArray()); DraftedRemoved = Array.AsReadOnly(removed.ToArray());
        BasicsAdded = Array.AsReadOnly(basicsAdded.ToArray()); BasicsRemoved = Array.AsReadOnly(basicsRemoved.ToArray());
    }
    public IReadOnlyList<DeckCardEntry> DraftedAdded { get; }
    public IReadOnlyList<DeckCardEntry> DraftedRemoved { get; }
    public IReadOnlyList<GeneratedBasicLandEntry> BasicsAdded { get; }
    public IReadOnlyList<GeneratedBasicLandEntry> BasicsRemoved { get; }
    public int AddedCount => DraftedAdded.Sum(e => e.Count) + BasicsAdded.Sum(e => e.Count);
    public int RemovedCount => DraftedRemoved.Sum(e => e.Count) + BasicsRemoved.Sum(e => e.Count);
}

/// <summary>Multiset difference against the recommended baseline. Land changes are separate from spell changes.</summary>
public sealed class SuggestedDeckDifference
{
    private SuggestedDeckDifference(IEnumerable<DeckCardEntry> added, IEnumerable<DeckCardEntry> removed,
        int shared, SuggestedDeckLandDifference lands)
    {
        CardsAdded = Array.AsReadOnly(added.ToArray()); CardsRemoved = Array.AsReadOnly(removed.ToArray());
        SharedNonlandCount = shared; Lands = lands;
    }
    public IReadOnlyList<DeckCardEntry> CardsAdded { get; }
    public IReadOnlyList<DeckCardEntry> CardsRemoved { get; }
    public int SharedNonlandCount { get; }
    public int ChangedNonlandCount => CardsAdded.Sum(e => e.Count);
    public SuggestedDeckLandDifference Lands { get; }

    public static SuggestedDeckDifference Compare(BaselineDeck baseline, BaselineDeck alternative)
    {
        var before = baseline.Nonlands.ToDictionary(e => e.CardIdentifier, e => e.Count);
        var after = alternative.Nonlands.ToDictionary(e => e.CardIdentifier, e => e.Count);
        var basicsBefore = baseline.GeneratedBasics.ToDictionary(e => e.Type, e => e.Count);
        var basicsAfter = alternative.GeneratedBasics.ToDictionary(e => e.Type, e => e.Count);
        return new(Added(baseline.Nonlands, alternative.Nonlands), Added(alternative.Nonlands, baseline.Nonlands),
            before.Sum(e => Math.Min(e.Value, after.GetValueOrDefault(e.Key))),
            new(Added(baseline.NonbasicLands, alternative.NonbasicLands), Added(alternative.NonbasicLands, baseline.NonbasicLands),
                BasicDelta(basicsBefore, basicsAfter), BasicDelta(basicsAfter, basicsBefore)));
    }

    private static IEnumerable<DeckCardEntry> Added(IReadOnlyList<DeckCardEntry> before, IReadOnlyList<DeckCardEntry> after)
    {
        var counts = before.ToDictionary(e => e.CardIdentifier, e => e.Count);
        return after.OrderBy(e => e.CardIdentifier.Value, StringComparer.Ordinal)
            .Select(e => new DeckCardEntry(e.CardIdentifier, e.Count - counts.GetValueOrDefault(e.CardIdentifier))).Where(e => e.Count > 0);
    }
    private static IEnumerable<GeneratedBasicLandEntry> BasicDelta(Dictionary<BasicLandType, int> before, Dictionary<BasicLandType, int> after) =>
        after.OrderBy(e => e.Key).Select(e => new GeneratedBasicLandEntry(e.Key, e.Value - before.GetValueOrDefault(e.Key))).Where(e => e.Count > 0);
}

public sealed record SuggestedDeck(SuggestedDeckId Id, SuggestedDeckRank BuildRank, DeckBuildAvailability Availability,
    BaselineDeck Deck, SuggestedDeckComparison Comparison, SuggestedDeckDifference Difference)
{
    public bool IsRecommended => BuildRank.Value == 1;
    public string Label => $"Build {BuildRank.Value}" + (IsRecommended ? " — Recommended Baseline" : " — Alternative");
    public string PlanLabel => Deck.Plan.Pair.DisplayCode + (Deck.Plan.Archetype is { } a ? " — " + a.Name : "");
}

/// <summary>Immutable completed-session proposals; selecting creates a new set without rebuilding or touching Arena.</summary>
public sealed class SuggestedDeckSet
{
    internal SuggestedDeckSet(string sessionIdentity, string poolIdentity, DraftPoolSnapshot pool,
        DeckBuildResult baseline, IEnumerable<SuggestedDeck> builds, IEnumerable<SuggestedDeckCandidateDiagnostic> candidates,
        SuggestedDeckId? selected = null)
    {
        SessionIdentity = sessionIdentity; PoolIdentity = poolIdentity; Pool = pool; BaselineResult = baseline;
        Builds = Array.AsReadOnly(builds.ToArray()); Candidates = Array.AsReadOnly(candidates.ToArray());
        Selected = Builds.FirstOrDefault(b => b.Id == selected) ?? Recommended;
        ExcludedCandidates = Array.AsReadOnly(Candidates.Where(c => !c.ValidCandidate).ToArray());
    }
    public string SessionIdentity { get; }
    public string PoolIdentity { get; }
    public DraftPoolSnapshot Pool { get; }
    public DeckBuildResult BaselineResult { get; }
    public DeckBuildAvailability Availability => BaselineResult.Availability;
    public IReadOnlyList<SuggestedDeck> Builds { get; }
    public SuggestedDeck? Recommended => Builds.FirstOrDefault();
    public SuggestedDeck? Selected { get; }
    public IReadOnlyList<SuggestedDeckCandidateDiagnostic> Candidates { get; }
    public IReadOnlyList<SuggestedDeckCandidateDiagnostic> ExcludedCandidates { get; }
    public int ViablePairCandidatesEvaluated => Candidates.Count(c => c.InitiallyViable);
    public SuggestedDeckSet Select(SuggestedDeckId? id) => new(SessionIdentity, PoolIdentity, Pool, BaselineResult, Builds, Candidates, id);
}
