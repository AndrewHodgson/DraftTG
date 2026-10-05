using System.Diagnostics;
using DraftTG.Application;
using DraftTG.Domain;
using SkiaSharp;

namespace DraftTG.App;

internal sealed record CardRecognitionCandidate(int PackIndex, string Name, string Identifier, double Score, NormalizedDraftRegion Rectangle)
{
    public NormalizedDraftRegion ArtworkRectangle => CardTemplateRecognizer.ArtworkCore(Rectangle);
}
internal sealed record CardRecognitionSlotAudit(int Slot, NormalizedDraftRegion Rectangle,
    CardRecognitionCandidate Best, CardRecognitionCandidate? Second, CardRecognitionCandidate Assigned,
    double CompetingSlotScore, double Threshold, double RequiredMargin, bool Accepted, string RejectionReason,
    IReadOnlyList<CardRecognitionCandidate> Candidates);
internal sealed record CardRecognitionAudit(int Width, int Height, string Method, int Matched,
    IReadOnlyList<string> MissingReferences, IReadOnlyList<CardRecognitionSlotAudit> Slots);

/// <summary>Geometry proposals are hypotheses; only image evidence can authorize a placement.</summary>
internal static class CardRectangleProposals
{
    public static IEnumerable<NormalizedDraftRegion[]> Create(int count, DraftCardLayout calibrated)
    {
        if (count is < 1 or > 14) yield break;
        yield return calibrated.Slots.Take(count).Select(s => new NormalizedDraftRegion(s.X, s.Y, s.Width, s.Height)).ToArray();
        for (var columns = 4; columns <= 7; columns++)
        foreach (var rows in new[] { (int)Math.Ceiling(count / (double)columns), (int)Math.Ceiling(14d / columns) }.Distinct())
        foreach (var centered in new[] { false, true })
        {
            yield return Enumerable.Range(0, count).Select(i =>
            {
                var inRow = Math.Min(columns, count - i / columns * columns);
                var offset = centered ? (columns - inRow) / 2d : 0;
                return new NormalizedDraftRegion((i % columns + offset + .025) / columns,
                    (i / columns + .025) / rows, .95 / columns, .95 / rows);
            }).ToArray();
        }
    }
}

internal sealed class CardTemplateRecognizer
{
    // A strict similarity gate, not a calibrated probability of correctness.
    public const double IdentityMargin = .07;
    private const int SampleWidth = 20, SampleHeight = 14;

