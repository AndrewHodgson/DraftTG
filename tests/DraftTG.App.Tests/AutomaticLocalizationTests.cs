using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using DraftTG.Application;
using DraftTG.App.Platform;
using DraftTG.ArenaIntegration;
using DraftTG.Data;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;
using SkiaSharp;
using Xunit.Abstractions;

namespace DraftTG.App.Tests;

public sealed partial class MainWindowViewModelTests
{
    internal static CardVisualLocalizationRequest AutoRequest(int count, bool duplicate = false)
    {
        var ids = Enumerable.Range(0, count).Select(i => CardIdentifier.Create($"card-{(duplicate && i == 1 ? 0 : i)}")).ToArray();
        var pack = new DraftPack(new(PackNumber.Create(1), PickNumber.Create(6)), ids);
        return new(pack, ids.Select((id, i) => new CurrentPackCardPresentation(new(i, id))).ToArray(), DraftCardLayout.Grid(5));
    }

    internal static SKBitmap SyntheticCard(int seed)
    {
        var image = new SKBitmap(146, 204);
        using var canvas = new SKCanvas(image);
        canvas.Clear(SKColors.DarkSlateGray);
        var random = new Random(seed + 914);
        using var paint = new SKPaint();
        for (var y = 0; y < 4; y++)
        for (var x = 0; x < 5; x++)
        {
            paint.Color = new((byte)random.Next(20, 240), (byte)random.Next(20, 240), (byte)random.Next(20, 240));
            canvas.DrawRect((float)(.075 * 146 + x * .85 * 146 / 5), (float)(.165 * 204 + y * .40 * 204 / 4),
                (float)(.85 * 146 / 5 + 1), (float)(.40 * 204 / 4 + 1), paint);
        }
        return image;
    }

    internal static SKBitmap SyntheticPack(CardVisualLocalizationRequest request, IReadOnlyDictionary<CardIdentifier, SKBitmap> references,
        int[] order, IReadOnlyList<NormalizedDraftRegion>? rectangles = null, int width = 1000, int height = 600)
    {
        var image = new SKBitmap(width, height);
        using var canvas = new SKCanvas(image);
        canvas.Clear(SKColors.Black);
        rectangles ??= request.CalibratedLayout.Slots.Take(order.Length).Select(s => new NormalizedDraftRegion(s.X, s.Y, s.Width, s.Height)).ToArray();
        for (var i = 0; i < order.Length; i++)
        {
            var r = rectangles[i];
            canvas.DrawBitmap(references[request.Occurrences[order[i]].CardIdentifier],
                new SKRect((float)(r.X * width), (float)(r.Y * height), (float)((r.X + r.Width) * width), (float)((r.Y + r.Height) * height)));
        }
        return image;
    }

    internal static Dictionary<CardIdentifier, SKBitmap> SyntheticReferences(CardVisualLocalizationRequest request) =>
        request.Occurrences.Select(c => c.CardIdentifier).Distinct().Select((id, i) => (id, image: SyntheticCard(i)))
            .ToDictionary(p => p.id, p => p.image);
    internal static void DisposeReferences(Dictionary<CardIdentifier, SKBitmap> references)
    { foreach (var image in references.Values) image.Dispose(); }

    [Fact]
    public void GlobalAssignmentBeatsGreedyAndIsDeterministic()
    {
        double[,] scores = { { .99, .98, .1 }, { .97, .1, .1 }, { .1, .1, .99 } };
        Assert.Equal([1, 0, 2], PackVisualAssignment.Solve(scores));
        Assert.Equal(PackVisualAssignment.Solve(scores), PackVisualAssignment.Solve(scores));
        Assert.Throws<ArgumentException>(() => PackVisualAssignment.Solve(new double[15, 15]));
        Assert.Throws<ArgumentException>(() => PackVisualAssignment.Solve(new double[,] { { double.NaN } }));
    }

