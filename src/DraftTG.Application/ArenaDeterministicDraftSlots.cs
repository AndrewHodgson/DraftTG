using System.Globalization;
using DraftTG.Domain;

namespace DraftTG.Application;

/// <summary>
/// A rectangle in Arena's CLIENT coordinates: physical pixels relative to the client-area origin of Arena's rendering
/// HWND (<c>GetClientRect</c> + <c>ClientToScreen</c>, as in <c>ArenaWindowGeometry</c>). Never screen, window-frame,
/// capture-crop or overlay-local coordinates. Double precision; nothing is rounded before the platform boundary.
/// </summary>
public readonly record struct ArenaClientRectangle(double X, double Y, double Width, double Height)
{
    public double CenterX => X + Width / 2;
    public double CenterY => Y + Height / 2;
    public bool Contains(double x, double y) => x >= X && x < X + Width && y >= Y && y < Y + Height;
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"({X:F1},{Y:F1} {Width:F1}x{Height:F1})");
}

public enum ArenaDraftView { DraftPackGrid, Other }
public enum ArenaWindowMode { Windowed, Fullscreen, Unknown }

/// <summary>What the caller knows about Arena's current view. Only a windowed draft-pack grid has been measured.</summary>
public sealed record ArenaDraftSlotContext(ArenaDraftView View, ArenaWindowMode WindowMode)
{
    public static ArenaDraftSlotContext WindowedDraftPack { get; } = new(ArenaDraftView.DraftPackGrid, ArenaWindowMode.Windowed);
}

public enum ArenaDraftSlotRejection
{
    None, InvalidClientRectangle, UnsupportedCardCount, UnsupportedHeight, UnsupportedAspectRatio, UnsupportedScene, UnsupportedWindowMode
}

/// <summary>Slot rectangles in visual reading order (row-major), one per current card.</summary>
public sealed record ArenaDraftSlotLayout(int ClientWidth, int ClientHeight, int CardCount, double Scale,
    IReadOnlyList<ArenaClientRectangle> Slots);

public sealed record ArenaDraftSlotLayoutResult(ArenaDraftSlotLayout? Layout, ArenaDraftSlotRejection Rejection, string? Reason)
{
    public bool IsAvailable => Layout is not null;
}

/// <summary>
/// Phase 9E.2A deterministic Arena draft-pack slot geometry (UNTAPPED_DRAFTSMITH_LOCALIZATION_AUDIT.md §20). Pure and
/// portable: no Avalonia, Windows API, capture, Scryfall or Player.log access. Measured on 49 real cards at client
/// sizes 1723×1009, 1280×720 and 1920×1080 (RMS 0.40 px; a pre-registered 1920×1080 prediction held to ≤0.7 px).
/// Outside the measured envelope it is Unavailable: it never extrapolates.
/// </summary>
public static class ArenaDraftSlotGeometry
{
    public const int Columns = 5;
    public const double ReferenceClientHeight = 1009.0;
    // Reference-unit constants at client height 1009 (scale s = clientHeight / 1009).
    public const double ColumnOffset = -634.5, ColumnPitch = 183.25, RowOffset = 175.1, RowPitch = 250.0;
    public const double CardWidth = 158.5, CardHeight = 216.8;

    // Validated envelope. Measured aspect ratios: 1723/1009 = 1.708 and 16:9 = 1.778.
    public const int MinimumCards = 13, MaximumCards = 14;
    public const int MinimumClientHeight = 720, MaximumClientHeight = 1080;
    public const double MinimumAspectRatio = 1.70, MaximumAspectRatio = 1.78;

    /// <summary>The formula for 0-based visual slot <paramref name="index"/>, without any envelope check.</summary>
    public static ArenaClientRectangle Slot(int index, int clientWidth, int clientHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        var s = clientHeight / ReferenceClientHeight;
        int column = index % Columns, row = index / Columns; // Row-major; a short last row stays left-aligned.
        return new(clientWidth / 2.0 + s * (ColumnOffset + ColumnPitch * column), s * (RowOffset + RowPitch * row),
            s * CardWidth, s * CardHeight);
    }

