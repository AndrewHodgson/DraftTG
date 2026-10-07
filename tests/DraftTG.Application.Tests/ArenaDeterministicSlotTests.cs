using DraftTG.Application;
using DraftTG.Domain;

namespace DraftTG.Application.Tests;

/// <summary>Phase 9E.2A deterministic slot geometry, locator and shadow comparison (shadow mode only).</summary>
public sealed class ArenaDeterministicSlotTests
{
    private static readonly ArenaDraftSlotContext Windowed = ArenaDraftSlotContext.WindowedDraftPack;

    // Real card rectangles measured in UNTAPPED_DRAFTSMITH_LOCALIZATION_AUDIT.md §20 (client px; holder-edge method).
    [Theory]
    [InlineData(1723, 1009, 1, 410, 175, 159, 216)]   // P1P2, 13 cards
    [InlineData(1723, 1009, 9, 960, 425, 158, 217)]
    [InlineData(1723, 1009, 13, 777, 675, 158, 217)]  // P1P1, 14 cards: last row, column 3
    [InlineData(1280, 720, 0, 187, 125, 113, 154)]
    [InlineData(1280, 720, 12, 449, 482, 113, 155)]
    [InlineData(1920, 1080, 4, 1065, 187, 170, 232)]  // pre-registered out-of-sample size
    [InlineData(1920, 1080, 10, 281, 723, 170, 232)]
    public void FormulaReproducesAuditMeasurements(int width, int height, int index, double x, double y, double w, double h)
    {
        var slot = ArenaDraftSlotGeometry.Slot(index, width, height);
        Assert.InRange(slot.X - x, -1.0, 1.0); Assert.InRange(slot.Y - y, -1.0, 1.0);
        Assert.InRange(slot.Width - w, -1.5, 1.5); Assert.InRange(slot.Height - h, -1.5, 1.5);
    }

    [Fact]
    public void GridIsRowMajorWithLeftAlignedLastRowAndIsRepeatable()
    {
        foreach (var count in new[] { 13, 14 })
        {
            var layout = ArenaDraftSlotGeometry.TryCreateLayout(1723, 1009, count, Windowed).Layout!;
            Assert.Equal(count, layout.Slots.Count);
            for (var i = 10; i < count; i++) // Short last row starts at column 0, not centred.
            {
                Assert.Equal(layout.Slots[i - 10].X, layout.Slots[i].X, 9);
                Assert.Equal(layout.Slots[i - 5].Y + 250.0, layout.Slots[i].Y, 9);
            }
            Assert.Equal(layout.Slots, ArenaDraftSlotGeometry.TryCreateLayout(1723, 1009, count, Windowed).Layout!.Slots);
        }
        // Double precision is preserved until the render boundary.
        Assert.NotEqual(Math.Round(ArenaDraftSlotGeometry.Slot(0, 1366, 768).X), ArenaDraftSlotGeometry.Slot(0, 1366, 768).X);
    }

    [Theory]
    [InlineData(1723, 1009, 13, ArenaDraftView.DraftPackGrid, ArenaWindowMode.Windowed, ArenaDraftSlotRejection.None)]
    [InlineData(1280, 720, 14, ArenaDraftView.DraftPackGrid, ArenaWindowMode.Windowed, ArenaDraftSlotRejection.None)]
    [InlineData(1920, 1080, 12, ArenaDraftView.DraftPackGrid, ArenaWindowMode.Windowed, ArenaDraftSlotRejection.UnsupportedCardCount)]
    [InlineData(1920, 1080, 15, ArenaDraftView.DraftPackGrid, ArenaWindowMode.Windowed, ArenaDraftSlotRejection.UnsupportedCardCount)]
    [InlineData(1024, 576, 14, ArenaDraftView.DraftPackGrid, ArenaWindowMode.Windowed, ArenaDraftSlotRejection.UnsupportedHeight)]
    [InlineData(2048, 1152, 14, ArenaDraftView.DraftPackGrid, ArenaWindowMode.Windowed, ArenaDraftSlotRejection.UnsupportedHeight)]
    [InlineData(1440, 900, 14, ArenaDraftView.DraftPackGrid, ArenaWindowMode.Windowed, ArenaDraftSlotRejection.UnsupportedAspectRatio)]
    [InlineData(2520, 1080, 14, ArenaDraftView.DraftPackGrid, ArenaWindowMode.Windowed, ArenaDraftSlotRejection.UnsupportedAspectRatio)]
    [InlineData(1920, 1080, 14, ArenaDraftView.DraftPackGrid, ArenaWindowMode.Fullscreen, ArenaDraftSlotRejection.UnsupportedWindowMode)]
    [InlineData(1920, 1080, 14, ArenaDraftView.Other, ArenaWindowMode.Windowed, ArenaDraftSlotRejection.UnsupportedScene)]
    [InlineData(0, 1080, 14, ArenaDraftView.DraftPackGrid, ArenaWindowMode.Windowed, ArenaDraftSlotRejection.InvalidClientRectangle)]
    public void EnvelopeFailsClosedOutsideMeasuredConditions(int width, int height, int count, ArenaDraftView view, ArenaWindowMode mode,
        ArenaDraftSlotRejection expected)
    {
        var result = ArenaDraftSlotGeometry.TryCreateLayout(width, height, count, new(view, mode));
        Assert.Equal(expected, result.Rejection);
        Assert.Equal(expected == ArenaDraftSlotRejection.None, result.IsAvailable);
    }

