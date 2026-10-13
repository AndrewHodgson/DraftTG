using DraftTG.Domain;

namespace DraftTG.RecommendationEngine;

/// <summary>Read-only quality-relevance view of existing Phase 9A evidence and pre-candidate projections.</summary>
public sealed record ProjectedColorCommitment(ArchetypeColorPair? Pair, int ProjectedSpells,
    double ColoredSupport, double MaturityEvidence, double EvidenceMaturity, double BestPairProbability,
    double SecondPairProbability, double ProjectionConfidence, double InventoryCoverage, double Strength)
{
    public static ProjectedColorCommitment Open { get; } = new(null, 0, 0, 7, 0, 0, 0, 0, 0, 0);

    public static ProjectedColorCommitment From(ColorCommitmentProfile colors,
        IReadOnlyList<PoolProjectionEvidence> projections, DraftPoolSnapshot pool, int coordinatePicks)
    {
        if (projections.Count == 0 || projections[0].Quality is null) return Open;
        var top = projections[0];
        var best = top.PlayabilityWeight;
        var second = projections.Skip(1).Select(p => p.PlayabilityWeight).DefaultIfEmpty(0).Max();
        var confidence = Math.Clamp(best-second, 0, 1);
        var support = top.Pair.Colors.Colors.Sum(c => colors.For(c).Evidence);
        // Half the existing Phase 9A full-evidence anchor: enough concentrated colored investment to develop
        // commitment within pack one, while projection ambiguity still prevents an early pair lock.
        var maturityEvidence = colors.Configuration.FullCommitmentAfterPicks / 2d;
        var maturity = ContextualPickScoreConfiguration.SmoothStep(support / maturityEvidence);
        var known = Math.Max(0, pool.TotalCardCount-colors.UnresolvedPickCount);
        var expected = Math.Max(coordinatePicks, pool.TotalCardCount + pool.UnresolvedOccurrenceCount);
        var coverage = expected > 0 ? Math.Clamp(known/(double)expected, 0, 1) : 0;
        return new(top.Pair,top.Spells,support,maturityEvidence,maturity,best,second,confidence,coverage,
            Math.Clamp(maturity*confidence*coverage,0,1));
    }
}

public sealed record PoolProjectionEvidence(ArchetypeColorPair Pair, double? Quality, int Spells,
    int Shortfall, double PlayabilityWeight);
