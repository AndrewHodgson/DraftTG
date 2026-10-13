using DraftTG.ArenaIntegration.Memory;

namespace DraftTG.ArenaIntegration.Tests;

public sealed class ArenaDraftMemoryWatcherTests
{
    private static ArenaDraftMemorySnapshot Pack(int pick = 1, int count = 14, string process = "123:start", string mode = "list") =>
        new(ArenaMemoryStatus.Success, "fixture", 123, null, "fixture", DateTimeOffset.UnixEpoch, true, mode,
            Enumerable.Range(100, count).Select(i => (uint)i).ToArray(), $"scene:1/pod:2/holder:3/list:4/version:{pick}", TimeSpan.Zero, 0)
        { ProcessInstance = process, DraftSessionId = "draft", PackNumber = 1, PickNumber = pick };
    [Fact]
    public void RequiresTwoIdenticalCompleteReadsAndDeduplicatesStablePolls()
    {
        using var watcher = new ArenaDraftMemoryWatcher(reader: () => Pack());
        Assert.Null(watcher.Observe(Pack())!.CurrentPack);
        var stable = watcher.Observe(Pack());
        Assert.NotNull(stable!.CurrentPack);
        Assert.Null(watcher.Observe(Pack()));
        Assert.Equal(2, watcher.Generation);
    }
    [Fact]
    public void SameCountNewPickRetiresBeforeNextStableReadAndChangesSemanticKey()
    {
        using var w = new ArenaDraftMemoryWatcher(reader: () => Pack());
        w.Observe(Pack()); var first = w.Observe(Pack())!;
        var transition = w.Observe(Pack(pick: 2))!;
        Assert.Null(transition.CurrentPack); Assert.Same(first.CurrentPack, transition.LastStablePack);
        var second = w.Observe(Pack(pick: 2))!;
        Assert.NotEqual(first.SemanticPackKey, second.SemanticPackKey);
        Assert.True(second.Generation > first.Generation);
    }
    [Fact]
    public void TornReadNeverPublishesPartialOrderAndRecoveryRequiresTwoReads()
    {
        using var w = new ArenaDraftMemoryWatcher(reader: () => Pack());
        w.Observe(Pack()); w.Observe(Pack());
        var failed = w.Observe(Pack() with { Status = ArenaMemoryStatus.TransientReadFailure, OrderedOfferedGrpIds = [] })!;
        Assert.Null(failed.CurrentPack); Assert.NotNull(failed.LastStablePack);
        Assert.Null(w.Observe(Pack())!.CurrentPack);
        Assert.Equal(ArenaMemoryTransition.Recovered, w.Observe(Pack())!.Transition);
    }
    [Fact]
    public void ProcessExitAndReplacementClearCachedPackAndRequireFreshStability()
    {
        using var w = new ArenaDraftMemoryWatcher(reader: () => Pack());
        w.Observe(Pack()); w.Observe(Pack());
        var exit = w.Observe(Pack() with { Status = ArenaMemoryStatus.NotRunning, ProcessInstance = null, OrderedOfferedGrpIds = [] })!;
        Assert.Null(exit.LastStablePack);
        Assert.Null(w.Observe(Pack(process: "456:new"))!.CurrentPack);
        Assert.NotNull(w.Observe(Pack(process: "456:new"))!.CurrentPack);
        var replacement = w.Observe(Pack(process: "789:new"))!;
        Assert.Equal(ArenaMemoryTransition.ProcessReplaced, replacement.Transition); Assert.Null(replacement.LastStablePack);
    }
    [Fact]
    public void UnsupportedBuildCannotRecoverAsAStablePackAndSmallCountsRemainSupportedForIdentity()
    {
        using var w = new ArenaDraftMemoryWatcher(reader: () => Pack());
        var bad = Pack() with { Status = ArenaMemoryStatus.UnsupportedBuild, OrderedOfferedGrpIds = [] };
        Assert.Null(w.Observe(bad)!.CurrentPack); Assert.Null(w.Observe(bad));
        w.Observe(Pack(count: 5)); Assert.Equal(5, w.Observe(Pack(count: 5))!.CurrentPack!.OrderedOfferedGrpIds.Count);
    }
    [Fact]
    public void ViewChangeHasNewGenerationWithoutInventingAnotherSemanticPack()
    {
        using var w = new ArenaDraftMemoryWatcher(reader: () => Pack());
        w.Observe(Pack()); var first = w.Observe(Pack())!;
        w.Observe(Pack(mode: "column")); var second = w.Observe(Pack(mode: "column"))!;
        Assert.Equal(ArenaMemoryTransition.ViewModeChanged, second.Transition);
        Assert.Equal(first.SemanticPackKey, second.SemanticPackKey);
    }
}