    private static (DraftPack Pack, int[] Log) Pack(int count, int pick = 2, int duplicateOf = -1)
    {
        var log = Enumerable.Range(0, count).Select(i => i == count - 1 && duplicateOf >= 0 ? 100 + duplicateOf : 100 + i).ToArray();
        var pack = new DraftPack(new(PackNumber.Create(1), PickNumber.Create(pick)), log.Select(id => CardIdentifier.Create($"card-{id}")));
        return (pack, log);
    }

    private static ArenaDisplayOrderPrediction Unanimous(IReadOnlyList<int> order) =>
        new(ArenaDisplayOrderPredictionStatus.Unanimous, [new ArenaPredictedOrder(order, 6)], 6);

    private static DeterministicSlotResult Locate(DraftPack pack, int[] log, ArenaDisplayOrderPrediction prediction, long generation = 7,
        int width = 1723, int height = 1009) =>
        DeterministicDraftCardLocator.Locate(new(pack, generation, log, prediction, width, height, Windowed));

    [Fact]
    public void UnanimousOrderIsZippedToSlotsInPredictedOrder()
    {
        var (pack, log) = Pack(13);
        var order = log.Reverse().ToArray();
        var result = Locate(pack, log, Unanimous(order));
        Assert.Equal(DeterministicSlotStatus.Available, result.Status);
        Assert.Equal(order, result.Assignments.Select(a => a.ArenaGrpId));
        Assert.Equal(Enumerable.Range(0, 13), result.Assignments.Select(a => a.Slot));
        Assert.All(result.Assignments, a => Assert.Equal(pack.AvailableCardIdentifiers[a.Key.PackIndex], a.Key.CardIdentifier));
        Assert.Equal(ArenaDraftSlotGeometry.Slot(12, 1723, 1009), result.Assignments[12].Rectangle);
    }

    [Theory]
    [InlineData(ArenaDisplayOrderPredictionStatus.Discriminating, DeterministicSlotStatus.OrderDiscriminating)]
    [InlineData(ArenaDisplayOrderPredictionStatus.Unavailable, DeterministicSlotStatus.OrderUnavailable)]
    public void NonUnanimousOrderNeverProducesSlots(ArenaDisplayOrderPredictionStatus status, DeterministicSlotStatus expected)
    {
        var (pack, log) = Pack(14);
        // No "most likely" rule: even a 99-to-1 split is rejected.
        var prediction = status == ArenaDisplayOrderPredictionStatus.Discriminating
            ? new ArenaDisplayOrderPrediction(status, [new(log, 99), new(log.Reverse().ToArray(), 1)], 100)
            : ArenaDisplayOrderPrediction.Unavailable("Arena card database not found.");
        var result = Locate(pack, log, prediction);
        Assert.Equal(expected, result.Status);
        Assert.Empty(result.Assignments);
        Assert.False(result.IsSafeFor(pack, 7, 1723, 1009));
    }

    [Fact]
    public void UnanimousOrderStillFailsClosedOutsideGeometryEnvelope()
    {
        var (pack, log) = Pack(12);
        var result = Locate(pack, log, Unanimous(log));
        Assert.Equal(DeterministicSlotStatus.GeometryUnavailable, result.Status);
        Assert.Equal(ArenaDraftSlotRejection.UnsupportedCardCount, result.Geometry!.Rejection);
        Assert.Empty(result.Assignments);
    }

    [Fact]
    public void DuplicateCopiesStayDistinctOccurrences()
    {
        var (pack, log) = Pack(13, duplicateOf: 3); // Pack indices 3 and 12 are both GrpId 103.
        var order = new[] { 103, 100, 101, 102, 103, 104, 105, 106, 107, 108, 109, 110, 111 };
        var result = Locate(pack, log, Unanimous(order));
        var copies = result.Assignments.Where(a => a.ArenaGrpId == 103).ToArray();
        Assert.Equal([(3, 0), (12, 4)], copies.Select(a => (a.Key.PackIndex, a.Slot)));
        Assert.Equal(13, result.Assignments.Select(a => a.Key).Distinct().Count());
    }

