using System.Text.Json;
using DraftTG.Application;
using DraftTG.App.Platform;
using DraftTG.Data;
using DraftTG.Tests.Shared;

namespace DraftTG.App.Tests;

public sealed partial class MainWindowViewModelTests
{
    private static FixedSlotVerificationResult StrictResult(int count, int failed = -1, double margin = .5) => new(
        Enumerable.Range(0, count).Select(i => new FixedSlotVerification(i, i, .98, .98 - margin, i != failed, i == failed ? "failed" : "")).ToArray(), TimeSpan.Zero);

    [Theory]
    [InlineData("full", true)]
    [InlineData("partial", false)]
    [InlineData("ambiguity", false)]
    [InlineData("manual", false)]
    [InlineData("stale", false)]
    [InlineData("resize", false)]
    public void FastEvidenceRequiresEveryOccurrenceAndCurrentAutomaticContext(string scenario, bool accepted)
    {
        var request = AutoRequest(13) with { PackGeneration = 7 };
        var log = Enumerable.Range(1, 13).ToArray();
        var prediction = new ArenaDisplayOrderPrediction(ArenaDisplayOrderPredictionStatus.Unanimous, [new(log, 120)], 120);
        var verification = scenario == "partial" ? StrictResult(13, 12) : StrictResult(13, margin: scenario == "ambiguity" ? .069 : .5);
        var result = FixedSlotOrderEvidence.VerifyCandidates(prediction, _ => verification);
        var gate = FixedSlotOrderEvidence.Gate(request, log, result, scenario == "stale" ? 8 : 7, request.Pack,
            scenario != "resize", scenario == "manual");
        Assert.Equal(accepted, gate.Accepted);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public void DiscriminatingEvidenceRequiresExactlyOneSuccessfulOrder(int passing, bool accepted)
    {
        var request = AutoRequest(13) with { PackGeneration = 7 };
        var log = Enumerable.Range(1, 13).ToArray();
        var prediction = new ArenaDisplayOrderPrediction(ArenaDisplayOrderPredictionStatus.Discriminating, [new(log, 1), new(log.Reverse().ToArray(), 119)], 120);
        var calls = 0;
        var result = FixedSlotOrderEvidence.VerifyCandidates(prediction, order =>
        {
            var pass = calls++ < passing;
            return new(order.Select((id, slot) => new FixedSlotVerification(slot, Array.IndexOf(log, id), .98, .1, pass, "")).ToArray(), TimeSpan.Zero);
        });
        Assert.Equal(passing, result.VerifiedCount(13));
        Assert.Equal(accepted, FixedSlotOrderEvidence.Gate(request, log, result, 7, request.Pack, true, false).Accepted);
        if (accepted) Assert.Equal(log, FixedSlotOrderEvidence.Gate(request, log, result, 7, request.Pack, true, false).VisualOrder); // Minority order wins by pixels.
    }

    [Fact]
    public void CandidateVerificationDeduplicatesFunctionalOrdersBeforeScoring()
    {
        var log = Enumerable.Range(1, 13).ToArray();
        var prediction = new ArenaDisplayOrderPrediction(ArenaDisplayOrderPredictionStatus.Discriminating,
            Enumerable.Range(0, 120).Select(i => new ArenaPredictedOrder(i < 110 ? log : log.Reverse().ToArray(), 1)).ToArray(), 120);
        var calls = 0;
        var result = FixedSlotOrderEvidence.VerifyCandidates(prediction, _ => { calls++; return StrictResult(13); });
        Assert.Equal(2, calls); Assert.Equal([110, 10], result.PredictionBefore.Orders.Select(o => o.RuleCount));
    }

    [Fact]
    public void TooManyDistinctOrdersDeclineTheWholeAttempt()
    {
        var prediction = new ArenaDisplayOrderPrediction(ArenaDisplayOrderPredictionStatus.Discriminating,
            Enumerable.Range(0, 9).Select(i => new ArenaPredictedOrder(Enumerable.Range(0, 13).Select(n => (n + i) % 13).ToArray(), 1)).ToArray(), 9);
        var result = FixedSlotOrderEvidence.VerifyCandidates(prediction, _ => throw new InvalidOperationException("must not score"));
        Assert.Empty(result.Candidates); Assert.Contains("limit exceeded", result.DeclineReason);
    }

    [Fact]
    public void PredictionOrdersAreFrozenBeforeAnyVerificationCallback()
    {
        var order = Enumerable.Range(1, 13).ToArray();
        var prediction = new ArenaDisplayOrderPrediction(ArenaDisplayOrderPredictionStatus.Unanimous, [new(order, 120)], 120);
        var result = FixedSlotOrderEvidence.VerifyCandidates(prediction, frozen =>
        { Array.Reverse(order); Assert.Equal(1, frozen[0]); return StrictResult(13); });
        Assert.Equal(1, result.PredictionBefore.Orders[0].Order[0]);
    }

    [Fact]
    public void DuplicateOccurrencesMustEachPassAtTheirOwnPixelSlot()
    {
        var request = AutoRequest(13, duplicate: true) with { PackGeneration = 7 };
        var mapping = VisualCaptureMapping.FromAnchor(new(.1, .1, .65, .8), 1723, 1009);
        var slots = ArenaDraftSlotGeometry.TryCreateLayout(1723, 1009, 13, ArenaDraftSlotContext.WindowedDraftPack).Layout!.Slots.Select(s => mapping.ToCrop(s)!).ToArray();
        var references = SyntheticReferences(request);
        try
        {
            using var frame = SyntheticPack(request, references, Enumerable.Range(0, 13).ToArray(), slots, mapping.CropWidth, mapping.CropHeight);
            var result = new CardTemplateRecognizer().VerifyFixedSlots(request, frame, references, slots.Select((r, i) => (i, r)).ToArray());
            Assert.True(result.FullyVerifies(13));
            var duplicateLog = Enumerable.Range(1, 13).Select(i => i == 2 ? 1 : i).ToArray();
            var prediction = new ArenaDisplayOrderPrediction(ArenaDisplayOrderPredictionStatus.Unanimous, [new(duplicateLog, 1)], 1);
            var verified = FixedSlotOrderEvidence.VerifyCandidates(prediction, _ => result);
            Assert.True(FixedSlotOrderEvidence.Gate(request, duplicateLog, verified, 7, request.Pack, true, false).Accepted);
            var aliasedLog = Enumerable.Range(1, 13).ToArray();
            var aliased = FixedSlotOrderEvidence.VerifyCandidates(prediction with { Orders = [new(aliasedLog, 1)] }, _ => result);
            Assert.Contains("unresolved duplicate", FixedSlotOrderEvidence.Gate(request, aliasedLog, aliased, 7, request.Pack, true, false).Reason);
            using var canvas = new SkiaSharp.SKCanvas(frame);
            var r = slots[1];
            using var paint = new SkiaSharp.SKPaint { Color = SkiaSharp.SKColors.Black };
            canvas.DrawRect((float)(r.X * frame.Width), (float)(r.Y * frame.Height), (float)(r.Width * frame.Width), (float)(r.Height * frame.Height), paint);
            Assert.False(new CardTemplateRecognizer().VerifyFixedSlots(request, frame, references, slots.Select((region, i) => (i, region)).ToArray()).FullyVerifies(13));
        }
        finally { DisposeReferences(references); }
    }

    [Fact]
    public void FastVerifierRejectsOverlappingRegionsAndMalformedOccurrenceCount()
    {
        var request = AutoRequest(13);
        var references = SyntheticReferences(request);
        try
        {
            using var frame = SyntheticPack(request, references, Enumerable.Range(0, 13).ToArray());
            var slots = Enumerable.Range(0, 13).Select(i => (i, new NormalizedDraftRegion(.1, .1, .2, .2))).ToArray();
            Assert.NotNull(new CardTemplateRecognizer().VerifyFixedSlots(request, frame, references, slots).Error);
            Assert.NotNull(new CardTemplateRecognizer().VerifyFixedSlots(request with { Occurrences = request.Occurrences.Take(12).ToArray() }, frame, references, slots.Take(12).ToArray()).Error);
        }
        finally { DisposeReferences(references); }
        var malformed = StrictResult(13) with { Slots = Enumerable.Range(0, 13).Select(i => new FixedSlotVerification(i, 0, .98, .1, true, "")).ToArray() };
        Assert.False(malformed.FullyVerifies(13)); Assert.False(StrictResult(12).FullyVerifies(13));
    }

    [Fact]
    public async Task ShadowAdmitsFreshVerifiedEvidenceAfterPartialMatcherAndRejectsQueuedStaleFrameWithoutChangingBadges()
    {
        var directory = SyntheticArenaDatabase.TemporaryDirectory();
        var (data, statistics) = FourBadgeFixture(); var session = AssociationSession(data.Catalog);
        using var overlay = new OverlayViewModel(session); overlay.SetCalibrationState(false, true);
        var database = SyntheticArenaDatabase.Create(directory.FullName, SyntheticArenaDatabase.Sql
            + "INSERT INTO Cards (GrpId, ExpansionCode, CollectorNumber, DraftContent, IsToken, IsPrimaryCard, Order_LandLast, Order_ColorOrder,"
            + " Order_CreaturesFirst, Order_CMCWithXLast, Order_Title, Order_MythicToCommon, Order_BasicLandsFirst) VALUES"
            + " (601,'WOE','1',1,0,1,0,0,0,2,'c',3,1), (602,'WOE','2',1,0,1,0,2,0,2,'a',3,1), (603,'WOE','3',1,0,1,0,1,0,2,'d',2,1), (604,'WOE','4',1,0,1,0,1,0,2,'b',3,1);");
        var ledger = new JsonLinesLedgerFile(Path.Combine(directory.FullName, "order.jsonl"));
        var shadowLog = new JsonLinesLedgerFile(Path.Combine(directory.FullName, "shadow.jsonl"));
        var recorder = new OrderEvidenceRecorder(overlay, () => (1723, 1009), new(new(ledger), () => new(database)), action => action());
        recorder.Start(); await recorder.Completion;
        var current = false;
        var dispatch = new Queue<Action>();
        void Drain() { while (true) { Action next; lock (dispatch) { if (!dispatch.TryDequeue(out next!)) break; } next(); } }
        var oneRule = new ArenaDisplayOrderEvaluation(400, 0, 0, [ArenaDisplayOrderModel.Hypotheses[0]], [], [], 0, 0, 0);
        var window = new ArenaWindowGeometry(1, 50, 80, 1723, 1009, 1, true);
        var anchor = new NormalizedDraftRegion(.1, .1, .65, .8);
        var shadow = new DeterministicSlotShadowObserver(overlay, () => window, () => anchor, () => oneRule, () => new(database),
            _ => ArenaWindowMode.Windowed, shadowLog, action => { lock (dispatch) dispatch.Enqueue(action); }, _ => current, recorder.RecordVerified);
        var log = Enumerable.Range(0, 13).Select(i => 601 + i % 4).ToArray();
        var pack = AssociationPack(data, log); session.ApplySessionUpdate(pack);
        session.ApplyStatisticsUpdate(new(pack.SnapshotResult.Snapshot, false, statistics));
        await shadow.Completion; Drain();
        var context = overlay.PlacementContext!;
        var request = new CardVisualLocalizationRequest(context.Pack, session.CurrentPackPresentations, overlay.Layout) { PackGeneration = context.Generation };
        var prediction = ArenaDisplayOrderModel.Predict(log, new ArenaCardDatabaseReader(database).ReadSortKeys(log).Keys, oneRule.Survivors);
        var candidate = DeterministicDraftCardLocator.Locate(new(context.Pack, context.Generation, log, prediction, 1723, 1009, ArenaDraftSlotContext.WindowedDraftPack));
        var mapping = VisualCaptureMapping.FromAnchor(anchor, 1723, 1009);
        var slots = candidate.Assignments.Select(a => mapping.ToCrop(a.Rectangle)!).ToArray();
        var references = SyntheticReferences(request);
        try
        {
            using var frame = SyntheticPack(request, references, candidate.Assignments.Select(a => a.Key.PackIndex).ToArray(), slots, mapping.CropWidth, mapping.CropHeight);
            var visual = new CardVisualLocalizationResult(context.Pack, [], slots, frame.Width, frame.Height, TimeSpan.Zero, "partial") { PackGeneration = context.Generation };
            Assert.True(overlay.ApplyAutomaticLocalization(context, visual));
            await recorder.Completion;
            var badges = overlay.Badges.Select(b => (b.VisualSlotIndex, b.X, b.Y, b.IsPlaced)).ToArray();
            var observation = new RecognitionFrameObservation(request, frame, references, window, anchor)
            { CaptureGeneration = 1, CaptureRequestGeneration = 1, Visual = visual };
            shadow.ObserveFrame(observation); await shadow.Completion; Drain(); await shadow.Completion;
            Assert.Empty(ledger.ReadLines()); // Pixels passed, but the UI admission sees a stale frame.
            current = true;
            shadow.ObserveFrame(observation with { CaptureRequestGeneration = 2 }); await shadow.Completion; Drain(); await shadow.Completion; await recorder.Completion;
            Assert.Contains(ArenaDisplayOrderObservation.FixedSlotEvidenceSource, Assert.Single(ledger.ReadLines()));
            Assert.Equal(badges, overlay.Badges.Select(b => (b.VisualSlotIndex, b.X, b.Y, b.IsPlaced)));
            Assert.Contains(shadowLog.ReadLines(), line => line.Contains("stale pack, geometry or frame"));
            Assert.Contains(shadowLog.ReadLines(), line => line.Contains("\"VerifiedCandidateOrderCount\":1"));
        }
        finally { DisposeReferences(references); await shadow.DisposeAsync(); await recorder.DisposeAsync(); directory.Delete(true); }
    }
}
