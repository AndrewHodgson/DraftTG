using System.Text.Json;

namespace DraftTG.App.Tests;

public sealed partial class MainWindowViewModelTests
{
    [Fact]
    public void BadgeStageAuditDistinguishesMissingMatcherPlacementFromAbsentScoreWithoutChangingBadges()
    {
        using var overlay = CaptureOverlay();
        var context = overlay.PlacementContext!;
        var request = new DraftTG.Application.CardVisualLocalizationRequest(context.Pack, overlay.Session.CurrentPackPresentations, overlay.Layout)
        { PackGeneration = context.Generation };
        var complete = ReliabilityResult(request);
        var partial = complete with { Matches = complete.Matches.Skip(1).ToArray() };
        Assert.True(overlay.ApplyAutomaticLocalization(context, partial));
        var before = overlay.Badges.Select(b => (b.OccurrenceKey, b.IsPlaced, b.X, b.Y)).ToArray();
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ProductionBadgeDiagnostics.Capture(overlay, context, partial)));
        var cards = json.RootElement.GetProperty("Cards").EnumerateArray().ToArray();
        Assert.Equal(request.Occurrences.Count, cards.Length);
        Assert.All(cards, c => Assert.True(c.GetProperty("BadgeModelCreated").GetBoolean()));
        var missingIndex = complete.Matches[0].Key.PackIndex;
        var missing = cards.Single(c => c.GetProperty("PackIndex").GetInt32() == missingIndex);
        Assert.False(missing.GetProperty("FullMatcherIdentified").GetBoolean());
        Assert.False(missing.GetProperty("PlacementAccepted").GetBoolean());
        Assert.Contains(cards, c => !c.GetProperty("ScoreProduced").GetBoolean() && c.GetProperty("PlacementAccepted").GetBoolean());
        Assert.Equal(before, overlay.Badges.Select(b => (b.OccurrenceKey, b.IsPlaced, b.X, b.Y)));
    }
}
