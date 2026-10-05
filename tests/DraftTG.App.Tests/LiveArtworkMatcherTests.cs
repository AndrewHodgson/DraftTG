using System.Text.Json;
using DraftTG.Application;
using DraftTG.App;
using DraftTG.Domain;
using SkiaSharp;

namespace DraftTG.App.Tests;

public sealed class LiveArtworkMatcherTests
{
    private static string FixtureDirectory => Path.Combine(AppContext.BaseDirectory, "Fixtures", "arena-artwork-live");
    private sealed class LiveFixture : IDisposable
    {
        public OfflineLocalizationManifest Manifest { get; } = JsonSerializer.Deserialize<OfflineLocalizationManifest>(
            File.ReadAllText(Path.Combine(FixtureDirectory, "manifest.json")), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        public SKBitmap Frame { get; } = SKBitmap.Decode(Path.Combine(FixtureDirectory, "arena-draft-capture.png"));
        public Dictionary<CardIdentifier, SKBitmap> References { get; }
        public CardVisualLocalizationRequest Request { get; }
        public LiveFixture()
        {
            Request = OfflineArtworkCommand.CreateRequest(Manifest);
            References = OfflineArtworkCommand.LoadReferences(Manifest, FixtureDirectory);
        }
        public void Dispose() { Frame.Dispose(); foreach (var image in References.Values) image.Dispose(); }
    }
    private static void AssertCorrect(CardVisualLocalizationResult result, LiveFixture fixture)
    {
        Assert.Equal(9, result.Matches.Count); Assert.True(result.IsSafeFor(fixture.Request));
        Assert.Equal(fixture.Manifest.ExpectedVisualOrder, result.Matches.OrderBy(m => m.VisualSlot).Select(m => m.Key.CardIdentifier.Value));
        Assert.All(result.Matches, match => Assert.True(match.Confidence >= CardVisualLocalizationResult.MinimumConfidence));
    }

    [Fact]
    public void OriginalLiveFailureIsCorrectAssignmentRejectedOnlyByThreshold()
    {
        using var fixture = new LiveFixture(); CardRecognitionAudit? audit = null;
        var result = new CardTemplateRecognizer().Recognize(fixture.Request, fixture.Frame, fixture.References,
            diagnostic: value => audit = value, registerArtwork: false);
        Assert.Empty(result.Matches); Assert.Empty(audit!.MissingReferences);
        double[] before = [.900651, .883120, .803668, .795374, .805130, .906660, .856670, .788314, .876549];
        Assert.Equal(fixture.Manifest.ExpectedVisualOrder, audit.Slots.Select(s => s.Best.Identifier));
        Assert.Equal(fixture.Manifest.ExpectedVisualOrder, audit.Slots.Select(s => s.Assigned.Identifier));
        foreach (var slot in audit.Slots)
        {
            Assert.InRange(Math.Abs(slot.Assigned.Score - before[slot.Slot - 1]), 0, .000001);
            Assert.Equal(.94, slot.Threshold); Assert.Equal(.07, slot.RequiredMargin);
            Assert.StartsWith("assigned score", slot.RejectionReason); Assert.DoesNotContain(";", slot.RejectionReason);
        }
    }

    [Fact]
    public void RegisteredLiveArtworkMatchesAllNineWithoutLoweringThreshold()
    {
        using var fixture = new LiveFixture(); CardRecognitionAudit? audit = null;
        var result = new CardTemplateRecognizer().Recognize(fixture.Request, fixture.Frame, fixture.References, diagnostic: value => audit = value);
        AssertCorrect(result, fixture);
        Assert.All(audit!.Slots, slot =>
        {
            Assert.True(slot.Accepted); Assert.Empty(slot.RejectionReason); Assert.InRange(slot.Assigned.Score, .97, 1);
            Assert.Equal(.94, slot.Threshold); Assert.Equal(.07, slot.RequiredMargin);
            Assert.Equal(slot.Best.Identifier, slot.Assigned.Identifier);
            Assert.True(slot.Assigned.ArtworkRectangle.IsValid);
        });
        Assert.Equal(9, result.Matches.Select(m => m.Key).Distinct().Count());
    }

