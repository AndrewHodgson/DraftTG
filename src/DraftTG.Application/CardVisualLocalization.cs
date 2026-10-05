using DraftTG.Domain;

namespace DraftTG.Application;

public enum LocalizationConfidence { Unresolved, Ambiguous, HighConfidence, Confirmed }

/// <summary>Coordinates are normalized to the captured draft region, never desktop pixels.</summary>
public sealed record CardVisualMatch(CardOccurrenceKey Key, int VisualSlot, NormalizedDraftRegion Rectangle,
    double Confidence, LocalizationConfidence State, string Method);

public sealed record CardVisualLocalizationRequest(DraftPack Pack,
    IReadOnlyList<CurrentPackCardPresentation> Occurrences, DraftCardLayout CalibratedLayout)
{
    // Zero denotes a standalone/offline request. Live orchestration supplies its immutable pack token.
    public long PackGeneration { get; init; }
}

public sealed record CardVisualLocalizationResult(DraftPack Pack, IReadOnlyList<CardVisualMatch> Matches,
    IReadOnlyList<NormalizedDraftRegion> Rectangles, int CaptureWidth, int CaptureHeight,
    TimeSpan Duration, string Method, string? Diagnostic = null)
{
    public long PackGeneration { get; init; }
    public long CaptureRequestGeneration { get; init; }
    public const double MinimumConfidence = .94;
    public bool IsSafeFor(CardVisualLocalizationRequest request)
    {
        var keys = request.Pack.AvailableCardIdentifiers.Select((id, i) => new CardOccurrenceKey(i, id)).ToHashSet();
        return PackGeneration == request.PackGeneration && Pack.Equals(request.Pack) && Rectangles.Count == keys.Count
            && Rectangles.All(r => r.IsValid) && Matches.Select(m => m.Key).Distinct().Count() == Matches.Count
            && Matches.Select(m => m.VisualSlot).Distinct().Count() == Matches.Count
            && Matches.All(m => keys.Contains(m.Key) && m.VisualSlot >= 0 && m.VisualSlot < Rectangles.Count
                && m.Rectangle == Rectangles[m.VisualSlot] && double.IsFinite(m.Confidence)
                && m.Confidence is >= MinimumConfidence and <= 1
                && m.State is LocalizationConfidence.HighConfidence or LocalizationConfidence.Confirmed)
            && !Matches.Any(a => Matches.Any(b => a.VisualSlot != b.VisualSlot && Overlap(a.Rectangle, b.Rectangle) > .25));
    }
    private static double Overlap(NormalizedDraftRegion a, NormalizedDraftRegion b) =>
        Math.Max(0, Math.Min(a.X + a.Width, b.X + b.Width) - Math.Max(a.X, b.X))
        * Math.Max(0, Math.Min(a.Y + a.Height, b.Y + b.Height) - Math.Max(a.Y, b.Y)) / Math.Min(a.Width * a.Height, b.Width * b.Height);
}

/// <summary>Platform providers capture only Arena's draft region. Recognition is local and read-only.</summary>
public interface ICardVisualLocator
{
    Task<CardVisualLocalizationResult> LocateAsync(CardVisualLocalizationRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Exact deterministic maximum-weight assignment for at most 14 occurrences.</summary>
public static class PackVisualAssignment
{
    public static int[] Solve(double[,] scores, CancellationToken cancellationToken = default)
    {
        var n = scores.GetLength(0);
        if (n is < 1 or > DraftCardLayout.MaximumCards || scores.GetLength(1) != n)
            throw new ArgumentException("Expected a square score matrix of 1–14 occurrences.", nameof(scores));
        if (scores.Cast<double>().Any(s => !double.IsFinite(s)))
            throw new ArgumentException("Scores must be finite.", nameof(scores));
        var best = Enumerable.Repeat(double.NegativeInfinity, 1 << n).ToArray();
        var previous = new int[best.Length];
        best[0] = 0;
        for (var mask = 0; mask < best.Length; mask++)
        {
            if ((mask & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
            var row = System.Numerics.BitOperations.PopCount((uint)mask);
            if (row == n) continue;
            for (var column = 0; column < n; column++)
            {
                if ((mask & (1 << column)) != 0) continue;
                var next = mask | (1 << column);
                var score = best[mask] + scores[row, column];
                if (score <= best[next]) continue;
                best[next] = score;
                previous[next] = column;
            }
        }
        var assignment = new int[n];
        var remaining = best.Length - 1;
        for (var row = n - 1; row >= 0; row--)
        {
            assignment[row] = previous[remaining];
            remaining ^= 1 << assignment[row];
        }
        return assignment;
    }
}
