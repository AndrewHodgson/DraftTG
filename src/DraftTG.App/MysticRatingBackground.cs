using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DraftTG.RecommendationEngine;
using SkiaSharp;

namespace DraftTG.App;

// UI palette only. The dominant charcoal and existing recommendation border are unchanged.
internal enum MysticBadgeQualityTier { Unavailable, Graphite, Steel, Silver, Gold, RichGold, Premium }
internal sealed record MysticBadgeTierPalette(Color BaseBackground, Color AccentPrimary, Color AccentSecondary,
    double HighlightOpacity, double NeutralOpacity, Color Glow, Color Sparkle, double SparkleStrength)
{
    private static readonly Color Charcoal = Color.Parse("#E6171B22");
    internal static readonly Color NeutralSilver = Color.Parse("#B9C6D8");
    private static readonly MysticBadgeTierPalette[] Palettes =
    [
        Create("#6D798B", "#8797A9", "#B9C6D8", "#D6E2F2", .14, .18, .88),
        Create("#555D69", "#727B88", "#8792A3", "#A3AFBF", .11, .14, .65),
        Create("#6D798B", "#8797A9", "#A4B7CE", "#CAD8EA", .14, .18, .78),
        Create("#A3B0C1", "#8797A9", "#B9C6D8", "#D6E2F2", .18, .18, .88),
        Create("#BFA56D", "#A78B68", "#C5A568", "#E4D3AC", .20, .19, .93),
        Create("#E2BE64", "#B58A60", "#D8A24F", "#F3DCA2", .24, .20, 1),
        Create("#E1AE70", "#BD8656", "#DC9D57", "#FFE0AC", .26, .20, 1)
    ];
    private static MysticBadgeTierPalette Create(string primary, string secondary, string glow, string sparkle,
        double highlight, double neutral, double strength) => new(Charcoal, Color.Parse(primary), Color.Parse(secondary),
            highlight, neutral, Color.Parse(glow), Color.Parse(sparkle), strength);
    /// <summary>Visual palette of the canonical Pick Score tier (PickScoreTierThresholds); the badge never re-derives bands.</summary>
    public static MysticBadgeQualityTier Tier(int? score) => PickScoreTierThresholds.Classify(score) switch
    {
        PickScoreTier.VeryWeak => MysticBadgeQualityTier.Graphite, PickScoreTier.Marginal => MysticBadgeQualityTier.Steel,
        PickScoreTier.Solid => MysticBadgeQualityTier.Silver, PickScoreTier.Strong => MysticBadgeQualityTier.Gold,
        PickScoreTier.Excellent => MysticBadgeQualityTier.RichGold, PickScoreTier.Premium => MysticBadgeQualityTier.Premium,
        _ => MysticBadgeQualityTier.Unavailable
    };
    public static MysticBadgeTierPalette For(int? score) => Palettes[(int)Tier(score)];
    public static string Label(int? score)
    {
        var name = Tier(score) switch
        {
            MysticBadgeQualityTier.Graphite => "Graphite", MysticBadgeQualityTier.Steel => "Steel", MysticBadgeQualityTier.Silver => "Silver",
            MysticBadgeQualityTier.Gold => "Gold", MysticBadgeQualityTier.RichGold => "Rich gold", MysticBadgeQualityTier.Premium => "Premium",
            _ => null
        };
        return name is not null && PickScoreTierThresholds.Range(PickScoreTierThresholds.Classify(score)) is var (low, high)
            ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{name} · {low}–{high}") : "Score unavailable";
    }
}

// Nominal badge DIPs. Changes to texture constants take effect on the next process start.
internal static class MysticBadgeTuning
{
    internal const double WaveOpacity = 1.24;
    internal const double GlowStrength = .69;
    internal const float SmokeSoftness = 2.7f;
    internal const float GlowSoftness = 6f;
    internal const int SparkleCount = 5;
    internal const double SparkleSizeMin = 1.10;
    internal const double SparkleSizeMax = 1.28;
    internal const double SparkleOpacity = .72;
    internal const double SparkleIntensityMin = .82;
    internal const double SparkleFadeDurationMin = 6.5;
    internal const double SparkleFadeDurationMax = 10;
    internal const double MotionSpeed = 1;
}

