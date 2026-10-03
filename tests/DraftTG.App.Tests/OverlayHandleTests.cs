using System.Xml.Linq;
using Avalonia.Controls;

namespace DraftTG.App.Tests;

public sealed class OverlayHandleTests
{
    // Inspect the actual shipped view without creating native windows in ordinary tests.
    private static XElement View()
    {
        using var stream = typeof(OverlayHandleTests).Assembly.GetManifestResourceStream("CardOverlayWindow.axaml")!;
        return XElement.Load(stream);
    }

    [Theory]
    [InlineData("Left", "Top", WindowEdge.NorthWest)]
    [InlineData(null, "Top", WindowEdge.North)]
    [InlineData("Right", "Top", WindowEdge.NorthEast)]
    [InlineData("Right", null, WindowEdge.East)]
    [InlineData("Right", "Bottom", WindowEdge.SouthEast)]
    [InlineData(null, "Bottom", WindowEdge.South)]
    [InlineData("Left", "Bottom", WindowEdge.SouthWest)]
    [InlineData("Left", null, WindowEdge.West)]
    public void EveryVisibleHandleUsesTheCorrectNativeEdge(string? horizontal, string? vertical, WindowEdge expected)
    {
        var handles = View().Descendants().Where(e => (string?)e.Attribute("PointerPressed") == "Resize_OnPointerPressed").ToArray();
        Assert.Equal(8, handles.Length);
        var handle = Assert.Single(handles, e => (string?)e.Attribute("HorizontalAlignment") == horizontal
            && (string?)e.Attribute("VerticalAlignment") == vertical);
        Assert.Equal(expected, Enum.Parse<WindowEdge>((string)handle.Attribute("Tag")!));
        Assert.InRange(double.Parse((string)(handle.Attribute("Width") ?? handle.Attribute("Height"))!,
            System.Globalization.CultureInfo.InvariantCulture), 12, 20);
        Assert.NotNull(handle.Attribute("Background")); // Transparent backgrounds still receive pointer input.
        Assert.NotNull(handle.Attribute("Cursor"));
    }

    [Fact]
    public void WindowAllowsManualResizeAndBadgesRemainVisualOnly()
    {
        var view = View();
        Assert.Equal("True", (string?)view.Attribute("CanResize"));
        Assert.Equal("Manual", (string?)view.Attribute("SizeToContent"));
        Assert.Equal("False", (string?)view.Attribute("ShowActivated"));
        var badges = Assert.Single(view.Descendants(), e => (string?)e.Attribute("ItemsSource") == "{Binding Badges}");
        Assert.Equal("False", (string?)badges.Attribute("IsHitTestVisible"));
        Assert.Equal("False", (string?)badges.Attribute("Focusable"));
        Assert.DoesNotContain(badges.DescendantsAndSelf().Attributes(), a => a.Name.LocalName is "PointerPressed" or "ToolTip.Tip");
    }

    [Fact]
    public void EveryCalibrationVisualBelongsToOneVisibilityGateAndBadgesAreOutsideIt()
    {
        var view = View();
        var root = Assert.Single(view.Elements(), e => e.Name.LocalName == "Grid");
        var layer = Assert.Single(root.Elements(), e => e.Name.LocalName == "Grid");
        Assert.Equal("{Binding IsCalibrating}", (string?)layer.Attribute("IsVisible"));
        Assert.Equal("Transparent", (string?)view.Attribute("Background"));
        Assert.Null(root.Attribute("Background"));
        Assert.Equal(2, root.Elements().Count()); // Badge renderer plus the gated calibration subtree.
        Assert.Single(root.Elements(), e => (string?)e.Attribute("ItemsSource") == "{Binding Badges}");
        Assert.Single(layer.Descendants(), e => (string?)e.Attribute("ItemsSource") == "{Binding Guides}");
        Assert.Single(layer.Descendants(), e => (string?)e.Attribute("PointerPressed") == "Move_OnPointerPressed");
        Assert.Equal(8, layer.Descendants().Count(e => (string?)e.Attribute("PointerPressed") == "Resize_OnPointerPressed"));
        Assert.DoesNotContain(layer.Descendants(), e => (string?)e.Attribute("ItemsSource") == "{Binding Badges}");
    }
}
