using DraftTG.Application;
using DraftTG.ArenaIntegration.Memory;
using DraftTG.App.Platform;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;

namespace DraftTG.App.Tests;

public sealed class Phase9F4OverlayTests
{
    [Fact]
    public void DeckChangesRetireDeckGenerationWithoutInvalidatingPackStabilityAndMoveTranslates()
    {
        var read=new ArenaDraftMemorySnapshot(ArenaMemoryStatus.Success,"fixture",123,null,null,DateTimeOffset.UnixEpoch,true,
            "list",new uint[]{101},"fixture",TimeSpan.Zero,0) {ProcessInstance="instance",Ui=new(null,new([],1,0,false,null),null,0)};
        var window=new ArenaWindowGeometry(1,0,0,1600,900,1,true) {ProcessId=123,IsWindowed=true,IsVisible=true,IsMinimized=false};
        var pack=ArenaUiLocalizationDebugCommand.Key(read,window);
        var scrolled=read with {Ui=read.Ui with {DeckView=read.Ui.DeckView! with {MainScroll=.5f}}};
        Assert.Equal(pack,ArenaUiLocalizationDebugCommand.Key(scrolled,window));
        Assert.NotEqual(ArenaUiLocalizationDebugCommand.DeckKey(read,window),ArenaUiLocalizationDebugCommand.DeckKey(scrolled,window));
        Assert.Equal(pack,ArenaUiLocalizationDebugCommand.Key(read,window with {X=400,Y=200}));
        Assert.NotEqual(pack,ArenaUiLocalizationDebugCommand.Key(read,window with {Width=1280,Height=720}));
        Assert.NotEqual(pack,ArenaUiLocalizationDebugCommand.Key(read with {PrintingIdentities=[new(101,102,"basic",true,true,"fixture")]},window));
        var gate=new CaptureFreeUiStability();gate.Observe("main1",DateTimeOffset.UnixEpoch);gate.Observe("main1",DateTimeOffset.UnixEpoch.AddMilliseconds(200));
        var oldGeneration=gate.Generation;Assert.True(gate.IsStable);
        gate.Observe("scroll2",DateTimeOffset.UnixEpoch.AddMilliseconds(300));Assert.False(gate.IsStable);Assert.NotEqual(oldGeneration,gate.Generation);
    }
    [Fact]
    public void OrdinaryDeckMarkersRequirePhysicallyValidatedDpiSizeAndNonScrollableState()
    {
        var layout=new CaptureFreeDeckLayoutResult(new(1000,100,300,600),new(1000,100,300,400),0,[],null);
        Assert.True(DeckQualityBadges.PhysicallyQualified(1600,900,96,layout));
        Assert.False(DeckQualityBadges.PhysicallyQualified(1600,900,120,layout));
        Assert.False(DeckQualityBadges.PhysicallyQualified(1280,720,96,layout));
        Assert.False(DeckQualityBadges.PhysicallyQualified(1600,900,96,layout with {Content=new(1000,100,300,900)}));
        Assert.False(DeckQualityBadges.PhysicallyQualified(1600,900,96,layout with {Decline="unsupported"}));
        var missing=new LimitedStatisticsLoadResult(new("FRA",LimitedStatisticsFormat.QuickDraft),null,new(),LimitedStatisticsSource.Unavailable,null,"fixture");
        Assert.Empty(DeckQualityBadges.Create(layout,new Dictionary<uint,CardIdentifier>(),missing,1));
    }
}
