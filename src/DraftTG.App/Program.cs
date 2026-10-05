using Avalonia;

namespace DraftTG.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--capture-deck-builder")
        { Environment.ExitCode = DeckBuilderCaptureAuditCommand.Run(args); return; }
        if (args.Length > 0 && args[0] == "--localize-image")
        { Environment.ExitCode = OfflineArtworkCommand.Run(args); return; }
        if (args.Length > 0 && args[0] == "--compare-arena-capture")
        { Environment.ExitCode = CompareArenaCaptureCommand.Run(args); return; }
        if (args.Length > 0 && args[0] == "--stress-wgc")
        { Environment.ExitCode = WgcStressCommand.Run(args); return; }
        if (args.Length > 0 && args[0] == "--benchmark-arena-capture")
        { Environment.ExitCode = DesktopDuplicationBenchmarkCommand.Run(args); return; }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
