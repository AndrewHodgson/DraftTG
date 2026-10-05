using System.Diagnostics;
using System.Text.Json;
using DraftTG.Application;
using DraftTG.App.Platform;
using DraftTG.ArenaIntegration;
using DraftTG.Data;
using SkiaSharp;

namespace DraftTG.App;

/// <summary>Opt-in event-driven CLI. No game input, recommendation calculation, production selection or badge application.</summary>
internal static class DesktopDuplicationBenchmarkCommand
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    private sealed record CalibrationDocument(int Version, OverlayScreen Screen, NormalizedDraftRegion Region, CardSlot[] Slots);
    public static int Run(string[] args) => RunAsync(args).GetAwaiter().GetResult();
    private static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Live DXGI benchmark requires Windows.");
            if (args.Length < 3 || args[1] != "--output")
                throw new ArgumentException("Usage: --benchmark-arena-capture --output <directory> [--duration-seconds 1200] [--capture-only] [--save-images|--save-failures]");
            var output = Path.GetFullPath(args[2]); var duration = 1200; var captureOnly = false; var saveImages = false; var saveFailures = false;
            for (var i = 3; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--duration-seconds" when ++i < args.Length && int.TryParse(args[i], out duration) && duration is >= 2 and <= 3600: break;
                    case "--capture-only": captureOnly = true; break;
                    case "--save-images": saveImages = true; break;
                    case "--save-failures": saveFailures = true; break;
                    default: throw new ArgumentException("Invalid benchmark argument.");
                }
            }
#if WINDOWS
            PhysicalPixelContext.ConfigureBenchmarkProcess();
