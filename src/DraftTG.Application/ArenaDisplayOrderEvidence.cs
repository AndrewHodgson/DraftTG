using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DraftTG.Data;
using DraftTG.Domain;

namespace DraftTG.Application;

/// <summary>
/// One self-contained, versioned ledger record: a draft pack's Arena GrpIds in log order and the visual order
/// confirmed independently by the automatic artwork matcher, plus the Arena sort keys in effect when recorded.
/// Contains no pixels, screenshots, account names, file paths or raw Arena draft identifiers.
/// </summary>
public sealed record ArenaDisplayOrderObservation
{
    public const int CurrentSchemaVersion = 1;
    public const string CurrentEvidenceSource = "DraftTG/9E.1 automatic-artwork-matcher";

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string EvidenceSource { get; init; } = CurrentEvidenceSource;
    public required string ObservationId { get; init; }
    /// <summary>SHA-256 digest of the draft identity (Arena draft ID, or runtime session plus event and epoch).</summary>
    public required string DraftScope { get; init; }
    public string? EventName { get; init; }
    public string? Expansion { get; init; }
    public int Pack { get; init; }
    public int Pick { get; init; }
    public int CardCount { get; init; }
    public required IReadOnlyList<int> LogOrder { get; init; }
    public required IReadOnlyList<int> VisualOrder { get; init; }
    public int ClientWidth { get; init; }
    public int ClientHeight { get; init; }
    public int CaptureWidth { get; init; }
    public int CaptureHeight { get; init; }
    public string? ArenaDataVersion { get; init; }
    public string? ArenaGrpVersion { get; init; }
    public string? ArenaDatabaseFile { get; init; }
    public DateTimeOffset RecordedAt { get; init; }
    public string? Method { get; init; }
    public double MinimumScore { get; init; }
    public double MeanScore { get; init; }
    public required IReadOnlyList<ArenaCardSortKeys> SortKeys { get; init; }

    public IReadOnlyDictionary<int, ArenaCardSortKeys> KeyMap()
    {
        var map = new Dictionary<int, ArenaCardSortKeys>();
        foreach (var key in SortKeys) map.TryAdd(key.GrpId, key);
        return map;
    }

    /// <summary>One visual pack state contributes once: draft scope, coordinate and the unordered card multiset.</summary>
    public static string CreateId(string draftScope, int pack, int pick, IEnumerable<int> cards) =>
        Digest($"{draftScope}|P{pack}P{pick}|{string.Join(",", cards.Order())}")[..32];

    public static string ScopeDigest(string rawScope) => Digest("draft-scope|" + rawScope)[..32];

    public string? Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion) return $"unsupported schema version {SchemaVersion}";
        if (string.IsNullOrWhiteSpace(ObservationId) || string.IsNullOrWhiteSpace(DraftScope)) return "missing identity";
        if (Pack < 1 || Pick < 1) return "invalid pack coordinate";
        if (LogOrder is null || VisualOrder is null || SortKeys is null || SortKeys.Any(k => k is null)) return "missing card lists";
        if (LogOrder.Count is < 1 or > 15 || CardCount != LogOrder.Count || VisualOrder.Count != LogOrder.Count) return "inconsistent card count";
        if (!LogOrder.Order().SequenceEqual(VisualOrder.Order())) return "visual order is not a permutation of the pack";
        if (ObservationId != CreateId(DraftScope, Pack, Pick, LogOrder)) return "observation identity does not match its contents";
        var keys = KeyMap();
        if (LogOrder.Any(id => !keys.ContainsKey(id))) return "missing sort keys";
        return null;
    }

    private static string Digest(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}

/// <summary>Outcome of the strict recording gate. Rejections are diagnostic only.</summary>
public sealed record ArenaDisplayOrderGateResult(bool Accepted, string Reason, IReadOnlyList<int> VisualOrder,
    double MinimumScore = 0, double MeanScore = 0)
{
    public static ArenaDisplayOrderGateResult Reject(string reason) => new(false, reason, []);
}

