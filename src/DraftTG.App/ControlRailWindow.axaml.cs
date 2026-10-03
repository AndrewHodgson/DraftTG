using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace DraftTG.App;

public sealed partial class ControlRailWindow : Window
{
    private readonly Flyout _flyout;
    private readonly RailPanelView _panel = new();
    public ControlRailWindow()
    {
        InitializeComponent();
        _panel.ActionRequested += action => ActionRequested?.Invoke(action);
        _flyout = new Flyout { Content = _panel, Placement = PlacementMode.Right };
        _flyout.Closed += (_, _) =>
        {
            if (!_flyout.IsOpen && DataContext is OverlayViewModel viewModel) viewModel.SetPanel(RailPanel.None);
        };
    }
    public ControlRailWindow(OverlayViewModel viewModel) : this()
    {
        DataContext = viewModel;
        _panel.DataContext = viewModel;
    }
    public OverlayViewModel ViewModel => (OverlayViewModel)DataContext!;
    public event Action<string>? ActionRequested;
    public void OpenPanel(RailPanel panel)
    {
        _flyout.Hide();
        ViewModel.SetPanel(panel);
        _flyout.ShowAt(this);
    }
    public void ClosePanel() { _flyout.Hide(); ViewModel.SetPanel(RailPanel.None); }
    private void Drag_OnPointerPressed(object? sender, PointerPressedEventArgs e) => WindowDrag.Move(this, e);
    private void Status_OnClick(object? sender, RoutedEventArgs e) => OpenPanel(RailPanel.Status);
    private void History_OnClick(object? sender, RoutedEventArgs e) => OpenPanel(RailPanel.History);
    private void Calibration_OnClick(object? sender, RoutedEventArgs e) => ActionRequested?.Invoke("edit");
    private void Visibility_OnClick(object? sender, RoutedEventArgs e) => ViewModel.ToggleStatistics();
    private void Close_OnClick(object? sender, RoutedEventArgs e) => Close();
}
