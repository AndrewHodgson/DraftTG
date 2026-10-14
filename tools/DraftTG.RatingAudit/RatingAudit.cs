using DraftTG.Application;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;

namespace DraftTG.RatingAudit;

public enum RatingCoverage { ExactPrimary, SparsePrimary, AlternateFormatPrior, NeutralNoUsableGIH,
    MissingIdentity, AmbiguousIdentity, MissingStatRow, NullGIH, UnsupportedFormat }
public sealed record RatingAuditRow(string? Identifier, int? ArenaGrpId, string? Set, string? Collector,
    string Name, string? Rarity, string[] Colors, RatingCoverage Coverage, double? Q, double? PrimaryGIH,
    int? PrimaryGames, double? PrimaryBaseline, double? AdjustedValue, string QProvenance,
    bool Fallback, string? FallbackFormat, double? AlternateGIH, int? AlternateGames,
    AlternateFormatQualityPrior? SparsePrior, bool NeutralFallback, bool IdentityMissing, bool IdentityAmbiguous,
    bool StatRowMissing, bool GihNull, bool SampleCountMissing, string[] Risks)
{
    public bool PrimaryRowPresent { get; init; }
    public bool AlternateRowPresent { get; init; }
    public double? AlternateBaseline { get; init; }
    public bool IsBasicLand { get; init; }
    public string? ProvenLogicalIdentity { get; init; }
}

public static class RatingAuditClassifier
{
    public static RatingAuditRow Create(Card? card, int? grpId, ArenaCardResolutionStatus identity,
        StatisticalIdentityResolution? mapping, LimitedCardStatistics? stats, CardRecommendation? recommendation,
        double? baseline, AlternateFormatQualityPrior? prior)
    {
        var ambiguous = identity == ArenaCardResolutionStatus.Ambiguous || mapping?.Kind is
            StatisticalIdentityKind.ConflictingProviderRows or StatisticalIdentityKind.AmbiguousMultifaceAlias;
        var missing = card is null && !ambiguous;
        var usable = SparsePrimaryQualityPrior.HasUsableGih(stats) && recommendation?.AdjustedValue is not null;
        var state = ambiguous ? RatingCoverage.AmbiguousIdentity : missing ? RatingCoverage.MissingIdentity
            : baseline is null ? RatingCoverage.NeutralNoUsableGIH : prior is not null ? RatingCoverage.AlternateFormatPrior
            : usable ? stats!.GameInHandGameCount < 500 ? RatingCoverage.SparsePrimary : RatingCoverage.ExactPrimary
            : stats is null ? RatingCoverage.MissingStatRow : stats.GameInHandWinRate is null ? RatingCoverage.NullGIH : RatingCoverage.NeutralNoUsableGIH;
        var q = baseline is { } b && card is not null
            ? prior is not null ? new ContextualPickScoreConfiguration().NormalizeQuality(prior.EffectiveQualityValue, b)
                : usable ? new ContextualPickScoreConfiguration().NormalizeQuality(recommendation!.AdjustedValue!.Value, b) : .5
            : (double?)null;
        var risks = new List<string>();
        if (missing) risks.Add("MissingIdentity"); if (ambiguous) risks.Add("AmbiguousIdentity");
        if (stats is null) risks.Add("MissingStatRow"); else
        {
            if (stats.GameInHandWinRate is null) risks.Add("NullGIH");
            if (stats.GameInHandGameCount is null) risks.Add("MissingSampleCount");
            else if (stats.GameInHandGameCount < 500) risks.Add("SparseStats");
        }
        if (prior is not null) risks.Add("AlternateFormatPrior");
        if (!usable && prior is null) risks.Add("NoUsableQualityEvidence");
        if (baseline is null) risks.Add("NoEnvironmentBaseline");
        return new(card?.Identifier.Value, grpId, card?.SetCode.Value, card?.CollectorNumber.Value, card?.Name ?? "<unresolved>",
            card?.Rarity.ToString(), card?.Colors.Colors.Select(x => x.ToString()).ToArray() ?? [], state, q,
            stats?.GameInHandWinRate, stats?.GameInHandGameCount, baseline, recommendation?.AdjustedValue,
            prior is not null ? "AlternateFormatPrior" : usable ? mapping?.Kind == StatisticalIdentityKind.MultifaceFrontFace
                ? "FrontFacePrimary" : "DirectPrimary" : baseline is null ? "Missing" : "NeutralEstimate",
            prior is not null, prior?.Evidence.SourceContext.Format.ToString(), prior?.Evidence.RawGameInHandWinRate,
            prior?.Evidence.GameInHandGameCount, prior, !usable && prior is null, missing, ambiguous,
            stats is null, stats is not null && stats.GameInHandWinRate is null,
            stats is not null && stats.GameInHandGameCount is null, risks.ToArray());
    }
}

public static class RatingRankComparison
{
    public static object Pack(IReadOnlyList<double> contextualScores, IReadOnlyList<double> referenceRatings)
    {
        if(contextualScores.Count!=referenceRatings.Count || contextualScores.Count==0) throw new ArgumentException("Complete same-pack occurrences required.");
        var left=Enumerable.Range(0,contextualScores.Count).OrderByDescending(i=>contextualScores[i]).ThenBy(i=>i).ToArray();
        var right=Enumerable.Range(0,referenceRatings.Count).OrderByDescending(i=>referenceRatings[i]).ThenBy(i=>i).ToArray();
        var top=Math.Min(3,left.Length);
        return new { Label="Secondary: external static grade vs DraftTG final contextual score; distinct concepts.",
            Top1Agreement=left[0]==right[0],Top3Overlap=left.Take(top).Intersect(right.Take(top)).Count()/(double)top,
            Spearman=Spearman(contextualScores,referenceRatings),TieBreak="Observed pack occurrence order; ties retained in Spearman midranks.",
            Differences=Enumerable.Range(0,left.Length).Where(i=>Math.Abs(Array.IndexOf(left,i)-Array.IndexOf(right,i))>=3)
                .Select(i=>new {Index=i,DraftTGRank=Array.IndexOf(left,i)+1,ReferenceRank=Array.IndexOf(right,i)+1}).ToArray() };
    }
    // Midrank ties, [0,1] percentiles; comparable scope must be explicitly reported by the caller.
    public static double[] Percentiles(IReadOnlyList<double> values)
    {
        if (values.Any(x => !double.IsFinite(x))) throw new ArgumentException("Finite ratings required.");
        if (values.Count == 0) return [];
        if (values.Count == 1) return [.5];
        return values.Select(v => (values.Count(x => x < v) + (values.Count(x => x == v) - 1) / 2d) / (values.Count - 1)).ToArray();
    }
    public static double? Spearman(IReadOnlyList<double> left, IReadOnlyList<double> right)
    {
        if (left.Count != right.Count) throw new ArgumentException("Same matched identities required.");
        if (left.Count < 2) return null;
        var x = Percentiles(left); var y = Percentiles(right); var mx = x.Average(); var my = y.Average();
        var denominator = Math.Sqrt(x.Sum(v => (v-mx)*(v-mx)) * y.Sum(v => (v-my)*(v-my)));
        return denominator == 0 ? null : x.Zip(y, (a,b) => (a-mx)*(b-my)).Sum() / denominator;
    }
}
