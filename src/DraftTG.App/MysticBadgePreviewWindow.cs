using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using DraftTG.Application;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;
using SkiaSharp;

namespace DraftTG.App;

/// <summary>Explicit offline developer route. Uses the shipped badge template, with no draft runtime.</summary>
internal sealed class MysticBadgePreviewWindow : Window
{
    private readonly Control _preview;
    private readonly Button _toggle;
    private readonly TextBlock _diagnostics;
    private MysticRatingBackground[] _effects = [];
    private readonly CancellationTokenSource _closed = new();
    private bool _animated = true;
    private double _lastDiagnostics;
    public MysticBadgePreviewWindow(string[] args)
    {
        Title = "DraftTG — Mystic badge preview"; Width = 1060; Height = 580; CanResize = false;
        RequestedThemeVariant = ThemeVariant.Dark;
        Background = Brush("#10151D"); WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var template = ((Grid)new CardOverlayWindow().Content!).Children.OfType<ItemsControl>().First().ItemTemplate!;
        var cards = new Grid { ColumnDefinitions = new("*,*,*,*,*"), RowDefinitions = new("110,110,110") };
        var names = new[] { "Rated Card", "Below Top Pick", "Very Long Other Card Name That Should Ellipsize", "Recommended Card", "Very Long Context Pick Name That Should Ellipsize" };
        int?[] scores = [5, 15, 25, 35, 42, 47, 14, 24, 34, 39, 44, 50, null, 30, 45];
        for (var index = 0; index < 15; index++)
        {
            var badge = Sample(index, names[index % 5], scores[index], index == 11, estimated: index is 4 or 13);
            var visual = template.Build(badge)!; visual.DataContext = badge;
            visual.HorizontalAlignment = HorizontalAlignment.Center; visual.VerticalAlignment = VerticalAlignment.Bottom;
            visual.Margin = new(0, 0, 0, 18);
            var cell = new Grid { Margin = new(5), Background = Brush("#202833") };
            cell.Children.Add(new TextBlock { Text = MysticBadgeTierPalette.Label(scores[index]),
                Foreground = Brush("#9DAABD"), FontSize = 11, Margin = new(10, 8), VerticalAlignment = VerticalAlignment.Top });
            cell.Children.Add(visual); Grid.SetRow(cell, index / 5); Grid.SetColumn(cell, index % 5); cards.Children.Add(cell);
        }
        _toggle = new Button { Content = "Pause animation", HorizontalAlignment = HorizontalAlignment.Left };
        _toggle.Click += (_, _) => SetAnimated(!_animated);
        _diagnostics = new TextBlock { Foreground = Brush("#A8B4C5"), FontSize = 11 };
        _preview = new Border { Background = Background, Child = new StackPanel
        {
            Margin = new(22), Spacing = 12,
            Children =
            {
                new TextBlock { Text = "Mystic smoke rating backgrounds", FontSize = 20, Foreground = Brushes.White },
                new TextBlock { Text = "15 original-size badges · six Pick Score tiers · five sparkles · long names · unavailable score · two estimated (EST) scores", FontSize = 12, Foreground = Brush("#A8B4C5") },
                cards, _toggle, _diagnostics
            }
        } };
        Content = _preview;
        Opened += async (_, _) =>
        {
            _effects = _preview.GetVisualDescendants().OfType<MysticRatingBackground>().ToArray();
            MysticBadgeClock.Updated += ClockUpdated;
            UpdateDiagnostics();
            var outputIndex = Array.IndexOf(args, "--measure-output");
            var captureIndex = Array.IndexOf(args, "--capture-motion");
            if (outputIndex < 0 && captureIndex < 0) return;
            var argumentIndex = outputIndex >= 0 ? outputIndex : captureIndex;
            if (argumentIndex + 1 >= args.Length) { Environment.ExitCode = 1; Close(); return; }
            try
            {
                if (outputIndex >= 0) await MeasureAndExportAsync(Path.GetFullPath(args[outputIndex + 1]), _closed.Token);
                else
                {
                    var directory = Path.GetFullPath(args[captureIndex + 1]);
                    Directory.CreateDirectory(directory); await Task.Delay(1000, _closed.Token);
                    var motion = await CaptureMotionAsync(directory, _closed.Token);
                    await File.WriteAllTextAsync(Path.Combine(directory, "motion.json"), JsonSerializer.Serialize(motion,
                        new JsonSerializerOptions { WriteIndented = true }), _closed.Token);
                }
            }
            catch (OperationCanceledException) when (_closed.IsCancellationRequested) { }
            catch (Exception error)
            {
                Trace.WriteLine("Badge preview measurement failed: " + error);
                Environment.ExitCode = 1;
            }
            finally { Close(); }
        };
        Closed += (_, _) => { MysticBadgeClock.Updated -= ClockUpdated; _closed.Cancel(); _closed.Dispose(); };
    }
    internal static CardBadgeViewModel Sample(int index, string name, bool isContextPick)
        => Sample(index, name, isContextPick ? 47 : 25, isContextPick);
    internal static CardBadgeViewModel Sample(int index, string name, int? score, bool isContextPick = false, bool estimated = false)
    {
        var id = CardIdentifier.Create($"preview-{index}");
        // Printed metadata is fixture data only; it is never consulted by visual styling.
        var card = new Card(id, name, ColorSet.Colorless, CardRarity.Common, CardSetCode.Create("TST"), CollectorNumber.Create((index + 1).ToString()));
        var rate = isContextPick ? .624 : .547 + index % 3 * .012;
        // Estimated fixtures model a provider row without GIH: no rate is shown, ALSA still is.
        var stats = new LimitedCardStatistics(id, GameInHandGameCount: estimated ? null : 5000, GameInHandWinRate: estimated ? null : rate, AverageLastSeenAt: 6.2);
        // Sample values only; no scoring engine is invoked by this preview.
        var recommendation = new CardRecommendation(index, id, rate, 5000, rate, 1, isContextPick ? 1 : index % 3 + 3, isContextPick);
        var impact = new CandidateDeckImpact(false, false, null, null, 0, 0, 0, 0, false, false, false,
            null, null, 0, .5, Array.Empty<string>());
        var pickScore = new ContextualPickScore(index, id, score / 50d, .5, .5, .5, .5, 0, 0, 1,
            new ContextualPickScoreConfiguration().EarlyWeights, score is null ? PickScoreAvailability.NoEnvironmentBaseline : estimated ? PickScoreAvailability.EstimatedMissingStatistics : PickScoreAvailability.Measured,
            "offline-preview-fixture", impact, new(LimitedCardRole.None, LimitedCardRole.None, false, "Preview fixture"),
            Array.Empty<PickScoreContribution>(), Array.Empty<string>()) { CurrentPackRank = isContextPick ? 1 : index + 2, IsContextPick = isContextPick };
        var badge = new CardBadgeViewModel(new(new(index, id), card, stats, name, recommendation, pickScore: pickScore));
        badge.Place(new(index, 0, 0, 1, 1), 180, 100); return badge;
    }
    private static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color)).ToImmutable();
    private void SetAnimated(bool enabled)
    {
        _animated = enabled;
        foreach (var effect in _effects) effect.IsAnimated = enabled;
        _toggle.Content = enabled ? "Pause animation" : "Resume animation";
        UpdateDiagnostics();
    }
    private void ClockUpdated()
    {
        if (MysticBadgeClock.Seconds - _lastDiagnostics < 1) return;
        UpdateDiagnostics();
    }
    private void UpdateDiagnostics()
    {
        _lastDiagnostics = MysticBadgeClock.Seconds;
        var status = !_animated ? "Paused" : _effects.Length == 15 && _effects.All(e => e.AnimationActive) ? "Running" : "Stopped";
        _diagnostics.Text = $"Animation: {status} · Active: {MysticBadgeClock.ActiveCount} · Ticks: {MysticBadgeClock.TickCount}"
            + $" · Phase: {_effects.FirstOrDefault()?.FrameSeconds:F1}s · Renders: {_effects.Sum(e => e.RenderCount)}";
    }
    private sealed record MotionFrame(double ElapsedSeconds, double FrameSeconds, long ClockTicks,
        long FrameUpdates, long LiveRenderCalls, MysticWaveState State, string PixelHash, string OtherCardPixelHash);
    private async Task<MotionFrame[]> CaptureMotionAsync(string directory, CancellationToken token)
    {
        if (_effects.Length != 15 || _effects.Any(e => !e.AnimationActive))
            throw new InvalidOperationException("Motion capture requires 15 actively animated badges.");
        var frames = new List<MotionFrame>(); var elapsed = Stopwatch.StartNew();
        var effect = _effects.Single(e => e.OccurrenceIndex == 3);
        var other = _effects.Single(e => e.OccurrenceIndex == 0);
        foreach (var seconds in new[] { 0d, 1.5, 3 })
        {
            var wait = TimeSpan.FromSeconds(seconds) - elapsed.Elapsed;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, token);
            UpdateDiagnostics();
            var liveRenders = effect.RenderCount;
            // No manual phase override/invalidation: these samples are driven by the live shared clock.
            Export(Path.Combine(directory, $"badge-preview-{seconds:0.0}.png"));
            var hash = ExportWave(effect, Path.Combine(directory, $"wave-{seconds:0.0}.png"));
            var otherHash = ExportWave(other, Path.Combine(directory, $"wave-other-{seconds:0.0}.png"));
            frames.Add(new(elapsed.Elapsed.TotalSeconds, effect.FrameSeconds, MysticBadgeClock.TickCount,
                effect.FrameUpdateCount, liveRenders, effect.LastRenderedState, hash, otherHash));
        }
        if (frames.Select(f => f.PixelHash).Distinct().Count() != 3
            || frames.Select(f => f.OtherCardPixelHash).Distinct().Count() != 3
            || frames[2].ClockTicks <= frames[0].ClockTicks || frames[2].FrameUpdates <= frames[0].FrameUpdates
            || frames[2].LiveRenderCalls <= frames[0].LiveRenderCalls)
            throw new InvalidOperationException("Live clock/render/pixel motion verification failed.");
        return frames.ToArray();
    }
    private async Task MeasureAndExportAsync(string output, CancellationToken token)
    {
        if (_effects.Length != 15) throw new InvalidOperationException($"Expected 15 preview effects; found {_effects.Length}.");
        var directory = Path.GetDirectoryName(output)!; Directory.CreateDirectory(directory);
        await Task.Delay(TimeSpan.FromSeconds(2), token);
        var motion = await CaptureMotionAsync(directory, token);
        SetAnimated(false); await Task.Delay(1000, token);
        var staticSample = await MeasureAsync(8, token);
        Export(Path.Combine(directory, "badge-preview-static.png"));
        var effect = _effects.Single(e => e.OccurrenceIndex == 3);
        var other = _effects.Single(e => e.OccurrenceIndex == 0);
        var staticHash = ExportWave(effect, Path.Combine(directory, "wave-static-0.0.png"));
        var otherStaticHash = ExportWave(other, Path.Combine(directory, "wave-other-static-0.0.png"));
        await Task.Delay(1500, token);
        var staticPixelsUnchanged = staticHash == ExportWave(effect, Path.Combine(directory, "wave-static-1.5.png"))
            && otherStaticHash == ExportWave(other, Path.Combine(directory, "wave-other-static-1.5.png"));
        if (!staticPixelsUnchanged) throw new InvalidOperationException("Paused wave pixels changed.");
        SetAnimated(true); await Task.Delay(1000, token);
        var animatedSample = await MeasureAsync(12, token);
        SetAnimated(false);
        var stoppedClock = MysticBadgeClock.ActiveCount == 0;
        SetAnimated(true); Hide();
        var hiddenClockStopped = MysticBadgeClock.ActiveCount == 0;
        Show();
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new
        {
            BadgeCount = _effects.Length, Environment.ProcessorCount, Framework = "Avalonia 12.1.3",
            Motion = motion, StaticPixelsUnchanged = staticPixelsUnchanged,
            Static = staticSample, Animated = animatedSample, StoppedClock = stoppedClock, HiddenClockStopped = hiddenClockStopped,
            Note = "Whole-process CPU/managed allocation; timer/render dispatch cadence is not GPU presentation FPS."
        }, new JsonSerializerOptions { WriteIndented = true }), token);
    }
    private async Task<object> MeasureAsync(int durationSeconds, CancellationToken token)
    {
        using var process = Process.GetCurrentProcess();
        var cpu = process.TotalProcessorTime; var allocated = GC.GetTotalAllocatedBytes(true);
        var memory = process.WorkingSet64; var ticks = MysticBadgeClock.TickCount;
        var updates = _effects.Sum(e => e.FrameUpdateCount);
        var renders = _effects.Sum(e => e.RenderCount); var clock = Stopwatch.StartNew();
        await Task.Delay(TimeSpan.FromSeconds(durationSeconds), token);
        process.Refresh(); var seconds = clock.Elapsed.TotalSeconds;
        var corePercent = (process.TotalProcessorTime - cpu).TotalSeconds / seconds * 100;
        return new
        {
            Seconds = seconds, CpuOneCorePercent = corePercent, CpuMachinePercent = corePercent / Environment.ProcessorCount,
            AllocatedBytesPerSecond = (GC.GetTotalAllocatedBytes(true) - allocated) / seconds,
            WorkingSetStartBytes = memory, WorkingSetEndBytes = process.WorkingSet64,
            ClockTicks = MysticBadgeClock.TickCount - ticks, ActiveSubscribers = MysticBadgeClock.ActiveCount,
            FrameUpdates = _effects.Sum(e => e.FrameUpdateCount) - updates,
            RenderCalls = _effects.Sum(e => e.RenderCount) - renders, MaximumClockGapMs = MysticBadgeClock.MaximumTickGapMs
        };
    }
    private void Export(string path)
        => ExportControl(_preview, path);
    private static void ExportControl(Control control, string path, double scale = 1)
    {
        using var bitmap = new RenderTargetBitmap(new((int)Math.Ceiling(control.Bounds.Width * scale),
            (int)Math.Ceiling(control.Bounds.Height * scale)), new(96 * scale, 96 * scale));
        bitmap.Render(control); bitmap.Save(path, PngBitmapEncoderOptions.Default);
    }
    private static string ExportWave(MysticRatingBackground effect, string path)
    {
        ExportControl(effect, path);
        ExportControl(effect, Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + "-detail.png"), 4);
        using var pixels = SKBitmap.Decode(path);
        return Convert.ToHexString(SHA256.HashData(pixels.Bytes));
    }
}
