using DraftTG.Domain;

namespace DraftTG.RecommendationEngine;

public enum DeckBuildAvailability { Ready, ProvisionalPartialPool, ProvisionalUnknownPool, DraftInProgress, InsufficientEligibleCards, MissingRequiredMetadata }
public enum DeckBuildConfidence { Statistical, NeutralPriorsUsed, InsufficientStatistics }
public enum DeckPlanSource { ActiveArchetype, PoolEvidenceFallback, AlternativeColorPair }
public enum DeckCardStrengthSource { OverallStatistical, ArchetypeAdjusted, NeutralBaselineFallback, DeterministicUnscored }
[Flags]
public enum DeckDecisionReason
{
    None = 0, StatisticalStrength = 1, ArchetypeAffinity = 2, NeutralStatisticalPrior = 4,
    CreatureRequirement = 8, EarlyCurveRequirement = 16, OffColor = 32,
    LowerSelectionValue = 64, HighCostConstraint = 128, CompositionConstraint = 256,
    MissingMetadata = 512, NonbasicLandPolicy = 1024, GeneratedBasicsReplaceDraftedBasics = 2048,
    DeterministicFallback = 4096
}

public sealed record DeckCardStrength(CardIdentifier CardIdentifier, double? SelectionValue,
    DeckCardStrengthSource Source, double? Phase8AdjustedValue, double ArchetypeAdjustment)
{
    public bool IsMeasured => Source is DeckCardStrengthSource.OverallStatistical or DeckCardStrengthSource.ArchetypeAdjusted;
}
public sealed record DeckCardEntry(CardIdentifier CardIdentifier, int Count);
public sealed record GeneratedBasicLandEntry(BasicLandType Type, int Count);
public sealed record DeckCardDecision(CardIdentifier CardIdentifier, int PoolCount, int SelectedCount,
    DeckCardStrength? Strength, DeckDecisionReason Reasons);
public sealed record DeckPairEvidence(ArchetypeColorPair Pair, double PoolFit, double CombinedEvidence, double Balance, int EligibleNonlands);
public sealed record DeckPlan(ArchetypeColorPair Pair, DeckPlanSource Source, ArchetypeDefinition? Archetype,
    double PairConfidence, ArchetypeContextProfile FinalArchetypeContext);

public sealed record DeckBuildInput(DraftPoolSnapshot Pool, CardCatalog Catalog,
    LimitedCardStatisticsCatalog Statistics, LimitedCardStatisticsCatalog? EnvironmentStatistics = null,
    LimitedStatisticsContext? StatisticsContext = null, SetArchetypeProfile? Archetypes = null,
    ArchetypePairStatistics? PairStatistics = null);

/// <summary>Product heuristics, not universal Limited composition targets.</summary>
public sealed record BaselineDeckConfiguration
{
    public BaselineDeckConfiguration(int targetDeckSize = 40, int targetLandCount = 17, int targetNonlandCount = 23,
        int minimumCreatures = 14, int preferredCreatures = 16, int minimumEarlyPlays = 4,
        double highCostThreshold = 5, int maximumHighCostCards = 6, int maximumDraftedNonbasicLands = 4,
        int minimumBasicsPerUsedColor = 6)
    {
        if (targetDeckSize != targetLandCount + targetNonlandCount || targetLandCount < 0 || targetNonlandCount is < 1 or > 40
            || minimumCreatures < 0 || minimumEarlyPlays < 0 || minimumCreatures + minimumEarlyPlays > targetNonlandCount
            || preferredCreatures < minimumCreatures || preferredCreatures > targetNonlandCount
            || !double.IsFinite(highCostThreshold) || highCostThreshold <= 2 || maximumHighCostCards < 0
            || maximumHighCostCards > targetNonlandCount || maximumDraftedNonbasicLands < 0 || minimumBasicsPerUsedColor < 0)
            throw new ArgumentOutOfRangeException(nameof(targetDeckSize), "Invalid baseline composition configuration.");
        TargetDeckSize = targetDeckSize; TargetLandCount = targetLandCount; TargetNonlandCount = targetNonlandCount;
        MinimumCreatures = minimumCreatures; PreferredCreatures = preferredCreatures; MinimumEarlyPlays = minimumEarlyPlays;
        HighCostThreshold = highCostThreshold; MaximumHighCostCards = maximumHighCostCards;
        MaximumDraftedNonbasicLands = maximumDraftedNonbasicLands; MinimumBasicsPerUsedColor = minimumBasicsPerUsedColor;
    }
    public int TargetDeckSize { get; }
    public int TargetLandCount { get; }
    public int TargetNonlandCount { get; }
    public int MinimumCreatures { get; }
    public int PreferredCreatures { get; }
    public int MinimumEarlyPlays { get; }
    public double HighCostThreshold { get; }
    public int MaximumHighCostCards { get; }
    public int MaximumDraftedNonbasicLands { get; }
    public int MinimumBasicsPerUsedColor { get; }
}

