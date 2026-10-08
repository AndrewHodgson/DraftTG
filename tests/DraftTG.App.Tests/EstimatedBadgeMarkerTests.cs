using System.Xml.Linq;

namespace DraftTG.App.Tests;

/// <summary>Phase 3: the visible EST marker. Expected behaviour is stated before each assertion.</summary>
public sealed class EstimatedBadgeMarkerTests
{
    private static XElement BadgeTemplate()
    {
        using var stream = typeof(OverlayHandleTests).Assembly.GetManifestResourceStream("CardOverlayWindow.axaml")!;
        return XElement.Load(stream);
    }

    [Fact] public void ShippedBadgePlacesASecondaryEstMarkerBesideTheScore()
    {
        // Expected: EST sits in the same row as the Pick Score, is bound to the estimate flag, is smaller than the score
        // and uses the neutral secondary text colour (the ALSA colour), not a warning colour.
        var blocks = BadgeTemplate().Descendants().Where(e => e.Name.LocalName == "TextBlock").ToArray();
        var score = Assert.Single(blocks, e => (string?)e.Attribute("Text") == "{Binding Presentation.DisplayedPickScore}");
        var est = Assert.Single(blocks, e => (string?)e.Attribute("Text") == "EST");
        var alsa = Assert.Single(blocks, e => (string?)e.Attribute("Text") == "{Binding Presentation.DisplayedALSA}");
        Assert.Same(score.Parent, est.Parent);
        Assert.Equal("{Binding IsPickScoreEstimate}", (string?)est.Attribute("IsVisible"));
        Assert.True(double.Parse((string)est.Attribute("FontSize")!) < double.Parse((string)score.Attribute("FontSize")!));
        Assert.Equal((string?)alsa.Attribute("Foreground"), (string?)est.Attribute("Foreground"));
    }

    [Theory]
    [InlineData(41, true)] [InlineData(41, false)] [InlineData(30, true)]
    public void EstimateFlagFollowsEvidenceWithoutChangingTheQualityTier(int score, bool estimated)
    {
        // Expected: an estimated badge shows EST and "GIH —" but keeps the animation tier of its score; a direct badge has no EST.
        var badge = MysticBadgePreviewWindow.Sample(1, "Card", score, estimated: estimated);
        Assert.Equal(estimated, badge.IsPickScoreEstimate);
        Assert.Equal(score.ToString(System.Globalization.CultureInfo.InvariantCulture), badge.PickScore);
        Assert.Equal(estimated ? "—" : badge.Presentation!.DisplayedGIH, badge.Presentation!.DisplayedGIH);
        if (estimated) Assert.Equal("—", badge.Presentation.DisplayedGIH);
        Assert.Equal(MysticBadgeTierPalette.For(score), MysticBadgeTierPalette.For(badge.Presentation.PickScore!.Score0To50));
    }

    [Fact] public void UnavailableScoresNeverShowEst()
    {
        // Expected: with no score at all (dash), there is nothing to qualify, so EST stays hidden.
        Assert.False(MysticBadgePreviewWindow.Sample(2, "Card", null, estimated: true).IsPickScoreEstimate);
    }
}