internal readonly record struct MysticSparkleState(double X, double Y, double Opacity, double SizeScale)
{
    public static MysticSparkleState At(double seconds, int occurrenceIndex, int sparkleIndex, bool animated)
    {
        var time = (animated && double.IsFinite(seconds) ? seconds : 0) + MysticWaveState.PhaseOffset(occurrenceIndex);
        // Permuted sizes, intensities and periods keep the five particles from flashing together.
        var variation = (sparkleIndex * 3 % Math.Max(1, MysticBadgeTuning.SparkleCount))
            / (double)Math.Max(1, MysticBadgeTuning.SparkleCount - 1);
        var duration = MysticBadgeTuning.SparkleFadeDurationMin
            + (MysticBadgeTuning.SparkleFadeDurationMax - MysticBadgeTuning.SparkleFadeDurationMin) * variation;
        var phase = time * Math.Tau / duration + sparkleIndex * 2.399;
        var fade = (.5 - .5 * Math.Cos(phase));
        var (x, y) = sparkleIndex switch
        {
            0 => (12d, 6d), 1 => (70d, 32d), 2 => (40d, 36d), 3 => (74d, 7d),
            4 => (12d, 31d), _ => (10 + sparkleIndex * 23 % 64d, 32 + sparkleIndex % 3 * 2d)
        };
        var intensity = MysticBadgeTuning.SparkleIntensityMin + (1 - MysticBadgeTuning.SparkleIntensityMin) * variation;
        return new(x + 1.5 * Math.Sin(time * Math.Tau / 14 + sparkleIndex),
            y + .8 * Math.Cos(time * Math.Tau / 16 + sparkleIndex), MysticBadgeTuning.SparkleOpacity * intensity * fade * fade,
            MysticBadgeTuning.SparkleSizeMin + (MysticBadgeTuning.SparkleSizeMax - MysticBadgeTuning.SparkleSizeMin) * variation);
    }
}

internal readonly record struct MysticWaveState(double X1, double Y1, double X2, double Y2, double X3, double Y3)
{
    public static double PhaseOffset(int occurrenceIndex) => Math.Max(0, occurrenceIndex) * 1.37;
    public static MysticWaveState At(double seconds, int occurrenceIndex, bool animated)
    {
        var time = (animated && double.IsFinite(seconds) ? seconds * MysticBadgeTuning.MotionSpeed : 0) + PhaseOffset(occurrenceIndex);
        // Tier current travels 48 DIPs peak-to-peak (56% of the nominal badge width).
        // Silver counter-drifts at a different speed; all cycles return continuously.
        return new(-15 * Math.Sin(time * Math.Tau / 12), 1.8 * Math.Cos(time * Math.Tau / 12),
            24 * Math.Sin(time * Math.Tau / 8 + .2), 2.4 * Math.Cos(time * Math.Tau / 8 + .2),
            10 * Math.Sin(time * Math.Tau / 10 + 1.7), 1.4 * Math.Cos(time * Math.Tau / 10 + 1.7));
    }
}

