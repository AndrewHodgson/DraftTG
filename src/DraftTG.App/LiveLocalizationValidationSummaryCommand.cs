using System.Globalization;
using System.Text;
using System.Text.Json;
using DraftTG.Application;
using DraftTG.Data;

namespace DraftTG.App;

/// <summary>Read-only reduction of the existing shadow ledger. No captures or promotion side effects.</summary>
internal static class LiveLocalizationValidationSummaryCommand
{
    internal const string Source = "DraftTG/9E.2B live deterministic validation";
    private const string Usage = "Usage: DraftTG.App --live-localization-validation-summary [--ledger <jsonl>] [--build-id <MVID>] [--session-id <runtime session>] [--output <report.md>]";

    public static int Run(string[] args)
    {
        try
        {
            var ledger = LiveLocalizationValidation.LedgerPath;
            var buildId = LiveLocalizationValidation.BuildId;
            string? output = null, sessionId = null;
            for (var i = 1; i < args.Length; i += 2)
            {
                if (i + 1 >= args.Length) throw new ArgumentException(Usage);
                switch (args[i])
                {
                    case "--ledger": ledger = args[i + 1]; break;
                    case "--build-id": buildId = args[i + 1]; break;
                    case "--session-id": sessionId = args[i + 1]; break;
                    case "--output": output = args[i + 1]; break;
                    default: throw new ArgumentException(Usage);
                }
            }
            var report = Summarize(new JsonLinesLedgerFile(ledger).ReadLines(), buildId, sessionId);
            if (output is not null) File.WriteAllText(output, report);
            Console.WriteLine(report);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine($"{ex.GetType().Name}: {ex.Message}"); return 1; }
    }

    internal static JsonElement Get(JsonElement row, string name) => row.ValueKind == JsonValueKind.Object && row.TryGetProperty(name, out var value) ? value : default;
    internal static string Text(JsonElement row, string name) => Get(row, name) is { ValueKind: JsonValueKind.String } value ? value.GetString()! : "";
    internal static bool Flag(JsonElement row, string name) => Get(row, name).ValueKind == JsonValueKind.True;
    internal static double Number(JsonElement row, string name) => Get(row, name) is { ValueKind: JsonValueKind.Number } value && value.TryGetDouble(out var n) && double.IsFinite(n) ? n : double.NaN;
    private static JsonElement[] Array(JsonElement row, string name) => Get(row, name) is { ValueKind: JsonValueKind.Array } value ? value.EnumerateArray().ToArray() : [];
    private static bool Timestamp(JsonElement row, string name, out DateTimeOffset timestamp) => DateTimeOffset.TryParse(Text(row, name), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out timestamp);

    internal static bool IsLive(JsonElement row) => Number(row, "Schema") == 3 && Text(row, "Source") == Source
        && Flag(row, "LiveValidationMode") && Text(row, "Kind") == "PackObservation"
        && Text(row, "Backend") == "Windows.Graphics.Capture" && Flag(row, "LiveSynchronizedFrame")
        && Number(row, "CaptureGeneration") > 0 && Number(row, "CaptureRequestGeneration") > 0;

