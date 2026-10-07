using System.Globalization;
using System.Text;
using System.Text.Json;
using DraftTG.Application;
using DraftTG.App.Platform;
using DraftTG.ArenaIntegration;
using DraftTG.Data;
using DraftTG.Domain;

namespace DraftTG.App;

/// <summary>
/// Phase 9E.2A developer command: one live shadow evaluation of the current Arena draft pack. It replays Player.log,
/// predicts the order from DraftTG's own order-evidence ledger (never from third-party data), captures Arena with WGC,
/// runs the unchanged full artwork matcher and reports the deterministic shadow comparison and the experimental
/// fixed-slot verifier. Read-only: no badges, no input, no process memory, no ledger writes. Pixels are never saved.
///
/// The capture region is the formula's grid bounds plus a small margin; it stands in for a careful manual draft-region
/// calibration (the matcher still searches its own 4–7 column proposals and registers artwork from pixels).
/// </summary>
internal static class DeterministicShadowLiveCommand
{
    public static int Run(string[] args) => RunAsync(args).GetAwaiter().GetResult();

    private static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Live capture requires Windows.");
            string? Option(string name) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
            var output = Path.GetFullPath(Option("--output") ?? throw new ArgumentException(
                "Usage: --deterministic-shadow-live --output <dir> [--ledger <order-evidence.jsonl> | --evidence-pairs <json>] [--db <path>]"
                + " [--image <overlay-free capture.png> --capture-origin X,Y --client X,Y,W,H --pack P,K --grpids a,b,...]"));
            Directory.CreateDirectory(output);
            var report = new StringBuilder();
            void Line(string text) { Console.WriteLine(text); report.AppendLine(text); }
            if (Option("--fixture") is { } fixture) return ReplayFixture(fixture, output, Option("--db"), Option("--evidence-pairs"));

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var paths = ApplicationDataPathProviderFactory.CreateDefault();
            DraftTGCardDataBootstrapResult bootstrap;
            if (Option("--image") is not null)
            {
                // Saved-image replay is strictly offline: reuse local catalog and reference thumbnails only.
                var data = await new ScryfallJsonlGzipCardDataLoader().LoadAsync(Path.Combine(paths.GetApplicationDataDirectory(),
                    "card-data", "scryfall-default-cards.jsonl.gz"));
                var resolver = new ArenaCardResolver(data, ArenaDatabasePrintingIdentitySource.CreateDefault());
                bootstrap = new(data.Catalog, resolver, new(resolver), ScryfallCardDataLoadSource.Cache, null, false, null);
            }
            else bootstrap = await new DraftTGCardDataBootstrapper(new ScryfallCardDataProvider(new ScryfallBulkDataClient(http),
                new ScryfallJsonlGzipCardDataLoader(), paths), ArenaDatabasePrintingIdentitySource.CreateDefault()).BootstrapAsync();
            static int[] Ints(string text) => text.Split(',').Select(v => int.Parse(v, CultureInfo.InvariantCulture)).ToArray();

