using DraftTG.Application;
using SkiaSharp;

namespace DraftTG.App;

internal static class LocalizationDebugCapture
{
    /// <summary>Explicit developer opt-in only; normal operation never calls this.</summary>
    public static void Save(string directory, SKBitmap frame, CardVisualLocalizationRequest request, CardVisualLocalizationResult result)
    {
        Directory.CreateDirectory(directory);
        using var annotated = frame.Copy();
        using var canvas = new SKCanvas(annotated);
        using var outline = new SKPaint { Color = SKColors.Gold, Style = SKPaintStyle.Stroke, StrokeWidth = 2 };
        using var ink = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var background = new SKPaint { Color = SKColors.Black };
        using var font = new SKFont(SKTypeface.Default, 14);
        for (var i = 0; i < result.Rectangles.Count; i++)
        {
            var r = result.Rectangles[i];
            var bounds = new SKRect((float)(r.X * frame.Width), (float)(r.Y * frame.Height),
                (float)((r.X + r.Width) * frame.Width), (float)((r.Y + r.Height) * frame.Height));
            canvas.DrawRect(bounds, outline);
            var match = result.Matches.SingleOrDefault(m => m.VisualSlot == i);
            var name = match is null ? "unresolved" : request.Occurrences.Single(c => c.Key == match.Key).CardName;
            var label = $"Slot {i + 1}: {name} {match?.Confidence:F3}";
            canvas.DrawRect(bounds.Left, bounds.Top, Math.Min(310, frame.Width - bounds.Left), 20, background);
            canvas.DrawText(label, bounds.Left + 3, bounds.Top + 15, font, ink);
        }
        using var image = SKImage.FromBitmap(annotated);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        // One bounded latest capture, no screenshot archive or ordinary-log payload.
        using var file = File.Create(Path.Combine(directory, "localization-latest.png"));
        png.SaveTo(file);
    }
}
