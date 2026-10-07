using System.Text.Json;
using DraftTG.Application;
using DraftTG.Data;
using DraftTG.Tests.Shared;

namespace DraftTG.Application.Tests;

public sealed class VerifiedOrderEvidenceTests
{
    private static readonly int[] Log = [86982, 86889, 86849, 86831, 86840, 86978, 86799, 86853, 87058];
    private static readonly int[] Visual = [87058, 86853, 86799, 86831, 86840, 86849, 86889, 86978, 86982];
    private static ArenaDisplayOrderPrediction Bounded(ArenaDisplayOrderPrediction prediction) => prediction with
    { Orders = [prediction.Orders.First(o => o.Order.SequenceEqual(Visual)), prediction.Orders.First(o => !o.Order.SequenceEqual(Visual))] };

    [Fact]
    public void VerifiedPackCannotTrainItsOwnPreVerificationPredictionIncludingReplay()
    {
        var directory = SyntheticArenaDatabase.TemporaryDirectory();
        try
        {
            var reader = new ArenaCardDatabaseReader(SyntheticArenaDatabase.Create(directory.FullName));
            var file = new JsonLinesLedgerFile(Path.Combine(directory.FullName, "order.jsonl"));
            var service = new ArenaDisplayOrderEvidenceService(new(file), () => reader);
            service.Initialize();
            var before = ArenaDisplayOrderModel.Predict(Log, reader.ReadSortKeys(Log).Keys, service.BeforePack(1, 6, Log).Survivors);
            Assert.True(before.Orders.Count > 1);
            var candidate = new ArenaDisplayOrderCandidate("test-draft", "QuickDraft_WOE", 1, 6, Log,
                new(true, "unique full pixel verification", Visual, .98, .99), 1723, 1009, 1000, 800, "fixed-slot", DateTimeOffset.UtcNow)
            { EvidenceSource = ArenaDisplayOrderObservation.FixedSlotEvidenceSource, PredictionBeforeVerification = Bounded(before),
                CandidateOrderCount = 2, MinimumAmbiguityMargin = .5 };
            var outcome = service.Record(candidate);
            Assert.Equal(ArenaDisplayOrderRecordStatus.Recorded, outcome.Status);
            Assert.Equal(ArenaDisplayOrderPredictionStatus.Discriminating, outcome.PredictionBefore.Status);
            Assert.Equal(ArenaDisplayOrderPredictionStatus.Unanimous, ArenaDisplayOrderModel.Predict(Log, reader.ReadSortKeys(Log).Keys, service.Evaluation.Survivors).Status);
            Assert.Equal(400, service.BeforePack(1, 6, Log).Survivors.Count);
            service.Initialize();
            Assert.Equal(400, service.BeforePack(1, 6, Log).Survivors.Count);
            Assert.Equal(before.Orders.Select(o => string.Join(",", o.Order)),
                ArenaDisplayOrderModel.Predict(Log, reader.ReadSortKeys(Log).Keys, service.BeforePack(1, 6, Log).Survivors).Orders.Select(o => string.Join(",", o.Order)));
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public void LedgerPreservesBothEvidenceSourcesAndSummaryResolutions()
    {
        var directory = SyntheticArenaDatabase.TemporaryDirectory();
        try
        {
            var reader = new ArenaCardDatabaseReader(SyntheticArenaDatabase.Create(directory.FullName));
            var file = new JsonLinesLedgerFile(Path.Combine(directory.FullName, "order.jsonl"));
            var service = new ArenaDisplayOrderEvidenceService(new(file), () => reader); service.Initialize();
            var before = ArenaDisplayOrderModel.Predict(Log, reader.ReadSortKeys(Log).Keys, service.Evaluation.Survivors);
            var candidate = new ArenaDisplayOrderCandidate("full", "QuickDraft_WOE", 1, 6, Log, new(true, "full", Visual, .98, .99), 1723, 1009, 1000, 800, "full-matcher", DateTimeOffset.UtcNow);
            service.Record(candidate);
            service.Record(candidate with { RawDraftScope = "fast", EvidenceSource = ArenaDisplayOrderObservation.FixedSlotEvidenceSource,
                PredictionBeforeVerification = Bounded(before), CandidateOrderCount = 2, MinimumAmbiguityMargin = .5 });
            service.Initialize();
            Assert.Equal([ArenaDisplayOrderObservation.CurrentEvidenceSource, ArenaDisplayOrderObservation.FixedSlotEvidenceSource], service.Observations.Select(o => o.EvidenceSource));
            var summary = ArenaDisplayOrderEvidencePresentation.Summary(service);
            Assert.Contains("Full matcher 1 | Fast verifier 1", summary); Assert.Contains("Candidate-order resolutions: 1", summary);
            Assert.Contains("Gate 1: not satisfied", summary);
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public void MalformedFastLedgerRowsAreSkippedWithoutTrainingTheModel()
    {
        var directory = SyntheticArenaDatabase.TemporaryDirectory();
        try
        {
            var reader = new ArenaCardDatabaseReader(SyntheticArenaDatabase.Create(directory.FullName));
            var file = new JsonLinesLedgerFile(Path.Combine(directory.FullName, "order.jsonl"));
            var scope = ArenaDisplayOrderObservation.ScopeDigest("s");
            var valid = new ArenaDisplayOrderObservation { ObservationId = ArenaDisplayOrderObservation.CreateId(scope, 1, 6, Log), DraftScope = scope,
                Pack = 1, Pick = 6, CardCount = Log.Length, LogOrder = Log, VisualOrder = Visual, SortKeys = reader.ReadSortKeys(Log).Keys.Values.ToArray() };
            file.AppendLine("{broken");
            foreach (var bad in new[] { valid with { EvidenceSource = "predicted-order" },
                valid with { EvidenceSource = ArenaDisplayOrderObservation.FixedSlotEvidenceSource, MinimumScore = .99, MeanScore = .99, MinimumAmbiguityMargin = .069, CandidateOrderCount = 2 },
                valid with { SortKeys = [.. valid.SortKeys, valid.SortKeys[0]] }, valid with { LogOrder = null! } }) file.AppendLine(JsonSerializer.Serialize(bad));
            file.AppendLine(JsonSerializer.Serialize(valid)); // Existing schema-1 full matcher row still replays.
            var service = new ArenaDisplayOrderEvidenceService(new(file), () => reader); service.Initialize();
            Assert.Single(service.Observations); Assert.Equal(5, service.LastLoad!.SkippedLines.Count);
            Assert.Equal(120, service.Evaluation.Survivors.Count);
        }
        finally { directory.Delete(true); }
    }
}