            int[] log; DraftPack pack; ArenaWindowGeometry window; ArenaWindowMode mode; SkiaSharp.SKBitmap? image = null; int[]? captureOrigin = null;
            if (Option("--image") is { } imagePath)
            {
                // Saved overlay-free capture of Arena's window rectangle (e.g. the localization audit's arena-only frames).
                log = Ints(Option("--grpids") ?? throw new ArgumentException("--image requires --grpids"));
                var coordinateParts = Ints(Option("--pack") ?? throw new ArgumentException("--image requires --pack P,K"));
                var client = Ints(Option("--client") ?? throw new ArgumentException("--image requires --client X,Y,W,H"));
                captureOrigin = Ints(Option("--capture-origin") ?? throw new ArgumentException("--image requires --capture-origin X,Y"));
                pack = new DraftPack(new(PackNumber.Create(coordinateParts[0]), PickNumber.Create(coordinateParts[1])), log.Select(id =>
                    bootstrap.ArenaCardResolver.TryResolve(ArenaCardIdentifier.Create(id), out var identifier) ? identifier
                        : throw new InvalidDataException($"GrpId {id} does not resolve to a DraftTG card.")));
                window = new ArenaWindowGeometry(0, client[0], client[1], client[2], client[3], 1, true);
                mode = ArenaWindowMode.Windowed; // The audit captures were taken from a captioned (windowed) Arena.
                image = SkiaSharp.SKBitmap.Decode(imagePath) ?? throw new InvalidDataException("Image could not be decoded.");
            }
            else
            {
                var state = ReplayPlayerLog();
                if (state.CurrentPack is not { } arenaPack) throw new InvalidOperationException("No current draft pack in Player.log.");
                log = arenaPack.CardIdentifiers.Select(c => c.Value).ToArray();
                pack = bootstrap.SnapshotAdapter.Convert(state).Snapshot?.CurrentPack
                    ?? throw new InvalidOperationException("Current pack could not be resolved to DraftTG card identities.");
                window = new WindowsGraphicsCaptureFrameCapture().InspectWindow().Window ?? throw new InvalidOperationException("No Arena rendering HWND found.");
                mode = ArenaWindowModeProbe.Detect(window);
            }
            if (pack.AvailableCardIdentifiers.Count != log.Length) throw new InvalidDataException("Resolved pack size differs from the Arena pack.");
            var coordinate = $"P{pack.Position.Pack.Value}P{pack.Position.Pick.Value}";

