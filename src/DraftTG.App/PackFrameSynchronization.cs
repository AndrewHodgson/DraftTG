using System.Globalization;
using System.Security.Cryptography;
using DraftTG.Application;
using DraftTG.App.Platform;
using SkiaSharp;

namespace DraftTG.App;

internal sealed record PackFrameTiming(TimeSpan VisualSettleDelay, TimeSpan FirstRetryDelay, TimeSpan SecondRetryDelay)
{
    public static PackFrameTiming FromEnvironment()
    {
        static TimeSpan Read(string name, int fallback) => TimeSpan.FromMilliseconds(
            double.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var ms)
                && ms is >= 0 and <= 2000 ? ms : fallback);
        return new(Read("DRAFTTG_PACK_VISUAL_SETTLE_MS", 250), Read("DRAFTTG_CAPTURE_RETRY_FIRST_MS", 150),
            Read("DRAFTTG_CAPTURE_RETRY_SECOND_MS", 250));
    }
}

/// <summary>An immutable candidate snapshot paired with a semantic pack token, not a mutable latest-pack read.</summary>
internal sealed record PackFrameContext(CardVisualLocalizationRequest Request, DateTimeOffset ChangedAt);
internal sealed class StaleArenaFrameException(string message) : IOException(message);

/// <summary>Shares freshness policy between automatic placement, Retry and explicit debug save. Stores hashes, never reusable pixels.</summary>
internal sealed class PackFrameSynchronizer(PackFrameTiming timing,
    Func<TimeSpan, CancellationToken, Task>? delay = null) : IDisposable
{
    private sealed record CardFingerprint(NormalizedDraftRegion Rectangle, string Hash);
    private sealed record Evidence(long PackGeneration, int ExpectedCount, string Hash, int Width, int Height,
        IReadOnlyList<CardFingerprint> ConfirmedCards);
    private readonly object _gate = new();
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;
    private PackFrameContext? _current;
    private Evidence? _latest, _previous;
    private CancellationTokenSource _packCancellation = new();
    private bool _disposed;

    public void Activate(PackFrameContext? context)
    {
        lock (_gate)
        {
            if (_current?.Request.PackGeneration == context?.Request.PackGeneration) return;
            _packCancellation.Cancel(); _packCancellation.Dispose(); _packCancellation = new();
            _previous = _latest ?? _previous; _latest = null;
            _current = context;
        }
    }
    public bool IsCurrent(PackFrameContext context)
    {
        lock (_gate) return !_disposed && _current?.Request.PackGeneration == context.Request.PackGeneration
            && _current.Request.Pack.Equals(context.Request.Pack);
    }
    private void Verify(PackFrameContext context, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!IsCurrent(context)) throw new OperationCanceledException("Pack generation changed; stale capture/result discarded.", token);
    }

    public async Task<ArenaRegionFrame> AcquireAsync(PackFrameContext context,
        Func<CancellationToken, Task<ArenaRegionFrame>> acquire, Action<string> progress, CancellationToken cancellationToken)
    {
        CancellationToken packToken;
        lock (_gate) { ObjectDisposedException.ThrowIf(_disposed, this); packToken = _packCancellation.Token; }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, packToken);
        var token = linked.Token;
        Verify(context, token);
        var remaining = context.ChangedAt + timing.VisualSettleDelay - DateTimeOffset.UtcNow;
        if (remaining > TimeSpan.Zero)
        {
            progress("Waiting for Arena visual update");
            await _delay(remaining, token);
            Verify(context, token);
        }
        string? reason = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            Verify(context, token);
            progress($"Capturing fresh frame — attempt {attempt}/3; pack generation {context.Request.PackGeneration}");
            ArenaRegionFrame? frame = null;
            try
            {
                frame = await acquire(token);
                Verify(context, token);
                if (frame.PackGeneration != context.Request.PackGeneration || frame.CapturedAt < context.ChangedAt)
                    reason = "Stale frame rejected: frame timestamp or requested pack generation differs from current pack.";
                else
                {
                    var evaluation = await Task.Run(() => Evaluate(context, frame.Image), token);
                    Verify(context, token);
                    reason = evaluation.Rejection;
                    if (reason is null)
                    {
                        lock (_gate)
                        {
                            Verify(context, token);
                            var confirmed = _latest?.Hash == evaluation.Hash ? _latest.ConfirmedCards : [];
                            _latest = new(context.Request.PackGeneration, context.Request.Occurrences.Count,
                                evaluation.Hash, frame.Image.Width, frame.Image.Height, confirmed);
                        }
                        var accepted = frame with { ImageHash = evaluation.Hash };
                        frame = null;
                        return accepted;
                    }
                }
            }
            catch (InvalidDataException ex) when (ex.Message.StartsWith("Stale frame rejected", StringComparison.Ordinal))
            { reason = ex.Message; }
            finally { frame?.Dispose(); }
            Verify(context, token);
            progress(reason!);
            if (attempt < 3)
            {
                progress($"Retrying fresh capture — next attempt {attempt + 1}/3");
                await _delay(attempt == 1 ? timing.FirstRetryDelay : timing.SecondRetryDelay, token);
            }
        }
        throw new StaleArenaFrameException($"Failed after 3 synchronized capture attempts. {reason}");
    }

    private (string Hash, string? Rejection) Evaluate(PackFrameContext context, SKBitmap image)
    {
        var hash = Hash(image);
        Evidence? previous;
        lock (_gate) previous = _previous;
        if (previous is null || previous.PackGeneration == context.Request.PackGeneration) return (hash, null);
        var expected = context.Request.Occurrences.Count;
        // Card-count changes require changed pixels. Same-pack Retry and same-count transitions are not rejected just for identical pixels.
        if (previous.ExpectedCount != expected && previous.Hash == hash)
            return (hash, previous.ConfirmedCards.Count > expected
                ? $"Stale frame rejected: captured frame appears stale: expected {expected} cards, detected {previous.ConfirmedCards.Count} unchanged confirmed card rectangles."
                : $"Stale frame rejected: image is identical to prior pack crop despite card count changing {previous.ExpectedCount} -> {expected}.");
        // A cursor/highlight outside the art can change the overall hash while all nine old cards remain visible.
        if (previous.Width == image.Width && previous.Height == image.Height && previous.ConfirmedCards.Count > expected)
        {
            var unchanged = previous.ConfirmedCards.Count(c => HashRegion(image, c.Rectangle) == c.Hash);
            if (unchanged > expected)
                return (hash, $"Stale frame rejected: captured frame appears stale: expected {expected} cards, detected {unchanged} unchanged confirmed card rectangles.");
        }
        return (hash, null);
    }

    public async Task RecordConfirmedCardsAsync(PackFrameContext context, ArenaRegionFrame frame,
        CardVisualLocalizationResult result, CancellationToken token)
    {
        if (!result.IsSafeFor(context.Request)) return;
        var confirmed = await Task.Run(() => result.Matches.Select(m =>
        {
            var r = m.Rectangle;
            // Same artwork core as the unchanged matcher, only for exact occupancy/change evidence.
            var art = CardTemplateRecognizer.ArtworkCore(r);
            return new CardFingerprint(art, HashRegion(frame.Image, art));
        }).Where(c => c.Hash != "outside-image").ToArray(), token);
        Verify(context, token);
        lock (_gate)
            if (_latest?.PackGeneration == context.Request.PackGeneration && _latest.Hash == frame.ImageHash)
                _latest = _latest with { ConfirmedCards = confirmed };
    }

    internal static string Hash(SKBitmap image) => Convert.ToHexString(SHA256.HashData(image.Bytes));
    private static string HashRegion(SKBitmap image, NormalizedDraftRegion r)
    {
        var bounds = new SKRectI((int)(r.X * image.Width), (int)(r.Y * image.Height),
            (int)((r.X + r.Width) * image.Width), (int)((r.Y + r.Height) * image.Height));
        if (bounds.Left < 0 || bounds.Top < 0 || bounds.Right > image.Width || bounds.Bottom > image.Height
            || bounds.Width <= 0 || bounds.Height <= 0) return "outside-image";
        using var crop = new SKBitmap(bounds.Width, bounds.Height);
        using (var canvas = new SKCanvas(crop))
            canvas.DrawBitmap(image, new SKRect(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom),
                new SKRect(0, 0, bounds.Width, bounds.Height));
        return Hash(crop);
    }
    public void Dispose()
    {
        lock (_gate) { _disposed = true; _packCancellation.Cancel(); _packCancellation.Dispose(); _latest = null; _previous = null; }
    }
}