    [Fact]
    public void AssignmentCancellationIsObserved()
    {
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => PackVisualAssignment.Solve(new double[14, 14], cancelled.Token));
    }

    [Fact]
    public void PixelsRecoverOrderWithoutLogOrRankSorting()
    {
        var request = AutoRequest(4); var references = SyntheticReferences(request);
        try
        {
            using var frame = SyntheticPack(request, references, [3, 1, 0, 2]);
            var result = new CardTemplateRecognizer().Recognize(request, frame, references);
            Assert.True(result.Matches.Count == 4, result.Diagnostic);
            Assert.Equal([3, 1, 0, 2], result.Matches.OrderBy(m => m.VisualSlot).Select(m => m.Key.PackIndex));
            Assert.True(result.IsSafeFor(request));
            Assert.All(result.Matches, m => Assert.True(m.Confidence >= .94));
        }
        finally { DisposeReferences(references); }
    }

    [Fact]
    public void FlatOrUnrelatedFramesNeverAuthorizeBadges()
    {
        var request = AutoRequest(3); var references = SyntheticReferences(request);
        try
        {
            using var frame = new SKBitmap(1000, 600); frame.Erase(SKColors.Gray);
            Assert.Empty(new CardTemplateRecognizer().Recognize(request, frame, references).Matches);
            using var unrelated = SyntheticCard(71);
            using var canvas = new SKCanvas(frame); canvas.DrawBitmap(unrelated, new SKRect(0, 0, 1000, 600));
            Assert.Empty(new CardTemplateRecognizer().Recognize(request, frame, references).Matches);
        }
        finally { DisposeReferences(references); }
    }

    [Fact]
    public void IdenticalArtworkForDifferentIdentifiersIsAmbiguous()
    {
        var request = AutoRequest(2); var references = SyntheticReferences(request);
        try
        {
            references[request.Occurrences[1].CardIdentifier].Dispose();
            references[request.Occurrences[1].CardIdentifier] = references[request.Occurrences[0].CardIdentifier].Copy();
            using var frame = SyntheticPack(request, references, [1, 0]);
            Assert.Empty(new CardTemplateRecognizer().Recognize(request, frame, references).Matches);
        }
        finally { DisposeReferences(references); }
    }

    [Fact]
    public void GenuineDuplicateIdentifiersReceiveSeparateOccurrences()
    {
        var request = AutoRequest(3, true); var references = SyntheticReferences(request);
        try
        {
            using var frame = SyntheticPack(request, references, [2, 1, 0]);
            var result = new CardTemplateRecognizer().Recognize(request, frame, references);
            Assert.Equal(3, result.Matches.Count);
            Assert.Equal(3, result.Matches.Select(m => m.Key).Distinct().Count());
            Assert.Equal(2, result.Matches.Count(m => m.Key.CardIdentifier == request.Occurrences[0].CardIdentifier));
        }
        finally { DisposeReferences(references); }
    }

    [Fact]
    public void MissingReferenceRemainsUnresolvedWhileOtherCardsCanMatch()
    {
        var request = AutoRequest(4); var references = SyntheticReferences(request);
        try
        {
            using var frame = SyntheticPack(request, references, [3, 2, 1, 0]);
            references.Remove(request.Occurrences[1].CardIdentifier, out var missing); missing!.Dispose();
            var result = new CardTemplateRecognizer().Recognize(request, frame, references);
            Assert.Equal(3, result.Matches.Count);
            Assert.DoesNotContain(result.Matches, m => m.Key.PackIndex == 1);
        }
        finally { DisposeReferences(references); }
    }

    [Fact]
    public void CompactRowsAndRegionScalingAreDetectedFromImageEvidence()
    {
        var request = AutoRequest(6); var references = SyntheticReferences(request);
        try
        {
            var rects = CardRectangleProposals.Create(6, request.CalibratedLayout).Skip(1).First();
            using var frame = SyntheticPack(request, references, [5, 4, 3, 2, 1, 0], rects, 1280, 720);
            var result = new CardTemplateRecognizer().Recognize(request, frame, references);
            Assert.Equal(6, result.Matches.Count);
            Assert.All(result.Matches, m => Assert.True(m.Rectangle.IsValid));
            Assert.Equal([5, 4, 3, 2, 1, 0], result.Matches.OrderBy(m => m.VisualSlot).Select(m => m.Key.PackIndex));
        }
        finally { DisposeReferences(references); }
    }

    private static (MainWindowViewModel Session, OverlayViewModel Overlay, CardVisualLocalizationRequest Request) AutoOverlay()
    {
        var (data, statistics) = FourBadgeFixture(); var session = AssociationSession(data.Catalog);
        var overlay = new OverlayViewModel(session); overlay.SetCalibrationState(false, true);
        var pack = AssociationPack(data, [601, 602, 603, 604]); session.ApplySessionUpdate(pack);
        session.ApplyStatisticsUpdate(new(pack.SnapshotResult.Snapshot, false, statistics));
        return (session, overlay, new(session.CurrentPackIdentity!, session.CurrentPackPresentations, overlay.Layout));
    }

    [Fact]
    public void ConfidentAutomaticResultBypassesManualConfirmationAndKeepsStatistics()
    {
        var (session, overlay, request) = AutoOverlay(); using var owner = overlay;
        var before = session.CurrentPackPresentations.ToArray(); var references = SyntheticReferences(request);
        try
        {
            using var frame = SyntheticPack(request, references, [2, 0, 3, 1]);
            Assert.True(overlay.ApplyAutomaticLocalization(overlay.PlacementContext!, new CardTemplateRecognizer().Recognize(request, frame, references)
                with { PackGeneration = overlay.PlacementContext!.Generation }));
            Assert.True(overlay.HasAutomaticVisualPlacement); Assert.False(overlay.HasConfirmedVisualPlacement);
            Assert.All(overlay.Badges, b => { Assert.True(b.IsPlaced); Assert.Same(before[b.Index], b.Presentation); });
            Assert.Contains("Automatic", overlay.LocalizationStatus);
        }
        finally { DisposeReferences(references); }
    }

    [Fact]
    public void PartialResultsPrefillKnownCardsAndOnlyMissingChoicesNeedConfirmation()
    {
        var (_, overlay, request) = AutoOverlay(); using var owner = overlay;
        var references = SyntheticReferences(request);
        try
        {
            using var frame = SyntheticPack(request, references, [3, 2, 1, 0]);
            references.Remove(request.Occurrences[1].CardIdentifier, out var missing); missing!.Dispose();
            var result = new CardTemplateRecognizer().Recognize(request, frame, references) with { PackGeneration = overlay.PlacementContext!.Generation };
            Assert.True(overlay.ApplyAutomaticLocalization(overlay.PlacementContext!, result));
            Assert.Equal(3, overlay.Badges.Count(b => b.IsPlaced));
            var row = Assert.Single(overlay.VisualSlots, s => s.SelectedCard is null);
            row.SelectedCard = row.Choices.Single(c => c.Key.PackIndex == 1);
            Assert.Equal(3, overlay.Badges.Count(b => b.IsPlaced));
            Assert.True(overlay.ConfirmVisualSelections()); Assert.All(overlay.Badges, b => Assert.True(b.IsPlaced));
            Assert.Equal(result.Rectangles[0].X, overlay.Badges.Single(b => b.VisualSlotIndex == 0).X / 1000, 1);
        }
        finally { DisposeReferences(references); }
    }

    [Fact]
    public void ForeignStaleLowConfidenceAndDuplicateMatchesAreRejected()
    {
        var (_, overlay, request) = AutoOverlay(); using var owner = overlay;
        var rects = request.CalibratedLayout.Slots.Take(4).Select(s => new NormalizedDraftRegion(s.X, s.Y, s.Width, s.Height)).ToArray();
        var match = new CardVisualMatch(request.Occurrences[0].Key, 0, rects[0], .98, LocalizationConfidence.HighConfidence, "test");
        var valid = new CardVisualLocalizationResult(request.Pack, [match], rects, 1000, 600, TimeSpan.Zero, "test")
            { PackGeneration = overlay.PlacementContext!.Generation };
        Assert.False(overlay.ApplyAutomaticLocalization(new(request.Pack, overlay.PlacementContext!.Generation), valid));
        foreach (var invalid in new[] { valid with { Matches = [match with { Confidence = .93 }] }, valid with { Matches = [match, match] },
            valid with { Matches = [match with { Confidence = double.NaN }] }, valid with { Matches = [match with { Confidence = 1.2 }] },
            valid with { Matches = [match with { State = LocalizationConfidence.Ambiguous }] },
            valid with { Matches = [match with { Key = new(0, CardIdentifier.Create("foreign")) }] },
            valid with { Matches = [match with { Rectangle = new(2, 0, .1, .1) }] } })
            Assert.False(overlay.ApplyAutomaticLocalization(overlay.PlacementContext!, invalid));
        Assert.All(overlay.Badges, b => Assert.False(b.IsPlaced));
    }

    [Fact]
    public void WindowRelativeAnchorTracksMovementAndDpiWithoutDesktopAssumptions()
    {
        var calibration = new OverlayCalibration(new(-1920, 0, 1920, 1080, 1.5), new(.2, .2, .6, .6), DraftCardLayout.Grid(5));
        var first = new ArenaWindowGeometry(1, -1920, 0, 1920, 1080, 1.5, true);
        var anchor = AutomaticCardLocalizationSession.Anchor(calibration, first)!;
        Assert.Equal(calibration.Region, anchor);
        var moved = first with { X = 100, Y = 80, Width = 1280, Height = 720, Scaling = 2 };
        Assert.Equal(356, moved.X + anchor.X * moved.Width);
        Assert.Equal(384, anchor.Width * moved.Width / moved.Scaling);
        Assert.Null(AutomaticCardLocalizationSession.Anchor(calibration, moved));
    }

    [Fact]
    public async Task ImageCacheUsesOnlyAllowedSmallSourcesAndReusesBytesOffline()
    {
        var dir = Path.Combine(Path.GetTempPath(), "DraftTG-visual-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "card-data"));
        try
        {
            var id = CardIdentifier.Create("e8ebcbfb-1522-4ff5-b23a-f3ea9e08ad9b");
            var url = "https://cards.scryfall.io/small/front/e/8/" + id.Value + ".jpg";
            using (var file = File.Create(Path.Combine(dir, "card-data", "scryfall-default-cards.jsonl.gz")))
            using (var gzip = new GZipStream(file, CompressionMode.Compress))
            using (var writer = new StreamWriter(gzip)) writer.WriteLine(JsonSerializer.Serialize(new { id = id.Value, image_uris = new { small = url } }));
            Directory.CreateDirectory(Path.Combine(dir, "visual-references"));
            using var image = SyntheticCard(0); using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 90);
            File.WriteAllBytes(Path.Combine(dir, "visual-references", id.Value + ".jpg"), encoded.ToArray());
            using var http = new HttpClient(new RejectNetworkHandler()); using var cache = new ScryfallVisualReferenceCache(dir, http);
            Assert.Single(await cache.GetAsync([id, id], CancellationToken.None));
            Assert.Single(await cache.GetAsync([id], CancellationToken.None));
            Assert.True(ScryfallVisualReferenceCache.IsAllowedImageUri(new(url)));
            Assert.False(ScryfallVisualReferenceCache.IsAllowedImageUri(new("https://example.com/small/front/x.jpg")));
            Assert.False(ScryfallVisualReferenceCache.IsAllowedImageUri(new("https://cards.scryfall.io/large/front/x.jpg")));
        }
        finally { Directory.Delete(dir, true); }
    }
    private sealed class RejectNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The cached reference must not make a network request.");
    }

    [Fact]
    public void AllPackCountsHaveBoundedGeometryHypothesesWithoutAuthorizingIdentity()
    {
        for (var count = 14; count >= 1; count--)
        {
            var proposals = CardRectangleProposals.Create(count, DraftCardLayout.Grid(5)).ToArray();
            Assert.InRange(proposals.Length, 1, 17);
            Assert.All(proposals, p => { Assert.Equal(count, p.Length); Assert.All(p, r => Assert.True(r.IsValid)); });
        }
    }

    [Fact]
    public void NextPickClearsAutomaticPlacementAndRejectsPreviousGeneration()
    {
        var (session, overlay, request) = AutoOverlay(); using var owner = overlay;
        var references = SyntheticReferences(request);
        try
        {
            using var frame = SyntheticPack(request, references, [3, 2, 1, 0]);
            var result = new CardTemplateRecognizer().Recognize(request, frame, references);
            var context = overlay.PlacementContext!;
            result = result with { PackGeneration = context.Generation };
            Assert.True(overlay.ApplyAutomaticLocalization(context, result));
            var (data, _) = FourBadgeFixture(); session.ApplySessionUpdate(AssociationPack(data, [601, 602, 603], 2));
            Assert.All(overlay.Badges, b => Assert.False(b.IsPlaced));
            Assert.False(overlay.ApplyAutomaticLocalization(context, result));
            Assert.All(overlay.VisualSlots, s => Assert.Null(s.SelectedCard));
        }
        finally { DisposeReferences(references); }
    }

    [Fact]
    public void DebugImageIsExplicitAndContainsAnnotatedRegionOnly()
    {
        var dir = Path.Combine(Path.GetTempPath(), "DraftTG-debug-" + Guid.NewGuid().ToString("N"));
        var request = AutoRequest(1); var references = SyntheticReferences(request);
        try
        {
            using var frame = SyntheticPack(request, references, [0]);
            var result = new CardTemplateRecognizer().Recognize(request, frame, references);
            LocalizationDebugCapture.Save(dir, frame, request, result);
            using var annotated = SKBitmap.Decode(Path.Combine(dir, "localization-latest.png"));
            Assert.Equal(frame.Width, annotated.Width); Assert.Equal(frame.Height, annotated.Height);
            Assert.Single(Directory.GetFiles(dir));
        }
        finally { DisposeReferences(references); if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void ExactP1P6IdentityOrderIsAutomaticallyRecognizedOnSyntheticImages()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "arena-woe-p1p6-visual-placement.json")));
        var root = fixture.RootElement; var data = new ScryfallCardCatalogDecoder().DecodeCatalogData(root.GetProperty("Cards").GetRawText());
        var raw = root.GetProperty("LogPack"); var engine = new ArenaDraftStateEngine(); ArenaDraftStateSnapshot? arena = null;
        foreach (var fact in new ArenaDraftLogParser().Parse(new ArenaLogSourceEvent.Line(JsonSerializer.Serialize(new { CurrentModule = "BotDraft", Payload = raw.GetRawText() }))))
            arena = engine.Apply(fact).Snapshot;
        var state = Update(arena!, new ArenaDraftSnapshotAdapter(new(data))); var session = AssociationSession(data.Catalog);
        using var overlay = new OverlayViewModel(session); session.ApplySessionUpdate(state);
        var statisticsContext = new LimitedStatisticsContext("WOE", LimitedStatisticsFormat.QuickDraft);
        var ratings = root.GetProperty("Ratings").EnumerateArray().Select(r => new SeventeenLandsRating(
            r.GetProperty("name").GetString()!, GameInHandWinRate: r.GetProperty("ever_drawn_win_rate").GetDouble(),
            GameInHandGameCount: r.GetProperty("ever_drawn_game_count").GetInt32(), AverageLastSeenAt: r.GetProperty("avg_seen").GetDouble())).ToArray();
        var mapped = LimitedStatisticsMapper.Map(ratings, data.Catalog, statisticsContext, state.SnapshotResult.Snapshot);
        var statistics = new LimitedStatisticsLoadResult(statisticsContext, statisticsContext, mapped.Catalog, LimitedStatisticsSource.Cache, null, null)
        { CardCatalog = data.Catalog, StatisticsResolvedNames = mapped.ResolvedNames,
            EnvironmentCatalog = new([new(CardIdentifier.Create("baseline"), GameInHandWinRate: .578, GameInHandGameCount: 10000)]) };
        session.ApplyStatisticsUpdate(new(state.SnapshotResult.Snapshot, false, statistics));
        var request = new CardVisualLocalizationRequest(session.CurrentPackIdentity!, session.CurrentPackPresentations, DraftCardLayout.Grid(5));
        var ids = raw.GetProperty("DraftPack").EnumerateArray().Select(v => int.Parse(v.GetString()!)).ToArray();
        var order = root.GetProperty("VisualArenaIds").EnumerateArray().Select(v => Array.IndexOf(ids, v.GetInt32())).ToArray();
        var references = SyntheticReferences(request);
        try
        {
            using var frame = SyntheticPack(request, references, order);
            var stopwatch = Stopwatch.StartNew(); var result = new CardTemplateRecognizer().Recognize(request, frame, references);
            result = result with { PackGeneration = overlay.PlacementContext!.Generation };
            Assert.Equal(9, result.Matches.Count); Assert.True(overlay.ApplyAutomaticLocalization(overlay.PlacementContext!, result));
            Assert.Equal("Hatching Plans", overlay.Badges.Single(b => b.VisualSlotIndex == 0).Name);
            Assert.Equal("Scarecrow Guide", overlay.Badges.Single(b => b.VisualSlotIndex == 8).Name);
            Assert.Equal(("60.6%", "ALSA 5.22", "#1"), (overlay.Badges[8].WinRate, overlay.Badges[8].Secondary, overlay.Badges[8].Rank));
            Assert.Equal(("55.6%", "ALSA 6.95", "#8"), (overlay.Badges[0].WinRate, overlay.Badges[0].Secondary, overlay.Badges[0].Rank));
            Assert.False(overlay.HasConfirmedVisualPlacement);
            // Generous regression ceiling for bounded event-driven work, not a hardware performance guarantee.
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30));
        }
        finally { DisposeReferences(references); }
    }
}

