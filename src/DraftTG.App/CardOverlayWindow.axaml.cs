using Avalonia.Controls;
using Avalonia.Input;

namespace DraftTG.App;

public sealed partial class CardOverlayWindow : Window
{
    public CardOverlayWindow()
    {
        InitializeComponent();
        SizeChanged += (_, _) =>
        {
            if (DataContext is OverlayViewModel viewModel) viewModel.SetViewport(ClientSize.Width, ClientSize.Height);
        };
    }
    public CardOverlayWindow(OverlayViewModel viewModel) : this() => DataContext = viewModel;
    public OverlayViewModel ViewModel => (OverlayViewModel)DataContext!;
    private void Move_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel.IsCalibrating) WindowDrag.Move(this, e);
    }
    private void Resize_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel.IsCalibrating && sender is Control { Tag: string edge })
            WindowDrag.Resize(this, Enum.Parse<WindowEdge>(edge), e);
    }
}
