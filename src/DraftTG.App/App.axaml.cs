using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DraftTG.Application;

namespace DraftTG.App;

public sealed partial class App : Avalonia.Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (desktop.Args is { Length: > 0 } args && args[0] == "--badge-preview")
            {
                desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose;
                desktop.MainWindow = new MysticBadgePreviewWindow(args);
                base.OnFrameworkInitializationCompleted(); return;
            }
            var viewModel = new MainWindowViewModel(
                new DraftTGRuntimeFactory(),
                new AvaloniaUiDispatcher());
            var session = new OverlayDesktopSession(viewModel, OverlayCalibrationService.CreateDefault());
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose;
            desktop.MainWindow = session.Rail;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
