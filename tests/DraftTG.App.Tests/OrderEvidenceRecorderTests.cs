using DraftTG.Application;
using DraftTG.Data;
using DraftTG.Tests.Shared;

namespace DraftTG.App.Tests;

public sealed partial class MainWindowViewModelTests
{
    [Fact]
    public async Task OrderEvidenceRecordsOnlyFullAutomaticPlacementAndNeverChangesBadges()
    {
        var directory = SyntheticArenaDatabase.TemporaryDirectory();
        var (data, statistics) = FourBadgeFixture(); var session = AssociationSession(data.Catalog);
        using var overlay = new OverlayViewModel(session); overlay.SetCalibrationState(false, true);
        var database = SyntheticArenaDatabase.Create(directory.FullName, SyntheticArenaDatabase.Sql
            + "INSERT INTO Cards (GrpId, ExpansionCode, CollectorNumber, DraftContent, IsToken, IsPrimaryCard, Order_LandLast, Order_ColorOrder,"
            + " Order_CreaturesFirst, Order_CMCWithXLast, Order_Title, Order_MythicToCommon, Order_BasicLandsFirst) VALUES"
            + " (601,'WOE','1',1,0,1,0,0,0,2,'c',3,1), (602,'WOE','2',1,0,1,0,2,0,2,'a',3,1), (603,'WOE','3',1,0,1,0,1,0,2,'d',2,1), (604,'WOE','4',1,0,1,0,1,0,2,'b',3,1);");
        var ledger = new JsonLinesLedgerFile(Path.Combine(directory.FullName, "localization", "order-evidence.jsonl"));
        var recorder = new OrderEvidenceRecorder(overlay, () => (2560, 1440),
            new ArenaDisplayOrderEvidenceService(new(ledger), () => new ArenaCardDatabaseReader(database)), action => action());
        recorder.Start();
        var pack = AssociationPack(data, [601, 602, 603, 604]); session.ApplySessionUpdate(pack);
        session.ApplyStatisticsUpdate(new(pack.SnapshotResult.Snapshot, false, statistics));
        var request = new CardVisualLocalizationRequest(session.CurrentPackIdentity!, session.CurrentPackPresentations, overlay.Layout);
        var references = SyntheticReferences(request);
        try
        {
            using var frame = SyntheticPack(request, references, [2, 0, 3, 1]);
            var full = new CardTemplateRecognizer().Recognize(request, frame, references) with { PackGeneration = overlay.PlacementContext!.Generation };
            references.Remove(request.Occurrences[1].CardIdentifier, out var missing); missing!.Dispose();
            var partial = new CardTemplateRecognizer().Recognize(request, frame, references) with { PackGeneration = overlay.PlacementContext.Generation };

            Assert.True(overlay.ApplyAutomaticLocalization(overlay.PlacementContext, partial));
            await recorder.Completion;
            Assert.Contains("not recorded (partial automatic result (3 / 4))", overlay.OrderEvidenceStatus);
            Assert.Empty(ledger.ReadLines());

            Assert.True(overlay.ApplyAutomaticLocalization(overlay.PlacementContext, full));
            var placed = overlay.Badges.Select(b => (b.VisualSlotIndex, b.X, b.Y)).ToArray();
            await recorder.Completion;
            Assert.StartsWith("P1P1 predicted order: undetermined", overlay.OrderEvidenceStatus);
            var record = Assert.Single(ledger.ReadLines());
            Assert.Contains("\"VisualOrder\":[603,601,604,602]", record);
            Assert.Equal(placed, overlay.Badges.Select(b => (b.VisualSlotIndex, b.X, b.Y)));
            Assert.All(overlay.Badges, b => Assert.True(b.IsPlaced));

            Assert.True(overlay.ApplyAutomaticLocalization(overlay.PlacementContext, full)); // Same visual pack state again.
            await recorder.Completion;
            Assert.Equal("P1P1 order evidence already recorded", overlay.OrderEvidenceStatus);
            Assert.Single(ledger.ReadLines());
        }
        finally
        {
            DisposeReferences(references);
            await recorder.DisposeAsync();
            directory.Delete(true);
        }
    }
}
