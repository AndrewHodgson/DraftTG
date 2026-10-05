using System.Text;
using System.Text.Json;
using DraftTG.Application;
using DraftTG.Domain;
using SkiaSharp;

namespace DraftTG.App;

internal sealed record OfflineReference(string Id, string Name, int ArenaId, string Set, string CollectorNumber,
    string ReferenceFile, string ReferenceUri, string? ArtUri = null, string? Artist = null);
internal sealed record OfflineLocalizationManifest(int PackNumber, int PickNumber, int Columns,
    OfflineReference[] Cards, string[]? ExpectedVisualOrder = null);

/// <summary>Explicit offline developer command. No window discovery, capture, network or scoring.</summary>
internal static class OfflineArtworkCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    public static int Run(string[] args)
    {
        try
        {
            if (args.Length is < 6 or > 7 || args[0] != "--localize-image" || args[2] != "--manifest" || args[4] != "--output"
                || (args.Length == 7 && args[6] != "--coarse-only"))
                throw new ArgumentException("Usage: DraftTG.App --localize-image <PNG> --manifest <JSON> --output <directory> [--coarse-only]");
            var manifestPath = Path.GetFullPath(args[3]);
            var manifest = JsonSerializer.Deserialize<OfflineLocalizationManifest>(File.ReadAllText(manifestPath), JsonOptions)
                ?? throw new InvalidDataException("Empty manifest.");
            using var frame = SKBitmap.Decode(args[1]) ?? throw new InvalidDataException("Image could not be decoded.");
            var request = CreateRequest(manifest);
            var references = LoadReferences(manifest, Path.GetDirectoryName(manifestPath)!);
            try
            {
                CardRecognitionAudit? audit = null;
                var result = new CardTemplateRecognizer().Recognize(request, frame, references, diagnostic: value => audit = value,
                    registerArtwork: args.Length != 7);
                if (audit is null) throw new InvalidDataException(result.Diagnostic);
                var output = Path.GetFullPath(args[5]); Directory.CreateDirectory(output);
                var referenceDimensions = manifest.Cards.Select(c => references.TryGetValue(CardIdentifier.Create(c.Id), out var image)
                    ? $"Decoded reference: {c.Name}; {image.Width} x {image.Height}; exact ID {c.Id}"
                    : $"Decoded reference: {c.Name}; MISSING; exact ID {c.Id}").ToArray();
                var text = Format(audit, manifest) + "\n" + string.Join("\n", referenceDimensions) + "\n";
                File.WriteAllText(Path.Combine(output, "scores.txt"), text);
                File.WriteAllText(Path.Combine(output, "scores.json"), JsonSerializer.Serialize(new { Manifest = manifest, Audit = audit, ReferenceDimensions = referenceDimensions }, JsonOptions));
                LocalizationDebugCapture.Save(output, frame, request, result);
                Console.WriteLine(text);
                return 0;
            }
            finally { foreach (var image in references.Values) image.Dispose(); }
        }
        catch (Exception ex) { Console.Error.WriteLine($"{ex.GetType().Name}: {ex.Message}"); return 1; }
    }
    internal static CardVisualLocalizationRequest CreateRequest(OfflineLocalizationManifest manifest)
    {
        var ids = manifest.Cards.Select(c => CardIdentifier.Create(c.Id)).ToArray();
        var pack = new DraftPack(new(PackNumber.Create(manifest.PackNumber), PickNumber.Create(manifest.PickNumber)), ids);
        return new(pack, manifest.Cards.Select((c, i) => new CurrentPackCardPresentation(new(i, ids[i]),
            new Card(ids[i], c.Name, ColorSet.Colorless, CardRarity.Common, CardSetCode.Create(c.Set), CollectorNumber.Create(c.CollectorNumber)))).ToArray(),
            DraftCardLayout.Grid(manifest.Columns));
    }
    internal static Dictionary<CardIdentifier, SKBitmap> LoadReferences(OfflineLocalizationManifest manifest, string directory)
    {
        var references = new Dictionary<CardIdentifier, SKBitmap>();
        try
        {
            foreach (var card in manifest.Cards)
            {
                var id = CardIdentifier.Create(card.Id); if (references.ContainsKey(id)) continue;
                var path = Path.Combine(directory, card.ReferenceFile);
                if (File.Exists(path) && SKBitmap.Decode(path) is { } image) references.Add(id, image);
            }
            return references;
        }
        catch { foreach (var image in references.Values) image.Dispose(); throw; }
    }
    internal static string Format(CardRecognitionAudit audit, OfflineLocalizationManifest manifest)
    {
        var text = new StringBuilder($"Method: {audit.Method}; frame {audit.Width} x {audit.Height}; matched {audit.Matched}/{audit.Slots.Count}\n");
        foreach (var reference in manifest.Cards)
            text.AppendLine($"Reference: {reference.Name}; ID {reference.Id}; Arena {reference.ArenaId}; {reference.Set}/{reference.CollectorNumber}; {reference.ReferenceUri}; file {reference.ReferenceFile}");
        foreach (var slot in audit.Slots)
        {
            var expected = manifest.ExpectedVisualOrder is { } order && order.Length >= slot.Slot
                ? manifest.Cards.First(c => c.Id == order[slot.Slot - 1]).Name : "unspecified";
            var r = slot.Rectangle;
            text.AppendLine($"\nSlot {slot.Slot}:\n  Expected: {expected}\n  Crop (pixels x/y/w/h): {r.X * audit.Width:F2}/{r.Y * audit.Height:F2}/{r.Width * audit.Width:F2}/{r.Height * audit.Height:F2}");
            text.AppendLine($"  Best: {slot.Best.Name}; score {slot.Best.Score:F6}\n  Second: {slot.Second?.Name ?? "none"}; score {slot.Second?.Score:F6}\n  Assigned: {slot.Assigned.Name}; score {slot.Assigned.Score:F6}");
            text.AppendLine($"  Competing-slot score: {slot.CompetingSlotScore:F6}\n  Threshold: {slot.Threshold:F2}; required margin: {slot.RequiredMargin:F2}\n  Result: {(slot.Accepted ? "accepted" : "rejected — " + slot.RejectionReason)}");
            var art = slot.Assigned.ArtworkRectangle;
            text.AppendLine($"  Artwork crop (pixels x/y/w/h): {art.X * audit.Width:F2}/{art.Y * audit.Height:F2}/{art.Width * audit.Width:F2}/{art.Height * audit.Height:F2}\n  Normalization: 20 x 14 area-averaged RGB; mean/energy normalized; NCC with left/right agreement");
        }
        return text.ToString();
    }
}
