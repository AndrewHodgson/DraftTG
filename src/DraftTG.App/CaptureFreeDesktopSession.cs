using System.ComponentModel;
using Avalonia;
using Avalonia.Threading;
using DraftTG.Application;
using DraftTG.ArenaIntegration.Memory;
using DraftTG.App.Platform;

namespace DraftTG.App;

/// <summary>Opt-in shell reusing the existing VM/scoring/badge windows, with no visual-localization objects.</summary>
internal sealed class CaptureFreeDesktopSession
{
    private readonly MainWindowViewModel runtime;
    private readonly OverlayViewModel presentation;
    private readonly CardOverlayWindow cards;
    private readonly OverlayInteractionController interaction;
    private readonly CaptureFreeOptions options;
    private readonly CaptureFreeEvidenceLedger ledger;
    private readonly DispatcherTimer health = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly ArenaWindowEventPump? events;
    private ArenaWindowGeometry? window;
    private CaptureFreeDraftSession? source;
    private bool closing, allowClose;
    private string? lastPlacement;
    private CaptureFreeDraftLayoutResult? lastLayout;
    private string? geometryKey;
    private string? rejectedPack;

    internal CaptureFreeDesktopSession(MainWindowViewModel runtime, CaptureFreeOptions options)
    {
        if (!CaptureFreeSafety.Enabled) throw new InvalidOperationException("Capture-free guard must precede composition.");
        this.runtime = runtime; this.options = options;
        ledger = new(options.Ledger, "Overlay");
        presentation = new(runtime, captureFree: true);
        Rail = new(presentation); cards = new(presentation);
        interaction = new(ClickThroughWindowControllerFactory.Create(cards));
        if (OperatingSystem.IsWindows()) events = new(new WindowsArenaWindowEvents(), a => Dispatcher.UIThread.Post(a), RefreshWindow);
        health.Tick += (_, _) => RefreshWindow();
        runtime.PropertyChanged += RuntimeChanged;
        runtime.PackPresentationChanged += PresentationChanged;
        runtime.CaptureFreeStaleResultRejected += Stale;
        presentation.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(OverlayViewModel.ShowBadges)) Visibility(); };
        Rail.ActionRequested += Action;
        Rail.Opened += (_, _) =>
        {
            if (Rail.Screens.Primary is { } screen) Rail.Position = new(screen.WorkingArea.X + 12, screen.WorkingArea.Y + 60);
            Rail.OpenPanel(RailPanel.Status);
            RefreshWindow(); health.Start(); runtime.Start();
            if (options.Seconds is { } seconds) _ = CloseAfter(seconds);
        };
        Rail.Closing += Closing;
        cards.Closing += (_, e) => { if (!allowClose) { e.Cancel = true; Rail.Close(); } };
        Rail.Screens.Changed += (_, _) => RefreshWindow();
        Console.WriteLine("Capture-free draft overlay: ENABLED\nScreen capture localization: DISABLED\nWGC: DISABLED\nGDI fallback: DISABLED\nArena memory: connecting");
        Console.WriteLine("Live evidence: " + options.Ledger);
        presentation.SetCaptureRuntimeDiagnostics("Capture-free mode. WGC/GDI/artwork/visual verifier: DISABLED. No capture fallback.");
    }
    internal ControlRailWindow Rail { get; }
    private async Task CloseAfter(int seconds)
    { await Task.Delay(TimeSpan.FromSeconds(seconds)); await Dispatcher.UIThread.InvokeAsync(() => { if (!closing) Rail.Close(); }); }
    private void Stale() => ledger.Record("StaleUiRejected", new { source?.Generation });
    private void RuntimeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.CaptureFreeSession) || runtime.CaptureFreeSession is not { } session) return;
        source = session; source.Observed += Observed; source.Watcher.PollObserved += ledger.Poll;
        source.Diagnostic += Diagnostic;
        source.PickRetired += PickRetired;
    }
    private void PickRetired() => ledger.Record("PickAccepted", new { source?.Generation });
    private void Diagnostic(string message) => ledger.Record("SourceDiagnostic", new { Message = message });
    private void Observed(ArenaDraftMemoryUpdate update, CaptureFreeReconciliation? reconciliation) => Dispatcher.UIThread.Post(() =>
    {
        if (closing) return;
        if (source?.MemoryUpdate?.Generation != update.Generation) { Stale(); return; }
        var names = new MemoryCardNames(runtime.CardResolver, runtime.CardCatalog).Resolve(update.CurrentPack ?? update.CurrentRead);
        ledger.Memory(update, names, reconciliation, window);
        var stable = update.CurrentPack is { };
        var text = $"Arena memory: {(stable ? "CONNECTED" : update.CurrentRead.Status.ToString())}; {update.Transition}\n"
            + $"Draft: {update.CurrentRead.DraftSessionId ?? update.CurrentRead.CourseId ?? reconciliation?.State.DraftIdentifier?.Value ?? "unavailable"}; "
            + $"Pack/Pick: {update.CurrentRead.PackNumber?.ToString() ?? "log"}/{update.CurrentRead.PickNumber?.ToString() ?? "log"}\n"
            + $"View: {update.CurrentRead.ViewMode ?? "unavailable"}; Cards: {update.CurrentPack?.OrderedOfferedGrpIds.Count ?? 0}; "
            + $"Memory generation: {update.Generation}; Stable read: {(stable ? "yes" : "no")}\n"
            + $"Reconciliation: {reconciliation?.Status}; {reconciliation?.Reason}";
        Console.WriteLine(text);
        if (options.Verbose && stable) MemoryCardNames.Print(names);
        presentation.SetCaptureRuntimeDiagnostics(text + "\nWGC: DISABLED; GDI fallback: DISABLED; capture initialization attempts: " + CaptureFreeSafety.RejectedCaptureAttempts);
        Place();
    });
    private void PresentationChanged(bool newPack) { if (!closing) Place(); }
    private void RefreshWindow()
    {
        if (closing) return;
        var inspection = OperatingSystem.IsWindows() ? ArenaWindowInspector.InspectWindow() : new ArenaWindowInspection(false, null, "Windows only");
        var previous = window; window = inspection.Window; events?.Track(window);
        if (window is null || !window.IsVisible || window.IsMinimized)
        {
            if (previous is not null) ledger.Record("WindowUnavailable", new { Reason = inspection.Diagnostic ?? "Hidden/minimized/exited" });
            Place(); return;
        }
        if (previous is not null)
        {
            if (previous.Scaling != window.Scaling) ledger.Record("DPI", new { Before = previous.Scaling * 96, After = window.Scaling * 96 });
            if (previous.Width != window.Width || previous.Height != window.Height)
                ledger.Record("Resize", new { Before = $"{previous.Width}x{previous.Height}", After = $"{window.Width}x{window.Height}", CaptureRequested = false });
            else if (previous.X != window.X || previous.Y != window.Y)
                ledger.Record("Move", new { window.X, window.Y, IdentityReadRequested = false, CaptureRequested = false });
        }
        cards.Position = new(window.X, window.Y);
        cards.Width = window.Width / window.Scaling; cards.Height = window.Height / window.Scaling;
        presentation.SetViewport(cards.Width, cards.Height);
        if (source?.MemoryUpdate is { CurrentPack: not null } memory && !ArenaWindowGeometry.SamePlacementGeometry(previous, window))
            ledger.Memory(memory, new MemoryCardNames(runtime.CardResolver, runtime.CardCatalog).Resolve(memory.CurrentPack), source.Reconciliation, window);
        Place();
    }
    private void Place()
    {
        var memory = source?.MemoryUpdate;
        var current = memory?.CurrentPack;
        var context = presentation.PlacementContext;
        var reason = "Waiting for a coherent reconciled memory pack";
        var rejected = memory?.SemanticPackKey is { } packKey && packKey == rejectedPack;
        if (rejected) reason = "Manual comparison reported an order/placement mismatch; this pack is withheld";
        CaptureFreeDraftLayoutResult? layout = null;
        if (!rejected && current is not null && source?.Reconciliation?.CanScore == true && runtime.CaptureFreePresentationGeneration == source.Generation
            && context is not null && window is { IsVisible: true, IsMinimized: false } w && w.ProcessId == current.ProcessId)
        {
            layout = CaptureFreeDraftLayout.Map(w.Width, w.Height, current.OrderedOfferedGrpIds.Count, current.ViewMode, w.IsWindowed);
            reason = layout.Decline ?? "Current memory order at measured slots";
        }
        var placed = layout?.IsAvailable == true && context is not null && presentation.ApplyMemoryPlacement(context, layout);
        if (!placed) presentation.RetireMemoryPlacement(reason);
        presentation.SetCalibrationState(false, placed);
        lastLayout = layout;
        geometryKey = window is null ? null : $"{window.Width}x{window.Height}@{window.Scaling * 96}";
        var key = $"{source?.Generation}:{memory?.Generation}:{geometryKey}:{placed}:{reason}";
        if (key != lastPlacement)
        {
            lastPlacement = key;
            ledger.Record("Placement", new { Placed = placed, Reason = reason, memory?.Generation, source?.Reconciliation?.Status,
                memory?.SemanticPackKey, Profile = layout?.Profile, GeometryKey = geometryKey });
        }
        Visibility();
        var generation = source?.Generation;
        Dispatcher.UIThread.Post(() =>
        {
            if (closing || generation != source?.Generation) { if (!closing) Stale(); return; }
            ledger.Record("BadgeStages", new { memory?.Generation, memory?.SemanticPackKey,
                Cards = presentation.Badges.Select(b => new { b.Index, b.Name,
                    MemoryGrpId = current?.OrderedOfferedGrpIds.ElementAtOrDefault(b.Index),
                    CatalogResolved = b.Presentation?.Card is not null, StatsAvailable = b.Presentation?.RawStatistics is not null,
                    ScoreGenerated = b.Presentation?.PickScore?.Score0To50 is not null, b.Presentation?.IsLoading,
                    BadgeModelGenerated = true, SlotMapped = b.IsPlaced,
                    MissingRatingReason = b.Presentation?.IsLoading == true ? "Loading" : b.Presentation?.PickScore?.Availability.ToString() ?? "Score unavailable" }).ToArray(),
                Render = cards.ReadBadgeRenderDiagnostics() });
        }, DispatcherPriority.Background);
    }
    private void Visibility()
    {
        if (closing) return;
        if (!presentation.ShowBadges || window is null || !window.IsVisible || window.IsMinimized) { cards.Hide(); return; }
        if (!interaction.TryApply(false, out var error)) { cards.Hide(); presentation.SetDiagnostic(error); return; }
        if (!cards.IsVisible) cards.Show();
        if (!interaction.TryApply(false, out error) || cards.ActualTransparencyLevel != Avalonia.Controls.WindowTransparencyLevel.Transparent)
        { cards.Hide(); presentation.SetDiagnostic(error ?? "Transparent surface unavailable"); }
    }
    private void Action(string action)
    {
        if (action == "close-panel") Rail.ClosePanel();
        else if (action == "capture-free-confirm" && source?.MemoryUpdate is { CurrentPack: not null } memory
            && runtime.CaptureFreePresentationGeneration == source.Generation)
        {
            ledger.Record("ManualReview", new { memory.SemanticPackKey, memory.Generation, Profile = lastLayout?.Profile, GeometryKey = geometryKey,
                OrderCorrect = true, PlacementCorrect = lastLayout?.IsAvailable == true && cards.IsVisible
                    && presentation.Badges.Count == memory.CurrentPack.OrderedOfferedGrpIds.Count && presentation.Badges.All(b => b.IsPlaced) ? true : (bool?)null });
            rejectedPack = null; Place();
        }
        else if (action == "capture-free-mismatch" && source?.MemoryUpdate is { CurrentPack: not null } mismatch)
        {
            ledger.Record("ManualReview", new { mismatch.SemanticPackKey, mismatch.Generation, Profile = lastLayout?.Profile, GeometryKey = geometryKey,
                OrderCorrect = false, PlacementCorrect = false });
            rejectedPack = mismatch.SemanticPackKey; Place();
        }
        else presentation.SetDiagnostic("Capture-free mode: manual calibration and screen-capture actions are disabled.");
    }
    private async void Closing(object? sender, Avalonia.Controls.WindowClosingEventArgs e)
    {
        if (allowClose) return;
        e.Cancel = true; if (closing) return; closing = true; health.Stop(); cards.Hide(); events?.Dispose();
        if (source is not null) { source.Observed -= Observed; source.Watcher.PollObserved -= ledger.Poll; source.Diagnostic -= Diagnostic; source.PickRetired -= PickRetired; }
        runtime.PropertyChanged -= RuntimeChanged; runtime.PackPresentationChanged -= PresentationChanged; runtime.CaptureFreeStaleResultRejected -= Stale;
        try { await runtime.DisposeAsync(); }
        finally { ledger.Dispose(); presentation.Dispose(); interaction.Dispose(); allowClose = true; cards.Close(); Rail.Close(); }
    }
}