            var reader = ArenaCardDatabaseLocator.FindNewest(Option("--db")) is { } dbPath ? new ArenaCardDatabaseReader(dbPath)
                : throw new InvalidOperationException("Arena card database not found.");
            ArenaDisplayOrderEvaluation evaluation; string evidenceSource;
            if (Option("--evidence-pairs") is { } pairsPath)
            {
                var pairs = JsonSerializer.Deserialize<EvidencePair[]>(File.ReadAllText(pairsPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
                var observations = pairs.Select((pair, i) =>
                {
                    var keys = reader.ReadSortKeys(pair.Log.Concat(pair.Visual));
                    return new ArenaDisplayOrderObservation
                    {
                        ObservationId = $"pair-{i}", DraftScope = "explicit-pairs", Pack = 1, Pick = i + 1, CardCount = pair.Log.Count,
                        LogOrder = pair.Log, VisualOrder = pair.Visual, SortKeys = keys.Keys.Values.ToArray()
                    };
                }).ToArray();
                evaluation = ArenaDisplayOrderModel.Evaluate(observations);
                evidenceSource = $"explicit pairs ({string.Join("; ", pairs.Select(p => p.Label))})";
            }
            else
            {
                var service = OrderEvidenceRecorder.CreateDefaultService(Option("--ledger"), Option("--db"));
                service.Initialize();
                evaluation = service.Evaluation; evidenceSource = "DraftTG order-evidence ledger";
            }
            var lookup = reader.ReadSortKeys(log);
            var prediction = lookup.Status == ArenaCardDatabaseStatus.Available && lookup.MissingGrpIds.Count == 0
                ? ArenaDisplayOrderModel.Predict(log, lookup.Keys, evaluation.Survivors)
                : ArenaDisplayOrderPrediction.Unavailable(lookup.Diagnostic ?? "Arena card database unavailable.");
            var context = new ArenaDraftSlotContext(ArenaDraftView.DraftPackGrid, mode);
            const long generation = 1;
            var deterministic = DeterministicDraftCardLocator.Locate(new(pack, generation, log, prediction, window.Width, window.Height, context));

            Line($"Pack {coordinate} · {log.Length} cards · Arena DB {lookup.Database?.FileName} (Data {lookup.Database?.DataVersion})");
            Line($"Source: {(image is null ? "live Arena (WGC)" : "saved overlay-free capture " + Path.GetFileName(Option("--image")))}");
            Line($"Client {window.Width}x{window.Height} · aspect {window.Width / (double)window.Height:F3} · scale {window.Height / ArenaDraftSlotGeometry.ReferenceClientHeight:F4} · DPI scale {window.Scaling:F2} · mode {mode}");
            Line($"Order evidence: {evidenceSource} · {evaluation.ObservationCount} observations · {evaluation.Survivors.Count} surviving rules");
            Line($"Order prediction: {prediction.Status} ({prediction.Orders.Count} predicted orders){(prediction.Reason is null ? "" : " · " + prediction.Reason)}");
            Line($"Deterministic candidate: {deterministic.Status} · {deterministic.Reason}");

            var geometry = ArenaDraftSlotGeometry.TryCreateLayout(window.Width, window.Height, log.Length, context);
            if (geometry.Layout is not { } layout) { Line("Geometry: " + geometry.Reason + " · no live comparison."); File.WriteAllText(Path.Combine(output, "report.txt"), report.ToString()); return 0; }
            Line("Predicted slot rectangles (client px, reading order):");
            foreach (var (slot, i) in layout.Slots.Select((s, i) => (s, i))) Line($"  slot {i + 1,2}: {slot}");

            var margin = .02 * window.Height;
            double left = layout.Slots.Min(s => s.X) - margin, top = layout.Slots.Min(s => s.Y) - margin;
            double right = layout.Slots.Max(s => s.X + s.Width) + margin, bottom = layout.Slots.Max(s => s.Y + s.Height) + margin;
            var region = new NormalizedDraftRegion(left / window.Width, top / window.Height, (right - left) / window.Width, (bottom - top) / window.Height);
            var mapping = VisualCaptureMapping.FromAnchor(region, window.Width, window.Height);
            using var frameImage = image is null ? await LiveFrameAsync(window, region, generation) : Crop(image, mapping, window, captureOrigin!);
            image?.Dispose();

            using var references = new ScryfallVisualReferenceCache(paths.GetApplicationDataDirectory(), http);
            var images = Option("--image") is not null ? LocalReferences(pack.AvailableCardIdentifiers, paths.GetApplicationDataDirectory())
                : await references.GetAsync(pack.AvailableCardIdentifiers, CancellationToken.None);
            var presentations = pack.AvailableCardIdentifiers.Select((id, i) => new CurrentPackCardPresentation(new(i, id))).ToArray();
            var request = new CardVisualLocalizationRequest(pack, presentations, DraftCardLayout.Grid(ArenaDraftSlotGeometry.Columns)) { PackGeneration = generation };
            var recognizer = new CardTemplateRecognizer();
            var visual = recognizer.Recognize(request, frameImage, images) with { PackGeneration = generation };
            Line($"Visual matcher: {visual.Matches.Count}/{log.Length} matched in {visual.Duration.TotalMilliseconds:F0} ms · safe {visual.IsSafeFor(request)} · references {images.Count}/{pack.AvailableCardIdentifiers.Distinct().Count()}");

            // Order from the visual result (reading order of matched rectangles) — used only for diagnostics below.
            var visualOrder = ArenaDisplayOrderEvidenceGate.ReadingOrder(visual.Matches)?.Select(m => log[m.Key.PackIndex]).ToArray();
            Line($"Visual reading order: {(visualOrder is null ? "ambiguous" : string.Join(",", visualOrder))}");
            foreach (var (order, i) in prediction.Orders.Select((o, i) => (o, i)))
                Line($"  predicted order {i + 1} ({order.RuleCount} rules): {(visualOrder is not null && order.Order.SequenceEqual(visualOrder) ? "MATCHES visual" : "differs from visual")}");

            object? shadow = null;
            if (deterministic.IsAvailable)
            {
                var comparison = DeterministicSlotShadowComparer.Compare(deterministic, request, visual, mapping);
                Line("Shadow comparison: " + DeterministicSlotPresentation.Detail(coordinate, deterministic, comparison));
                shadow = Summary(comparison);
            }
            else Line("Shadow comparison: not run (deterministic candidate unavailable; production would use the visual path).");

            var unmatched = Enumerable.Range(0, log.Length).Where(i => visual.Matches.All(m => m.Key.PackIndex != i))
                .Select(i => $"{bootstrap.Catalog.Find(pack.AvailableCardIdentifiers[i])?.Name ?? "?"} ({log[i]})").ToArray();
            if (unmatched.Length > 0) Line("Visual matcher unresolved: " + string.Join(", ", unmatched));
            if (geometry.Layout is { } grid && visual.Matches.Count > 0)
            {
                // Matched subset: each visual centre against the formula slot that contains it (identity not used).
                var deltas = visual.Matches.Select(m => mapping.ToClient(m.Rectangle)).Select(r =>
                    grid.Slots.FirstOrDefault(s => s.Contains(r.CenterX, r.CenterY)) is var s && s.Width > 0
                        ? (Dx: r.CenterX - s.CenterX, Dy: r.CenterY - s.CenterY) : (Dx: double.NaN, Dy: double.NaN)).ToArray();
                var inside = deltas.Where(d => double.IsFinite(d.Dx)).ToArray();
                if (inside.Length > 0)
                {
                    double bx = inside.Average(d => d.Dx), by = inside.Average(d => d.Dy);
                    var scatter = inside.Select(d => Math.Sqrt(Math.Pow(d.Dx - bx, 2) + Math.Pow(d.Dy - by, 2))).ToArray();
                    Line(FormattableString.Invariant($"Geometry (matched subset {inside.Length}/{visual.Matches.Count} centres inside a formula slot): centre max {inside.Max(d => Math.Sqrt(d.Dx * d.Dx + d.Dy * d.Dy)):F1} px · offset ({bx:+0.0;-0.0},{by:+0.0;-0.0}) · scatter RMS {Math.Sqrt(scatter.Average(v => v * v)):F1} max {scatter.Max():F1} px"));
                }
            }

            object? geometryOnly = null;
            if (visualOrder is not null && visual.Matches.Count == log.Length)
            {
                // GEOMETRY-ONLY diagnostic: the visual order stands in for the prediction to measure slot geometry alone.
                var pseudo = new ArenaDisplayOrderPrediction(ArenaDisplayOrderPredictionStatus.Unanimous, [new ArenaPredictedOrder(visualOrder, 0)], 0);
                var asIfOrdered = DeterministicDraftCardLocator.Locate(new(pack, generation, log, pseudo, window.Width, window.Height, context));
                var comparison = DeterministicSlotShadowComparer.Compare(asIfOrdered, request, visual, mapping);
                Line("Geometry-only (visual order, NOT a deterministic candidate): " + DeterministicSlotPresentation.Detail(coordinate, asIfOrdered, comparison));
                foreach (var row in comparison.Slots)
                    Line(FormattableString.Invariant($"  slot {row.PredictedSlot + 1,2}: predicted {row.Predicted} visual {row.Visual} Δcentre ({row.CenterDx:+0.0;-0.0},{row.CenterDy:+0.0;-0.0})"));
                geometryOnly = Summary(comparison);
            }

            var verifiedOrders = FixedSlotOrderEvidence.Verify(request, log, prediction, window.Width, window.Height,
                context, mapping, frameImage, images);
            if (verifiedOrders.DeclineReason is { } decline) Line("Fast evidence declined: " + decline);
            var verifications = new List<object>();
            foreach (var (verifiedOrder, i) in verifiedOrders.Candidates.Select((o, i) => (o, i)))
            {
                var order = verifiedOrders.PredictionBefore.Orders[i];
                var result = verifiedOrder.Verification;
                Line(FormattableString.Invariant($"Fixed-slot verifier, predicted order {i + 1}: {result.Accepted}/{result.Slots.Count} pass 0.94/0.07 in {result.Duration.TotalMilliseconds:F0} ms · min score {result.Slots.Min(s => s.ExpectedScore):F3} · min lead {result.Slots.Min(s => s.ExpectedScore - s.CompetingScore):+0.000;-0.000}"));
                foreach (var slot in result.Slots) Line(FormattableString.Invariant($"  slot {slot.Slot + 1}: GrpId {log[slot.ExpectedOccurrence]} score {slot.ExpectedScore:F6} margin {slot.ExpectedScore - slot.CompetingScore:F6} · {slot.Accepted}"));
                verifications.Add(new { Order = i + 1, order.RuleCount, result.Accepted, Count = result.Slots.Count, Milliseconds = Math.Round(result.Duration.TotalMilliseconds),
                    MinScore = Math.Round(result.Slots.Min(s => s.ExpectedScore), 4), MinLead = Math.Round(result.Slots.Min(s => s.ExpectedScore - s.CompetingScore), 4),
                    Slots = result.Slots.Select(s => new { s.Slot, GrpId = log[s.ExpectedOccurrence], s.ExpectedScore, Margin = s.ExpectedScore - s.CompetingScore, s.Accepted, s.Reason }) });
            }
            var verifiedCount = verifiedOrders.VerifiedCount(log.Length);
            Line($"Fully verified candidate orders: {verifiedCount}; unique full pixel evidence: {verifiedCount == 1 && verifiedOrders.DeclineReason is null}. Replay/diagnostic command writes no evidence and satisfies no live gate.");
            foreach (var reference in images.Values) reference.Dispose();

            File.WriteAllText(Path.Combine(output, "report.txt"), report.ToString());
            File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new
            {
                Coordinate = coordinate, CardCount = log.Length, ClientWidth = window.Width, ClientHeight = window.Height, Mode = mode.ToString(),
                Prediction = prediction.Status.ToString(), PredictedOrders = prediction.Orders.Count, prediction.SurvivingRules,
                Deterministic = deterministic.Status.ToString(), VisualMatched = visual.Matches.Count, VisualMs = Math.Round(visual.Duration.TotalMilliseconds),
                Shadow = shadow, GeometryOnly = geometryOnly, Verifier = verifications, ArenaData = lookup.Database?.DataVersion
                , VerifiedCandidateOrderCount = verifiedCount, verifiedOrders.DeclineReason, LivePromotionEvidence = false
            }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine($"{ex.GetType().Name}: {ex.Message}"); return 1; }
    }