/// <summary>
/// Order evidence is written only from a fully trustworthy automatic result. Manual mapping, partial or
/// ambiguous results, stale generations and count mismatches never become training data.
/// </summary>
public static class ArenaDisplayOrderEvidenceGate
{
    public static ArenaDisplayOrderGateResult Evaluate(CardVisualLocalizationRequest request, CardVisualLocalizationResult result,
        long currentPackGeneration, DraftPack? currentPack, IReadOnlyList<int>? arenaLogOrder, int arenaPack, int arenaPick,
        bool manualPlacementInvolved)
    {
        if (manualPlacementInvolved) return ArenaDisplayOrderGateResult.Reject("manual placement involved");
        if (request.PackGeneration <= 0 || request.PackGeneration != currentPackGeneration || result.PackGeneration != currentPackGeneration
            || currentPack is null || !request.Pack.Equals(currentPack))
            return ArenaDisplayOrderGateResult.Reject("stale or offline pack generation");
        if (!result.IsSafeFor(request)) return ArenaDisplayOrderGateResult.Reject("result is not safe for the current request");
        var count = request.Pack.AvailableCardIdentifiers.Count;
        if (request.Occurrences.Count != count || result.Matches.Count != count || result.Rectangles.Count != count)
            return ArenaDisplayOrderGateResult.Reject($"partial automatic result ({result.Matches.Count} / {count})");
        if (result.Matches.Any(m => m.State is not (LocalizationConfidence.HighConfidence or LocalizationConfidence.Confirmed)))
            return ArenaDisplayOrderGateResult.Reject("ambiguous or unresolved occurrence");
        if (arenaLogOrder is null || arenaLogOrder.Count != count || arenaPack != request.Pack.Position.Pack.Value
            || arenaPick != request.Pack.Position.Pick.Value)
            return ArenaDisplayOrderGateResult.Reject("Arena log pack does not match the localized pack");
        if (ReadingOrder(result.Matches) is not { } ordered) return ArenaDisplayOrderGateResult.Reject("visual rows are ambiguous");
        if (ordered.Any(m => m.Key.PackIndex < 0 || m.Key.PackIndex >= count)) return ArenaDisplayOrderGateResult.Reject("occurrence outside the pack");
        return new(true, "full high-confidence automatic result", ordered.Select(m => arenaLogOrder[m.Key.PackIndex]).ToArray(),
            result.Matches.Min(m => m.Confidence), result.Matches.Average(m => m.Confidence));
    }

    /// <summary>Left to right, then top to bottom, from the matched rectangles themselves. Null when row membership is unclear.</summary>
    public static IReadOnlyList<CardVisualMatch>? ReadingOrder(IReadOnlyList<CardVisualMatch> matches)
    {
        if (matches.Count == 0) return [];
        var height = matches.Select(m => m.Rectangle.Height).Order().ElementAt(matches.Count / 2);
        var byCenter = matches.OrderBy(m => m.Rectangle.Y + m.Rectangle.Height / 2).ToArray();
        var rows = new List<List<CardVisualMatch>> { new() { byCenter[0] } };
        for (var i = 1; i < byCenter.Length; i++)
        {
            var gap = Center(byCenter[i]) - Center(byCenter[i - 1]);
            if (gap > .75 * height) rows.Add([]);
            else if (gap >= .25 * height) return null;
            rows[^1].Add(byCenter[i]);
        }
        if (rows.Any(row => Center(row[^1]) - Center(row[0]) >= .5 * height)) return null;
        return rows.SelectMany(row => row.OrderBy(m => m.Rectangle.X + m.Rectangle.Width / 2)).ToArray();
        static double Center(CardVisualMatch m) => m.Rectangle.Y + m.Rectangle.Height / 2;
    }
}

