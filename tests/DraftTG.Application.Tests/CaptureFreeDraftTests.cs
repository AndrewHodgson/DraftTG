using DraftTG.ArenaIntegration;
using DraftTG.ArenaIntegration.Memory;

namespace DraftTG.Application.Tests;

public sealed class CaptureFreeDraftTests
{
    private static ArenaDraftMemorySnapshot Memory(int pick = 1) =>
        new(ArenaMemoryStatus.Success, "fixture", 123, null, null, DateTimeOffset.UnixEpoch, true, "list",
            new uint[] { 103, 101, 103 }, "fixture", TimeSpan.Zero, 0)
        { PackNumber = 1, PickNumber = pick, DraftSessionId = "draft" };
    private static ArenaDraftStateSnapshot Log(int pick = 1) => new(ArenaDraftSessionStatus.Active,
        ArenaDraftIdentifier.Create("draft"), ArenaDraftMode.Quick, "event", new(ArenaDraftIdentifier.Create("draft"),
            ArenaDraftCoordinate.Create(1, pick), new([ArenaCardIdentifier.Create(101), ArenaCardIdentifier.Create(103), ArenaCardIdentifier.Create(103)])), new([]));
    [Fact]
    public void ReconciliationRetainsExactMemoryOccurrenceOrderAndLogHistory()
    {
        var log = Log(); var result = CaptureFreeDraftReconciler.Reconcile(Memory(), log);
        Assert.True(result.CanScore);
        Assert.Equal(new[] { 103, 101, 103 }, result.State.CurrentPack!.CardIdentifiers.Select(c => c.Value));
        Assert.Same(log.CompletedPicks, result.State.CompletedPicks);
        var resolver = ArenaCardResolverTests.CreateResolver((101, "A"), (103, "B"));
        var adapted = new ArenaDraftSnapshotAdapter(resolver).Convert(result.State);
        Assert.Equal(DraftSnapshotAvailability.Ready, adapted.Availability);
    }
    [Fact]
    public void LogAndMemoryMayArriveAtDifferentTimesButAmbiguousContextDeclines()
    {
        Assert.Equal("MemoryAhead", CaptureFreeDraftReconciler.Reconcile(Memory(2), Log()).Status);
        Assert.Equal("PlayerLogAhead", CaptureFreeDraftReconciler.Reconcile(Memory(), Log(2)).Status);
        Assert.False(CaptureFreeDraftReconciler.Reconcile(Memory(), null).CanScore);
        Assert.Equal("SessionDisagreement", CaptureFreeDraftReconciler.Reconcile(Memory() with { DraftSessionId = "other" }, Log()).Status);
        Assert.Equal("PackSetDisagreement", CaptureFreeDraftReconciler.Reconcile(Memory() with { OrderedOfferedGrpIds = new uint[] { 101, 102, 103 } }, Log()).Status);
    }
    [Fact]
    public void AcceptedPickRetiresOutgoingMemoryPackEvenBeforeNextMemoryTransition()
    {
        var log = Log();
        log = log with { CurrentPack = null, CompletedPicks = new([new(log.DraftIdentifier, ArenaDraftCoordinate.Create(1, 1), new([ArenaCardIdentifier.Create(101)]))]) };
        var result = CaptureFreeDraftReconciler.Reconcile(Memory(), log);
        Assert.False(result.CanScore); Assert.Equal("PlayerLogAhead", result.Status); Assert.Null(result.State.CurrentPack);
    }
    [Fact]
    public void LayoutUsesMeasuredIndexPositionsAndDeclinesUnsupportedModesCountsAndSizes()
    {
        var layout = CaptureFreeDraftLayout.Map(1600, 900, 14, "list", true);
        Assert.True(layout.IsAvailable);
        for (var i = 0; i < 14; i++)
        {
            var expected = ArenaDraftSlotGeometry.Slot(i, 1600, 900);
            Assert.Equal(expected.X / 1600, layout.Rectangles[i].X);
        }
        Assert.Contains("column-mode layout not validated", CaptureFreeDraftLayout.Map(1600, 900, 14, "column", true).Decline);
        foreach (var count in new[] { 1, 12, 15 }) Assert.False(CaptureFreeDraftLayout.Map(1600, 900, count, "list", true).IsAvailable);
        Assert.False(CaptureFreeDraftLayout.Map(900, 900, 14, "list", true).IsAvailable);
        Assert.False(CaptureFreeDraftLayout.Map(1600, 900, 14, "list", false).IsAvailable);
        Assert.True(CaptureFreeDraftLayout.Map(1280, 720, 13, "list", true).IsAvailable);
    }
    [Fact]
    public async Task LiveFeedRetiresBeforeIdentityResolutionAndInvalidatesQueuedReadyUpdate()
    {
        var lines = System.Threading.Channels.Channel.CreateUnbounded<ArenaLogSourceEvent>();
        var resolver = ArenaCardResolverTests.CreateResolver((101, "A"), (103, "B"));
        var adapter = new ArenaDraftSnapshotAdapter(resolver);
        var logs = new DraftSessionCoordinator(new ChannelLog(lines.Reader), new ArenaDraftLogParser(), new ArenaDraftStateEngine(), adapter);
        var snapshot = Memory() with { DraftSessionId = null, CourseId = "course-1", ProcessInstance = "123:start", EventName = "QuickDraft_TST" };
        var watcher = new ArenaDraftMemoryWatcher(reader: () => snapshot);
        await using var feed = new CaptureFreeDraftSession(logs, adapter, watcher);
        using var token = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var ready = new TaskCompletionSource<DraftSessionUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
        var retired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var accepted = false;
        feed.PickRetired += () => accepted = true;
        feed.Invalidated += () => { if (accepted) retired.TrySetResult(); return Task.CompletedTask; };
        var consuming = Task.Run(async () =>
        {
            try { await foreach (var update in feed.RunAsync(token.Token)) if (update.SnapshotResult.Snapshot is not null) ready.TrySetResult(update); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        });
        try
        {
            lines.Writer.TryWrite(new ArenaLogSourceEvent.Line("""{"method":"BotDraftDraftStatus","response":{"EventName":"QuickDraft_TST","DraftPack":[101,103,103],"PackNumber":0,"PickNumber":0,"DraftStatus":"Drafting"}}"""));
            var initial = await ready.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal("course:course-1", initial.ArenaState.DraftIdentifier!.Value);
            lines.Writer.TryWrite(new ArenaLogSourceEvent.Line("""{"method":"EventPlayerDraftMakePick","request":{"DraftId":"draft-1","GrpIds":[101],"Pack":1,"Pick":1}}"""));
            await retired.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(feed.IsCurrent(initial));
        }
        finally { await token.CancelAsync(); await consuming; }
    }
    private sealed class ChannelLog(System.Threading.Channels.ChannelReader<ArenaLogSourceEvent> reader) : IArenaLogSource
    { public IAsyncEnumerable<ArenaLogSourceEvent> ReadEventsAsync(CancellationToken cancellationToken = default) => reader.ReadAllAsync(cancellationToken); }
}