    private static object Summary(DeterministicShadowComparison c) => new
    {
        Status = c.Status.ToString(), c.Compared, c.SlotAgreements, c.ReadingOrderAgrees,
        MaxCenter = Math.Round(c.MaxCenterDelta, 2), RmsCenter = Math.Round(c.RmsCenterDelta, 2), OffsetX = Math.Round(c.BiasX, 2), OffsetY = Math.Round(c.BiasY, 2),
        MaxScatter = Math.Round(c.MaxResidual, 2), RmsScatter = Math.Round(c.RmsResidual, 2), WidthRatio = Math.Round(c.MedianWidthRatio, 3), HeightRatio = Math.Round(c.MedianHeightRatio, 3)
    };

    private sealed record EvidencePair(string Label, IReadOnlyList<int> Log, IReadOnlyList<int> Visual);

    private static IReadOnlyDictionary<CardIdentifier, SkiaSharp.SKBitmap> LocalReferences(IEnumerable<CardIdentifier> ids, string root) =>
        ids.Distinct().Select(id => (Id: id, Path: Path.Combine(root, "visual-references", id.Value + ".jpg")))
            .Where(p => File.Exists(p.Path)).Select(p => (p.Id, Image: SkiaSharp.SKBitmap.Decode(p.Path)))
            .Where(p => p.Image is not null).ToDictionary(p => p.Id, p => p.Image!);

