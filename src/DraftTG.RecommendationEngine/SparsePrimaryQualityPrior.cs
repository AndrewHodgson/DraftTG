using DraftTG.Domain;

namespace DraftTG.RecommendationEngine;

public enum ContextualPickScoreVersion { V1_6, V1_6_1, V1_6_2 }
public enum PickScoreQualityProvenance { DirectPrimary, FrontFacePrimary, AlternateFormatPrior, NeutralEstimate, Missing }

/// <summary>Quality-only evidence. Never inserted into primary statistics, lane history or optimizer inputs.</summary>
public sealed record AlternateFormatQualityEvidence(CardIdentifier CardIdentifier,
    LimitedStatisticsContext PrimaryContext, LimitedStatisticsContext SourceContext,
    double RawGameInHandWinRate, int GameInHandGameCount, double SourceEnvironmentBaseline,
    string ProviderName, DateTimeOffset? FetchedAt, string Source, string TimePeriod);

public sealed record AlternateFormatQualityPrior(AlternateFormatQualityEvidence Evidence,
    double PrimaryEnvironmentBaseline, double ShrinkageGames, double SampleWeight,
    double CrossFormatReliability, double EffectiveReliability, double EffectiveQualityValue)
{
    public string Profile => "fra-quick-from-premier-v1";
    public double ShrinkageAmount => 1 - SampleWeight;
}

/// <summary>FRA-only calibration from the two frozen ALL_TIME snapshots. Not a generic Bo1/Bo3/reprint policy.</summary>
public static class SparsePrimaryQualityPrior
{
    public const double Reliability = .9;
    public const double ShrinkageGames = 500;

    public static LimitedStatisticsFormat? SourceFormat(LimitedStatisticsContext context) =>
        string.Equals(context.Expansion, "FRA", StringComparison.OrdinalIgnoreCase)
        && context.Format == LimitedStatisticsFormat.QuickDraft ? LimitedStatisticsFormat.PremierDraft : null;

    public static bool HasUsableGih(LimitedCardStatistics? statistics) => statistics?.GameInHandGameCount is > 0
        && statistics.GameInHandWinRate is { } rate && ValidRate(rate);

    public static AlternateFormatQualityPrior? TryCreate(Card? card, LimitedCardStatistics? primary,
        double primaryBaseline, AlternateFormatQualityEvidence? evidence)
    {
        // Missing primary rows deliberately remain neutral; failed/ambiguous identity joins cannot broaden the policy.
        if (card is null || primary is null || HasUsableGih(primary) || evidence is null
            || primary.CardIdentifier != card.Identifier || evidence.CardIdentifier != card.Identifier
            || !ValidRate(primaryBaseline) || !ValidRate(evidence.SourceEnvironmentBaseline)
            || !ValidRate(evidence.RawGameInHandWinRate) || evidence.GameInHandGameCount <= 0
            || !string.Equals(card.SetCode.Value, evidence.PrimaryContext.Expansion, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(evidence.SourceContext.Expansion, evidence.PrimaryContext.Expansion, StringComparison.OrdinalIgnoreCase)
            || SourceFormat(evidence.PrimaryContext) != evidence.SourceContext.Format
            || evidence.TimePeriod != "ALL_TIME" || evidence.Source is not ("Live" or "Cache" or "StaleCache")) return null;
        var sample = evidence.GameInHandGameCount / (evidence.GameInHandGameCount + ShrinkageGames);
        var reliability = Reliability * sample;
        var effective = Math.Clamp(primaryBaseline + reliability *
            (evidence.RawGameInHandWinRate - evidence.SourceEnvironmentBaseline), 0, 1);
        return new(evidence, primaryBaseline, ShrinkageGames, sample, Reliability, reliability, effective);
    }

    private static bool ValidRate(double rate) => double.IsFinite(rate) && rate is >= 0 and <= 1;
}
