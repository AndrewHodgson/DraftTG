using DraftTG.Domain;

namespace DraftTG.RecommendationEngine;

public sealed record ContextualCardRecommendation(CardRecommendation StatisticalRecommendation,
    ColorSet? Colors, double ColorFit, double ColorAdjustment, double? ContextualValue,
    int? ContextualRank, bool IsTopContextualCandidate)
{
    public int PackIndex => StatisticalRecommendation.PackIndex;
    public CardIdentifier CardIdentifier => StatisticalRecommendation.CardIdentifier;
}

public sealed class ContextualDraftRecommendation
{
    internal ContextualDraftRecommendation(DraftRecommendation statistical, ColorCommitmentProfile profile,
        IEnumerable<ContextualCardRecommendation> cards)
    {
        StatisticalRecommendation = statistical;
        Profile = profile;
        Cards = Array.AsReadOnly(cards.ToArray());
    }
    public DraftRecommendation StatisticalRecommendation { get; }
    public ColorCommitmentProfile Profile { get; }
    public IReadOnlyList<ContextualCardRecommendation> Cards { get; }
    public int? TopRecommendedPackIndex => Cards.FirstOrDefault(c => c.IsTopContextualCandidate)?.PackIndex;
}

/// <summary>Composes with Phase 8, without altering its independently reproducible result.</summary>
public sealed class ContextualRecommendationEngine(ColorCommitmentConfiguration? configuration = null)
{
    private readonly ColorCommitmentConfiguration _configuration = configuration ?? new();

    public ContextualDraftRecommendation Recommend(DraftSnapshot snapshot, CardCatalog catalog,
        DraftRecommendation statistical)
    {
        if (!snapshot.CurrentPack.AvailableCardIdentifiers.SequenceEqual(statistical.Cards.Select(c => c.CardIdentifier))
            || statistical.Cards.Where((c, index) => c.PackIndex != index).Any())
            throw new ArgumentException("The statistical result must match the ordered current pack.", nameof(statistical));
        var profile = ColorCommitmentProfile.From(snapshot.DraftedPool, catalog, _configuration);
        var cards = statistical.Cards.Select(card =>
        {
            var colors = catalog.Find(card.CardIdentifier)?.Colors;
            // Unknown metadata is neutral and remains explicitly distinguishable from colorless.
            var fit = colors is { } known ? profile.Fit(known) : 0;
            var adjustment = fit * _configuration.MaxColorAdjustment;
            return new ContextualCardRecommendation(card, colors, fit, adjustment,
                card.AdjustedValue is { } value ? AdjustValue(value, fit) : null, null, false);
        }).ToArray();
        var ranked = cards.Where(c => c.ContextualValue is not null)
            .OrderByDescending(c => c.ContextualValue)
            .ThenByDescending(c => c.StatisticalRecommendation.AdjustedValue)
            .ThenByDescending(c => c.StatisticalRecommendation.GamesInHandSampleCount)
            .ThenBy(c => c.PackIndex).ToArray();
        if (ranked.Length >= 2)
            for (var index = 0; index < ranked.Length; index++)
                cards[ranked[index].PackIndex] = ranked[index] with
                { ContextualRank = index + 1, IsTopContextualCandidate = index == 0 };
        return new(statistical, profile, cards);
    }

    public double AdjustValue(double statisticalValue, double colorFit) =>
        Math.Clamp(statisticalValue + colorFit * _configuration.MaxColorAdjustment, 0, 1);
}