    public static ArenaDraftSlotLayoutResult TryCreateLayout(int clientWidth, int clientHeight, int cardCount, ArenaDraftSlotContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        static ArenaDraftSlotLayoutResult Reject(ArenaDraftSlotRejection rejection, string reason) => new(null, rejection, reason);
        if (clientWidth <= 0 || clientHeight <= 0) return Reject(ArenaDraftSlotRejection.InvalidClientRectangle, "invalid client rectangle");
        if (context.View != ArenaDraftView.DraftPackGrid) return Reject(ArenaDraftSlotRejection.UnsupportedScene, "not the draft pack grid");
        if (context.WindowMode != ArenaWindowMode.Windowed)
            return Reject(ArenaDraftSlotRejection.UnsupportedWindowMode, $"window mode {context.WindowMode} outside validated envelope");
        if (cardCount is < MinimumCards or > MaximumCards)
            return Reject(ArenaDraftSlotRejection.UnsupportedCardCount, $"card count {cardCount} outside validated envelope");
        if (clientHeight is < MinimumClientHeight or > MaximumClientHeight)
            return Reject(ArenaDraftSlotRejection.UnsupportedHeight, $"client height {clientHeight} outside validated envelope");
        var aspect = clientWidth / (double)clientHeight;
        if (aspect is < MinimumAspectRatio or > MaximumAspectRatio)
            return Reject(ArenaDraftSlotRejection.UnsupportedAspectRatio,
                string.Create(CultureInfo.InvariantCulture, $"client aspect {aspect:F3} outside validated envelope"));
        var slots = Enumerable.Range(0, cardCount).Select(i => Slot(i, clientWidth, clientHeight)).ToArray();
        return new(new(clientWidth, clientHeight, cardCount, clientHeight / ReferenceClientHeight, Array.AsReadOnly(slots)),
            ArenaDraftSlotRejection.None, null);
    }
}

public enum DeterministicSlotStatus { Available, InvalidRequest, OrderUnavailable, OrderDiscriminating, GeometryUnavailable }

/// <summary>One pack occurrence (duplicates stay distinct) and the slot it is predicted to occupy.</summary>
public sealed record DeterministicSlotAssignment(CardOccurrenceKey Key, int ArenaGrpId, int Slot, ArenaClientRectangle Rectangle);

/// <param name="ArenaLogOrder">Arena GrpIds by pack index; index i is the same occurrence as <c>Pack.AvailableCardIdentifiers[i]</c>.</param>
/// <param name="Prediction">ArenaDisplayOrderModel.Predict over exactly <paramref name="ArenaLogOrder"/>, from evidence recorded before this pack.</param>
public sealed record DeterministicSlotRequest(DraftPack Pack, long PackGeneration, IReadOnlyList<int> ArenaLogOrder,
    ArenaDisplayOrderPrediction Prediction, int ClientWidth, int ClientHeight, ArenaDraftSlotContext Context);

public sealed record DeterministicSlotResult(DeterministicSlotStatus Status, string Reason, DraftPack Pack, long PackGeneration,
    int ClientWidth, int ClientHeight, int CardCount, ArenaDisplayOrderPrediction Prediction, ArenaDraftSlotLayoutResult? Geometry,
    IReadOnlyList<DeterministicSlotAssignment> Assignments)
{
    public bool IsAvailable => Status == DeterministicSlotStatus.Available;

    /// <summary>Same pack, same pack generation and the same client size; P1P1 never serves P1P2 and no result survives a resize.</summary>
    public bool IsSafeFor(DraftPack pack, long packGeneration, int clientWidth, int clientHeight) =>
        IsAvailable && PackGeneration > 0 && PackGeneration == packGeneration && Pack.Equals(pack)
        && ClientWidth == clientWidth && ClientHeight == clientHeight
        && CardCount == pack.AvailableCardIdentifiers.Count && Assignments.Count == CardCount;
}

