using DraftTG.Application;
using DraftTG.App.Platform;
using DraftTG.ArenaIntegration;
using DraftTG.Domain;
using SkiaSharp;

namespace DraftTG.App.Tests;

public sealed partial class MainWindowViewModelTests
{
    private static CardVisualLocalizationResult ReliabilityResult(CardVisualLocalizationRequest request, int width = 800, int height = 480)
    {
        var rectangles = request.CalibratedLayout.Slots.Take(request.Occurrences.Count)
            .Select(s => new NormalizedDraftRegion(s.X, s.Y, s.Width, s.Height)).ToArray();
        return new(request.Pack, request.Occurrences.Select((c, i) => new CardVisualMatch(c.Key, i, rectangles[i],
            .99, LocalizationConfidence.HighConfidence, "fake visual evidence")).ToArray(), rectangles, width, height,
            TimeSpan.Zero, "fake visual evidence") { PackGeneration = request.PackGeneration };
    }
    private static void ReliabilityPlace(OverlayViewModel overlay)
    {
        var context = overlay.PlacementContext!;
        Assert.True(overlay.ApplyAutomaticLocalization(context, ReliabilityResult(new(context.Pack,
            overlay.Session.CurrentPackPresentations, overlay.Layout) { PackGeneration = context.Generation })));
    }
    private static ArenaDraftStateSnapshot AcceptedCurrentPick(OverlayViewModel overlay)
    {
        var state = overlay.Session.CurrentArenaState!;
        var pack = state.CurrentPack!;
        return state with { CurrentPack = null, CompletedPicks = new(state.CompletedPicks.Append(new(
            state.DraftIdentifier, pack.Coordinate, new ArenaCardIdentifierList([pack.CardIdentifiers[0]])))) };
    }

    [Fact]
    public void CardNameUsesResolvedOccurrenceAndLeavesRatingBindingsUnchanged()
    {
        var (data, stats) = BadgeFixture(("Dawn of Hope", .587, 6.2));
        var runtime = AssociationSession(data.Catalog); using var overlay = new OverlayViewModel(runtime);
        var update = AssociationPack(data, [601]); runtime.ApplySessionUpdate(update);
        runtime.ApplyStatisticsUpdate(new(update.SnapshotResult.Snapshot, false, stats));
        var badge = Assert.Single(overlay.Badges);
        Assert.Equal("Dawn of Hope", badge.DisplayName); Assert.True(badge.HasDisplayName);
        Assert.Equal(badge.Presentation!.DisplayedGIH, badge.WinRate);
        Assert.Equal(badge.Presentation.DisplayedALSA, badge.Secondary);
        Assert.Equal(badge.Presentation.DisplayedContextRank, badge.Rank);
    }
    [Fact]
    public void LongCardNamesKeepCanonicalTextAndUseBoundedEllipsisRendering()
    {
        var name = "An Extremely Long Canonical Card Name That Exceeds The Available Width";
        var (data, _) = BadgeFixture((name, .587, 6.2));
        var runtime = AssociationSession(data.Catalog); using var overlay = new OverlayViewModel(runtime);
        runtime.ApplySessionUpdate(AssociationPack(data, [601])); ReliabilityPlace(overlay);
        var badge = Assert.Single(overlay.Badges);
        Assert.Equal(name, badge.DisplayName); Assert.InRange(badge.NameWidth, 1, 180);
        var text = Assert.Single(PassiveBadgeView().Descendants(), e => (string?)e.Attribute("Text") == "{Binding DisplayName}");
        Assert.Equal("NoWrap", (string?)text.Attribute("TextWrapping"));
        Assert.Equal("CharacterEllipsis", (string?)text.Attribute("TextTrimming"));
        Assert.Equal("Center", (string?)text.Attribute("TextAlignment"));
        Assert.Contains(text.Ancestors(), e => (string?)e.Attribute("IsVisible") == "{Binding IsPlaced}");
    }
    [Fact]
    public void MultifaceNameUsesTheExistingCanonicalConvention()
    {
        const string name = "Hallway Heckler // Vicious Verse";
        var (data, _) = BadgeFixture((name, .587, 6.2));
        var runtime = AssociationSession(data.Catalog); using var overlay = new OverlayViewModel(runtime);
        runtime.ApplySessionUpdate(AssociationPack(data, [601]));
        Assert.Equal(name, Assert.Single(overlay.Badges).DisplayName);
    }
    [Fact]
    public void DuplicateNamesStayOccurrenceSpecificAndCanBeHiddenWithoutChangingRatings()
    {
        var (data, _) = FourBadgeFixture(); var runtime = AssociationSession(data.Catalog);
        using var overlay = new OverlayViewModel(runtime); runtime.ApplySessionUpdate(AssociationPack(data, [601,602,601]));
        Assert.Equal(["A","B","A"], overlay.Badges.Select(b=>b.DisplayName));
        Assert.Equal(3, overlay.Badges.Select(b=>b.OccurrenceKey).Distinct().Count());
        var before = overlay.Badges.Select(b=>(b.WinRate,b.Secondary,b.Rank,b.BorderColor)).ToArray();
        overlay.SetCardNamesVisible(false); Assert.All(overlay.Badges,b=>Assert.False(b.HasDisplayName));
        overlay.SetCardNamesVisible(true); Assert.All(overlay.Badges,b=>Assert.True(b.HasDisplayName));
        Assert.Equal(before, overlay.Badges.Select(b=>(b.WinRate,b.Secondary,b.Rank,b.BorderColor)));
    }
    [Fact]
    public void UnresolvedOrInconsistentIdentityNeverShowsAName()
    {
        var id = CardIdentifier.Create("missing");
        var unresolved = new CardBadgeViewModel(new CurrentPackCardPresentation(new(0,id)));
        Assert.Empty(unresolved.DisplayName); Assert.False(unresolved.HasDisplayName);
        var (data, _) = FourBadgeFixture();
        var bad = new CardBadgeViewModel(new CurrentPackCardPresentation(new(0,id),data.Catalog.Cards.First()));
        Assert.Empty(bad.DisplayName); Assert.False(bad.HasDisplayName);
    }

