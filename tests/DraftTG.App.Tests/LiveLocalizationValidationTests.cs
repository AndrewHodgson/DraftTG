using System.Text.Json;
using System.Text.Json.Nodes;

namespace DraftTG.App.Tests;

public sealed class LiveLocalizationValidationTests
{
    // Numeric records are confined to this unit-test fixture; never appended to the live ledger.
    private static JsonObject Observation(int pick = 1, string draft = "draft-a", int width = 1723, int height = 1009)
    {
        var slots = Enumerable.Range(0, 13).Select(i => new
        {
            Slot = i, ExpectedOccurrence = i, ArenaId = 100 + i, CardIdentity = "card-" + i,
            Confidence = .94, AmbiguityMargin = .07, Accepted = true
        }).ToArray();
        return JsonSerializer.SerializeToNode(new
        {
            Schema = 3, Source = LiveLocalizationValidationSummaryCommand.Source, Kind = "PackObservation",
            LiveValidationMode = true, BuildId = "test-build", Build = "test-only", SessionId = "test-session",
            DraftScope = draft, DraftIdentifierDigest = draft, Backend = "Windows.Graphics.Capture", LiveSynchronizedFrame = true,
            Pack = 1, Pick = pick, CardCount = 13, ClientWidth = width, ClientHeight = height, DpiScale = 1, Dpi = 96,
            WindowMode = "Windowed", GeometryAvailable = true, OrderStatus = "Unanimous", Deterministic = "Available",
            PredictionExistedBeforeVerification = true, CurrentGenerationFrame = true, ManualPlacementInvolved = false,
            PackGeneration = pick, CurrentPackGeneration = pick, FramePackGeneration = pick,
            CaptureGeneration = 1, CaptureRequestGeneration = pick, WgcPackGeneration = pick, WgcCaptureRequestGeneration = pick,
            PackChangedAt = "2026-10-12T10:00:00Z", CapturedAt = "2026-10-12T10:00:01Z", PredictedAt = "2026-10-12T10:00:00Z",
            VerificationStartedAt = "2026-10-12T10:00:02Z",
            WgcFullyMatched = true, FullPackVerified = true, PlacementAgreedWithWgc = true, ReadingOrderAgrees = true,
            Shadow = "ExactAgreement", MaxScatter = 0, RmsScatter = 0, OffsetX = 0, OffsetY = 0,
            PredictedOrderIdentities = new[] { Enumerable.Range(100, 13).ToArray() },
            CandidateVerifications = new[] { new { FullyVerified = true, Order = Enumerable.Range(100, 13).ToArray(), Slots = slots } },
            DeterministicSlots = Enumerable.Range(0, 13).Select(i => new { Slot = i, PackIndex = i, ArenaGrpId = 100 + i, CardIdentity = "card-" + i }).ToArray(),
            WgcMatches = Enumerable.Range(0, 13).Select(i => new { PackIndex = i, ArenaId = 100 + i, CardIdentity = "card-" + i, Confidence = .98 }).ToArray(),
            ComparisonSlots = Enumerable.Range(0, 13).Select(i => new { PredictedSlot = i, IdentityAgrees = true }).ToArray(),
            FallbackInvoked = false, WgcFullMatcherInvoked = true, FullMatcherMilliseconds = 100, VerificationMilliseconds = 50
        })!.AsObject();
    }

    private static JsonElement Row(JsonObject value) => JsonSerializer.SerializeToElement(value);

    [Fact]
    public void PromotionRequiresIndependentCurrentCompleteStrictEvidenceAndSupportedGeometry()
    {
        Assert.True(LiveLocalizationValidationSummaryCommand.CountsForPromotion(Row(Observation())));
        Action<JsonObject>[] decline =
        [
            r => r["LiveValidationMode"] = false,
            r => r["Backend"] = "saved image",
            r => r["CurrentPackGeneration"] = 2,
            r => r["FramePackGeneration"] = 2,
            r => r["CurrentGenerationFrame"] = false,
            r => r["CapturedAt"] = "2026-10-12T09:00:00Z",
            r => r["ManualPlacementInvolved"] = true,
            r => r["PredictionExistedBeforeVerification"] = false,
            r => r["OrderStatus"] = "Discriminating",
            r => r["PredictedOrderIdentities"]![0]![0] = 999,
            r => r["WgcMatches"]![0]!["CardIdentity"] = "wrong-card",
            r => r["CardCount"] = 12,
            r => r["CardCount"] = 15,
            r => r["ClientHeight"] = 1440,
            r => r["WgcFullyMatched"] = false,
            r => r["WgcCaptureRequestGeneration"] = 2,
            r => r["MaxScatter"] = 3.0001,
            r => r["PlacementAgreedWithWgc"] = false,
            r => r["CandidateVerifications"]![0]!["Slots"]![0]!["Confidence"] = .939999,
            r => r["CandidateVerifications"]![0]!["Slots"]![0]!["AmbiguityMargin"] = .069999,
            r => r["CandidateVerifications"]![0]!["Slots"]![0]!["ExpectedOccurrence"] = 1,
            r => r["ComparisonSlots"]![0]!["IdentityAgrees"] = false,
            r => r["WgcMatches"]!.AsArray().RemoveAt(0)
        ];
        foreach (var mutate in decline)
        {
            var record = Observation(); mutate(record);
            Assert.False(LiveLocalizationValidationSummaryCommand.CountsForPromotion(Row(record)), record.ToJsonString());
        }
    }

