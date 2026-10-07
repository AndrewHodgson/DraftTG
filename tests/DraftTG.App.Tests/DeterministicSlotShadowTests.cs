using DraftTG.Application;
using DraftTG.App.Platform;
using DraftTG.Data;
using DraftTG.Tests.Shared;
using SkiaSharp;

namespace DraftTG.App.Tests;

public sealed partial class MainWindowViewModelTests
{
    [Fact]
    public void FixedSlotVerifierAcceptsTrueAssignmentAndRejectsSwapWithUnchangedThresholds()
    {
        var request = AutoRequest(13);
        var mapping = VisualCaptureMapping.FromAnchor(new(200 / 1723d, 150 / 1009d, 1000 / 1723d, 800 / 1009d), 1723, 1009);
        var slots = ArenaDraftSlotGeometry.TryCreateLayout(1723, 1009, 13, ArenaDraftSlotContext.WindowedDraftPack).Layout!.Slots
            .Select(s => mapping.ToCrop(s)!).ToArray();
        var references = SyntheticReferences(request);
        try
        {
            using var frame = SyntheticPack(request, references, Enumerable.Range(0, 13).ToArray(), slots, mapping.CropWidth, mapping.CropHeight);
            var recognizer = new CardTemplateRecognizer();

            var truth = recognizer.VerifyFixedSlots(request, frame, references, slots.Select((r, i) => (i, r)).ToArray());
            Assert.True(truth.AllAccepted, string.Join("; ", truth.Slots.Select(s => s.Reason)));
            Assert.All(truth.Slots, s => Assert.True(s.ExpectedScore >= CardVisualLocalizationResult.MinimumConfidence));

            var swapped = slots.Select((r, i) => (i == 0 ? 1 : i == 1 ? 0 : i, r)).ToArray();
            var check = recognizer.VerifyFixedSlots(request, frame, references, swapped);
            Assert.False(check.Slots[0].Accepted); Assert.False(check.Slots[1].Accepted);
            Assert.Equal(11, check.Accepted);
            Assert.NotNull(recognizer.VerifyFixedSlots(request, frame, references, swapped.Take(12).ToArray()).Error);
        }
        finally { DisposeReferences(references); }
    }

    [Fact]
    public async Task DeterministicShadowFailsClosedOutsideEnvelopeAndNeverChangesBadges()
    {
        var directory = SyntheticArenaDatabase.TemporaryDirectory();
        var (data, statistics) = FourBadgeFixture(); var session = AssociationSession(data.Catalog);
        using var overlay = new OverlayViewModel(session); overlay.SetCalibrationState(false, true);
        var database = SyntheticArenaDatabase.Create(directory.FullName, SyntheticArenaDatabase.Sql
            + "INSERT INTO Cards (GrpId, ExpansionCode, CollectorNumber, DraftContent, IsToken, IsPrimaryCard, Order_LandLast, Order_ColorOrder,"
            + " Order_CreaturesFirst, Order_CMCWithXLast, Order_Title, Order_MythicToCommon, Order_BasicLandsFirst) VALUES"
            + " (601,'WOE','1',1,0,1,0,0,0,2,'c',3,1), (602,'WOE','2',1,0,1,0,2,0,2,'a',3,1), (603,'WOE','3',1,0,1,0,1,0,2,'d',2,1), (604,'WOE','4',1,0,1,0,1,0,2,'b',3,1);");
        var log = new JsonLinesLedgerFile(Path.Combine(directory.FullName, "localization", "deterministic-shadow.jsonl"));
        var oneRule = new ArenaDisplayOrderEvaluation(400, 0, 0, [ArenaDisplayOrderModel.Hypotheses[0]], [], [], 0, 0, 0);
        var shadow = new DeterministicSlotShadowObserver(overlay, () => new ArenaWindowGeometry(1, 0, 0, 1723, 1009, 1, true),
            () => new NormalizedDraftRegion(.1, .1, .6, .6), () => oneRule, () => new ArenaCardDatabaseReader(database),
            _ => ArenaWindowMode.Windowed, log, action => action());
        var pack = AssociationPack(data, [601, 602, 603, 604]); session.ApplySessionUpdate(pack);
        session.ApplyStatisticsUpdate(new(pack.SnapshotResult.Snapshot, false, statistics));
        var request = new CardVisualLocalizationRequest(session.CurrentPackIdentity!, session.CurrentPackPresentations, overlay.Layout);
        var references = SyntheticReferences(request);
        try
        {
            await shadow.Completion;
            Assert.Equal("Deterministic slots: P1P1 unavailable · card count 4 outside validated envelope", overlay.DeterministicSlotStatus);

            using var frame = SyntheticPack(request, references, [2, 0, 3, 1]);
            var full = new CardTemplateRecognizer().Recognize(request, frame, references) with { PackGeneration = overlay.PlacementContext!.Generation };
            Assert.True(overlay.ApplyAutomaticLocalization(overlay.PlacementContext, full));
            var placed = overlay.Badges.Select(b => (b.VisualSlotIndex, b.X, b.Y)).ToArray();
            await shadow.Completion;
            Assert.Contains("outside validated envelope", overlay.DeterministicSlotStatus);
            Assert.Contains("\"Shadow\":\"UnsupportedEnvelope\"", Assert.Single(log.ReadLines()));
            Assert.Equal(placed, overlay.Badges.Select(b => (b.VisualSlotIndex, b.X, b.Y)));
            Assert.All(overlay.Badges, b => Assert.True(b.IsPlaced));

            Assert.True(overlay.ApplyAutomaticLocalization(overlay.PlacementContext, full)); // Same pack and geometry: no second record.
            await shadow.Completion;
            Assert.Single(log.ReadLines());
        }
        finally
        {
            DisposeReferences(references);
            await shadow.DisposeAsync();
            directory.Delete(true);
        }
    }
}