    // Eligible describes independent preconditions, before checking whether the pixels passed.
    internal static bool IsEligible(JsonElement row)
    {
        var generation = Number(row, "PackGeneration");
        var count = Number(row, "CardCount");
        var width = Number(row, "ClientWidth"); var height = Number(row, "ClientHeight");
        if (!IsLive(row) || !Flag(row, "CurrentGenerationFrame") || Flag(row, "ManualPlacementInvolved")
            || generation <= 0 || generation != Number(row, "CurrentPackGeneration") || generation != Number(row, "FramePackGeneration")
            || !Flag(row, "PredictionExistedBeforeVerification") || Text(row, "OrderStatus") != "Unanimous"
            || Text(row, "Deterministic") != "Available" || !Flag(row, "GeometryAvailable")
            || string.IsNullOrWhiteSpace(Text(row, "DraftScope")) || string.IsNullOrWhiteSpace(Text(row, "SessionId"))
            || !(Number(row, "Pack") >= 1) || !(Number(row, "Pick") >= 1)
            || !double.IsFinite(count) || !double.IsFinite(width) || !double.IsFinite(height)
            || count != Math.Truncate(count) || width != Math.Truncate(width) || height != Math.Truncate(height)
            || count > 15 || width > 10000 || height > 10000 || !(Number(row, "DpiScale") > 0)
            || Text(row, "WindowMode") != "Windowed") return false;
        if (!ArenaDraftSlotGeometry.TryCreateLayout((int)width, (int)height, (int)count, ArenaDraftSlotContext.WindowedDraftPack).IsAvailable) return false;
        return Timestamp(row, "CapturedAt", out var captured) && Timestamp(row, "PackChangedAt", out var changed)
            && Timestamp(row, "PredictedAt", out var predicted) && Timestamp(row, "VerificationStartedAt", out var started)
            && predicted <= started && captured >= changed;
    }

    internal static bool StrictlyVerified(JsonElement row)
    {
        var count = Number(row, "CardCount");
        if (!double.IsFinite(count) || count is < 1 or > 15 || count != Math.Truncate(count)) return false;
        var candidates = Array(row, "CandidateVerifications");
        if (candidates.Length != 1 || !Flag(candidates[0], "FullyVerified") || Text(candidates[0], "Error").Length > 0) return false;
        var slots = Array(candidates[0], "Slots");
        var indices = Enumerable.Range(0, (int)count).Select(i => (double)i).ToArray();
        return slots.Length == count
            && slots.Select(s => Number(s, "Slot")).Order().SequenceEqual(indices)
            && slots.Select(s => Number(s, "ExpectedOccurrence")).Order().SequenceEqual(indices)
            && slots.All(s => Flag(s, "Accepted") && Number(s, "Confidence") is >= .94 and <= 1
                && Number(s, "AmbiguityMargin") >= .07);
    }

    internal static bool CountsForPromotion(JsonElement row)
    {
        if (!IsEligible(row) || !StrictlyVerified(row) || !Flag(row, "FullPackVerified") || !Flag(row, "WgcFullyMatched")
            || !Flag(row, "PlacementAgreedWithWgc") || !Flag(row, "ReadingOrderAgrees")
            || Number(row, "WgcPackGeneration") != Number(row, "PackGeneration")
            || Number(row, "WgcCaptureRequestGeneration") != Number(row, "CaptureRequestGeneration")
            || Text(row, "Shadow") is not ("ExactAgreement" or "GeometryAgreement")) return false;
        var count = (int)Number(row, "CardCount");
        var deterministic = Array(row, "DeterministicSlots"); var wgc = Array(row, "WgcMatches");
        var comparison = Array(row, "ComparisonSlots");
        var expected = Enumerable.Range(0, count).Select(i => (double)i).ToArray();
        if (deterministic.Length != count || wgc.Length != count || comparison.Length != count
            || !deterministic.Select(s => Number(s, "Slot")).Order().SequenceEqual(expected)
            || !deterministic.Select(s => Number(s, "PackIndex")).Order().SequenceEqual(expected)
            || !wgc.Select(s => Number(s, "PackIndex")).Order().SequenceEqual(expected)
            || !comparison.Select(s => Number(s, "PredictedSlot")).Order().SequenceEqual(expected)
            || !comparison.All(s => Flag(s, "IdentityAgrees"))
            || !wgc.All(s => Number(s, "Confidence") is >= .94 and <= 1)) return false;
        var winningCandidate = Array(row, "CandidateVerifications")[0];
        var slots = Array(winningCandidate, "Slots");
        var predictions = Array(row, "PredictedOrderIdentities");
        var winnerOrder = Array(winningCandidate, "Order");
        if (predictions.Length != 1 || predictions[0].ValueKind != JsonValueKind.Array || winnerOrder.Length != count
            || !predictions[0].EnumerateArray().Select(n => n.ToString()).SequenceEqual(winnerOrder.Select(n => n.ToString()))
            || !deterministic.OrderBy(d => Number(d, "Slot")).Select(d => Number(d, "ArenaGrpId").ToString(CultureInfo.InvariantCulture))
                .SequenceEqual(winnerOrder.Select(n => n.ToString()))
            || deterministic.Any(d => !wgc.Any(w => Number(w, "PackIndex") == Number(d, "PackIndex")
                && Text(w, "CardIdentity") == Text(d, "CardIdentity") && Number(w, "ArenaId") == Number(d, "ArenaGrpId")))) return false;
        if (deterministic.Any(d => !slots.Any(s => Number(s, "Slot") == Number(d, "Slot")
            && Number(s, "ExpectedOccurrence") == Number(d, "PackIndex") && Number(s, "ArenaId") == Number(d, "ArenaGrpId")
            && Text(s, "CardIdentity") == Text(d, "CardIdentity")))) return false;
        // Preserve the 9E.2A.1 diagnostic holder/artwork tolerance, including the common offset bound.
        return Number(row, "MaxScatter") <= 3 && Number(row, "RmsScatter") <= 3
            && Math.Sqrt(Math.Pow(Number(row, "OffsetX"), 2) + Math.Pow(Number(row, "OffsetY"), 2))
                <= 6 * Number(row, "ClientHeight") / ArenaDraftSlotGeometry.ReferenceClientHeight;
    }