public sealed record ArenaDisplayOrderLedgerLoad(IReadOnlyList<ArenaDisplayOrderObservation> Observations,
    IReadOnlyList<string> SkippedLines, int DuplicateLines, int ConflictingDuplicates);

/// <summary>Versioned JSONL codec over the append-only Data file. Appends are serialized by one lock.</summary>
public sealed class ArenaDisplayOrderEvidenceLedger(JsonLinesLedgerFile file)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    private readonly Lock _gate = new();
    private readonly Dictionary<string, ArenaDisplayOrderObservation> _known = new(StringComparer.Ordinal);

    public string Path => file.Path;

    public ArenaDisplayOrderLedgerLoad Load()
    {
        lock (_gate)
        {
            _known.Clear();
            var observations = new List<ArenaDisplayOrderObservation>();
            var skipped = new List<string>();
            int duplicates = 0, conflicts = 0, number = 0;
            foreach (var line in file.ReadLines())
            {
                number++;
                if (string.IsNullOrWhiteSpace(line)) continue;
                ArenaDisplayOrderObservation? observation;
                try { observation = JsonSerializer.Deserialize<ArenaDisplayOrderObservation>(line, Json); }
                catch (JsonException ex) { skipped.Add($"line {number}: malformed JSON ({ex.Message.Split('.')[0]})"); continue; }
                if (observation is null) { skipped.Add($"line {number}: empty record"); continue; }
                if (observation.Validate() is { } error) { skipped.Add($"line {number}: {error}"); continue; }
                if (_known.TryGetValue(observation.ObservationId, out var first))
                {
                    duplicates++;
                    if (!first.VisualOrder.SequenceEqual(observation.VisualOrder)) conflicts++;
                    continue;
                }
                _known[observation.ObservationId] = observation;
                observations.Add(observation);
            }
            return new(observations.AsReadOnly(), skipped.AsReadOnly(), duplicates, conflicts);
        }
    }

    public bool Contains(string observationId) { lock (_gate) return _known.ContainsKey(observationId); }

    /// <summary>Appends a valid, previously unseen observation. Returns false for a duplicate identity.</summary>
    public bool TryAppend(ArenaDisplayOrderObservation observation)
    {
        if (observation.Validate() is { } error) throw new ArgumentException("Invalid order observation: " + error, nameof(observation));
        lock (_gate)
        {
            if (_known.ContainsKey(observation.ObservationId)) return false;
            file.AppendLine(JsonSerializer.Serialize(observation, Json));
            _known[observation.ObservationId] = observation;
            return true;
        }
    }
}