    [Fact]
    public void SummaryDeduplicatesSemanticPacksIsolatesBuildsAndBlocksMismatchOrUncleanFallback()
    {
        var rows = Enumerable.Range(1, 29).Select(i => Observation(i, i <= 15 ? "draft-a" : "draft-b",
            i <= 15 ? 1723 : 1280, i <= 15 ? 1009 : 720).ToJsonString()).ToList();
        rows.Add(Observation(1, "draft-a", 1280, 720).ToJsonString()); // Resize, not a new pack.
        var otherBuild = Observation(30); otherBuild["BuildId"] = "another-build"; rows.Add(otherBuild.ToJsonString());
        Assert.Contains("distinct packs: 29", LiveLocalizationValidationSummaryCommand.Summarize(rows, "test-build"));
        Assert.Contains("**NOT PASSED**", LiveLocalizationValidationSummaryCommand.Summarize(rows, "test-build"));
        rows.Add(Observation(30, "draft-b", 1280, 720).ToJsonString());
        var report = LiveLocalizationValidationSummaryCommand.Summarize(rows, "test-build");
        Assert.Contains("**PASS**", report); Assert.Contains("distinct packs: 30", report);
        Assert.Contains("identified real drafts: 2", report); Assert.Contains("qualifying client sizes: 2", report);
        var mismatch = Observation(31); mismatch["Shadow"] = "OrderMismatch"; mismatch["PlacementAgreedWithWgc"] = false;
        mismatch["FallbackInvoked"] = true; mismatch["FallbackReason"] = "order mismatch";
        rows.Add(mismatch.ToJsonString());
        report = LiveLocalizationValidationSummaryCommand.Summarize(rows, "test-build");
        Assert.Contains("**NOT PASSED**", report); Assert.Contains("order mismatches: 1", report);
        Assert.Contains("False deterministic proposals against complete WGC evidence: 1", report);
        rows.RemoveAt(rows.Count - 1);
        var fallback = Observation(31); fallback["OrderStatus"] = "Discriminating";
        fallback["FallbackInvoked"] = true; fallback["WgcFullMatcherInvoked"] = false; fallback["FallbackReason"] = "declined";
        rows.Add(fallback.ToJsonString());
        Assert.Contains("**NOT PASSED**", LiveLocalizationValidationSummaryCommand.Summarize(rows, "test-build"));
        Assert.Contains("unclean/missing fallback diagnostics: 1", LiveLocalizationValidationSummaryCommand.Summarize(rows, "test-build"));
        rows.RemoveAt(rows.Count - 1);
        rows.Add(JsonSerializer.Serialize(new { Schema = 3, Source = LiveLocalizationValidationSummaryCommand.Source,
            Kind = "LocalizationFailure", LiveValidationMode = true, BuildId = "test-build", SessionId = "earlier-startup" }));
        Assert.Contains("**NOT PASSED**", LiveLocalizationValidationSummaryCommand.Summarize(rows, "test-build"));
        Assert.Contains("**PASS**", LiveLocalizationValidationSummaryCommand.Summarize(rows, "test-build", "test-session"));
        rows.RemoveAt(rows.Count - 1); rows.Add("{torn ledger row");
        Assert.Contains("malformed rows: 1", LiveLocalizationValidationSummaryCommand.Summarize(rows, "test-build"));
        Assert.Contains("**NOT PASSED**", LiveLocalizationValidationSummaryCommand.Summarize(rows, "test-build"));
    }
}
