using DraftTG.Application;
using DraftTG.Domain;
using SkiaSharp;

namespace DraftTG.App;

internal sealed record VerifiedOrderCandidate(IReadOnlyList<int> Order, FixedSlotVerificationResult Verification);
internal sealed record FixedSlotOrderEvidenceResult(ArenaDisplayOrderPrediction PredictionBefore,
    IReadOnlyList<VerifiedOrderCandidate> Candidates, string? DeclineReason = null)
{
    public int VerifiedCount(int count) => Candidates.Count(c => c.Verification.FullyVerifies(count));
}

/// <summary>Pixels confirm distinct, frozen predictions. This seam produces evidence, never placement.</summary>
internal static class FixedSlotOrderEvidence
{
    // Eight orders bound the worst-case pixel work; exceeding the limit declines the entire attempt.
    public const int MaximumCandidateOrders = 8;

    public static FixedSlotOrderEvidenceResult Verify(CardVisualLocalizationRequest request, IReadOnlyList<int> logOrder,
        ArenaDisplayOrderPrediction predictionBefore, int clientWidth, int clientHeight, ArenaDraftSlotContext context,
        VisualCaptureMapping mapping, SKBitmap frame, IReadOnlyDictionary<CardIdentifier, SKBitmap> references,
        CancellationToken token = default) => VerifyCandidates(predictionBefore, order =>
        {
            var candidate = DeterministicDraftCardLocator.Locate(new(request.Pack, request.PackGeneration, logOrder,
                new(ArenaDisplayOrderPredictionStatus.Unanimous, [new(order, 0)], predictionBefore.SurvivingRules),
                clientWidth, clientHeight, context));
            if (!candidate.IsSafeFor(request.Pack, request.PackGeneration, clientWidth, clientHeight))
                return new([], TimeSpan.Zero, candidate.Reason);
            if (mapping.ClientWidth != clientWidth || mapping.ClientHeight != clientHeight
                || mapping.CropWidth != frame.Width || mapping.CropHeight != frame.Height)
                return new([], TimeSpan.Zero, "capture geometry changed");
            var regions = candidate.Assignments.Select(a => mapping.ToCrop(a.Rectangle)).ToArray();
            if (regions.Any(r => r is null)) return new([], TimeSpan.Zero, "slot outside capture");
            return new CardTemplateRecognizer().VerifyFixedSlots(request, frame, references,
                candidate.Assignments.Select((a, i) => (a.Key.PackIndex, regions[i]!)).ToArray(), token);
        });

    internal static FixedSlotOrderEvidenceResult VerifyCandidates(ArenaDisplayOrderPrediction predictionBefore,
        Func<IReadOnlyList<int>, FixedSlotVerificationResult> verify)
    {
        // Copy before any verifier callback (and before recording); rule votes never select an order.
        var orders = predictionBefore.Orders.GroupBy(o => string.Join(",", o.Order), StringComparer.Ordinal)
            .Select(g => new ArenaPredictedOrder(Array.AsReadOnly(g.First().Order.ToArray()), g.Sum(o => o.RuleCount))).ToArray();
        var snapshot = predictionBefore with { Orders = Array.AsReadOnly(orders) };
        if (snapshot.Status == ArenaDisplayOrderPredictionStatus.Unavailable || orders.Length == 0)
            return new(snapshot, [], snapshot.Reason ?? "no candidate orders");
        if (orders.Length > MaximumCandidateOrders) return new(snapshot, [], $"candidate limit exceeded ({orders.Length} > {MaximumCandidateOrders})");
        return new(snapshot, orders.Select(o => new VerifiedOrderCandidate(o.Order, verify(o.Order))).ToArray());
    }

    public static ArenaDisplayOrderGateResult Gate(CardVisualLocalizationRequest request, IReadOnlyList<int> logOrder,
        FixedSlotOrderEvidenceResult result, long currentGeneration, DraftPack? currentPack,
        bool currentGeometryAndFrame, bool manualPlacementInvolved)
    {
        if (manualPlacementInvolved) return ArenaDisplayOrderGateResult.Reject("manual placement involved");
        if (!currentGeometryAndFrame || request.PackGeneration <= 0 || request.PackGeneration != currentGeneration
            || currentPack is null || !request.Pack.Equals(currentPack)) return ArenaDisplayOrderGateResult.Reject("stale pack, geometry or frame");
        var count = request.Pack.AvailableCardIdentifiers.Count;
        if (logOrder.Count != count || request.Occurrences.Count != count || result.DeclineReason is not null
            || request.Occurrences.Where((c, i) => c.Key != new CardOccurrenceKey(i, request.Pack.AvailableCardIdentifiers[i])).Any()
            || result.Candidates.Count != result.PredictionBefore.Orders.Count || result.Candidates.Count > MaximumCandidateOrders
            || result.Candidates.Select(c => string.Join(",", c.Order)).Distinct().Count() != result.Candidates.Count
            || result.Candidates.Where((c, i) => !c.Order.SequenceEqual(result.PredictionBefore.Orders[i].Order)
                || !c.Order.Order().SequenceEqual(logOrder.Order())).Any())
            return ArenaDisplayOrderGateResult.Reject(result.DeclineReason ?? "incomplete candidate verification");
        if (request.Occurrences.GroupBy(c => c.CardIdentifier).Any(g => g.Select(c => logOrder[c.Key.PackIndex]).Distinct().Count() > 1))
            return ArenaDisplayOrderGateResult.Reject("unresolved duplicate identity across Arena IDs");
        var verified = result.Candidates.Where(c => c.Verification.FullyVerifies(count)).ToArray();
        if (verified.Length != 1) return ArenaDisplayOrderGateResult.Reject($"{verified.Length} fully verified candidate orders");
        var winner = verified[0];
        // Bind the occurrence at each slot back to the Arena log occurrence, including duplicate copies.
        if (winner.Verification.Slots.Any(s => logOrder[s.ExpectedOccurrence] != winner.Order[s.Slot]))
            return ArenaDisplayOrderGateResult.Reject("unresolved occurrence assignment");
        return new(true, "unique full pixel verification", winner.Order,
            winner.Verification.Slots.Min(s => s.ExpectedScore), winner.Verification.Slots.Average(s => s.ExpectedScore));
    }
}
