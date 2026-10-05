using DraftTG.ArenaIntegration;
using DraftTG.Domain;

namespace DraftTG.Application.Tests;

public sealed class DraftSessionCoordinatorTests
{
    private const string PremierStart =
        """{"method":"EventJoin","request":{"EventName":"PremierDraft_TST","DraftId":"draft-1"}}""";
    private const string TraditionalStart =
        """{"method":"EventJoin","request":{"EventName":"TradDraft_TST","DraftId":"draft-1"}}""";
    private const string PickTwoStart =
        """{"method":"EventJoin","request":{"EventName":"PickTwoDraft_TST","DraftId":"draft-1"}}""";
    private const string PackOnePickOne =
        """Draft.Notify {"draftId":"draft-1","SelfPack":1,"SelfPick":1,"PackCards":"101,102,103"}""";
    private const string PickOnePickOne =
        """{"method":"EventPlayerDraftMakePick","request":{"DraftId":"draft-1","GrpIds":[101],"Pack":1,"Pick":1}}""";
    private const string PackOnePickTwo =
        """Draft.Notify {"draftId":"draft-1","SelfPack":1,"SelfPick":2,"PackCards":"102,103,104"}""";
    private const string MalformedDraftLine = "Draft.Notify {not-json}";
    private const string LiveQuickDraftMarker =
        "<== BotDraftDraftStatus(25e66e3f-1686-471b-b06a-2e4983a28adf)";
    private const string LiveQuickDraftStatus =
        """{"CurrentModule":"BotDraft","Payload":"{\"Result\":\"Success\",\"EventName\":\"QuickDraft_HOB_20260915\",\"DraftStatus\":\"PickNext\",\"PackNumber\":0,\"PickNumber\":2,\"NumCardsToPick\":1,\"DraftPack\":[\"103441\",\"103501\",\"103421\",\"103401\",\"103411\",\"103513\",\"103480\",\"103457\",\"103535\",\"103464\",\"103554\",\"103581\"],\"PackStyles\":[],\"PickedCards\":[\"103499\",\"103521\"],\"PickedStyles\":[]}"}""";

    [Fact]
    public async Task IrrelevantLineEmitsNothing()
    {
        var source = Source(FakeArenaLogSource.Line("[Unity] Graphics initialized"));

        var updates = await CollectAsync(CreateCoordinator(source).RunAsync());

        Assert.Empty(updates);
    }

    [Fact]
    public async Task ValidPremierFlowEndsWithReadyNextPackSnapshot()
    {
        var source = Source(
            FakeArenaLogSource.Line(PremierStart),
            FakeArenaLogSource.Line(PackOnePickOne),
            FakeArenaLogSource.Line(PickOnePickOne),
            FakeArenaLogSource.Line(PackOnePickTwo));

        var updates = await CollectAsync(CreateCoordinator(source).RunAsync());

        var snapshot = Assert.IsType<DraftSnapshot>(updates[^1].SnapshotResult.Snapshot);
        Assert.Equal(DraftSnapshotAvailability.Ready, updates[^1].SnapshotResult.Availability);
        Assert.Equal(DraftFormat.BestOfOne, snapshot.Format);
        Assert.Equal(2, snapshot.CurrentPack.Position.Pick.Value);
        Assert.Equal(["B", "C", "D"],
            snapshot.CurrentPack.AvailableCardIdentifiers.Select(identifier => identifier.Value));
        Assert.Equal("A", Assert.Single(snapshot.History.Picks).SelectedCardIdentifier.Value);
    }

    [Fact]
    public async Task QuickDraftMultiEventLineIsAppliedInParserOrder()
    {
        const string line =
            """{"method":"BotDraftDraftStatus","response":{"EventName":"QuickDraft_TST","DraftId":"quick-1","DraftPack":[101,102],"PackNumber":0,"PickNumber":0,"DraftStatus":"Drafting"}}""";
        var source = Source(FakeArenaLogSource.Line(line));

        var updates = await CollectAsync(CreateCoordinator(source).RunAsync());

        Assert.Equal(2, updates.Count);
        Assert.Equal(ArenaDraftModeKind.Quick, updates[0].ArenaState.Mode!.Kind);
        Assert.Null(updates[0].ArenaState.CurrentPack);
        Assert.Equal(DraftSnapshotAvailability.NoCurrentPack, updates[0].SnapshotResult.Availability);
        Assert.Equal(ArenaDraftCoordinate.Create(1, 1), updates[1].ArenaState.CurrentPack!.Coordinate);
        Assert.Equal(DraftFormat.BestOfOne, updates[1].SnapshotResult.Snapshot!.Format);
    }

