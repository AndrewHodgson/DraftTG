using System.Collections.Frozen;
using DraftTG.Domain;

namespace DraftTG.RecommendationEngine;

public sealed record TrophyRecommendationConfiguration
{
    public TrophyRecommendationConfiguration(int minimumTrophyCorpusDecks = 20, int minimumCardTrophyExposure = 5,
        int fullCorpusConfidenceDecks = 50, int fullCardExposureDecks = 15, int maxCorporaPerDraft = 3)
    {
        if (minimumTrophyCorpusDecks <= 0 || minimumCardTrophyExposure <= 0 || fullCorpusConfidenceDecks <= 0
            || fullCardExposureDecks <= 0 || maxCorporaPerDraft is < 0 or > 10)
            throw new ArgumentOutOfRangeException(nameof(minimumTrophyCorpusDecks));
        MinimumTrophyCorpusDecks = minimumTrophyCorpusDecks; MinimumCardTrophyExposure = minimumCardTrophyExposure;
        FullCorpusConfidenceDecks = fullCorpusConfidenceDecks; FullCardExposureDecks = fullCardExposureDecks;
        MaxCorporaPerDraft = maxCorporaPerDraft;
    }
    public int MinimumTrophyCorpusDecks { get; }
    public int MinimumCardTrophyExposure { get; }
    public int FullCorpusConfidenceDecks { get; }
    public int FullCardExposureDecks { get; }
    public int MaxCorporaPerDraft { get; }
    public string ModelVersion => "successful-deck-evidence-v1";
}

public sealed record TrophyCardEvidence(string CardName, int EligibleDecks, int DecksWithPool,
    int PoolDecks, int MainDeckDecks, int MainDeckDecksWithKnownPool, int PoolCopies, int MainDeckCopies,
    int MainDeckCopiesWithKnownPool)
{
    public double? MainDeckConversion => PoolDecks > 0 ? MainDeckDecksWithKnownPool / (double)PoolDecks : null;
    public double? CopyUtilization => PoolCopies > 0 ? MainDeckCopiesWithKnownPool / (double)PoolCopies : null;
    public double DeckPresenceRate => MainDeckDecks / (double)EligibleDecks;
    public double? AverageCopiesWhenPlayed => MainDeckDecks > 0 ? MainDeckCopies / (double)MainDeckDecks : null;
}

/// <summary>Aggregate each event once. Missing pools are unknown, never zero exposures.</summary>
public sealed class TrophyEvidenceCatalog
{
    private readonly FrozenDictionary<string, TrophyCardEvidence> _cards;
    public TrophyEvidenceCatalog(SuccessfulDeckCorpus corpus)
    {
        Corpus = corpus;
        var samples = corpus.Samples;
        var withPool = samples.Where(s => s.Pool is not null).ToArray();
        _cards = samples.SelectMany(s => s.Maindeck.Keys.Concat(s.Pool?.Keys ?? [])).Distinct(StringComparer.Ordinal)
            .ToDictionary(name => name, name => new TrophyCardEvidence(name, samples.Count, withPool.Length,
                withPool.Count(s => s.Pool!.ContainsKey(name)), samples.Count(s => s.Maindeck.ContainsKey(name)),
                withPool.Count(s => s.Maindeck.ContainsKey(name)), withPool.Sum(s => s.Pool!.GetValueOrDefault(name)),
                samples.Sum(s => s.Maindeck.GetValueOrDefault(name)), withPool.Sum(s => s.Maindeck.GetValueOrDefault(name))),
                StringComparer.Ordinal).ToFrozenDictionary(StringComparer.Ordinal);
    }
    public SuccessfulDeckCorpus Corpus { get; }
    public TrophyCardEvidence? ForName(string name) => _cards.GetValueOrDefault(name);
    public IEnumerable<TrophyCardEvidence> Cards => _cards.Values;
}