public sealed record DeckCompositionDiagnostics(int RequiredCreatures, int RequiredEarlyPlays, int EffectiveHighCostCap,
    int SelectedEarlyPlays, int SelectedHighCostCards, IReadOnlyList<string> Relaxations);
public sealed record DeckStrengthSummary(double TotalSelectionObjective, double? AverageMeasuredSelectionValue,
    int NeutralFallbackCards, int DeterministicUnscoredCards);

public sealed class BaselineDeck
{
    internal BaselineDeck(DeckPlan plan, IEnumerable<DeckCardEntry> nonlands, IEnumerable<DeckCardEntry> nonbasics,
        IEnumerable<GeneratedBasicLandEntry> basics, IEnumerable<DeckCardEntry> sideboard,
        IEnumerable<DeckCardDecision> decisions, DraftPoolAnalysis analysis,
        IReadOnlyDictionary<MagicColor, double> demand, IReadOnlyDictionary<MagicColor, int> sources,
        DeckCompositionDiagnostics composition, DeckStrengthSummary strength, DeckBuildConfidence confidence,
        IEnumerable<string> diagnostics)
    {
        Plan = plan; Nonlands = Array.AsReadOnly(nonlands.ToArray()); NonbasicLands = Array.AsReadOnly(nonbasics.ToArray());
        GeneratedBasics = Array.AsReadOnly(basics.ToArray()); Sideboard = Array.AsReadOnly(sideboard.ToArray());
        Decisions = Array.AsReadOnly(decisions.ToArray()); Analysis = analysis; ColoredDemand = demand; KnownColoredSources = sources;
        Composition = composition; Strength = strength; Confidence = confidence; Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }
    public DeckPlan Plan { get; }
    public IReadOnlyList<DeckCardEntry> Nonlands { get; }
    public IReadOnlyList<DeckCardEntry> NonbasicLands { get; }
    public IReadOnlyList<GeneratedBasicLandEntry> GeneratedBasics { get; }
    public IReadOnlyList<DeckCardEntry> Sideboard { get; }
    public IReadOnlyList<DeckCardDecision> Decisions { get; }
    public DraftPoolAnalysis Analysis { get; }
    public IReadOnlyDictionary<MagicColor, double> ColoredDemand { get; }
    public IReadOnlyDictionary<MagicColor, int> KnownColoredSources { get; }
    public DeckCompositionDiagnostics Composition { get; }
    public DeckStrengthSummary Strength { get; }
    public DeckBuildConfidence Confidence { get; }
    public IReadOnlyList<string> Diagnostics { get; }
    public int NonlandCount => Nonlands.Sum(e => e.Count);
    public int LandCount => NonbasicLands.Sum(e => e.Count) + GeneratedBasics.Sum(e => e.Count);
    public int TotalCardCount => NonlandCount + LandCount;
    public int CreatureCount => Analysis.CreatureCount;
}

public sealed record DeckBuildResult(DeckBuildAvailability Availability, BaselineDeck? Deck, string Diagnostic)
{
    public static DeckBuildResult InProgress { get; } = new(DeckBuildAvailability.DraftInProgress, null, "A baseline build is available after draft completion.");
}