    public CardVisualLocalizationResult Recognize(CardVisualLocalizationRequest request, SKBitmap frame,
        IReadOnlyDictionary<CardIdentifier, SKBitmap> references, CancellationToken cancellationToken = default,
        Action<CardRecognitionAudit>? diagnostic = null, bool registerArtwork = true)
    {
        var watch = Stopwatch.StartNew();
        var method = registerArtwork ? "known-pack-art-NCC-registered" : "known-pack-art-NCC";
        var occurrences = request.Occurrences;
        var n = occurrences.Count;
        if (n != request.Pack.AvailableCardIdentifiers.Count || n is < 1 or > 14
            || occurrences.Where((c, i) => c.Key != new CardOccurrenceKey(i, request.Pack.AvailableCardIdentifiers[i])).Any())
            return new(request.Pack, [], [], frame.Width, frame.Height, watch.Elapsed, "art-template", "Invalid pack occurrences.");
        var sampledFrame = new IntegralImage(frame);
        var templates = occurrences.Select(c => references.TryGetValue(c.CardIdentifier, out var image)
            ? Describe(new IntegralImage(image), new(0, 0, 1, 1)) : null).ToArray();
        double[,]? bestScores = null;
        NormalizedDraftRegion[,]? bestBounds = null;
        int[]? bestAssignment = null;
        var bestTotal = double.NegativeInfinity;
        foreach (var proposal in CardRectangleProposals.Create(n, request.CalibratedLayout))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var scores = new double[n, n];
            var bounds = new NormalizedDraftRegion[n, n];
            for (var slot = 0; slot < n; slot++)
            {
                for (var card = 0; card < n; card++) bounds[slot, card] = proposal[slot];
                foreach (var rectangle in Refine(proposal[slot], frame.Width, frame.Height))
                {
                    var descriptor = Describe(sampledFrame, rectangle);
                    for (var card = 0; card < n; card++)
                    {
                        var score = Similarity(descriptor, templates[card]);
                        if (score <= scores[slot, card]) continue;
                        scores[slot, card] = score;
                        bounds[slot, card] = rectangle;
                    }
                }
            }
            var assignment = PackVisualAssignment.Solve(scores, cancellationToken);
            var total = Enumerable.Range(0, n).Sum(slot => scores[slot, assignment[slot]]);
            if (total <= bestTotal) continue;
            bestTotal = total; bestScores = scores; bestBounds = bounds; bestAssignment = assignment;
        }
        // Coarse proposals locate cards. Register artwork locally at finer resolution instead of
        // forcing Arena's title/text/frame proportions to coincide with a Scryfall portrait card.
        if (registerArtwork)
        {
            for (var slot = 0; slot < n; slot++)
            for (var card = 0; card < n; card++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (templates[card] is null) continue;
                var refined = RegisterArtwork(sampledFrame, bestBounds![slot, card], templates[card]!, bestScores![slot, card]);
                bestBounds[slot, card] = refined.Rectangle; bestScores[slot, card] = refined.Score;
            }
        }
        bestAssignment = PackVisualAssignment.Solve(bestScores!, cancellationToken);
        var rectangles = Enumerable.Range(0, n).Select(slot => bestBounds![slot, bestAssignment![slot]]
            ?? new NormalizedDraftRegion(0, 0, 1, 1)).ToArray();
        var matches = new List<CardVisualMatch>();
        var evidence = new List<string>();
        for (var slot = 0; slot < n; slot++)
        {
            var card = bestAssignment![slot];
            var key = occurrences[card].Key;
            var score = bestScores![slot, card];
            var otherIdentity = Enumerable.Range(0, n).Where(c => occurrences[c].CardIdentifier != key.CardIdentifier)
                .Select(c => bestScores[slot, c]).DefaultIfEmpty(0).Max();
            var otherSlot = Enumerable.Range(0, n).Where(s => s != slot && occurrences[bestAssignment[s]].CardIdentifier != key.CardIdentifier)
                .Select(s => bestScores[s, card]).DefaultIfEmpty(0).Max();
            var overlap = Enumerable.Range(0, n).Any(s => s != slot && Intersection(rectangles[slot], rectangles[s]) > .25);
            evidence.Add($"Slot {slot + 1}: {occurrences[card].CardName}; NCC {score:F3}; alternative {Math.Max(otherIdentity, otherSlot):F3}; overlap {overlap}");
            if (score < CardVisualLocalizationResult.MinimumConfidence || score - Math.Max(otherIdentity, otherSlot) < IdentityMargin || overlap) continue;
            matches.Add(new(key, slot, rectangles[slot], score, LocalizationConfidence.HighConfidence, method));
        }
        // Unrecognizable geometry cannot anchor a partial map. Avoid filling selectors from a guessed layout.
        if (matches.Count < Math.Max(1, (n + 1) / 2)) matches.Clear();
        diagnostic?.Invoke(CreateAudit(request, frame, references, bestScores!, bestBounds!, bestAssignment!, rectangles, matches, method));
        return new(request.Pack, matches.AsReadOnly(), Array.AsReadOnly(rectangles), frame.Width, frame.Height,
            watch.Elapsed, method, (matches.Count == n ? "" : "Some artwork or geometry is unresolved; use the position matcher.\n") + string.Join("\n", evidence));
    }

    private static CardRecognitionAudit CreateAudit(CardVisualLocalizationRequest request, SKBitmap frame,
        IReadOnlyDictionary<CardIdentifier, SKBitmap> references, double[,] scores, NormalizedDraftRegion[,] bounds,
        int[] assignment, NormalizedDraftRegion[] rectangles, List<CardVisualMatch> matches, string method)
    {
        var occurrences = request.Occurrences; var n = occurrences.Count;
        var slots = Enumerable.Range(0, n).Select(slot =>
        {
            var candidates = Enumerable.Range(0, n).Select(card => new CardRecognitionCandidate(card, occurrences[card].CardName,
                occurrences[card].CardIdentifier.Value, scores[slot, card], bounds[slot, card])).OrderByDescending(c => c.Score).ThenBy(c => c.PackIndex).ToArray();
            var assigned = candidates.Single(c => c.PackIndex == assignment[slot]);
            var second = candidates.FirstOrDefault(c => c.Identifier != candidates[0].Identifier);
            var otherIdentity = candidates.Where(c => c.Identifier != assigned.Identifier).Select(c => c.Score).DefaultIfEmpty(0).Max();
            var otherSlot = Enumerable.Range(0, n).Where(s => s != slot && occurrences[assignment[s]].CardIdentifier.Value != assigned.Identifier)
                .Select(s => scores[s, assignment[slot]]).DefaultIfEmpty(0).Max();
            var overlap = Enumerable.Range(0, n).Any(s => s != slot && Intersection(rectangles[slot], rectangles[s]) > .25);
            var reasons = new List<string>();
            if (!references.ContainsKey(occurrences[assignment[slot]].CardIdentifier)) reasons.Add("missing exact-printing reference");
            if (assigned.Score < CardVisualLocalizationResult.MinimumConfidence) reasons.Add($"assigned score {assigned.Score:F6} below threshold {CardVisualLocalizationResult.MinimumConfidence:F2}");
            if (assigned.Score - Math.Max(otherIdentity, otherSlot) < IdentityMargin) reasons.Add($"identity/slot margin {assigned.Score - Math.Max(otherIdentity, otherSlot):F6} below {IdentityMargin:F2}");
            if (overlap) reasons.Add("card rectangles overlap by more than 0.25");
            var accepted = matches.Any(m => m.VisualSlot == slot);
            if (!accepted && reasons.Count == 0) reasons.Add("too few slots passed the pack geometry guard");
            return new CardRecognitionSlotAudit(slot + 1, rectangles[slot], candidates[0], second, assigned, otherSlot,
                CardVisualLocalizationResult.MinimumConfidence, IdentityMargin, accepted, string.Join("; ", reasons), candidates);
        }).ToArray();
        return new(frame.Width, frame.Height, method, matches.Count,
            occurrences.Where(c => !references.ContainsKey(c.CardIdentifier)).Select(c => c.CardIdentifier.Value).ToArray(), slots);
    }

    internal static NormalizedDraftRegion ArtworkCore(NormalizedDraftRegion card) =>
        new(card.X + card.Width * .075, card.Y + card.Height * .165, card.Width * .85, card.Height * .40);

    private static (NormalizedDraftRegion Rectangle, double Score) RegisterArtwork(IntegralImage frame,
        NormalizedDraftRegion seed, double[] template, double initialScore)
    {
        var rectangle = seed; var score = initialScore;
        // Bounded coordinate descent, coarse to sub-pixel. Width/height are independent because
        // Arena can stretch artwork differently from the paper-card frame. No new acceptance gate.
        foreach (var step in new[] { .04, .02, .01, .005, .0025 })
        for (var iteration = 0; iteration < 4; iteration++)
        {
            var next = rectangle; var nextScore = score;
            foreach (var candidate in AlignmentNeighbors(rectangle, step))
            {
                if (!candidate.IsValid || Math.Abs(candidate.X - seed.X) > .10 * seed.Width
                    || Math.Abs(candidate.Y - seed.Y) > .10 * seed.Height
                    || candidate.Width / seed.Width is < .85 or > 1.15 || candidate.Height / seed.Height is < .85 or > 1.15) continue;
                var value = Similarity(Describe(frame, candidate), template);
                if (value <= nextScore + 1e-8) continue;
                next = candidate; nextScore = value;
            }
            if (next == rectangle) break;
            rectangle = next; score = nextScore;
        }
        return (rectangle, score);
    }
    private static IEnumerable<NormalizedDraftRegion> AlignmentNeighbors(NormalizedDraftRegion r, double step)
    {
        foreach (var direction in new[] { -1, 1 })
        {
            yield return r with { X = r.X + direction * step * r.Width };
            yield return r with { Y = r.Y + direction * step * r.Height };
            // Scale the artwork around its centre, retaining the card-coordinate representation.
            var art = ArtworkCore(r);
            var width = r.Width * (1 + direction * step); var height = r.Height * (1 + direction * step);
            yield return r with { Width = width, X = art.X + (art.Width - width * .85) / 2 - width * .075 };
            yield return r with { Height = height, Y = art.Y + (art.Height - height * .40) / 2 - height * .165 };
        }
    }

    private static IEnumerable<NormalizedDraftRegion> Refine(NormalizedDraftRegion cell, int width, int height)
    {
        // Preserve a physical portrait-card aspect ratio as well as the exact user-calibrated cell.
        foreach (var portrait in new[] { false, true })
        foreach (var scale in new[] { .85, 1d, 1.1 })
        foreach (var dx in new[] { -.06, 0, .06 })
        foreach (var dy in new[] { -.06, 0, .06 })
        {
            var w = cell.Width * scale;
            var h = portrait ? w * width / height * 204d / 146 : cell.Height * scale;
            var r = new NormalizedDraftRegion(cell.X + (cell.Width - w) / 2 + dx * cell.Width,
                cell.Y + (cell.Height - h) / 2 + dy * cell.Height, w, h);
            if (r.IsValid) yield return r;
        }
    }

    private static double Intersection(NormalizedDraftRegion a, NormalizedDraftRegion b) =>
        Math.Max(0, Math.Min(a.X + a.Width, b.X + b.Width) - Math.Max(a.X, b.X))
        * Math.Max(0, Math.Min(a.Y + a.Height, b.Y + b.Height) - Math.Max(a.Y, b.Y)) / Math.Min(a.Width * a.Height, b.Width * b.Height);

    private static double[]? Describe(IntegralImage image, NormalizedDraftRegion rectangle)
    {
        // Match the standard front artwork, excluding frame, text, foil effects and the badge area.
        // Nonstandard frames / different Arena artwork fail closed rather than changing identity rules.
        var values = new double[SampleWidth * SampleHeight * 3];
        var k = 0;
        for (var y = 0; y < SampleHeight; y++)
        for (var x = 0; x < SampleWidth; x++)
        {
            var left = (rectangle.X + rectangle.Width * (.075 + .85 * x / SampleWidth)) * image.Width;
            var top = (rectangle.Y + rectangle.Height * (.165 + .40 * y / SampleHeight)) * image.Height;
            var right = left + rectangle.Width * .85 / SampleWidth * image.Width;
            var bottom = top + rectangle.Height * .40 / SampleHeight * image.Height;
            for (var channel = 0; channel < 3; channel++) values[k++] = image.Mean(left, top, right, bottom, channel);
        }
        var mean = values.Average();
        var norm = Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)));
        if (norm / Math.Sqrt(values.Length) < 12) return null; // Flat backgrounds are not evidence.
        for (var i = 0; i < values.Length; i++) values[i] = (values[i] - mean) / norm;
        return values;
    }

    /// <summary>Constant-time area averaging prevents aliasing while avoiding a large CV dependency.</summary>
    private sealed class IntegralImage
    {
        public int Width { get; }
        public int Height { get; }
        private readonly long[][] _sums;
        public IntegralImage(SKBitmap image)
        {
            Width = image.Width; Height = image.Height;
            _sums = Enumerable.Range(0, 3).Select(_ => new long[checked((Width + 1) * (Height + 1))]).ToArray();
            for (var y = 1; y <= Height; y++)
            {
                long red = 0, green = 0, blue = 0;
                for (var x = 1; x <= Width; x++)
                {
                    var color = image.GetPixel(x - 1, y - 1);
                    red += color.Red; green += color.Green; blue += color.Blue;
                    var i = y * (Width + 1) + x;
                    _sums[0][i] = _sums[0][i - Width - 1] + red;
                    _sums[1][i] = _sums[1][i - Width - 1] + green;
                    _sums[2][i] = _sums[2][i - Width - 1] + blue;
                }
            }
        }
        public double Mean(double left, double top, double right, double bottom, int channel)
        {
            var l = Math.Clamp((int)Math.Round(left), 0, Width - 1);
            var t = Math.Clamp((int)Math.Round(top), 0, Height - 1);
            var r = Math.Clamp((int)Math.Round(right), l + 1, Width);
            var b = Math.Clamp((int)Math.Round(bottom), t + 1, Height);
            var sums = _sums[channel]; var stride = Width + 1;
            return (sums[b * stride + r] - sums[b * stride + l] - sums[t * stride + r] + sums[t * stride + l]) / (double)((r - l) * (b - t));
        }
    }

    private static double Similarity(double[]? a, double[]? b)
    {
        if (a is null || b is null) return 0;
        double total = 0, first = 0, second = 0, firstA = 0, firstB = 0, secondA = 0, secondB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            total += a[i] * b[i];
            if (i % (SampleWidth * 3) < SampleWidth * 3 / 2)
            { first += a[i] * b[i]; firstA += a[i] * a[i]; firstB += b[i] * b[i]; }
            else { second += a[i] * b[i]; secondA += a[i] * a[i]; secondB += b[i] * b[i]; }
        }
        // Independent left/right agreement guards against a matching patch inside unrelated art.
        if (firstA * firstB <= 1e-12 || secondA * secondB <= 1e-12) return 0;
        return Math.Clamp(Math.Min(total, Math.Min(first / Math.Sqrt(firstA * firstB), second / Math.Sqrt(secondA * secondB))), 0, 1);
    }
}