/// <summary>
/// Phase 9E.2A shadow candidate: Player.log occurrences + a UNANIMOUS Arena display-order prediction + validated slot
/// geometry. No pixels, artwork, OCR or process memory. Never production-authoritative in 9E.2A.
/// </summary>
public static class DeterministicDraftCardLocator
{
    public static DeterministicSlotResult Locate(DeterministicSlotRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var pack = request.Pack; var count = pack.AvailableCardIdentifiers.Count;
        DeterministicSlotResult Result(DeterministicSlotStatus status, string reason, ArenaDraftSlotLayoutResult? geometry = null,
            IReadOnlyList<DeterministicSlotAssignment>? assignments = null) => new(status, reason, pack, request.PackGeneration,
            request.ClientWidth, request.ClientHeight, count, request.Prediction, geometry, assignments ?? []);

        if (request.PackGeneration <= 0) return Result(DeterministicSlotStatus.InvalidRequest, "no live pack generation");
        if (request.ArenaLogOrder.Count != count) return Result(DeterministicSlotStatus.InvalidRequest, "Arena log pack does not match the current pack");
        switch (request.Prediction.Status)
        {
            case ArenaDisplayOrderPredictionStatus.Unavailable:
                return Result(DeterministicSlotStatus.OrderUnavailable, "order unavailable: " + (request.Prediction.Reason ?? "no prediction"));
            case ArenaDisplayOrderPredictionStatus.Discriminating:
                // Never a "most likely" rule: any disagreement among surviving rules disables deterministic placement.
                return Result(DeterministicSlotStatus.OrderDiscriminating, $"order discriminating ({request.Prediction.Orders.Count} predicted orders)");
        }
        var order = request.Prediction.Orders.Count == 1 ? request.Prediction.Orders[0].Order : [];
        if (order.Count != count || !order.Order().SequenceEqual(request.ArenaLogOrder.Order()))
            return Result(DeterministicSlotStatus.InvalidRequest, "prediction is not a permutation of the current pack");
        var geometry = ArenaDraftSlotGeometry.TryCreateLayout(request.ClientWidth, request.ClientHeight, count, request.Context);
        if (geometry.Layout is not { } layout) return Result(DeterministicSlotStatus.GeometryUnavailable, geometry.Reason!, geometry);

        // Copies of one card are visually identical; they take their predicted slots in ascending pack-index order.
        var copies = request.ArenaLogOrder.Select((grpId, index) => (grpId, index)).GroupBy(p => p.grpId)
            .ToDictionary(g => g.Key, g => new Queue<int>(g.Select(p => p.index)));
        var assignments = order.Select((grpId, slot) =>
        {
            var index = copies[grpId].Dequeue();
            return new DeterministicSlotAssignment(new(index, pack.AvailableCardIdentifiers[index]), grpId, slot, layout.Slots[slot]);
        }).ToArray();
        return Result(DeterministicSlotStatus.Available, "unanimous order and validated geometry", geometry, Array.AsReadOnly(assignments));
    }
}

/// <summary>
/// Maps the visual locator's rectangles (normalized to the captured draft crop) into Arena client coordinates. The crop
/// origin and size mirror <c>ArenaDraftCrop.Calculate</c> exactly: rounded fractions of the client size.
/// </summary>
public sealed record VisualCaptureMapping(int CropX, int CropY, int CropWidth, int CropHeight, int ClientWidth, int ClientHeight)
{
    public static VisualCaptureMapping FromAnchor(NormalizedDraftRegion anchor, int clientWidth, int clientHeight) => new(
        (int)Math.Round(anchor.X * clientWidth), (int)Math.Round(anchor.Y * clientHeight),
        (int)Math.Round(anchor.Width * clientWidth), (int)Math.Round(anchor.Height * clientHeight), clientWidth, clientHeight);

    public ArenaClientRectangle ToClient(NormalizedDraftRegion r) =>
        new(CropX + r.X * CropWidth, CropY + r.Y * CropHeight, r.Width * CropWidth, r.Height * CropHeight);

    /// <summary>Client rectangle as a fraction of the capture crop, or null when any part lies outside the crop.</summary>
    public NormalizedDraftRegion? ToCrop(ArenaClientRectangle r)
    {
        var region = new NormalizedDraftRegion((r.X - CropX) / CropWidth, (r.Y - CropY) / CropHeight, r.Width / CropWidth, r.Height / CropHeight);
        return region.IsValid ? region : null;
    }
}

public enum DeterministicShadowStatus
{
    ExactAgreement, GeometryAgreement, OrderMismatch, GeometryMismatch, DeterministicUnavailable, UnsupportedEnvelope, VisualUnavailable, Stale
}

/// <summary>Per occurrence: deterministic slot vs the visually localized rectangle (client px).</summary>
public sealed record DeterministicShadowSlot(CardOccurrenceKey Key, int PredictedSlot, int? ContainingSlot, bool IdentityAgrees,
    ArenaClientRectangle Predicted, ArenaClientRectangle Visual, double CenterDx, double CenterDy)
{
    public double CenterDelta => Math.Sqrt(CenterDx * CenterDx + CenterDy * CenterDy);
}

