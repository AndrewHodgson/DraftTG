using Avalonia;

namespace DraftTG.App;

internal static class Program
{
    internal static CaptureFreeOptions? CaptureFreeOverlayOptions { get; private set; }
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--arena-ui-localization-debug")
        { Environment.ExitCode = ArenaUiLocalizationDebugCommand.Run(args); return; }
        if (args.Length > 0 && args[0] == "--arena-memory-draft-watch")
        { Environment.ExitCode = ArenaMemoryWatchCommand.Run(args); return; }
        if (args.Length > 0 && args[0] == "--capture-free-draft-summary")
        { Environment.ExitCode = CaptureFreeSummaryCommand.Run(args); return; }
        if (args.Length > 0 && args[0] == "--capture-free-draft-overlay")
        {
            try { CaptureFreeOverlayOptions = CaptureFreeOptions.Parse(args); }
            catch (ArgumentException ex) { Console.Error.WriteLine(ex.Message); Environment.ExitCode = 1; return; }
            if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess) { Console.Error.WriteLine("Capture-free overlay requires Windows x64."); Environment.ExitCode = 2; return; }
            CaptureFreeSafety.EnableForProcess(); args = [];
        }
        if (args.Length > 0 && args[0] is "--arena-memory-draft" or "--arena-memory-draft-once")
        { Environment.ExitCode = ArenaMemoryDraftCommand.Run(args); return; }
        if (args.Length > 0 && args[0] == "--capture-deck-builder")
        { Environment.ExitCode = DeckBuilderCaptureAuditCommand.Run(args); return; }
        if (args.Length > 0 && args[0] == "--localize-image")
        { Environment.ExitCode = OfflineArtworkCommand.Run(args); return; }
        if (args.Length > 0 && args[0] == "--compare-arena-capture")
        { Environment.ExitCode = CompareArenaCaptureCommand.Run(args); return; }
        if (args.Length > 0 && args[0] == "--stress-wgc")
        { Environment.ExitCode = WgcStressCommand.Run(args); return; }
        if (args.Length > 0 && args[0] == "--order-evidence-summary")
        { Environment.ExitCode = OrderEvidenceSummaryCommand.Run(args); return; }
        if (args.Length > 0 && args[0] == "--deterministic-shadow-live")
        { Environment.ExitCode = DeterministicShadowLiveCommand.Run(args); return; }
        if (args.Length > 0 && args[0] == "--benchmark-arena-capture")
        { Environment.ExitCode = DesktopDuplicationBenchmarkCommand.Run(args); return; }
        if (args.Length > 0 && args[0] == "--live-localization-validation-summary")
        { Environment.ExitCode = LiveLocalizationValidationSummaryCommand.Run(args); return; }
        if (args.Length > 0 && args[0] == "--live-localization-validation")
        {
            if (args.Length != 1) { Console.Error.WriteLine("Usage: DraftTG.App --live-localization-validation"); Environment.ExitCode = 1; return; }
            LiveLocalizationValidation.Enabled = true;
            Console.WriteLine($"Live shadow validation: {LiveLocalizationValidation.LedgerPath} (WGC authority retained)");
            args = [];
        }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
