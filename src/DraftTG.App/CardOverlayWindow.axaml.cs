using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

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
    // Layout/visibility evidence, not proof of compositor pixels or absence of external occlusion.
    internal object ReadBadgeRenderDiagnostics() => new
    {
        WindowVisible = IsVisible, SurfaceWidth = ClientSize.Width, SurfaceHeight = ClientSize.Height,
        Cards = ViewModel.Badges.Select(b =>
        {
            var root = this.GetVisualDescendants().OfType<Grid>().FirstOrDefault(c => c.Name == "BadgeRoot" && ReferenceEquals(c.DataContext, b));
            return new { PackIndex = b.OccurrenceKey?.PackIndex, CardIdentity = b.OccurrenceKey?.CardIdentifier.Value,
                b.IsPlaced, VisualCreated = root is not null,
                AncestorsVisible = root is not null && root.IsVisible && root.GetVisualAncestors().OfType<Control>().All(c => c.IsVisible),
                MeasuredWidth = root?.Bounds.Width, MeasuredHeight = root?.Bounds.Height,
                IntersectsSurface = b.Width > 0 && b.X < ClientSize.Width && b.X + b.Width > 0 && b.Y < ClientSize.Height && b.Y + 42 > 0 };
        }).ToArray()
    };
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