/// <summary>One UI clock for attached, visible animated badges. No per-badge timers.</summary>
internal static class MysticBadgeClock
{
    private static readonly Stopwatch Time = Stopwatch.StartNew();
    private static readonly HashSet<MysticRatingBackground> Subscribers = [];
    private static readonly DispatcherTimer Timer = new() { Interval = TimeSpan.FromSeconds(1d / 30) };
    public static double Seconds => Time.Elapsed.TotalSeconds;
    internal static bool IsAvailable { get; private set; } = true;
    internal static int ActiveCount => Subscribers.Count;
    internal static long TickCount { get; private set; }
    internal static double MaximumTickGapMs { get; private set; }
    internal static event Action? Updated;
    private static double _lastTick;
    static MysticBadgeClock() => Timer.Tick += (_, _) =>
    {
        var now = Seconds;
        if (_lastTick > 0) MaximumTickGapMs = Math.Max(MaximumTickGapMs, (now - _lastTick) * 1000);
        _lastTick = now; TickCount++;
        foreach (var control in Subscribers) control.AdvanceFrame(now);
        Updated?.Invoke();
    };
    public static void SetActive(MysticRatingBackground control, bool active)
    {
        if (!IsAvailable) return;
        if (active)
        {
            if (Subscribers.Add(control) && Subscribers.Count == 1)
            {
                _lastTick = 0; MaximumTickGapMs = 0;
                try { Timer.Start(); }
                catch (Exception error)
                {
                    IsAvailable = false; Subscribers.Clear();
                    Trace.WriteLine("Mystic badge clock unavailable; static currents retained: " + error.Message);
                }
            }
        }
        else if (Subscribers.Remove(control) && Subscribers.Count == 0) Timer.Stop();
    }
}

/// <summary>Cached soft smoke and sparse sparkles, clipped inside the rating box beneath text.</summary>
public sealed class MysticRatingBackground : Control
{
    public static readonly StyledProperty<int?> PickScoreProperty =
        AvaloniaProperty.Register<MysticRatingBackground, int?>(nameof(PickScore));
    public static readonly StyledProperty<int> OccurrenceIndexProperty =
        AvaloniaProperty.Register<MysticRatingBackground, int>(nameof(OccurrenceIndex));
    public static readonly StyledProperty<bool> IsAnimatedProperty =
        AvaloniaProperty.Register<MysticRatingBackground, bool>(nameof(IsAnimated), true);
    public int? PickScore { get => GetValue(PickScoreProperty); set => SetValue(PickScoreProperty, value); }
    public int OccurrenceIndex { get => GetValue(OccurrenceIndexProperty); set => SetValue(OccurrenceIndexProperty, value); }
    public bool IsAnimated { get => GetValue(IsAnimatedProperty); set => SetValue(IsAnimatedProperty, value); }
    private bool _attached, _drawingFailed;
    private Visual[] _visibilityAncestors = [];
    private DrawResources? _resources;
    private double _frameSeconds;
    internal double FrameSeconds => IsAnimated && MysticBadgeClock.IsAvailable ? _frameSeconds : 0;
    internal MysticWaveState RenderState => MysticWaveState.At(FrameSeconds, OccurrenceIndex, IsAnimated && MysticBadgeClock.IsAvailable);
    internal MysticSparkleState SparkleState(int index)
    {
        var state = MysticSparkleState.At(FrameSeconds, OccurrenceIndex, index, IsAnimated && MysticBadgeClock.IsAvailable);
        return state with { Opacity = state.Opacity * MysticBadgeTierPalette.For(PickScore).SparkleStrength };
    }
    internal MysticWaveState LastRenderedState { get; private set; }
    internal long FrameUpdateCount { get; private set; }
    internal long RenderCount { get; private set; }
    internal void AdvanceFrame(double seconds)
    { _frameSeconds = seconds; FrameUpdateCount++; InvalidateVisual(); }
    internal bool AnimationActive => _attached && IsVisible && IsAnimated && !_drawingFailed && MysticBadgeClock.IsAvailable
        && _visibilityAncestors.All(v => v.IsVisible && (v is not Window window || window.WindowState != WindowState.Minimized));