#endif
            Directory.CreateDirectory(output);
            var recordsFile = Path.Combine(output, "benchmark.jsonl");
            using var writes = new SemaphoreSlim(1, 1);
            async Task LogAsync(object value)
            {
                await writes.WaitAsync();
                try { await File.AppendAllTextAsync(recordsFile, JsonSerializer.Serialize(value, Json) + "\n"); }
                finally { writes.Release(); }
            }
            var dataDirectory = ApplicationDataPathProviderFactory.CreateDefault().GetApplicationDataDirectory();
            var settings = new OverlaySettingsFile(ApplicationDataPathProviderFactory.CreateDefault());
            var calibration = JsonSerializer.Deserialize<CalibrationDocument>(await settings.ReadAsync()
                ?? throw new InvalidDataException("Save the existing draft-region calibration before benchmarking."))
                ?? throw new InvalidDataException("Invalid calibration.");
            if (calibration.Version != 1 || !calibration.Screen.IsValid || !calibration.Region.IsValid)
                throw new InvalidDataException("Invalid calibration.");
            var layout = new DraftCardLayout(calibration.Slots);
            using var dd = new DesktopDuplicationFrameCapture();
            var initial = dd.InspectWindow().Window ?? throw new InvalidOperationException("No visible Arena rendering HWND on this desktop.");
            var region = new NormalizedDraftRegion(
                (calibration.Screen.X + calibration.Region.X * calibration.Screen.Width - initial.X) / initial.Width,
                (calibration.Screen.Y + calibration.Region.Y * calibration.Screen.Height - initial.Y) / initial.Height,
                calibration.Region.Width * calibration.Screen.Width / initial.Width,
                calibration.Region.Height * calibration.Screen.Height / initial.Height);
            _ = ArenaDraftCrop.Calculate(initial, region);
            using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(duration));
            using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
            using var references = new ScryfallVisualReferenceCache(dataDirectory, http);
            using var referenceGate = new SemaphoreSlim(1, 1);
            async Task<CardVisualLocalizationResult> Recognize(CardVisualLocalizationRequest request, ArenaRegionFrame frame, CancellationToken token)
            {
                await referenceGate.WaitAsync(token);
                try
                {
                    var images = await references.GetAsync(request.Occurrences.Select(c => c.CardIdentifier), token);
                    return await Task.Run(() => new CardTemplateRecognizer().Recognize(request, frame.Image, images, token), token);
                }
                finally { referenceGate.Release(); }
            }
            string? Save(CaptureBenchmarkRecord record, ArenaRegionFrame frame)
            {
                if (!saveImages && !(saveFailures && record.ExpectedCards is { } expected && record.Matched < expected)) return null;
                var label = record.DesktopDuplication is null ? "wgc" : "dd";
                var path = Path.Combine(output, $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss-fff}-{label}-pack{record.PackGeneration}-request{record.CaptureRequestGeneration}.png");
                using var image = SKImage.FromBitmap(frame.Image); using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                using var file = File.Create(path); data.SaveTo(file);
                return path;
            }
            using var comparison = new CaptureBackendComparison(new WindowsGraphicsCaptureFrameCapture(dd.InspectWindow), dd,
                ArenaCaptureOptions.FromEnvironment(), PackFrameTiming.FromEnvironment(), Recognize, Save);
            await LogAsync(new { Kind = "start", At = DateTimeOffset.UtcNow, ProcessId = Environment.ProcessId, duration, captureOnly,
                saveImages, saveFailures, Window = new { Handle = $"0x{initial.Handle.ToInt64():X}", initial.ProcessId,
                    initial.X, initial.Y, initial.Width, initial.Height, initial.Scaling, initial.IsVisible, initial.IsMinimized }, Region = region, ProductionBackendChanged = false,
                Threshold = .94, AmbiguityMargin = .07, CoordinateSpace = "Physical screen pixels; normalized client ROI; DIPs = pixels / frame.Scaling" });
            if (captureOnly)
            {
                var pair = await comparison.CompareAsync(null, initial, region, lifetime.Token);
                await LogAsync(new { Kind = "capture-only", At = DateTimeOffset.UtcNow, Pair = pair });
                Console.WriteLine($"Capture-only: {pair.Outcome}; no current-pack or matcher claim; {recordsFile}");
                return pair.Records.Any(r => r.FreshFrameAccepted) ? 0 : 2;
            }
            // Cache-only catalog bootstrap: no provider refresh, statistics load or recommendation work.
            var catalog = await new ScryfallJsonlGzipCardDataLoader().LoadAsync(Path.Combine(dataDirectory, "card-data", "scryfall-default-cards.jsonl.gz"), lifetime.Token);
            var coordinator = new DraftSessionCoordinator(new FileArenaLogSource(), new ArenaDraftLogParser(), new ArenaDraftStateEngine(),
                new ArenaDraftSnapshotAdapter(new ArenaCardResolver(catalog)));
            var jobs = new List<Task>(); CancellationTokenSource? pending = null; string? current = null; long generation = 0;
            var readyPacks = 0; var observedPicks = new HashSet<string>();
            try
            {
                await foreach (var update in coordinator.RunAsync(lifetime.Token))
                {
                    foreach (var pick in update.ArenaState.CompletedPicks)
                        observedPicks.Add($"P{pick.Coordinate.Pack}P{pick.Coordinate.Pick}");
                    if (update.Diagnostic is { } diagnostic)
                        await LogAsync(new { Kind = "semantic-diagnostic", At = DateTimeOffset.UtcNow, DiagnosticKind = diagnostic.Kind,
                            diagnostic.ParseErrorKind, diagnostic.PickedCardsDiagnosticKind });
                    var snapshot = update.SnapshotResult.Snapshot;
                    var key = snapshot is null ? null : $"{update.ArenaState.DraftIdentifier}:{snapshot.CurrentPack.Position}:"
                        + string.Join(',', snapshot.CurrentPack.AvailableCardIdentifiers.Select(c => c.Value));
                    if (key == current) continue;
                    current = key; if (pending is not null) { try { pending.Cancel(); } catch (ObjectDisposedException) { } } generation++;
                    if (snapshot is null) { comparison.Activate(null); continue; }
                    readyPacks++;
                    var pack = snapshot.CurrentPack;
                    var request = new CardVisualLocalizationRequest(pack, pack.AvailableCardIdentifiers.Select((id, index) =>
                        new CurrentPackCardPresentation(new(index, id), catalog.Catalog.Find(id) ?? throw new InvalidDataException("Resolved card missing from catalog."))).ToArray(), layout)
                        { PackGeneration = generation };
                    var context = new PackFrameContext(request, DateTimeOffset.UtcNow); comparison.Activate(context);
                    pending = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                    var jobToken = pending.Token; var jobCancellation = pending;
                    jobs.Add(RunObservation());
                    async Task RunObservation()
                    {
                        try
                        {
                            // Let initial byte-zero replay settle; historical candidates must not be treated as the live pack.
                            await Task.Delay(1000, jobToken);
                            var window = dd.InspectWindow().Window ?? throw new InvalidOperationException("Arena HWND unavailable.");
                            var pair = await comparison.CompareAsync(context, window, region, jobToken);
                            jobToken.ThrowIfCancellationRequested();
                            await LogAsync(new { Kind = "pack", At = DateTimeOffset.UtcNow, Pack = $"P{pack.Position.Pack.Value}P{pack.Position.Pick.Value}",
                                request.PackGeneration, ExpectedCards = request.Occurrences.Count,
                                ExactHistoryCardCount = update.ArenaState.ExactHistoryCardCount,
                                DraftedPoolCardCount = update.ArenaState.DraftedPool.Count,
                                UnqualifiedHistoryCardCount = update.ArenaState.UnqualifiedHistoryCardCount,
                                Candidates = request.Occurrences.Select(c => new { c.CardIdentifier, c.CardName }), Pair = pair,
                                CorrectVisibleCardCount = (int?)null, PhysicalBadgePlacement = "Unobserved; no UI badges in this shadow command" });
                            Console.WriteLine($"P{pack.Position.Pack.Value}P{pack.Position.Pick.Value}: {request.Occurrences.Count} candidates; {pair.Outcome}");
                        }
                        catch (OperationCanceledException) when (jobToken.IsCancellationRequested)
                        { await LogAsync(new { Kind = "pack-canceled", At = DateTimeOffset.UtcNow, request.PackGeneration, Reason = "Pack changed/lifetime ended; no stale result applied." }); }
                        catch (Exception ex) { await LogAsync(new { Kind = "observation-failed", At = DateTimeOffset.UtcNow, request.PackGeneration, Exception = ex.GetType().Name, ex.HResult }); }
                        finally { jobCancellation.Dispose(); }
                    }
                }
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception ex)
            {
                await LogAsync(new { Kind = "semantic-monitor-failed", At = DateTimeOffset.UtcNow, Exception = ex.GetType().Name, ex.HResult,
                    ConflictKind = (ex as ArenaDraftStateConflictException)?.Kind.ToString(),
                    ConflictPack = (ex as ArenaDraftStateConflictException)?.Coordinate.Pack,
                    ConflictPick = (ex as ArenaDraftStateConflictException)?.Coordinate.Pick,
                    Reason = "Existing semantic monitor stopped; no parser/state repair or stale-ID reuse performed." });
            }
            finally
            {
                if (pending is not null) { try { pending.Cancel(); } catch (ObjectDisposedException) { } }
                comparison.Activate(null); await Task.WhenAll(jobs);
            }
            await LogAsync(new { Kind = "stop", At = DateTimeOffset.UtcNow, ReadyPackStatesObserved = readyPacks,
                HistoricalAndLivePickCoordinatesSeen = observedPicks.Count, Note = "Replay picks are not a count of physically benchmarked consecutive picks.", ProductionBackendChanged = false });
            Console.WriteLine($"Benchmark stopped; {recordsFile}. Physical acceptance is not inferred from process exit.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine($"{ex.GetType().Name}; HRESULT 0x{ex.HResult:X8}; {ex.Message}"); return 1; }
    }
}
