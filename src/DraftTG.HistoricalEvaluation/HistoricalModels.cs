using System.Text.Json;
using System.Text.Json.Serialization;
using DraftTG.Domain;

namespace DraftTG.HistoricalEvaluation;

public enum IdentityStatus { ExactSet, FrontFace, ExplicitBonusSheet, ExplicitIdentifier, Unresolved, Ambiguous }
public enum EvaluationRegime { MatureFormatReplay, ChronologicalBacktest }
public enum DatasetPartition { Calibration, Development, Holdout }
public sealed record SourceProvenance(string Provider, string Uri, string Sha256, long Row, string Schema);
public sealed record HistoricalCardIdentity(string SourceIdentity, string? CanonicalId, string? PrintingSet,
    IdentityStatus Status, string Rule, string[] Candidates)
{
    [JsonIgnore] public CardIdentifier? Identifier => CanonicalId is null ? null : CardIdentifier.Create(CanonicalId);
}
public sealed record HistoricalCardQuantity(HistoricalCardIdentity Card, int Count);
public sealed record HistoricalPriorPick(int Pack, int Pick, HistoricalCardIdentity Card);

/// <summary>Only information observed before the decision. No selected card, outcomes or future inventory.</summary>
public sealed record PickTimeState(string SetCode, string Format, string DraftId, DateTimeOffset? DraftTimestamp,
    int PackNumber, int PickNumber, int GlobalPickIndex, HistoricalCardQuantity[] OfferedCards,
    HistoricalCardQuantity[] PoolBeforePick, HistoricalPriorPick[] PriorPicks, bool PoolIsComplete);
/// <summary>Source rank/lifetime buckets with uncertain temporal scope; analysis only, outside scoring inputs.</summary>
public sealed record HistoricalPlayerMetadata(string? SourceRank, IReadOnlyDictionary<string,string> LifetimeBuckets,
    string TemporalScope);

/// <summary>Analysis labels; this type is never accepted by the scoring adapter.</summary>
public sealed record DraftOutcome(int? FinalWins = null, int? FinalLosses = null,
    HistoricalCardQuantity[]? FinalPool = null, HistoricalCardQuantity[]? FinalMainDeck = null,
    HistoricalCardQuantity[]? FinalSideboard = null, string? DeckEvidence = null,
    int? ReportedEventWins = null, int? ReportedEventLosses = null, string? OutcomeEvidence = null);
public sealed record HistoricalPickLabel(HistoricalCardIdentity ActualPickedCard,
    double? ObservedMaindeckRate = null, double? ObservedSideboardInRate = null);
public sealed record HistoricalDraftDecision(string DatasetVersion, string DatasetId, PickTimeState Inputs,
    HistoricalPickLabel Label, DraftOutcome Outcome, SourceProvenance Provenance, HistoricalPlayerMetadata? PlayerMetadata=null);

public sealed record HistoricalCardStats(string SourceIdentity, string SetCode, string PrimaryFormat,
    double? GIHWinRate = null, int? GIHGames = null, double? GamesPlayedWinRate = null,
    double? OpeningHandWinRate = null, double? DrawnWinRate = null, double? ALSA = null, double? ATA = null,
    int? GamesPlayed = null, int? DraftedCount = null, double? SetBaseline = null,
    DateTimeOffset? AvailableAt = null, string? Period = null, string? Provenance = null,
    double? SampleAverageSeenPosition = null, double? SampleAverageTakenPosition = null);
public sealed record HistoricalStatsSnapshot(string SetCode, string Format, EvaluationRegime Regime,
    DateTimeOffset? AvailableAt, string Provenance, HistoricalCardStats[] Cards,
    HistoricalCardStats[]? AlternateQualityCards = null);
public sealed record HistoricalDeckSummary(int TotalCards, int UnresolvedCards, int StructuredMetadataCards,
    int Lands, int Spells, int Creatures, int NonCreatureSpells, int EarlyPlays, int TopEnd, int Fixing,
    double WeightedRemoval, int OracleTextCoveredCards, IReadOnlyDictionary<string, int> CurveBuckets,
    IReadOnlyDictionary<string, int> ColorDistribution, IReadOnlyDictionary<string, double> ColoredPips,
    int UnknownManaCards);
public sealed record HistoricalLimitedDeck(string SetCode, string Format, string DraftId, string BuildId,
    HistoricalCardQuantity[] MainDeck, HistoricalCardQuantity[]? Sideboard, HistoricalCardQuantity[]? FullPool,
    int? Wins, int? Losses, string? PlayerRank, string Evidence, HistoricalDeckSummary Summary,
    SourceProvenance Provenance);

public sealed record DatasetImportOptions(string DatasetId, string Source, string SourceUri, string SetCode,
    string Format, DatasetPartition Partition, string[] BonusSets, int PicksPerPack = 15,
    string SourceSchema = "17lands-wide-draft-v1", bool IsPrefixSample = false);
public sealed record HistoricalDatasetManifest(string DatasetId, string Source, string SourceUri, string SetCode,
    string Format, string SourceSchemaVersion, string[] ObservedColumns, string FileHash, long FileSize,
    DateTimeOffset ImportTimestamp, long RawRows, long PickCount, long DraftCount,
    DateTimeOffset? MinDate, DateTimeOffset? MaxDate, long IdentityResolvedCount, long IdentityUnresolvedCount,
    long IdentityAmbiguousCount, long MalformedRows, long DuplicateRows, long ConflictingRows,
    long OutcomeCoveredPicks, long FinalDeckCoveredPicks, long MaindeckRateCoveredPicks,
    DatasetPartition Partition, bool IsPrefixSample, double Seconds, double RowsPerSecond, long PeakWorkingSetBytes,
    IReadOnlyDictionary<string, long> IdentityIssues, string IdentityCatalogHash, string SplitHash,
    string ImportOptionsHash, int PicksPerPack, long FinalPoolCoveredPicks);
public sealed record SetSplitManifest(string Version, IReadOnlyDictionary<string, DatasetPartition> Sets)
{
    public DatasetPartition For(string set) => Sets.TryGetValue(set.ToUpperInvariant(), out var split)
        ? split : throw new InvalidDataException($"Set {set} has no explicit partition.");
}
public static class HistoricalJson
{
    public const string Schema = "historical-decision-v1";
    public static readonly JsonSerializerOptions Compact = new()
    { PropertyNameCaseInsensitive = true, RespectRequiredConstructorParameters=true, Converters = { new JsonStringEnumConverter() } };
    public static readonly JsonSerializerOptions Pretty = new(Compact) { WriteIndented = true };
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Compact);
    public static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, Compact)
        ?? throw new InvalidDataException("Null historical record.");
}