/// <summary>Gate 1 coverage. Computed strictly from the ledger; nothing here can promote a rule to production.</summary>
public sealed record ArenaDisplayOrderGateProgress(
    int Observations, int CardsObserved, int CompleteDrafts, int DraftScopes,
    bool FullFirstPick, bool Rare, bool Mythic, bool Multicolor, bool? Hybrid, bool BonusSheetSameRarity,
    bool DuplicatePack, bool NonbasicLand, int WindowSizes, int RawSurvivors, int RuleClasses, int Contradictions,
    bool FamilyRefuted)
{
    public const int RequiredCompleteDrafts = 2, RequiredObservations = 80, RequiredWindowSizes = 2, FullPackMinimum = 14;

    public bool Satisfied => CompleteDrafts >= RequiredCompleteDrafts && Observations >= RequiredObservations && FullFirstPick
        && Rare && Mythic && Multicolor && Hybrid != false && BonusSheetSameRarity && DuplicatePack && NonbasicLand
        && WindowSizes >= RequiredWindowSizes && !FamilyRefuted && RawSurvivors > 0 && RuleClasses == 1 && Contradictions == 0;

    public static ArenaDisplayOrderGateProgress From(IReadOnlyList<ArenaDisplayOrderObservation> observations,
        ArenaDisplayOrderEvaluation evaluation, IReadOnlyDictionary<int, ArenaCardSortKeys>? universe = null)
    {
        var cards = observations.SelectMany(o => o.LogOrder.Select(id => o.KeyMap().GetValueOrDefault(id))).OfType<ArenaCardSortKeys>().ToArray();
        // Hybrid is required only when the observed expansions' draft universe contains a hybrid card.
        bool? hybrid = cards.Any(c => c.IsHybrid == true) ? true
            : universe is not null && universe.Values.Any(c => c.IsHybrid == true) ? false : null;
        var scopes = observations.GroupBy(o => o.DraftScope, StringComparer.Ordinal).ToArray();
        return new(observations.Count, observations.Sum(o => o.CardCount), scopes.Count(IsCompleteDraft), scopes.Length,
            observations.Any(o => o.Pack == 1 && o.Pick == 1 && o.CardCount >= FullPackMinimum),
            cards.Any(c => c.MythicToCommon == 1), cards.Any(c => c.MythicToCommon == 0),
            cards.Any(c => c.IsMulticolor == true), hybrid, observations.Any(HasBonusSheetAtSameRarity),
            observations.Any(o => o.LogOrder.Distinct().Count() < o.LogOrder.Count), cards.Any(c => c.IsNonbasicLand == true),
            observations.Select(o => (o.ClientWidth, o.ClientHeight)).Distinct().Count(),
            evaluation.Survivors.Count, evaluation.Classes.Count, evaluation.Contradictions, evaluation.IsFamilyRefuted);
    }

    /// <summary>
    /// Every coordinate of all three packs must be observed, starting from P1P1, with card counts falling by one
    /// per pick. A draft joined late, or with any gap, is incomplete; its observations still count individually.
    /// </summary>
    public static bool IsCompleteDraft(IEnumerable<ArenaDisplayOrderObservation> draft)
    {
        var byCoordinate = draft.GroupBy(o => (o.Pack, o.Pick)).ToDictionary(g => g.Key, g => g.Select(o => o.CardCount).Distinct().ToArray());
        if (!byCoordinate.TryGetValue((1, 1), out var first) || first.Length != 1 || first[0] < 2) return false;
        var size = first[0];
        if (byCoordinate.Keys.Any(k => k.Pack is < 1 or > 3 || k.Pick > size)) return false;
        for (var pack = 1; pack <= 3; pack++)
        for (var pick = 1; pick <= size; pick++)
            if (!byCoordinate.TryGetValue((pack, pick), out var counts) || counts.Length != 1 || counts[0] != size - pick + 1) return false;
        return true;
    }

    private static bool HasBonusSheetAtSameRarity(ArenaDisplayOrderObservation observation)
    {
        var keys = observation.LogOrder.Select(id => observation.KeyMap()[id]).ToArray();
        var main = keys.GroupBy(k => k.ExpansionCode).OrderByDescending(g => g.Count()).First().Key;
        return keys.Any(k => k.ExpansionCode != main && keys.Any(m => m.ExpansionCode == main && m.MythicToCommon == k.MythicToCommon));
    }
}

/// <summary>
/// Separates drafts for complete-draft tracking. An Arena draft ID wins; Quick Draft has none, so the scope is the
/// DraftTG runtime plus event name plus an epoch that advances when a coordinate moves backwards or the event changes.
/// </summary>
public sealed class ArenaDraftEvidenceScopeTracker(string runtimeScope)
{
    private string? _identity;
    private (int Pack, int Pick)? _last;
    private int _epoch;

    public string Observe(string? draftIdentifier, string? eventName, int pack, int pick)
    {
        var identity = draftIdentifier is { Length: > 0 } ? "draft:" + draftIdentifier : $"runtime:{runtimeScope}:{eventName}";
        if (identity != _identity) { _identity = identity; _epoch++; }
        else if (_last is { } last && (pack < last.Pack || (pack == last.Pack && pick < last.Pick))) _epoch++;
        _last = (pack, pick);
        return draftIdentifier is { Length: > 0 } ? identity : $"{identity}:{_epoch}";
    }
}