public sealed record DeterministicShadowComparison(DeterministicShadowStatus Status, string Reason, int Compared, int SlotAgreements,
    bool? ReadingOrderAgrees, double MaxCenterDelta, double RmsCenterDelta, double BiasX, double BiasY, double MaxResidual,
    double RmsResidual, double MedianWidthRatio, double MedianHeightRatio, IReadOnlyList<DeterministicShadowSlot> Slots)
{
    public static DeterministicShadowComparison Unavailable(DeterministicShadowStatus status, string reason) =>
        new(status, reason, 0, 0, null, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, []);
}

/// <summary>
/// Diagnostic comparison of the deterministic candidate against a safe automatic visual result. It never alters
/// placement. Slot membership is geometric (which predicted slot contains each visual centre), not the matcher's
/// proposal-grid index. Duplicate copies agree when the containing slot predicts the same card identity.
/// </summary>
public static class DeterministicSlotShadowComparer
{
    /// <summary>Strong agreement: every visual centre within 3 px of its predicted slot centre.</summary>
    public const double StrongCenterTolerance = 3.0;

    public static DeterministicShadowComparison Compare(DeterministicSlotResult deterministic, CardVisualLocalizationRequest request,
        CardVisualLocalizationResult visual, VisualCaptureMapping mapping)
    {
        if (!deterministic.IsAvailable)
            return DeterministicShadowComparison.Unavailable(deterministic.Status == DeterministicSlotStatus.GeometryUnavailable
                ? DeterministicShadowStatus.UnsupportedEnvelope : DeterministicShadowStatus.DeterministicUnavailable, deterministic.Reason);
        if (!deterministic.IsSafeFor(request.Pack, request.PackGeneration, mapping.ClientWidth, mapping.ClientHeight)
            || visual.PackGeneration != request.PackGeneration || !visual.Pack.Equals(request.Pack)
            || visual.CaptureWidth != mapping.CropWidth || visual.CaptureHeight != mapping.CropHeight)
            return DeterministicShadowComparison.Unavailable(DeterministicShadowStatus.Stale, "pack, generation or client geometry changed");
        var count = deterministic.CardCount;
        if (!visual.IsSafeFor(request) || visual.Matches.Count != count)
            return DeterministicShadowComparison.Unavailable(DeterministicShadowStatus.VisualUnavailable,
                $"visual result not complete and safe ({visual.Matches.Count} / {count})");

        var slots = deterministic.Assignments;
        var byKey = slots.ToDictionary(a => a.Key);
        var rows = visual.Matches.Select(match =>
        {
            var rectangle = mapping.ToClient(match.Rectangle);
            var containing = slots.FirstOrDefault(a => a.Rectangle.Contains(rectangle.CenterX, rectangle.CenterY));
            var agrees = containing is not null && containing.Key.CardIdentifier == match.Key.CardIdentifier;
            var predicted = agrees ? containing!.Rectangle : byKey[match.Key].Rectangle;
            return new DeterministicShadowSlot(match.Key, agrees ? containing!.Slot : byKey[match.Key].Slot, containing?.Slot, agrees,
                predicted, rectangle, rectangle.CenterX - predicted.CenterX, rectangle.CenterY - predicted.CenterY);
        }).OrderBy(s => s.PredictedSlot).ToArray();
        var distinct = rows.Where(r => r.ContainingSlot is not null).Select(r => r.ContainingSlot).Distinct().Count() == rows.Length;
        var agreements = distinct ? rows.Count(r => r.IdentityAgrees) : 0;
        var reading = ArenaDisplayOrderEvidenceGate.ReadingOrder(visual.Matches) is { } ordered
            ? ordered.Select(m => m.Key.CardIdentifier).SequenceEqual(slots.Select(a => a.Key.CardIdentifier)) : (bool?)null;

        var deltas = rows.Select(r => r.CenterDelta).ToArray();
        double biasX = rows.Average(r => r.CenterDx), biasY = rows.Average(r => r.CenterDy);
        var residuals = rows.Select(r => Math.Sqrt(Math.Pow(r.CenterDx - biasX, 2) + Math.Pow(r.CenterDy - biasY, 2))).ToArray();
        static double Rms(double[] values) => Math.Sqrt(values.Average(v => v * v));
        static double Median(IEnumerable<double> values) { var a = values.Order().ToArray(); return a[a.Length / 2]; }
        double max = deltas.Max(), maxResidual = residuals.Max();
        var status = agreements < rows.Length ? DeterministicShadowStatus.OrderMismatch
            : max <= StrongCenterTolerance ? DeterministicShadowStatus.ExactAgreement
            // The artwork matcher's rectangle is a Scryfall portrait-card model registered on the art, not Arena's card
            // holder, so a uniform offset is expected; only the per-slot scatter around it must stay small.
            : maxResidual <= StrongCenterTolerance ? DeterministicShadowStatus.GeometryAgreement
            : DeterministicShadowStatus.GeometryMismatch;
        var reason = status switch
        {
            DeterministicShadowStatus.OrderMismatch => distinct ? $"{rows.Length - agreements} occurrence(s) in a slot predicting another card"
                : "visual centres do not map one-to-one onto predicted slots",
            DeterministicShadowStatus.ExactAgreement => "order agrees; every centre within 3 px",
            DeterministicShadowStatus.GeometryAgreement => "order agrees; uniform offset, per-slot scatter within 3 px",
            _ => "order agrees; per-slot centre scatter above 3 px"
        };
        return new(status, reason, rows.Length, agreements, reading, max, Rms(deltas), biasX, biasY, maxResidual, Rms(residuals),
            Median(rows.Select(r => r.Visual.Width / r.Predicted.Width)), Median(rows.Select(r => r.Visual.Height / r.Predicted.Height)),
            Array.AsReadOnly(rows));
    }
}