    [Fact]
    public void LiveArtworkSurvivesCaptureResizingAndUniformDimming()
    {
        using var fixture = new LiveFixture(); using var original = SKImage.FromBitmap(fixture.Frame);
        foreach (var scale in new[] { .75, 1.20 })
        {
            using var frame = new SKBitmap((int)Math.Round(fixture.Frame.Width * scale), (int)Math.Round(fixture.Frame.Height * scale));
            using (var canvas = new SKCanvas(frame))
                canvas.DrawImage(original, new SKRect(0, 0, frame.Width, frame.Height), new SKSamplingOptions(SKFilterMode.Linear));
            for (var y = 0; y < frame.Height; y++) for (var x = 0; x < frame.Width; x++)
            {
                var c = frame.GetPixel(x, y);
                frame.SetPixel(x, y, new((byte)(c.Red * .8 + 8), (byte)(c.Green * .8 + 8), (byte)(c.Blue * .8 + 8)));
            }
            AssertCorrect(new CardTemplateRecognizer().Recognize(fixture.Request, frame, fixture.References), fixture);
        }
    }

    [Fact]
    public void MissingExactPrintingReferenceStaysUnresolvedOnRealFrame()
    {
        using var fixture = new LiveFixture(); var missingId = CardIdentifier.Create(fixture.Manifest.ExpectedVisualOrder![0]);
        fixture.References.Remove(missingId, out var missing); missing!.Dispose(); CardRecognitionAudit? audit = null;
        var result = new CardTemplateRecognizer().Recognize(fixture.Request, fixture.Frame, fixture.References, diagnostic: value => audit = value);
        Assert.DoesNotContain(result.Matches, m => m.Key.CardIdentifier == missingId);
        Assert.Equal(8, result.Matches.Count); Assert.Contains(missingId.Value, audit!.MissingReferences);
        Assert.All(result.Matches, m => Assert.Equal(fixture.Manifest.ExpectedVisualOrder[m.VisualSlot], m.Key.CardIdentifier.Value));
    }

    [Fact]
    public void WrongReferenceArtworkCannotAuthorizeTheWrongCardIdentity()
    {
        using var fixture = new LiveFixture();
        var wrong = CardIdentifier.Create(fixture.Manifest.ExpectedVisualOrder![0]);
        var donor = CardIdentifier.Create(fixture.Manifest.ExpectedVisualOrder[4]);
        fixture.References[wrong].Dispose(); fixture.References[wrong] = fixture.References[donor].Copy();
        var result = new CardTemplateRecognizer().Recognize(fixture.Request, fixture.Frame, fixture.References);
        Assert.DoesNotContain(result.Matches, m => m.Key.CardIdentifier == wrong || m.Key.CardIdentifier == donor);
        Assert.All(result.Matches, m => Assert.Equal(fixture.Manifest.ExpectedVisualOrder[m.VisualSlot], m.Key.CardIdentifier.Value));
    }

    [Fact]
    public void OfflineCommandWritesCompleteSlotEvidenceWithoutArenaOrNetwork()
    {
        var directory = Path.Combine(Path.GetTempPath(), "DraftTG-artwork-command-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Equal(0, OfflineArtworkCommand.Run(["--localize-image", Path.Combine(FixtureDirectory, "arena-draft-capture.png"),
                "--manifest", Path.Combine(FixtureDirectory, "manifest.json"), "--output", directory]));
            var text = File.ReadAllText(Path.Combine(directory, "scores.txt"));
            Assert.Contains("matched 9/9", text); Assert.Contains("Slot 9:", text);
            Assert.Contains("Expected: Hatching Plans", text); Assert.Contains("Second:", text);
            Assert.Contains("Artwork crop (pixels x/y/w/h)", text); Assert.Contains("Threshold: 0.94; required margin: 0.07", text);
            Assert.Contains("https://cards.scryfall.io/small/front/", text);
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "scores.json")));
            Assert.Equal(9, json.RootElement.GetProperty("Audit").GetProperty("Slots").GetArrayLength());
            Assert.True(File.Exists(Path.Combine(directory, "localization-latest.png")));
        }
        finally
        {
            // Only files created in this unique test directory; no recursive deletion.
            if (Directory.Exists(directory))
            { foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file); Directory.Delete(directory); }
        }
    }
}