    [Fact]
    public async Task LiveQuickDraftMarkerAndStandaloneStatusProduceReadyRecoveredSnapshot()
    {
        int[] arenaIdentifiers =
        [
            103441, 103501, 103421, 103401, 103411, 103513,
            103480, 103457, 103535, 103464, 103554, 103581,
            103499, 103521
        ];
        var mappings = arenaIdentifiers
            .Select(identifier => (identifier, $"domain-{identifier}"))
            .ToArray();
        var source = Source(
            FakeArenaLogSource.Line(LiveQuickDraftMarker),
            FakeArenaLogSource.Line(LiveQuickDraftStatus));

        var updates = await CollectAsync(CreateCoordinator(source, mappings).RunAsync());

        Assert.Equal(3, updates.Count);
        Assert.All(updates, update => Assert.Null(update.Diagnostic));
        var final = updates[^1];
        var snapshot = Assert.IsType<DraftSnapshot>(final.SnapshotResult.Snapshot);
        Assert.Equal(DraftSnapshotAvailability.Ready, final.SnapshotResult.Availability);
        Assert.Equal(ArenaDraftModeKind.Quick, final.ArenaState.Mode!.Kind);
        Assert.Equal(DraftFormat.BestOfOne, snapshot.Format);
        Assert.Equal(1, snapshot.CurrentPack.Position.Pack.Value);
        Assert.Equal(3, snapshot.CurrentPack.Position.Pick.Value);
        Assert.Equal(12, snapshot.CurrentPack.AvailableCardIdentifiers.Count);
        Assert.Equal(0, snapshot.History.Count);
        Assert.Equal(
            ["domain-103499", "domain-103521"],
            snapshot.DraftedPool.CardIdentifiers.Select(identifier => identifier.Value));
    }

    [Fact]
    public async Task TraditionalDraftMapsToBestOfThree()
    {
        var source = Source(
            FakeArenaLogSource.Line(TraditionalStart),
            FakeArenaLogSource.Line(PackOnePickOne));

        var updates = await CollectAsync(CreateCoordinator(source).RunAsync());

        Assert.Equal(DraftFormat.BestOfThree, updates[^1].SnapshotResult.Snapshot!.Format);
    }

    [Fact]
    public async Task StateChangesPreserveSemanticOrder()
    {
        var source = Source(
            FakeArenaLogSource.Line(PremierStart),
            FakeArenaLogSource.Line(PackOnePickOne),
            FakeArenaLogSource.Line(PickOnePickOne),
            FakeArenaLogSource.Line(PackOnePickTwo));

        var updates = await CollectAsync(CreateCoordinator(source).RunAsync());

        Assert.Equal(4, updates.Count);
        Assert.Null(updates[0].ArenaState.CurrentPack);
        Assert.Equal(1, updates[1].ArenaState.CurrentPack!.Coordinate.Pick);
        Assert.Null(updates[2].ArenaState.CurrentPack);
        Assert.Single(updates[2].ArenaState.CompletedPicks);
        Assert.Equal(2, updates[3].ArenaState.CurrentPack!.Coordinate.Pick);
        Assert.Single(updates[3].ArenaState.CompletedPicks);
    }

    [Fact]
    public async Task IrrelevantLinesBetweenEventsDoNotEmitUpdates()
    {
        var source = Source(
            FakeArenaLogSource.Line(PremierStart),
            FakeArenaLogSource.Line("Unity noise one"),
            FakeArenaLogSource.Line("Unity noise two"),
            FakeArenaLogSource.Line(PackOnePickOne));

        var updates = await CollectAsync(CreateCoordinator(source).RunAsync());

        Assert.Equal(2, updates.Count);
        Assert.All(updates, update => Assert.Null(update.Diagnostic));
    }

    [Fact]
    public async Task DuplicateSemanticRecordDoesNotEmitRedundantUpdate()
    {
        var source = Source(
            FakeArenaLogSource.Line(PremierStart),
            FakeArenaLogSource.Line(PackOnePickOne),
            FakeArenaLogSource.Line(PackOnePickOne));

        var updates = await CollectAsync(CreateCoordinator(source).RunAsync());

        Assert.Equal(2, updates.Count);
    }