/// <summary>Developer rail text for Phase 9E.2A. Presentation only.</summary>
public static class DeterministicSlotPresentation
{
    public static string PredictionLine(string coordinate, DeterministicSlotResult result) => result.Status switch
    {
        DeterministicSlotStatus.Available => $"Deterministic slots: {coordinate} available · {result.CardCount} cards · order unanimous ({result.Prediction.SurvivingRules} rules) · geometry validated",
        _ => $"Deterministic slots: {coordinate} unavailable · {result.Reason}"
    };

    public static string ComparisonLine(string coordinate, DeterministicShadowComparison c) => c.Status switch
    {
        DeterministicShadowStatus.ExactAgreement or DeterministicShadowStatus.GeometryAgreement or DeterministicShadowStatus.GeometryMismatch =>
            string.Create(CultureInfo.InvariantCulture,
                $"Deterministic slots: {coordinate} shadow {c.SlotAgreements}/{c.Compared} order agreement · centre RMS {c.RmsCenterDelta:F1} px · max {c.MaxCenterDelta:F1} px · {Label(c.Status)}"),
        DeterministicShadowStatus.OrderMismatch => $"Deterministic slots: {coordinate} shadow ORDER MISMATCH {c.SlotAgreements}/{c.Compared} · {c.Reason}",
        _ => $"Deterministic slots: {coordinate} {Label(c.Status)} · {c.Reason}"
    };

    public static string Label(DeterministicShadowStatus status) => status switch
    {
        DeterministicShadowStatus.ExactAgreement => "exact agreement",
        DeterministicShadowStatus.GeometryAgreement => "geometry agreement (uniform offset)",
        DeterministicShadowStatus.GeometryMismatch => "geometry mismatch",
        DeterministicShadowStatus.OrderMismatch => "order mismatch",
        DeterministicShadowStatus.DeterministicUnavailable => "deterministic unavailable",
        DeterministicShadowStatus.UnsupportedEnvelope => "unavailable (outside validated envelope)",
        DeterministicShadowStatus.VisualUnavailable => "visual result unavailable",
        _ => "stale"
    };

    public static string Detail(string coordinate, DeterministicSlotResult d, DeterministicShadowComparison c)
    {
        var head = $"{coordinate} · {d.CardCount} cards · client {d.ClientWidth}x{d.ClientHeight} · {d.Status} · {c.Status}";
        if (c.Compared == 0) return head + " · " + c.Reason;
        var reading = c.ReadingOrderAgrees is { } r ? (r ? "agrees" : "differs") : "ambiguous";
        return head + FormattableString.Invariant(
            $" · order {c.SlotAgreements}/{c.Compared} (reading order {reading}) · centre RMS {c.RmsCenterDelta:F1} max {c.MaxCenterDelta:F1} px · offset ({c.BiasX:+0.0;-0.0},{c.BiasY:+0.0;-0.0}) · scatter RMS {c.RmsResidual:F1} max {c.MaxResidual:F1} px · size ratio {c.MedianWidthRatio:F2}x{c.MedianHeightRatio:F2}");
    }
}