public sealed record TrophyCardRecommendation(ArchetypeCardRecommendation ArchetypeRecommendation,
    TrophyCardEvidence? Evidence, bool Eligible, double CorpusConfidence, double CardExposureConfidence,
    double CombinedConfidence, string Diagnostic)
{
    public int PackIndex => ArchetypeRecommendation.PackIndex;
    public CardIdentifier CardIdentifier => ArchetypeRecommendation.CardIdentifier;
    // Deliberate decision gate: 17Lands play_rate is weighted by games AND copies.
    // It cannot baseline event-presence conversion; no opt-in can override this semantic mismatch.
    public double Adjustment => 0;
    public double? FinalValue => ArchetypeRecommendation.ContextualValue;
    public int? FinalRank => ArchetypeRecommendation.ContextualRank;
    public bool IsContextPick => ArchetypeRecommendation.IsTopContextualCandidate;
}

public sealed class TrophyRecommendationResult
{
    internal TrophyRecommendationResult(ArchetypeRecommendationResult archetype, TrophyEvidenceCatalog? evidence,
        TrophyRecommendationConfiguration configuration, IEnumerable<TrophyCardRecommendation> cards)
    { Archetype = archetype; Evidence = evidence; Configuration = configuration; Cards = Array.AsReadOnly(cards.ToArray()); }
    public const string DisabledReason = "Incompatible baseline semantics: 17Lands Play Rate is weighted by games and copies; trophy conversion counts events.";
    public bool ScoringEnabled => false;
    public ArchetypeRecommendationResult Archetype { get; }
    public TrophyEvidenceCatalog? Evidence { get; }
    public TrophyRecommendationConfiguration Configuration { get; }
    public IReadOnlyList<TrophyCardRecommendation> Cards { get; }
    public int? TopRecommendedPackIndex => Archetype.TopRecommendedPackIndex;
}

public sealed class TrophyRecommendationEngine(TrophyRecommendationConfiguration? configuration = null)
{
    private readonly TrophyRecommendationConfiguration _configuration = configuration ?? new();
    public TrophyRecommendationResult Recommend(ArchetypeRecommendationResult archetype, LimitedStatisticsContext context,
        CardCatalog cards, SuccessfulDeckCorpus? corpus = null)
    {
        var active = archetype.Profile.Active;
        var data = active is not null && corpus?.Key == new SuccessfulDeckKey(context.Expansion,
            Format(context.Format), active.Definition.Pair) ? new TrophyEvidenceCatalog(corpus) : null;
        var confidence = Math.Clamp((data?.Corpus.Samples.Count ?? 0) / (double)_configuration.FullCorpusConfidenceDecks, 0, 1);
        var result = archetype.Cards.Select(card =>
        {
            var metadata = cards.Find(card.CardIdentifier);
            var evidence = metadata is null ? null : data?.ForName(metadata.Name);
            var eligible = active is not null && metadata is not null && evidence is not null
                && metadata.Colors.Colors.All(active.Definition.Pair.Colors.Contains)
                && (metadata.Colors.Count > 0 || evidence.MainDeckDecks > 0);
            var exposure = Math.Clamp((evidence?.PoolDecks ?? 0) / (double)_configuration.FullCardExposureDecks, 0, 1);
            var diagnostic = active is null ? "Waiting for an active Phase 9C archetype."
                : data is null ? "Exact set/format/pair trophy evidence unavailable."
                : metadata is null ? "Unknown card identity."
                : !eligible ? "No trophy evidence for this card, or requires a third color."
                : data.Corpus.Samples.Count < _configuration.MinimumTrophyCorpusDecks ? "Small corpus; diagnostic evidence only."
                : evidence!.PoolDecks < _configuration.MinimumCardTrophyExposure ? "Small or unknown pool exposure; diagnostic evidence only."
                : TrophyRecommendationResult.DisabledReason;
            return new TrophyCardRecommendation(card, evidence, eligible, confidence, exposure,
                eligible ? active!.Confidence * confidence * exposure : 0, diagnostic);
        });
        return new(archetype, data, _configuration, result);
    }
    public static SuccessfulDeckFormat Format(LimitedStatisticsFormat format) => format switch
    {
        LimitedStatisticsFormat.QuickDraft => SuccessfulDeckFormat.QuickDraft,
        LimitedStatisticsFormat.PremierDraft => SuccessfulDeckFormat.PremierDraft,
        LimitedStatisticsFormat.TraditionalDraft => SuccessfulDeckFormat.TraditionalDraft,
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };
}