    private static int ReplayFixture(string directory, string output, string? database, string? evidencePairs)
    {
        var manifest = JsonSerializer.Deserialize<OfflineLocalizationManifest>(File.ReadAllText(Path.Combine(directory, "manifest.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var request = OfflineArtworkCommand.CreateRequest(manifest) with { PackGeneration = 1 };
        using var frame = SkiaSharp.SKBitmap.Decode(Path.Combine(directory, "arena-draft-capture.png"));
        var references = OfflineArtworkCommand.LoadReferences(manifest, directory);
        try
        {
            var reader = new ArenaCardDatabaseReader(ArenaCardDatabaseLocator.FindNewest(database) ?? throw new InvalidOperationException("Arena DB missing."));
            var log = manifest.Cards.Select(c => c.ArenaId).ToArray();
            var keys = reader.ReadSortKeys(log).Keys;
            var prior = evidencePairs is null ? [] : JsonSerializer.Deserialize<EvidencePair[]>(File.ReadAllText(evidencePairs),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!.Where(p => !p.Log.Order().SequenceEqual(log.Order()))
                .Select((p, i) => new ArenaDisplayOrderObservation { ObservationId = "replay-" + i, DraftScope = "offline", Pack = 1, Pick = i + 1,
                    CardCount = p.Log.Count, LogOrder = p.Log, VisualOrder = p.Visual, SortKeys = reader.ReadSortKeys(p.Log).Keys.Values.ToArray() }).ToArray();
            var prediction = ArenaDisplayOrderModel.Predict(log, keys, ArenaDisplayOrderModel.Evaluate(prior).Survivors);
            CardRecognitionAudit? audit = null;
            var visual = new CardTemplateRecognizer().Recognize(request, frame, references, diagnostic: a => audit = a) with { PackGeneration = 1 };
            var gate = ArenaDisplayOrderEvidenceGate.Evaluate(request, visual, 1, request.Pack, log, manifest.PackNumber, manifest.PickNumber, false);
            var slots = audit!.Slots.Select(s => new { s.Slot, GrpId = log[s.Assigned.PackIndex], Score = s.Assigned.Score,
                Margin = s.Assigned.Score - Math.Max(s.CompetingSlotScore, s.Candidates.Where(c => c.Identifier != s.Assigned.Identifier).Select(c => c.Score).DefaultIfEmpty(0).Max()), s.Accepted }).ToArray();
            var text = $"WOE P{manifest.PackNumber}P{manifest.PickNumber}: {prediction.Orders.Count} distinct candidates before pack; full matcher {visual.Matches.Count}/{log.Length}; full matcher evidence gate {gate.Accepted}; fast verifier declined (unsupported card count/client height); verified fast candidates 0.\n"
                + string.Join("\n", slots.Select(s => FormattableString.Invariant($"slot {s.Slot}: GrpId {s.GrpId} score {s.Score:F6} margin {s.Margin:F6} accepted {s.Accepted}")));
            File.WriteAllText(Path.Combine(output, "report.txt"), text);
            File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new { manifest.PackNumber, manifest.PickNumber,
                CardCount = log.Length, PredictedOrders = prediction.Orders.Count, VisualMatched = visual.Matches.Count, FullMatcherEvidence = gate.Accepted,
                VerifiedCandidateOrderCount = 0, FastDecline = "unsupported envelope", Slots = slots, LivePromotionEvidence = false }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine(text);
            return 0;
        }
        finally { foreach (var reference in references.Values) reference.Dispose(); }
    }

    private static async Task<SkiaSharp.SKBitmap> LiveFrameAsync(ArenaWindowGeometry window, NormalizedDraftRegion region, long generation)
    {
        using var coordinator = new ArenaCaptureCoordinator(new WindowsGraphicsCaptureFrameCapture(), new(TimeSpan.FromSeconds(5)));
        if (!ArenaWindowGeometry.SameCaptureGeometry(window, coordinator.Observe())) throw new InvalidOperationException("Arena geometry changed before capture.");
        using var frame = await coordinator.AcquireAsync(region, packGeneration: generation);
        return frame.Image.Copy();
    }

    /// <summary>The same crop ArenaDraftCrop.Calculate would request, cut from a saved window-rectangle capture.</summary>
    private static SkiaSharp.SKBitmap Crop(SkiaSharp.SKBitmap image, VisualCaptureMapping mapping, ArenaWindowGeometry window, int[] captureOrigin)
    {
        int x = mapping.CropX + window.X - captureOrigin[0], y = mapping.CropY + window.Y - captureOrigin[1];
        using var subset = new SkiaSharp.SKBitmap();
        if (!image.ExtractSubset(subset, SkiaSharp.SKRectI.Create(x, y, mapping.CropWidth, mapping.CropHeight)))
            throw new InvalidDataException("Crop lies outside the saved capture.");
        return subset.Copy();
    }

    /// <summary>Same replay the localization audit uses: parser + state engine over the bounded current Player.log.</summary>
    private static ArenaDraftStateSnapshot ReplayPlayerLog()
    {
        var path = ArenaLogLocationProviderFactory.CreateDefault().GetLocation().FilePath;
        if (new FileInfo(path).Length > 64L * 1024 * 1024) throw new InvalidOperationException("Player.log exceeds 64 MiB.");
        var engine = new ArenaDraftStateEngine(); var parser = new ArenaDraftLogParser();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            try { foreach (var fact in parser.Parse(new ArenaLogSourceEvent.Line(line))) engine.Apply(fact); }
            catch (Exception ex) when (ex is ArenaDraftLogParseException or ArenaDraftStateConflictException) { }
        }
        return engine.Current;
    }
}