    [Fact]
    public void ResultIsBoundToPackGenerationAndClientGeometry()
    {
        var (pack, log) = Pack(14);
        var result = Locate(pack, log, Unanimous(log), generation: 7);
        Assert.True(result.IsSafeFor(pack, 7, 1723, 1009));
        Assert.False(result.IsSafeFor(pack, 8, 1723, 1009));                       // newer pack generation
        Assert.False(result.IsSafeFor(Pack(14, pick: 3).Pack, 7, 1723, 1009));     // P1P2 result offered for P1P3
        Assert.False(result.IsSafeFor(pack, 7, 1280, 720));                        // window resized after calculation
        Assert.Equal(DeterministicSlotStatus.InvalidRequest, Locate(pack, log, Unanimous(log), generation: 0).Status);
    }

    // ---- Shadow comparison -------------------------------------------------------------------------------------

    private static readonly VisualCaptureMapping Crop =
        VisualCaptureMapping.FromAnchor(new(200 / 1723d, 150 / 1009d, 1000 / 1723d, 800 / 1009d), 1723, 1009);

    private static (CardVisualLocalizationRequest Request, CardVisualLocalizationResult Visual) Visual(DraftPack pack,
        IReadOnlyList<(CardOccurrenceKey Key, ArenaClientRectangle Rectangle)> placed, long generation = 7)
    {
        var rectangles = placed.Select(p => Crop.ToCrop(p.Rectangle)!).ToArray();
        var matches = placed.Select((p, slot) => new CardVisualMatch(p.Key, slot, rectangles[slot], .97, LocalizationConfidence.HighConfidence, "test")).ToArray();
        var request = new CardVisualLocalizationRequest(pack, pack.AvailableCardIdentifiers.Select((id, i) => new CurrentPackCardPresentation(new(i, id))).ToArray(),
            DraftCardLayout.Grid(5)) { PackGeneration = 7 };
        return (request, new(pack, matches, rectangles, Crop.CropWidth, Crop.CropHeight, TimeSpan.Zero, "test") { PackGeneration = generation });
    }

    [Theory]
    [InlineData("exact", DeterministicShadowStatus.ExactAgreement)]
    [InlineData("uniform-offset", DeterministicShadowStatus.GeometryAgreement)]
    [InlineData("scatter", DeterministicShadowStatus.GeometryMismatch)]
    [InlineData("swapped", DeterministicShadowStatus.OrderMismatch)]
    [InlineData("stale", DeterministicShadowStatus.Stale)]
    public void ShadowComparisonClassifiesAgreement(string scenario, DeterministicShadowStatus expected)
    {
        var (pack, log) = Pack(13);
        var deterministic = Locate(pack, log, Unanimous(log.Reverse().ToArray()));
        var placed = deterministic.Assignments.Select(a => (a.Key, Rectangle: scenario switch
        {
            "uniform-offset" => a.Rectangle with { X = a.Rectangle.X + 6, Y = a.Rectangle.Y - 5 },
            "scatter" => a.Rectangle with { X = a.Rectangle.X + (a.Slot % 2 == 0 ? 5 : -5) },
            _ => a.Rectangle
        })).ToList();
        if (scenario == "swapped") (placed[0], placed[1]) = ((placed[1].Key, placed[0].Rectangle), (placed[0].Key, placed[1].Rectangle));
        var (request, visual) = Visual(pack, placed, generation: scenario == "stale" ? 8 : 7);

        var comparison = DeterministicSlotShadowComparer.Compare(deterministic, request, visual, Crop);

        Assert.Equal(expected, comparison.Status);
        if (expected == DeterministicShadowStatus.Stale) return;
        Assert.Equal(13, comparison.Compared);
        Assert.Equal(expected == DeterministicShadowStatus.OrderMismatch ? 11 : 13, comparison.SlotAgreements);
        if (expected == DeterministicShadowStatus.ExactAgreement) Assert.True(comparison.MaxCenterDelta < 1e-6);
        if (expected == DeterministicShadowStatus.GeometryAgreement) { Assert.Equal(6, comparison.BiasX, 6); Assert.True(comparison.MaxResidual < 1e-6); }
    }

    [Fact]
    public void ShadowComparisonReportsUnavailableDeterministicPathWithoutComparing()
    {
        var (pack, log) = Pack(12);
        var deterministic = Locate(pack, log, Unanimous(log));
        var (request, visual) = Visual(pack, []);
        var comparison = DeterministicSlotShadowComparer.Compare(deterministic, request, visual, Crop);
        Assert.Equal(DeterministicShadowStatus.UnsupportedEnvelope, comparison.Status);
        Assert.Equal(0, comparison.Compared);
        Assert.Contains("card count 12", DeterministicSlotPresentation.ComparisonLine("P1P4", comparison));
    }
}