public sealed class LocalizationBenchmarkTests(ITestOutputHelper output)
{
    [Fact]
    public void KnownPackTemplateBenchmarkMeasuresCleanDimmedAndUnrelatedFrames()
    {
        var request = MainWindowViewModelTests.AutoRequest(9);
        var references = MainWindowViewModelTests.SyntheticReferences(request);
        int[] order = [8, 7, 6, 3, 4, 2, 1, 5, 0];
        try
        {
            foreach (var scenario in new[] { "clean", "dimmed", "unrelated" })
            {
                using var frame = MainWindowViewModelTests.SyntheticPack(request, references, order);
                if (scenario == "dimmed")
                    for (var y = 0; y < frame.Height; y++) for (var x = 0; x < frame.Width; x++)
                    {
                        var c = frame.GetPixel(x, y);
                        frame.SetPixel(x, y, new((byte)(c.Red * .7 + 10), (byte)(c.Green * .7 + 10), (byte)(c.Blue * .7 + 10)));
                    }
                if (scenario == "unrelated") frame.Erase(SKColors.Gray);
                var watch = Stopwatch.StartNew();
                var result = new CardTemplateRecognizer().Recognize(request, frame, references);
                var nccMs = watch.Elapsed.TotalMilliseconds;
                var correct = result.Matches.Count(m => m.Key.PackIndex == order[m.VisualSlot]);
                watch.Restart();
                var hashes = request.Occurrences.Select(c => Hash(references[c.CardIdentifier], new(0, 0, 1, 1))).ToArray();
                var matrix = new double[9, 9];
                for (var slot = 0; slot < 9; slot++)
                {
                    var s = request.CalibratedLayout.Slots[slot]; var hash = Hash(frame, new(s.X, s.Y, s.Width, s.Height));
                    for (var card = 0; card < 9; card++) matrix[slot, card] = 1 - System.Numerics.BitOperations.PopCount(hash ^ hashes[card]) / 64d;
                }
                var hashAssignment = PackVisualAssignment.Solve(matrix);
                var hashCorrect = Enumerable.Range(0, 9).Count(i => hashAssignment[i] == order[i]);
                output.WriteLine($"{scenario}: NCC {correct}/9 accepted-correct, {result.Matches.Count}/9 accepted, {nccMs:F1} ms; "
                    + $"dHash forced assignment {hashCorrect}/9 correct, {watch.Elapsed.TotalMilliseconds:F1} ms (known geometry; no confidence gate)");
                Assert.Equal(scenario == "unrelated" ? 0 : 9, correct);
            }
        }
        finally { MainWindowViewModelTests.DisposeReferences(references); }
    }

    private static ulong Hash(SKBitmap image, NormalizedDraftRegion r)
    {
        ulong hash = 0;
        double Gray(int x, int y)
        {
            var px = Math.Clamp((int)((r.X + r.Width * (.075 + .85 * x / 8)) * image.Width), 0, image.Width - 1);
            var py = Math.Clamp((int)((r.Y + r.Height * (.165 + .40 * (y + .5) / 8)) * image.Height), 0, image.Height - 1);
            var c = image.GetPixel(px, py); return .299 * c.Red + .587 * c.Green + .114 * c.Blue;
        }
        for (var y = 0; y < 8; y++) for (var x = 0; x < 8; x++) if (Gray(x, y) > Gray(x + 1, y)) hash |= 1ul << (y * 8 + x);
        return hash;
    }
}
