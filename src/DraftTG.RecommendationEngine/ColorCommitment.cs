using DraftTG.Domain;

namespace DraftTG.RecommendationEngine;

/// <summary>Transparent product heuristics for Phase 9A; not statistically optimal constants.</summary>
public sealed record ColorCommitmentConfiguration
{
    public ColorCommitmentConfiguration(double evidenceScale = 4, int fullCommitmentAfterPicks = 14,
        double maxColorAdjustment = 0.025)
    {
        if (!double.IsFinite(evidenceScale) || evidenceScale <= 0)
            throw new ArgumentOutOfRangeException(nameof(evidenceScale));
        if (fullCommitmentAfterPicks <= 0) throw new ArgumentOutOfRangeException(nameof(fullCommitmentAfterPicks));
        if (!double.IsFinite(maxColorAdjustment) || maxColorAdjustment < 0 || maxColorAdjustment > 1)
            throw new ArgumentOutOfRangeException(nameof(maxColorAdjustment));
        EvidenceScale = evidenceScale;
        FullCommitmentAfterPicks = fullCommitmentAfterPicks;
        MaxColorAdjustment = maxColorAdjustment;
    }

    public double EvidenceScale { get; }
    public int FullCommitmentAfterPicks { get; }
    public double MaxColorAdjustment { get; }
    public string ModelVersion => "pool-color-v1";
}

public sealed record ColorCommitmentEvidence(MagicColor Color, double Evidence, double RawSupport,
    double EffectiveSupport);

public sealed class ColorCommitmentProfile
{
    private ColorCommitmentProfile(DraftedCardPool pool, CardCatalog catalog, ColorCommitmentConfiguration configuration)
    {
        Configuration = configuration;
        CompletedPickCount = pool.Count;
        ProgressFactor = Math.Clamp((double)CompletedPickCount / configuration.FullCommitmentAfterPicks, 0, 1);
        var evidence = new double[5];
        foreach (var identifier in pool.CardIdentifiers)
        {
            var card = catalog.Find(identifier);
            if (card is null) { UnresolvedPickCount++; continue; }
            foreach (var color in card.Colors.Colors) evidence[(int)color] += 1.0 / card.Colors.Count;
        }
        MeanEvidence = evidence.Sum() / 5;
        Colors = Array.AsReadOnly(Enum.GetValues<MagicColor>().Select(color =>
        {
            var raw = Math.Clamp((evidence[(int)color] - MeanEvidence) / configuration.EvidenceScale, -1, 1);
            return new ColorCommitmentEvidence(color, evidence[(int)color], raw, raw * ProgressFactor);
        }).ToArray());
        var supported = Colors.Where(c => c.Evidence > 0).OrderByDescending(c => c.Evidence).ThenBy(c => c.Color).ToArray();
        PrimaryColor = supported.FirstOrDefault()?.Color;
        SecondaryColor = supported.Skip(1).FirstOrDefault()?.Color;
        CommitmentStrength = Colors.Max(c => c.EffectiveSupport);
    }

    public static ColorCommitmentProfile From(DraftHistory history, CardCatalog catalog,
        ColorCommitmentConfiguration? configuration = null) => From(new DraftedCardPool(history.SelectedCardIdentifiers), catalog, configuration);
    public static ColorCommitmentProfile From(DraftedCardPool pool, CardCatalog catalog,
        ColorCommitmentConfiguration? configuration = null) => new(pool, catalog, configuration ?? new());
    public ColorCommitmentConfiguration Configuration { get; }
    public IReadOnlyList<ColorCommitmentEvidence> Colors { get; }
    public ColorCommitmentEvidence For(MagicColor color) => Colors[(int)color];
    public int CompletedPickCount { get; }
    public int UnresolvedPickCount { get; }
    public double MeanEvidence { get; }
    public double ProgressFactor { get; }
    public double CommitmentStrength { get; }
    public MagicColor? PrimaryColor { get; }
    public MagicColor? SecondaryColor { get; }
    public double Fit(ColorSet colors) => colors.IsColorless ? 0 : colors.Colors.Min(c => For(c).EffectiveSupport);
}