    private sealed record DrawResources(Bitmap First, Bitmap Second, Bitmap Third, Bitmap Sparkle)
    {
        private static readonly Dictionary<MysticBadgeTierPalette, DrawResources> Cache = [];
        private const int TextureScale = 2;
        internal static readonly Rect SmokeBounds = new(-64, -16, 228, 74);
        public static DrawResources For(MysticBadgeTierPalette palette)
        {
            if (Cache.TryGetValue(palette, out var resources)) return resources;
            // Blur is baked once per treatment, never applied to text or recomputed per frame.
            resources = new(
                Smoke("M -48,8 C -38,-4 -27,13 -16,8 C 16,-5 39,22 60,11 S 96,-2 116,6 C 127,11 135,-5 148,6 L 148,13 C 135,2 127,18 116,13 C 89,8 83,29 58,19 S 15,3 -16,17 C -27,22 -38,4 -48,15 Z",
                    MysticBadgeTierPalette.NeutralSilver, palette.Glow, palette.NeutralOpacity),
                Smoke("M -48,27 C -38,17 -27,37 -16,31 C 14,13 30,40 55,29 S 93,16 116,28 C 127,34 136,18 148,27 L 148,34 C 136,25 127,40 116,34 C 91,22 77,44 53,36 S 15,21 -16,39 C -27,44 -38,24 -48,34 Z",
                    palette.AccentPrimary, palette.Glow, palette.HighlightOpacity),
                Smoke("M -48,22 C -37,31 -26,16 -16,22 C 9,34 28,7 53,18 S 95,35 116,19 C 127,10 137,28 148,21 L 148,26 C 137,33 127,15 116,23 C 91,41 78,27 52,23 S 11,39 -16,27 C -26,21 -37,36 -48,27 Z",
                    palette.AccentSecondary, palette.Glow, palette.HighlightOpacity * .55),
                SparkleTexture(palette.Sparkle));
            Cache.Add(palette, resources); return resources;
        }
        private static Bitmap Smoke(string pathData, Color accent, Color glow, double opacity)
        {
            using var pixels = new SKBitmap((int)SmokeBounds.Width * TextureScale, (int)SmokeBounds.Height * TextureScale);
            using var canvas = new SKCanvas(pixels);
            canvas.Clear(SKColors.Transparent); canvas.Scale(TextureScale); canvas.Translate(-(float)SmokeBounds.X, -(float)SmokeBounds.Y);
            using var path = SKPath.ParseSvgPathData(pathData);
            PaintSmoke(canvas, path, glow, opacity * MysticBadgeTuning.GlowStrength, MysticBadgeTuning.GlowSoftness);
            PaintSmoke(canvas, path, accent, opacity * MysticBadgeTuning.WaveOpacity, MysticBadgeTuning.SmokeSoftness);
            return ToBitmap(pixels);
        }
        private static void PaintSmoke(SKCanvas canvas, SKPath path, Color color, double opacity, float softness)
        {
            var bounds = path.Bounds;
            var tint = new SKColor(color.R, color.G, color.B, (byte)Math.Round(opacity * 255));
            using var gradient = SKShader.CreateLinearGradient(new(bounds.MidX, bounds.Top), new(bounds.MidX, bounds.Bottom),
                [tint.WithAlpha(0), tint.WithAlpha((byte)(tint.Alpha * .3)), tint, tint.WithAlpha((byte)(tint.Alpha * .3)), tint.WithAlpha(0)],
                [0, .2f, .5f, .8f, 1], SKShaderTileMode.Clamp);
            using var blur = SKImageFilter.CreateBlur(softness, softness);
            using var paint = new SKPaint { IsAntialias = true, Shader = gradient, ImageFilter = blur };
            canvas.DrawPath(path, paint);
        }
        private static Bitmap SparkleTexture(Color color)
        {
            using var pixels = new SKBitmap(24, 24);
            using var canvas = new SKCanvas(pixels);
            canvas.Clear(SKColors.Transparent); canvas.Scale(TextureScale);
            var tint = new SKColor(color.R, color.G, color.B);
            using var aura = SKShader.CreateRadialGradient(new(6, 6), 4,
                [tint.WithAlpha(120), tint.WithAlpha(34), tint.WithAlpha(0)], [0, .3f, 1], SKShaderTileMode.Clamp);
            using var paint = new SKPaint { IsAntialias = true, Shader = aura };
            canvas.DrawCircle(6, 6, 4, paint);
            paint.Shader = null; paint.Color = tint.WithAlpha(235);
            using var star = new SKPath();
            star.MoveTo(6, 4.7f); star.LineTo(6.35f, 5.65f); star.LineTo(7.3f, 6); star.LineTo(6.35f, 6.35f);
            star.LineTo(6, 7.3f); star.LineTo(5.65f, 6.35f); star.LineTo(4.7f, 6); star.LineTo(5.65f, 5.65f); star.Close();
            canvas.DrawPath(star, paint);
            return ToBitmap(pixels);
        }
        private static Bitmap ToBitmap(SKBitmap pixels)
        {
            using var image = SKImage.FromBitmap(pixels);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = encoded.AsStream();
            return new Bitmap(stream);
        }
    }
    // A fixed dark veil through the text zone suppresses highlights without changing text styling.
    private static readonly IBrush TextVeil = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = [new(Color.Parse("#00171B22"), 0), new(Color.Parse("#70171B22"), .35),
            new(Color.Parse("#70171B22"), .7), new(Color.Parse("#00171B22"), 1)]
    }.ToImmutable();

    static MysticRatingBackground() => AffectsRender<MysticRatingBackground>(PickScoreProperty, OccurrenceIndexProperty, IsAnimatedProperty);
    public MysticRatingBackground()
    {
        IsHitTestVisible = false; Focusable = false;
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e); _attached = true; _frameSeconds = MysticBadgeClock.Seconds;
        _visibilityAncestors = this.GetVisualAncestors().ToArray();
        foreach (var ancestor in _visibilityAncestors) ancestor.PropertyChanged += AncestorChanged;
        UpdateClock();
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false; UpdateClock();
        foreach (var ancestor in _visibilityAncestors) ancestor.PropertyChanged -= AncestorChanged;
        _visibilityAncestors = []; base.OnDetachedFromVisualTree(e);
    }
    private void AncestorChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
    { if (change.Property == IsVisibleProperty || change.Property == Window.WindowStateProperty) UpdateClock(); }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsAnimatedProperty || change.Property == IsVisibleProperty) UpdateClock();
        if (change.Property == PickScoreProperty) _resources = null;
    }
    private void UpdateClock() => MysticBadgeClock.SetActive(this, AnimationActive);
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_drawingFailed || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        try
        {
            _resources ??= DrawResources.For(MysticBadgeTierPalette.For(PickScore));
            var motion = RenderState;
            // Parent still paints the original charcoal background if the optional effect fails.
            using (context.PushClip(new RoundedRect(new Rect(Bounds.Size), 4)))
            using (context.PushTransform(Matrix.CreateScale(Bounds.Width / 86, Bounds.Height / 42)))
            {
                DrawBand(context, _resources.First, motion.X1, motion.Y1);
                DrawBand(context, _resources.Second, motion.X2, motion.Y2);
                DrawBand(context, _resources.Third, motion.X3, motion.Y3);
                for (var index = 0; index < MysticBadgeTuning.SparkleCount; index++)
                {
                    var sparkle = SparkleState(index);
                    var size = 12 * sparkle.SizeScale;
                    using (context.PushOpacity(sparkle.Opacity))
                        context.DrawImage(_resources.Sparkle, new Rect(sparkle.X - size / 2, sparkle.Y - size / 2, size, size));
                }
                context.DrawRectangle(TextVeil, null, new Rect(0, 0, 86, 42));
            }
            LastRenderedState = motion; RenderCount++;
        }
        catch (Exception error)
        {
            _drawingFailed = true; UpdateClock();
            Trace.WriteLine("Mystic badge effect disabled: " + error.Message);
            InvalidateVisual(); // Remove any partially recorded layer; rating siblings remain visible.
        }
    }
    private static void DrawBand(DrawingContext context, Bitmap texture, double x, double y)
    {
        context.DrawImage(texture, DrawResources.SmokeBounds.Translate(new Vector(x, y)));
    }
}
