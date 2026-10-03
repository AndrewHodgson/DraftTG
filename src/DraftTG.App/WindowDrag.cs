using Avalonia.Controls;
using Avalonia.Input;

namespace DraftTG.App;

internal static class WindowDrag
{
    // Test the decision without manufacturing native pointer events.
    internal static bool ShouldBegin(bool leftPressed, bool isDragRegion) => leftPressed && isDragRegion;
    public static void Move(Window window, PointerPressedEventArgs e)
    {
        if (!ShouldBegin(e.GetCurrentPoint(window).Properties.IsLeftButtonPressed, true)) return;
        window.BeginMoveDrag(e);
        e.Handled = true;
    }
    public static void Resize(Window window, WindowEdge edge, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(window).Properties.IsLeftButtonPressed) return;
        window.BeginResizeDrag(edge, e);
        e.Handled = true;
    }
}
