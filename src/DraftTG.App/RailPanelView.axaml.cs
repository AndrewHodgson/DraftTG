using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DraftTG.App;

public sealed partial class RailPanelView : UserControl
{
    public RailPanelView() => InitializeComponent();
    public event Action<string>? ActionRequested;
    private void Action_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string action }) ActionRequested?.Invoke(action);
    }
}