    [Fact]
    public async Task SourceResetEmitsIdleWhenActiveStateChanges()
    {
        var source = Source(
            FakeArenaLogSource.Line(PremierStart),
            FakeArenaLogSource.Line(PackOnePickOne),
            FakeArenaLogSource.Reset());

        var updates = await CollectAsync(CreateCoordinator(source).RunAsync());

        Assert.Equal(3, updates.Count);
        Assert.Equal(ArenaDraftSessionStatus.Idle, updates[^1].ArenaState.Status);
        Assert.Equal(DraftSnapshotAvailability.Idle, updates[^1].SnapshotResult.Availability);
    }

    [Fact]
    public async Task SourceResetWhileIdleDoesNotEmitUpdate()
    {
        var source = Source(FakeArenaLogSource.Reset());

        var updates = await CollectAsync(CreateCoordinator(source).RunAsync());

        Assert.Empty(updates);
    }

    [Fact]
    public async Task UnresolvedArenaCardIsReportedWithoutTerminatingPipeline()
    {
        const string unresolvedPack =
            """Draft.Notify {"draftId":"draft-1","SelfPack":1,"SelfPick":1,"PackCards":"101,999"}""";
        var source = Source(
            FakeArenaLogSource.Line(PremierStart),
            FakeArenaLogSource.Line(unresolvedPack),
            FakeArenaLogSource.Line("irrelevant after unresolved pack"));

        var updates = await CollectAsync(CreateCoordinator(source).RunAsync());

        var result = updates[^1].SnapshotResult;
        Assert.Equal(DraftSnapshotAvailability.UnresolvedArenaCard, result.Availability);
        Assert.Null(result.Snapshot);
        Assert.Equal([999], result.UnresolvedArenaCards.Select(identifier => identifier.Value));
    }

    [Fact]
    public async Task PickTwoRemainsActiveButSnapshotModeIsUnsupported()
    {
        var source = Source(
            FakeArenaLogSource.Line(PickTwoStart),
            FakeArenaLogSource.Line(PackOnePickOne));

        var updates = await CollectAsync(CreateCoordinator(source).RunAsync());

        Assert.Equal(ArenaDraftSessionStatus.Active, updates[^1].ArenaState.Status);
        Assert.Equal(ArenaDraftModeKind.PickTwo, updates[^1].ArenaState.Mode!.Kind);
        Assert.Equal(DraftSnapshotAvailability.UnsupportedDraftMode,
            updates[^1].SnapshotResult.Availability);
        Assert.Null(updates[^1].Diagnostic);
    }

    [Fact]
    public async Task ParseErrorEmitsDiagnosticAndDoesNotTerminate()
    {
        var source = Source(
            FakeArenaLogSource.Line(MalformedDraftLine),
            FakeArenaLogSource.Line(PremierStart));

        var updates = await CollectAsync(CreateCoordinator(source).RunAsync());

        Assert.Equal(2, updates.Count);
        var diagnostic = Assert.IsType<DraftSessionDiagnostic>(updates[0].Diagnostic);
        Assert.Equal(DraftSessionDiagnosticKind.ParseError, diagnostic.Kind);
        Assert.Equal(ArenaDraftLogParseErrorKind.MalformedOuterJson, diagnostic.ParseErrorKind);
        Assert.Equal(ArenaDraftSessionStatus.Idle, updates[0].ArenaState.Status);
        Assert.Null(updates[1].Diagnostic);
        Assert.Equal(ArenaDraftSessionStatus.Active, updates[1].ArenaState.Status);
    }

    [Fact]
    public async Task ValidStateRecoversAfterParseError()
    {
        var source = Source(
            FakeArenaLogSource.Line(PremierStart),
            FakeArenaLogSource.Line(MalformedDraftLine),
            FakeArenaLogSource.Line(PackOnePickOne));

        var updates = await CollectAsync(CreateCoordinator(source).RunAsync());

        Assert.Equal(3, updates.Count);
        Assert.NotNull(updates[1].Diagnostic);
        Assert.Equal(DraftSnapshotAvailability.Ready, updates[2].SnapshotResult.Availability);
        Assert.Equal(ArenaDraftCoordinate.Create(1, 1),
            updates[2].ArenaState.CurrentPack!.Coordinate);
    }

    [Fact]
    public async Task SourceErrorTerminatesRunWithoutDiagnosticConversion()
    {
        var sourceError = new InvalidOperationException("synthetic source failure");
        var source = Source(FakeArenaLogSource.Error(sourceError));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CollectAsync(CreateCoordinator(source).RunAsync()));

