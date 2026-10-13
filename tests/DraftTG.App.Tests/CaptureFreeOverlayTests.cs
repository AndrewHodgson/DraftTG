using System.Text.Json;
using DraftTG.Application;
using DraftTG.App.Platform;
using DraftTG.ArenaIntegration.Memory;

namespace DraftTG.App.Tests;

public sealed partial class MainWindowViewModelTests
{
    private static OverlayViewModel MemoryOverlay()
    {
        var (data, _) = FourBadgeFixture(); var runtime = AssociationSession(data.Catalog);
        var overlay = new OverlayViewModel(runtime, captureFree: true);
        runtime.ApplySessionUpdate(AssociationPack(data, Enumerable.Range(0, 14).Select(i => new[] { 604, 601, 604, 602 }[i % 4]).ToArray()));
        return overlay;
    }
    [Fact]
    public void MemorySlotsAssociateByOrderedOccurrenceIndexAndKeepRendererModels()
    {
        using var overlay = MemoryOverlay(); var context = overlay.PlacementContext!;
        var layout = CaptureFreeDraftLayout.Map(1600, 900, 14, "list", true);
        overlay.SetViewport(1600, 900);
        Assert.True(overlay.ApplyMemoryPlacement(context, layout));
        Assert.Equal(Enumerable.Range(0, 14), overlay.Badges.Select(b => b.VisualSlotIndex!.Value));
        Assert.All(overlay.Badges, b => Assert.Same(overlay.Session.CurrentPackPresentations[b.Index], b.Presentation));
        Assert.False(overlay.HasAutomaticVisualPlacement); Assert.False(overlay.CanUseManualPositions);
    }
    [Fact]
    public void ResizeReusesCurrentPackAndRetirementRejectsStalePlacementContext()
    {
        using var overlay = MemoryOverlay(); var context = overlay.PlacementContext!;
        overlay.SetViewport(1600, 900);
        Assert.True(overlay.ApplyMemoryPlacement(context, CaptureFreeDraftLayout.Map(1600, 900, 14, "list", true)));
        var original = overlay.Badges[0]; var x = original.X;
        overlay.SetViewport(1280, 720);
        Assert.True(overlay.ApplyMemoryPlacement(context, CaptureFreeDraftLayout.Map(1280, 720, 14, "list", true)));
        Assert.Same(original, overlay.Badges[0]); Assert.NotEqual(x, original.X);
        overlay.Session.InvalidateSubmittedPack(AcceptedCurrentPick(overlay));
        Assert.All(overlay.Badges, b => Assert.False(b.IsPlaced));
        Assert.False(overlay.ApplyMemoryPlacement(context, CaptureFreeDraftLayout.Map(1600, 900, 14, "list", true)));
    }
    [Fact]
    public void UnsupportedMemoryLayoutHasNoVisualFallback()
    {
        using var overlay = MemoryOverlay();
        Assert.True(overlay.ApplyMemoryPlacement(overlay.PlacementContext!, CaptureFreeDraftLayout.Map(1600, 900, 14, "list", true)));
        var declined = CaptureFreeDraftLayout.Map(1600, 900, 14, "column", true);
        Assert.False(overlay.ApplyMemoryPlacement(overlay.PlacementContext!, declined));
        overlay.RetireMemoryPlacement(declined.Decline!);
        Assert.All(overlay.Badges, b => Assert.False(b.IsPlaced));
        Assert.False(overlay.HasAutomaticVisualPlacement);
    }
    [Fact]
    public void CaptureFreeScopeRejectsFactoriesAndConcreteBackendsBeforeNativeInitialization()
    {
        using var scope = CaptureFreeSafety.TestScope();
        Assert.Throws<InvalidOperationException>(() => WindowsCaptureFactory.Create(null, false));
        Assert.Throws<InvalidOperationException>(() => new WindowsGraphicsCaptureFrameCapture());
        Assert.Throws<InvalidOperationException>(() => new GdiBitBltFrameCapture());
        Assert.Throws<InvalidOperationException>(() => new DesktopDuplicationFrameCapture());
        using var overlay = MemoryOverlay();
        Assert.Throws<InvalidOperationException>(() => new AutomaticCardLocalizationSession(overlay, () => null, (_, _, _, _) => { }, _ => { }));
        Assert.Equal(5, CaptureFreeSafety.RejectedCaptureAttempts);
    }
    [Fact]
    public void MissingCatalogCardIsExplicitRatherThanHidden()
    {
        var s = new ArenaDraftMemorySnapshot(ArenaMemoryStatus.Success, "fixture", 123, null, null, DateTimeOffset.UnixEpoch,
            true, "list", new uint[] { 123456 }, "fixture", TimeSpan.Zero, 0);
        var card = Assert.Single(new MemoryCardNames(null, null).Resolve(s));
        Assert.False(card.Resolved); Assert.Equal("<unresolved>", card.Name); Assert.NotNull(card.Reason);
    }
    [Fact]
    public void SummaryDoesNotCountRepeatedPollsOrSyntheticRunsTowardThirtyPackGate()
    {
        static JsonElement Row(string kind, object data, string run = "live") => JsonSerializer.SerializeToElement(new { Kind = kind, RunId = run, Data = data });
        var rows = new List<JsonElement> { Row("RunStarted", new { Source = "NativeWindowsMemory", CaptureBlocked = true }),
            Row("RunStarted", new { Source = "Synthetic", CaptureBlocked = true }, "fixture") };
        for (var i = 0; i < 40; i++)
        {
            rows.Add(Row("Memory", new { Stable = true, Generation = 2, Consistency = "Full checks + two coherent reads", RuntimeProfile = "fixture",
                OrderedGrpIds = Enumerable.Range(100, 14).ToArray(), SemanticPackKey = "one", CardCount = 14, DraftId = "draft", ViewMode = "list", Names = Array.Empty<object>() }));
            rows.Add(Row("Memory", new { Stable = true, SemanticPackKey = "fixture" + i, CardCount = 14 }, "fixture"));
        }
        rows.Add(Row("RunEnded", new { ScreenCaptureInitialized = false, RejectedCaptureAttempts = 0 }));
        var summary = CaptureFreeSummaryCommand.Summarize(rows);
        Assert.Contains("distinct semantic packs: 1", summary); Assert.Contains("Technical live gate: NOT PASSED", summary);
        Assert.Contains("Screen capture initialized: NO", summary);
    }
    [Fact]
    public void WindowedGeometryRequiresTheCompleteCaptionStyle()
    {
        Assert.True(ArenaWindowInspector.IsWindowedStyle(0xC00000));
        Assert.False(ArenaWindowInspector.IsWindowedStyle(0));
        Assert.False(ArenaWindowInspector.IsWindowedStyle(0x800000));
    }
    [Fact]
    public void SummaryGateRequiresPlacementReviewsAndRejectsCaptureAttemptsAndMismatches()
    {
        static JsonElement Row(string kind, object data) => JsonSerializer.SerializeToElement(new { Kind = kind, RunId = "fixture-live", Data = data });
        var rows = new List<JsonElement> { Row("RunStarted", new { Source = "NativeWindowsMemory", CaptureBlocked = true }) };
        for (var i = 0; i < 30; i++)
        {
            rows.Add(Row("Memory", new { Stable = true, Generation = i + 1, Consistency = "Full checks + two coherent reads", RuntimeProfile = "fixture",
                OrderedGrpIds = Enumerable.Range(100, 14).ToArray(), CardCount = 14, SemanticPackKey = "pack" + i, DraftId = "draft" + i / 15,
                Client = new { Width = i < 15 ? 1600 : 1280, Height = i < 15 ? 900 : 720 } }));
            rows.Add(Row("Placement", new { Placed = true, SemanticPackKey = "pack" + i, Profile = "fixture", GeometryKey = "size" + i / 15 }));
        }
        rows.Add(Row("RunEnded", new { ScreenCaptureInitialized = false, RejectedCaptureAttempts = 0 }));
        Assert.Contains("Technical live gate: NOT PASSED", CaptureFreeSummaryCommand.Summarize(rows));
        for (var i = 0; i < 30; i++) rows.Add(Row("ManualReview", new { SemanticPackKey = "pack" + i, Profile = "fixture", GeometryKey = "size" + i / 15, OrderCorrect = true, PlacementCorrect = true }));
        Assert.Contains("Technical live gate: PASS", CaptureFreeSummaryCommand.Summarize(rows));
        var wrong = Row("ManualReview", new { OrderCorrect = false, PlacementCorrect = false });
        rows.Add(wrong); Assert.Contains("Technical live gate: NOT PASSED", CaptureFreeSummaryCommand.Summarize(rows)); rows.RemoveAt(rows.Count - 1);
        rows.Add(Row("RunEnded", new { ScreenCaptureInitialized = false, RejectedCaptureAttempts = 1 }));
        Assert.Contains("Technical live gate: NOT PASSED", CaptureFreeSummaryCommand.Summarize(rows));
    }
}
