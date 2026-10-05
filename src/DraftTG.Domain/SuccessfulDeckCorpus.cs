using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace DraftTG.Domain;

public enum SuccessfulDeckFormat { PremierDraft, TraditionalDraft, QuickDraft }
public enum SuccessfulDeckSource { Live, Cache, StaleCache, Unavailable }
public enum SuccessfulDeckPoolBasis { DraftedPool, MaindeckAndSideboard }

public sealed record SuccessfulDeckKey
{
    public SuccessfulDeckKey(string expansion, SuccessfulDeckFormat format, ArchetypeColorPair pair)
    {
        if (!Regex.IsMatch(expansion ?? "", "^[A-Z0-9]{2,8}\\z", RegexOptions.CultureInvariant))
            throw new ArgumentException("An exact uppercase expansion is required.", nameof(expansion));
        if (!Enum.IsDefined(format)) throw new ArgumentOutOfRangeException(nameof(format));
        ArgumentNullException.ThrowIfNull(pair);
        Expansion = expansion!; Format = format; Pair = pair;
    }
    public string Expansion { get; }
    public SuccessfulDeckFormat Format { get; }
    public ArchetypeColorPair Pair { get; }
}

/// <summary>Maximum match wins come from source event metadata, not a universal seven-win rule.</summary>
public sealed record SuccessfulEventCriteria
{
    public SuccessfulEventCriteria(int maximumMatchWins, int bestOf)
    {
        if (maximumMatchWins is <= 0 or > 100 || bestOf is not (1 or 3))
            throw new ArgumentOutOfRangeException(nameof(maximumMatchWins));
        MaximumMatchWins = maximumMatchWins; BestOf = bestOf;
    }
    public int MaximumMatchWins { get; }
    public int BestOf { get; }
}

/// <summary>Exact canonical names retain counts across printings; identity never depends on array position.
/// Sample identity is for deduplication/provenance and is not displayed in the rail.</summary>
public sealed class SuccessfulDeckSample
{
    public SuccessfulDeckSample(SuccessfulDeckKey key, string sampleIdentity, SuccessfulEventCriteria criteria,
        int wins, int losses, DateTimeOffset completedAt, IEnumerable<KeyValuePair<string, int>> maindeck,
        IEnumerable<KeyValuePair<string, int>>? pool = null,
        SuccessfulDeckPoolBasis poolBasis = SuccessfulDeckPoolBasis.DraftedPool, string representativeDeck = "final")
    {
        ArgumentNullException.ThrowIfNull(key); ArgumentNullException.ThrowIfNull(criteria);
        if (string.IsNullOrWhiteSpace(sampleIdentity) || string.IsNullOrWhiteSpace(representativeDeck)
            || wins != criteria.MaximumMatchWins || losses is < 0 or > 100 || completedAt == default
            || !Enum.IsDefined(poolBasis)) throw new ArgumentException("Invalid successful event.");
        Key = key; SampleIdentity = sampleIdentity; Criteria = criteria; Wins = wins; Losses = losses;
        CompletedAt = completedAt; RepresentativeDeck = representativeDeck; PoolBasis = poolBasis;
        Maindeck = Counts(maindeck);
        Pool = pool is null ? null : Counts(pool);
        if (Maindeck.Count == 0 || (Pool is not null && (Pool.Count == 0
            || Maindeck.Any(c => Pool.GetValueOrDefault(c.Key) < c.Value))))
            throw new ArgumentException("Missing maindeck or maindeck exceeds the available pool.");
    }
    private static FrozenDictionary<string, int> Counts(IEnumerable<KeyValuePair<string, int>> counts)
    {
        var copy = counts.ToDictionary(c => c.Key, c => c.Value, StringComparer.Ordinal);
        if (copy.Any(c => string.IsNullOrWhiteSpace(c.Key) || c.Value is <= 0 or > 1000))
            throw new ArgumentException("Invalid card counts.");
        return copy.ToFrozenDictionary(StringComparer.Ordinal);
    }
    public SuccessfulDeckKey Key { get; }
    public string SampleIdentity { get; }
    public SuccessfulEventCriteria Criteria { get; }
    public int Wins { get; }
    public int Losses { get; }
    public DateTimeOffset CompletedAt { get; }
    public string RepresentativeDeck { get; }
    public SuccessfulDeckPoolBasis PoolBasis { get; }
    public IReadOnlyDictionary<string, int> Maindeck { get; }
    public IReadOnlyDictionary<string, int>? Pool { get; }
}

public sealed record SuccessfulDeckProvenance(string Source, string QueryMode, string Query,
    int SourceLimit, int RequestedLimit, int ReturnedEvents, string RepresentativePolicy);

public sealed class SuccessfulDeckCorpus
{
    public SuccessfulDeckCorpus(SuccessfulDeckKey key, IEnumerable<SuccessfulDeckSample> samples,
        DateTimeOffset retrievedAt, SuccessfulDeckProvenance provenance)
    {
        var copy = samples.ToArray();
        if (copy.Length == 0 || copy.Any(s => s.Key != key || s.CompletedAt > retrievedAt)
            || copy.Select(s => s.SampleIdentity).Distinct(StringComparer.Ordinal).Count() != copy.Length
            || retrievedAt == default || provenance.SourceLimit <= 0 || provenance.RequestedLimit <= 0
            || provenance.RequestedLimit > provenance.SourceLimit || copy.Length > provenance.RequestedLimit
            || provenance.ReturnedEvents < copy.Length || string.IsNullOrWhiteSpace(provenance.Source)
            || string.IsNullOrWhiteSpace(provenance.QueryMode) || string.IsNullOrWhiteSpace(provenance.Query)
            || string.IsNullOrWhiteSpace(provenance.RepresentativePolicy))
            throw new ArgumentException("Invalid, duplicate or mismatched successful-deck corpus.");
        Key = key; Samples = Array.AsReadOnly(copy); RetrievedAt = retrievedAt; Provenance = provenance;
    }
    public SuccessfulDeckKey Key { get; }
    public IReadOnlyList<SuccessfulDeckSample> Samples { get; }
    public DateTimeOffset RetrievedAt { get; }
    public SuccessfulDeckProvenance Provenance { get; }
}

public sealed record SuccessfulDeckLoadResult(SuccessfulDeckKey Key, SuccessfulDeckCorpus? Corpus,
    SuccessfulDeckSource Source, string? Diagnostic = null);

public interface ISuccessfulDeckProvider
{
    Task<SuccessfulDeckLoadResult> LoadAsync(SuccessfulDeckKey key, CancellationToken cancellationToken = default);
}