        Assert.Same(sourceError, error);
    }

    [Fact]
    public async Task CancellationEndsNormallyWithoutDiagnostic()
    {
        var source = Source(FakeArenaLogSource.Wait());
        var coordinator = CreateCoordinator(source);
        using var cancellation = new CancellationTokenSource();

        var collection = CollectAsync(coordinator.RunAsync(cancellation.Token));
        await source.WaitReached.WaitAsync(TimeSpan.FromSeconds(5));
        await cancellation.CancelAsync();
        var updates = await collection.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(updates);
    }

    [Fact]
    public async Task ConcurrentRunIsExplicitlyRejected()
    {
        var source = Source(FakeArenaLogSource.Wait());
        var coordinator = CreateCoordinator(source);
        using var cancellation = new CancellationTokenSource();

        var firstRun = CollectAsync(coordinator.RunAsync(cancellation.Token));
        await source.WaitReached.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Throws<DraftSessionAlreadyRunningException>(() => coordinator.RunAsync());

        await cancellation.CancelAsync();
        await firstRun.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task RestartAfterCancellationResetsPriorInMemoryState()
    {
        var source = new FakeArenaLogSource();
        source.EnqueueRun(
            FakeArenaLogSource.Line(PremierStart),
            FakeArenaLogSource.Line(PickOnePickOne),
            FakeArenaLogSource.Wait());
        source.EnqueueRun(
            FakeArenaLogSource.Line(PremierStart),
            FakeArenaLogSource.Line(PackOnePickTwo));
        var coordinator = CreateCoordinator(source);
        using var cancellation = new CancellationTokenSource();

        var firstRun = CollectAsync(coordinator.RunAsync(cancellation.Token));
        await source.WaitReached.WaitAsync(TimeSpan.FromSeconds(5));
        await cancellation.CancelAsync();
        var firstUpdates = await firstRun.WaitAsync(TimeSpan.FromSeconds(5));
        var secondUpdates = await CollectAsync(coordinator.RunAsync());

        Assert.Equal(2, firstUpdates.Count);
        var snapshot = Assert.IsType<DraftSnapshot>(secondUpdates[^1].SnapshotResult.Snapshot);
        Assert.Empty(snapshot.History.Picks);
        Assert.Equal(2, snapshot.CurrentPack.Position.Pick.Value);
    }

    [Fact]
    public async Task EarlierEmittedUpdateRemainsUnchangedAsPipelineAdvances()
    {
        var source = Source(
            FakeArenaLogSource.Line(PremierStart),
            FakeArenaLogSource.Line(PackOnePickOne),
            FakeArenaLogSource.Line(PickOnePickOne),
            FakeArenaLogSource.Line(PackOnePickTwo));

        var updates = await CollectAsync(CreateCoordinator(source).RunAsync());
        var earlier = updates[1];

        Assert.Equal(ArenaDraftCoordinate.Create(1, 1), earlier.ArenaState.CurrentPack!.Coordinate);
        Assert.Empty(earlier.ArenaState.CompletedPicks);
        Assert.Equal(["A", "B", "C"],
            earlier.SnapshotResult.Snapshot!.CurrentPack.AvailableCardIdentifiers
                .Select(identifier => identifier.Value));
        Assert.Equal(ArenaDraftCoordinate.Create(1, 2), updates[^1].ArenaState.CurrentPack!.Coordinate);
        Assert.Single(updates[^1].ArenaState.CompletedPicks);
    }

    private static FakeArenaLogSource Source(params FakeArenaLogStep[] steps)
    {
        var source = new FakeArenaLogSource();
        source.EnqueueRun(steps);
        return source;
    }

    private static DraftSessionCoordinator CreateCoordinator(FakeArenaLogSource source)
    {
        return CreateCoordinator(
            source,
            (101, "A"), (102, "B"), (103, "C"), (104, "D"));
    }

    private static DraftSessionCoordinator CreateCoordinator(
        FakeArenaLogSource source,
        params (int ArenaId, string DomainId)[] mappings)
    {
        var resolver = ArenaCardResolverTests.CreateResolver(mappings);
        return new DraftSessionCoordinator(
            source,
            new ArenaDraftLogParser(),
            new ArenaDraftStateEngine(),
            new ArenaDraftSnapshotAdapter(resolver));
    }

    private static async Task<List<DraftSessionUpdate>> CollectAsync(
        IAsyncEnumerable<DraftSessionUpdate> updates)
    {
        var collected = new List<DraftSessionUpdate>();
        await foreach (var update in updates.ConfigureAwait(false))
        {
            collected.Add(update);
        }
        return collected;
    }
}