    internal static string Summarize(IEnumerable<string> lines, string buildId, string? sessionId = null)
    {
        var rows = new List<JsonElement>(); var malformed = 0; var excluded = 0;
        foreach (var line in lines)
        {
            try
            {
                using var json = JsonDocument.Parse(line);
                var row = json.RootElement;
                if (Number(row, "Schema") == 3 && Text(row, "Source") == Source && Flag(row, "LiveValidationMode")
                    && Text(row, "BuildId") == buildId && (sessionId is null || Text(row, "SessionId") == sessionId)) rows.Add(row.Clone());
                else excluded++;
            }
            catch (JsonException) { malformed++; }
        }
        var acceptedPacks = rows.Where(r => Text(r, "Kind") == "PackAccepted").ToArray();
        var observations = rows.Where(r => Text(r, "Kind") == "PackObservation").ToArray();
        var live = observations.Where(IsLive).ToArray();
        var eligible = live.Where(IsEligible).ToArray();
        var supported = live.Where(r => Flag(r, "GeometryAvailable")).ToArray();
        // One semantic pack per draft, even across retries, moves, resizes or app restarts.
        // Reobserving a pack at another size adds size coverage but never another pack observation.
        string PackKey(JsonElement r) => $"{Text(r, "DraftScope")}/{Number(r, "Pack")}/{Number(r, "Pick")}";
        var verified = live.Where(CountsForPromotion).ToArray();
        var promotionCount = verified.Select(PackKey).Distinct().Count();
        var drafts = verified.Select(r => Text(r, "DraftIdentifierDigest")).Where(s => s.Length > 0).Distinct().Count();
        var sizes = verified.Select(r => $"{Number(r, "ClientWidth")}x{Number(r, "ClientHeight")}").Distinct().Count();
        var comparable = live.Where(r => Flag(r, "CurrentGenerationFrame") && !Flag(r, "ManualPlacementInvolved")
            && Flag(r, "WgcFullyMatched") && Text(r, "Deterministic") == "Available").ToArray();
        var orderMismatches = comparable.Count(r => Text(r, "Shadow") == "OrderMismatch");
        var geometryMismatches = comparable.Count(r => Text(r, "Shadow") == "GeometryMismatch"
            || (Flag(r, "PlacementAgreedWithWgc") && (Number(r, "MaxScatter") > 3 || Number(r, "RmsScatter") > 3
                || Math.Sqrt(Math.Pow(Number(r, "OffsetX"), 2) + Math.Pow(Number(r, "OffsetY"), 2)) > 6 * Number(r, "ClientHeight") / 1009)));
        var falseProposals = comparable.Count(r => StrictlyVerified(r) && (Text(r, "Shadow") is "OrderMismatch" or "GeometryMismatch"
            || !Flag(r, "PlacementAgreedWithWgc") || Number(r, "MaxScatter") > 3 || Number(r, "RmsScatter") > 3
            || Math.Sqrt(Math.Pow(Number(r, "OffsetX"), 2) + Math.Pow(Number(r, "OffsetY"), 2)) > 6 * Number(r, "ClientHeight") / 1009));
        var fallbacks = live.Where(r => Flag(r, "FallbackInvoked")).ToArray();
        var unclean = fallbacks.Count(r => !Flag(r, "WgcFullMatcherInvoked") || string.IsNullOrWhiteSpace(Text(r, "FallbackReason")));
        var failures = rows.Count(r => Text(r, "Kind") is "LocalizationFailure" or "ShadowFailure");
        var pass = promotionCount >= 30 && drafts >= 2 && sizes >= 2 && orderMismatches == 0
            && geometryMismatches == 0 && falseProposals == 0 && unclean == 0 && failures == 0 && malformed == 0;
        var report = new StringBuilder("# Phase 9E.2B — Live deterministic localization validation\n\n");
        report.AppendLine($"Promotion gate: **{(pass ? "PASS" : "NOT PASSED")}**. Production authority: **WGC full matcher**.");
        report.AppendLine($"\nBuild ID tested (assembly MVID): `{buildId}`. Build/commit metadata: {string.Join(", ", rows.Select(r => Text(r, "Build")).DefaultIfEmpty(buildId == LiveLocalizationValidation.BuildId ? LiveLocalizationValidation.Build : "unavailable").Distinct())}.");
        report.AppendLine("Only this build's opt-in synchronized WGC records are included. Offline, legacy, synthetic/unspecified captures and other builds do not count.");
        report.AppendLine($"\nTotal live observation attempts: {live.Length}; eligible attempts: {eligible.Length}; fully verified promotion-counting distinct packs: {promotionCount}.");
        report.AppendLine($"Accepted pack generation events: {acceptedPacks.Length}; accepted-event card counts (including packs with no recognized frame): "
            + (acceptedPacks.Length == 0 ? "none observed" : string.Join("; ", acceptedPacks.GroupBy(r => Number(Get(r, "Details"), "CardCount")).Select(g => $"{g.Key}: {g.Count()}"))));
        report.AppendLine($"Supported-geometry live attempts: {supported.Length}; supported attempts with any fully verified fast candidate: {supported.Count(r => Number(r, "VerifiedCandidateOrderCount") > 0)}; supported non-unanimous predictions: {supported.Count(r => Text(r, "OrderStatus") != "Unanimous")}.");
        report.AppendLine($"Fully verified attempts: {verified.Length}; qualifying identified real drafts: {drafts}; qualifying client sizes: {sizes}.");
        report.AppendLine($"Comparator/order mismatches: {orderMismatches}; geometry mismatches: {geometryMismatches}; verifier failures on eligible attempts: {eligible.Count(r => !StrictlyVerified(r))}.");
        report.AppendLine($"False deterministic proposals against complete WGC evidence: {falseProposals}; actual production deterministic placements: 0 (shadow only).");
        report.AppendLine($"Fallback events: {fallbacks.Length}; unclean/missing fallback diagnostics: {unclean}; localization/shadow failures requiring investigation: {failures}.");
        report.AppendLine($"Unsupported observations: {live.Count(r => !Flag(r, "GeometryAvailable"))}; rejected non-WGC/unsynchronized observation rows: {observations.Length - live.Length}; excluded ledger rows: {excluded}; malformed rows: {malformed}.");
        void Coverage(string label, Func<JsonElement, string> key) => report.AppendLine($"\n{label}: " + (live.Length == 0 ? "none observed" : string.Join("; ", live.GroupBy(key).OrderBy(g => g.Key).Select(g => $"{g.Key}: {g.Count()} attempts, {g.Where(CountsForPromotion).Select(PackKey).Distinct().Count()} verified packs"))));
        Coverage("Draft scopes", r => Text(r, "DraftScope"));
        Coverage("Runtime sessions", r => Text(r, "SessionId"));
        Coverage("Client sizes / DPI", r => $"{Number(r, "ClientWidth")}x{Number(r, "ClientHeight")} / {Number(r, "Dpi")} DPI");
        Coverage("Card-count coverage", r => Number(r, "CardCount").ToString(CultureInfo.InvariantCulture));
        report.AppendLine("\nFallback reasons: " + (fallbacks.Length == 0 ? "none observed" : string.Join("; ", fallbacks.GroupBy(r => Text(r, "FallbackReason")).Select(g => $"{g.Key}: {g.Count()}"))));
        var stale = live.Count(r => Text(r, "Shadow") == "Stale");
        report.AppendLine($"\nStale-frame admission rejections: {stale}; asynchronous stale-result rejection events: {rows.Count(r => Text(r, "Kind") == "StaleResultRejected")}; canceled localization attempts: {rows.Count(r => Text(r, "Kind") == "LocalizationCanceled")} (cancellation alone is not proof of stale rejection).");
        var moves = rows.Where(r => Text(r, "Kind") == "WindowTranslation").ToArray();
        var invalidations = rows.Where(r => Text(r, "Kind") == "WindowInvalidation").ToArray();
        report.AppendLine($"Move without resize: {moves.Length} events; completed placement retained without full relocalization: {moves.Count(r => Flag(Get(r, "Details"), "PlacementRetained") && !Flag(Get(r, "Details"), "FullRelocalizationRequested"))}.");
        report.AppendLine($"Resize/identity/visibility/DPI invalidation: {invalidations.Length} events; size changes: {invalidations.Count(r => Changed(r, "Width") || Changed(r, "Height"))}; DPI changes: {invalidations.Count(r => Changed(r, "Scaling"))}.");
        var retired = rows.Where(r => Text(r, "Kind") == "AcceptedPickBadgeRetirement").ToArray();
        report.AppendLine($"Accepted-pick badge retirement: {retired.Length} events; cleared context and zero placed badges: {retired.Count(r => Flag(Get(r, "Details"), "PlacementContextCleared") && Number(Get(r, "Details"), "PlacedBadges") == 0)}. Semantic-event/UI timestamps are recorded; Player.log emission latency is not measured.");
        foreach (var field in new[] { "CaptureMilliseconds", "FullMatcherMilliseconds", "VerificationMilliseconds", "AdmissionAgeMilliseconds" })
        {
            var values = live.Select(r => Number(r, field)).Where(double.IsFinite).Order().ToArray();
            report.AppendLine($"{field}: " + (values.Length == 0 ? "not measured" : FormattableString.Invariant($"n={values.Length}, median={values[values.Length / 2]:F1} ms, max={values[^1]:F1} ms")));
        }
        report.AppendLine("\nUnsupported/unvalidated: <=12 and 15 cards; non-windowed clients; height outside 720–1080; aspect outside 1.70–1.78; unusual UI scaling. No deterministic geometry is extrapolated. DPI/display transitions require live verification.");
        report.AppendLine(pass
            ? "\nRecommendation: the numeric gate passes for this build: >=30 independently predicted, strictly verified distinct packs, >=2 identified drafts, >=2 sizes and zero recorded mismatches/false proposals or unresolved failures. Review physical transition evidence, then request explicit approval before any authority change. No promotion was performed."
            : "\nRecommendation: retain WGC authority. Gather >=30 qualifying distinct packs across >=2 identified real drafts and >=2 client sizes; investigate every mismatch/failure and missing fallback diagnostic. Zero observations means no live validation, not proof of zero faults. Physical move, resize, pick retirement and stale-result behavior must be reviewed before proposing promotion.");
        return report.ToString();
    }

    private static bool Changed(JsonElement row, string field)
    {
        var details = Get(row, "Details");
        return Number(Get(details, "Previous"), field) != Number(Get(details, "Current"), field);
    }
}