    [Fact]
    public void AcceptedPickHidesPlacementsBeforeSnapshotAndRejectsOldResultsThenResumes()
    {
        using var overlay = CaptureOverlay(); overlay.SetCalibrationState(false,true); ReliabilityPlace(overlay);
        var runtime = overlay.Session; var context = overlay.PlacementContext!;
        var oldRows = runtime.CurrentPackPresentations; var pool = runtime.DraftPool;
        var stale = ReliabilityResult(new(context.Pack,oldRows,overlay.Layout) { PackGeneration=context.Generation });
        runtime.InvalidateSubmittedPack(AcceptedCurrentPick(overlay));
        Assert.False(overlay.ShowBadges); Assert.Null(overlay.PlacementContext);
        Assert.All(overlay.Badges,b=>Assert.False(b.IsPlaced));
        Assert.Same(oldRows,runtime.CurrentPackPresentations); Assert.Same(pool,runtime.DraftPool);
        Assert.NotNull(runtime.CurrentPackIdentity); Assert.False(overlay.ApplyAutomaticLocalization(context,stale));
        runtime.ApplyStatisticsUpdate(new(null,true,null)); Assert.False(overlay.ShowBadges);
        var (data,_) = FourBadgeFixture(); runtime.ApplySessionUpdate(AssociationPack(data,[602,603,604],2));
        Assert.True(overlay.PlacementContext!.Generation > context.Generation); ReliabilityPlace(overlay);
        Assert.True(overlay.ShowBadges); Assert.All(overlay.Badges,b=>Assert.True(b.IsPlaced));
    }
    [Fact]
    public void AcceptedPickNotificationsAreIdempotentAndRejectOtherCoordinatesOrDrafts()
    {
        using var overlay = CaptureOverlay(); ReliabilityPlace(overlay);
        var state = AcceptedCurrentPick(overlay); var context = overlay.PlacementContext; var notifications=0;
        overlay.Session.CurrentPackPlacementInvalidated += ()=>notifications++;
        overlay.Session.InvalidateSubmittedPack(state with { CompletedPicks = new([new(state.DraftIdentifier,
            ArenaDraftCoordinate.Create(1,9),new ArenaCardIdentifierList([ArenaCardIdentifier.Create(601)]))]) });
        overlay.Session.InvalidateSubmittedPack(state with { DraftIdentifier=ArenaDraftIdentifier.Create("other") });
        Assert.Same(context,overlay.PlacementContext);
        overlay.Session.InvalidateSubmittedPack(state); overlay.Session.InvalidateSubmittedPack(state);
        Assert.Equal(1,notifications); Assert.Null(overlay.PlacementContext);
    }
    [Fact]
    public async Task AcceptedPickCancelsOldAsyncLocalizationEvenIfRecognizerReturnsLate()
    {
        using var overlay = CaptureOverlay(); var fake = new FakeArenaCapture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delivered = new TaskCompletionSource<CardVisualLocalizationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        CardVisualLocalizationRequest? oldRequest = null; CancellationToken oldToken = default;
        await using var session = CaptureSession(overlay,fake,(request,_,token)=>
        {
            if (oldRequest is not null) return Task.FromResult(ReliabilityResult(request));
            oldRequest=request; oldToken=token; entered.SetResult(); return delivered.Task;
        });
        session.RetryManually(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        overlay.Session.InvalidateSubmittedPack(AcceptedCurrentPick(overlay));
        Assert.True(oldToken.IsCancellationRequested);
        delivered.SetResult(ReliabilityResult(oldRequest!)); await session.CurrentWork;
        Assert.Null(overlay.PlacementContext); Assert.False(overlay.HasAutomaticVisualPlacement);
        Assert.All(overlay.Badges,b=>Assert.False(b.IsPlaced));
        var (data,_) = FourBadgeFixture(); overlay.Session.ApplySessionUpdate(AssociationPack(data,[602,603,604],2));
        // The next pack must supply changed pixels; the production stale-frame guard stays enabled.
        fake.Handler = (window,region,generation,_,_) =>
        {
            var crop = ArenaDraftCrop.Calculate(window,region);
            return Task.FromResult(new ArenaRegionFrame(SolidFrame(SKColors.Blue,crop.Width,crop.Height),
                window.X+crop.X,window.Y+crop.Y,window.Scaling) {Generation=generation,WindowHandle=window.Handle});
        };
        session.RefreshWindow(); await session.CurrentWork;
        Assert.True(overlay.HasAutomaticVisualPlacement); Assert.All(overlay.Badges,b=>Assert.True(b.IsPlaced));
    }

    private sealed class FakeReliabilityEvents : IArenaWindowEvents
    {
        public event Action? Changed;
        public bool IsRegistered=>true;
        public bool Disposed {get;private set;}
        public ArenaWindowGeometry? Target {get;private set;}
        public void Track(ArenaWindowGeometry? window)=>Target=window;
        public void Emit()=>Changed?.Invoke();
        public void Dispose()=>Disposed=true;
    }
    private static AutomaticCardLocalizationSession ReliabilitySession(OverlayViewModel overlay,FakeArenaCapture fake,
        FakeReliabilityEvents events,List<(int X,int Y,double W,double H)> moves,List<bool>? visibility=null)
    {
        var calibration=new OverlayCalibration(new(0,0,1000,600,1),CaptureRegion,DraftCardLayout.Grid(5));
        return new(overlay,()=>calibration,(x,y,w,h)=>moves.Add((x,y,w,h)),v=>visibility?.Add(v),fake,
            recognize:(r,f,_)=>Task.FromResult(ReliabilityResult(r,f.Image.Width,f.Image.Height)),
            dispatch:action=>action(),timing:new(TimeSpan.Zero,TimeSpan.Zero,TimeSpan.Zero),windowEvents:events);
    }
    [Fact]
    public async Task LocationEventTranslatesCompletedPlacementWithoutCaptureOrNewPackToken()
    {
        using var overlay=CaptureOverlay(); var fake=new FakeArenaCapture(); var events=new FakeReliabilityEvents();
        var moves=new List<(int,int,double,double)>(); await using var session=ReliabilitySession(overlay,fake,events,moves);
        session.RetryManually(); await session.CurrentWork;
        var context=overlay.PlacementContext; var rows=overlay.Badges.ToArray(); var positions=rows.Select(b=>(b.X,b.Y)).ToArray();
        var captures=fake.Captures;
        fake.Inspection=fake.Inspection with {Window=fake.Inspection.Window! with {X=450,Y=170}}; events.Emit();
        Assert.Equal(captures,fake.Captures); Assert.Same(context,overlay.PlacementContext);
        Assert.All(overlay.Badges,b=>Assert.True(b.IsPlaced)); Assert.Equal(rows,overlay.Badges);
        Assert.Equal(positions,overlay.Badges.Select(b=>(b.X,b.Y))); Assert.Equal((550,230,800d,480d),moves[^1]);
        var count=moves.Count; events.Emit(); Assert.Equal(count,moves.Count); Assert.Equal(captures,fake.Captures);
    }
    [Fact]
    public async Task ClientResizeEventInvalidatesEvenManualPlacementAndRequestsVisualLocalization()
    {
        using var overlay=CaptureOverlay(); var fake=new FakeArenaCapture(); var events=new FakeReliabilityEvents();
        await using var session=ReliabilitySession(overlay,fake,events,[]); session.RetryManually(); await session.CurrentWork;
        var context=overlay.PlacementContext!;
        Assert.True(overlay.ConfirmVisualOrder(context,overlay.Badges.Select(b=>b.OccurrenceKey!.Value)));
        fake.Inspection=fake.Inspection with {Window=fake.Inspection.Window! with {Width=1001}}; events.Emit(); await session.CurrentWork;
        Assert.False(overlay.HasConfirmedVisualPlacement); Assert.True(fake.Captures>=2);
        Assert.Same(context,overlay.PlacementContext); Assert.Equal(1001,fake.LastWindow!.Width);
    }
    [Fact]
    public async Task DpiChangeEventIsNotTreatedAsTranslation()
    {
        using var overlay=CaptureOverlay(); var fake=new FakeArenaCapture(); var events=new FakeReliabilityEvents(); var moves=new List<(int,int,double,double)>();
        await using var session=ReliabilitySession(overlay,fake,events,moves); session.RetryManually(); await session.CurrentWork;
        fake.Inspection=fake.Inspection with {Window=fake.Inspection.Window! with {X=600,Scaling=1.25}};
        events.Emit(); await session.CurrentWork;
        Assert.True(fake.Captures>=2); Assert.Equal(640,moves[^1].Item3); Assert.Equal(384,moves[^1].Item4);
    }
    [Fact]
    public async Task LostWindowThenReplacementRecoversWithFreshVisualRequest()
    {
        using var overlay=CaptureOverlay(); var fake=new FakeArenaCapture(); var original=fake.Inspection.Window!;
        var events=new FakeReliabilityEvents(); var visibility=new List<bool>();
        await using var session=ReliabilitySession(overlay,fake,events,[],visibility); session.RetryManually(); await session.CurrentWork;
        Assert.True(overlay.ConfirmVisualOrder(overlay.PlacementContext!,overlay.Badges.Select(b=>b.OccurrenceKey!.Value)));
        fake.Inspection=new(false,null); events.Emit(); Assert.Null(events.Target);
        Assert.False(visibility[^1]); Assert.All(overlay.Badges,b=>Assert.False(b.IsPlaced));
        Assert.False(overlay.HasConfirmedVisualPlacement);
        fake.Inspection=new(true,original with {Handle=77,ProcessId=8}); session.RefreshWindow(); await session.CurrentWork;
        Assert.Equal((nint)77,events.Target!.Handle); Assert.Equal(8,events.Target.ProcessId);
        Assert.True(fake.Captures>=2); Assert.Equal((nint)77,fake.LastWindow!.Handle);
        Assert.Equal(TimeSpan.FromSeconds(2),AutomaticCardLocalizationSession.WindowHealthInterval);
    }
    [Fact]
    public async Task ForegroundEventRetainsVisiblePlacementAndDisposeStopsEventUpdates()
    {
        using var overlay=CaptureOverlay(); var fake=new FakeArenaCapture(); var events=new FakeReliabilityEvents();
        var session=ReliabilitySession(overlay,fake,events,[]); session.RetryManually(); await session.CurrentWork;
        var captures=fake.Captures; fake.Inspection=fake.Inspection with {Window=fake.Inspection.Window! with {IsForeground=true}};
        events.Emit(); Assert.All(overlay.Badges,b=>Assert.True(b.IsPlaced)); Assert.Equal(captures,fake.Captures);
        await session.DisposeAsync(); var inspections=fake.Inspections; events.Emit();
        Assert.True(events.Disposed); Assert.Equal(inspections,fake.Inspections);
    }
}
