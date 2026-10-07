using System.Globalization;
using System.Text;
using DraftTG.Data;
using DraftTG.Domain;

namespace DraftTG.Application;

/// <summary>A gate-accepted pack waiting for Arena sort keys. Raw scope text is hashed before persistence.</summary>
public sealed record ArenaDisplayOrderCandidate(string RawDraftScope, string? EventName, int Pack, int Pick,
    IReadOnlyList<int> LogOrder, ArenaDisplayOrderGateResult Gate, int ClientWidth, int ClientHeight,
    int CaptureWidth, int CaptureHeight, string? Method, DateTimeOffset RecordedAt);

public enum ArenaDisplayOrderRecordStatus { Recorded, Duplicate, Unavailable }

public sealed record ArenaDisplayOrderRecordOutcome(ArenaDisplayOrderRecordStatus Status, int Pack, int Pick, int CardCount,
    ArenaDisplayOrderPrediction PredictionBefore, ArenaDisplayOrderAgreement Agreement, int SurvivorsBefore, int ClassesBefore,
    int SurvivorsAfter, int ClassesAfter, IReadOnlyList<int> ActualOrder, string? Diagnostic = null);

/// <summary>
/// Synchronous evidence orchestration: Arena DB keys + ledger + pure order model. The caller serializes calls
/// and runs them off the UI thread. Nothing here is consumed by badge placement, matching or recommendations.
/// </summary>
public sealed class ArenaDisplayOrderEvidenceService(ArenaDisplayOrderEvidenceLedger ledger, Func<ArenaCardDatabaseReader?> database)
{
    private readonly List<ArenaDisplayOrderObservation> _observations = [];
    private IReadOnlyDictionary<int, ArenaCardSortKeys>? _universe;
    private string _universeCodes = "";

    public ArenaDisplayOrderLedgerLoad? LastLoad { get; private set; }
    public ArenaCardDatabaseInfo? Database { get; private set; }
    public string? DatabaseDiagnostic { get; private set; }
    public ArenaDisplayOrderEvaluation Evaluation { get; private set; } = ArenaDisplayOrderModel.Evaluate([]);
    public IReadOnlyList<ArenaDisplayOrderObservation> Observations => _observations.AsReadOnly();
    public ArenaDisplayOrderGateProgress Progress => ArenaDisplayOrderGateProgress.From(_observations, Evaluation, _universe);
    public string LedgerPath => ledger.Path;

    /// <summary>Replays the ledger. Corrupt rows are skipped with diagnostics; a missing DB only limits class grouping.</summary>
    public void Initialize()
    {
        LastLoad = ledger.Load();
        _observations.Clear();
        _observations.AddRange(LastLoad.Observations);
        var reader = database();
        var inspected = reader?.Inspect();
        Database = inspected?.Database;
        DatabaseDiagnostic = reader is null ? "Arena card database not found." : inspected!.Diagnostic;
        Reevaluate(reader);
    }

    public ArenaDisplayOrderRecordOutcome Record(ArenaDisplayOrderCandidate candidate)
    {
        var survivorsBefore = Evaluation.Survivors.Count; var classesBefore = Evaluation.Classes.Count;
        ArenaDisplayOrderRecordOutcome Unavailable(string reason) => new(ArenaDisplayOrderRecordStatus.Unavailable, candidate.Pack,
            candidate.Pick, candidate.LogOrder.Count, ArenaDisplayOrderPrediction.Unavailable(reason, survivorsBefore),
            ArenaDisplayOrderAgreement.Unavailable, survivorsBefore, classesBefore, survivorsBefore, classesBefore, candidate.Gate.VisualOrder, reason);
        if (!candidate.Gate.Accepted) return Unavailable("gate rejected: " + candidate.Gate.Reason);
        var reader = database();
        if (reader is null) { DatabaseDiagnostic = "Arena card database not found."; return Unavailable(DatabaseDiagnostic); }
        var lookup = reader.ReadSortKeys(candidate.LogOrder);
        Database = lookup.Database ?? Database;
        if (lookup.Status != ArenaCardDatabaseStatus.Available) { DatabaseDiagnostic = lookup.Diagnostic; return Unavailable(lookup.Diagnostic ?? "Arena card database unavailable."); }
        if (lookup.MissingGrpIds.Count > 0) return Unavailable(lookup.Diagnostic!);

        var scope = ArenaDisplayOrderObservation.ScopeDigest(candidate.RawDraftScope);
        var observation = new ArenaDisplayOrderObservation
        {
            ObservationId = ArenaDisplayOrderObservation.CreateId(scope, candidate.Pack, candidate.Pick, candidate.LogOrder),
            DraftScope = scope,
            EventName = candidate.EventName,
            Expansion = DraftExpansionResolver.Resolve(candidate.EventName, null, new CardCatalog())
                ?? lookup.Keys.Values.GroupBy(k => k.ExpansionCode).OrderByDescending(g => g.Count()).First().Key,
            Pack = candidate.Pack, Pick = candidate.Pick, CardCount = candidate.LogOrder.Count,
            LogOrder = candidate.LogOrder.ToArray(), VisualOrder = candidate.Gate.VisualOrder.ToArray(),
            ClientWidth = candidate.ClientWidth, ClientHeight = candidate.ClientHeight,
            CaptureWidth = candidate.CaptureWidth, CaptureHeight = candidate.CaptureHeight,
            ArenaDataVersion = lookup.Database?.DataVersion, ArenaGrpVersion = lookup.Database?.GrpVersion,
            ArenaDatabaseFile = lookup.Database?.FileName, RecordedAt = candidate.RecordedAt, Method = candidate.Method,
            MinimumScore = candidate.Gate.MinimumScore, MeanScore = candidate.Gate.MeanScore,
            SortKeys = candidate.LogOrder.Distinct().Order().Select(id => lookup.Keys[id]).ToArray()
        };
        // Predict from the evidence before this pack; never evaluate a pack after training on itself.
        var prediction = ArenaDisplayOrderModel.Predict(observation.LogOrder, observation.KeyMap(), Evaluation.Survivors);
        var agreement = prediction.CompareWith(observation.VisualOrder);
        if (!ledger.TryAppend(observation))
            return new(ArenaDisplayOrderRecordStatus.Duplicate, candidate.Pack, candidate.Pick, observation.CardCount, prediction, agreement,
                survivorsBefore, classesBefore, survivorsBefore, classesBefore, observation.VisualOrder, "Pack state already recorded.");
        _observations.Add(observation);
        Reevaluate(reader);
        return new(ArenaDisplayOrderRecordStatus.Recorded, candidate.Pack, candidate.Pick, observation.CardCount, prediction, agreement,
            survivorsBefore, classesBefore, Evaluation.Survivors.Count, Evaluation.Classes.Count, observation.VisualOrder);
    }

    private void Reevaluate(ArenaCardDatabaseReader? reader)
    {
        var codes = string.Join(",", _observations.SelectMany(o => o.SortKeys).Select(k => k.ExpansionCode)
            .OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        if (reader is not null && codes != _universeCodes)
        {
            var universe = reader.ReadDraftUniverse(codes.Split(',', StringSplitOptions.RemoveEmptyEntries));
            if (universe.Status == ArenaCardDatabaseStatus.Available) { _universe = universe.Keys; _universeCodes = codes; }
        }
        Evaluation = ArenaDisplayOrderModel.Evaluate(_observations, _universe);
    }
}

/// <summary>Concise diagnostic text for the rail and the developer summary command. Presentation only.</summary>
public static class ArenaDisplayOrderEvidencePresentation
{
    public static string RailLine(ArenaDisplayOrderRecordOutcome outcome)
    {
        var coordinate = $"P{outcome.Pack}P{outcome.Pick}";
        var classes = $" · rule classes {outcome.ClassesBefore} → {outcome.ClassesAfter}";
        return outcome.Status switch
        {
            ArenaDisplayOrderRecordStatus.Unavailable => $"{coordinate} predicted order: unavailable ({outcome.Diagnostic})",
            ArenaDisplayOrderRecordStatus.Duplicate => $"{coordinate} order evidence already recorded",
            _ => outcome.Agreement switch
            {
                ArenaDisplayOrderAgreement.Agrees => $"{coordinate} predicted order: agrees{classes}",
                ArenaDisplayOrderAgreement.Undetermined => $"{coordinate} predicted order: undetermined ({outcome.PredictionBefore.Orders.Count} candidate orders){classes}",
                ArenaDisplayOrderAgreement.Disagrees => $"{coordinate} predicted order: DISAGREES{classes}",
                _ => $"{coordinate} predicted order: unavailable ({outcome.PredictionBefore.Reason}){classes}"
            }
        };
    }

    public static string IdleLine(ArenaDisplayOrderEvaluation evaluation, string? databaseDiagnostic) =>
        evaluation.IsFamilyRefuted ? "Order evidence: hypothesis family REFUTED; DB order disabled"
        : evaluation.ObservationCount == 0 ? "Order evidence: no observations yet" + (databaseDiagnostic is null ? "" : " · Arena DB unavailable")
        : $"Order evidence: {evaluation.ObservationCount} observations · {evaluation.Classes.Count} rule classes"
          + (databaseDiagnostic is null ? "" : " · Arena DB unavailable");

    /// <summary>The live-validation record: coordinate, count, prediction before learning, actual order and classes after.</summary>
    public static string DetailLine(ArenaDisplayOrderRecordOutcome outcome) =>
        $"P{outcome.Pack}P{outcome.Pick} · {outcome.CardCount} cards · before: {outcome.PredictionBefore.Status}"
        + $" ({outcome.PredictionBefore.Orders.Count} orders, {outcome.ClassesBefore} classes, {outcome.SurvivorsBefore} rules)"
        + $" · actual: {string.Join(",", outcome.ActualOrder)} · {outcome.Status}/{outcome.Agreement}"
        + $" · after: {outcome.ClassesAfter} classes, {outcome.SurvivorsAfter} rules";

    public static string Summary(ArenaDisplayOrderEvidenceService service)
    {
        var evaluation = service.Evaluation; var progress = service.Progress; var load = service.LastLoad;
        static string YesNo(bool value) => value ? "yes" : "no";
        var text = new StringBuilder();
        text.AppendLine("Arena display-order evidence (Phase 9E.1, observational; not used for placement)");
        text.AppendLine($"Ledger: {service.LedgerPath}");
        text.AppendLine(service.Database is { } db
            ? $"Arena DB: {db.FileName} | Data {db.DataVersion ?? "unknown"} | GRP {db.GrpVersion ?? "unknown"} | modified {db.LastModifiedUtc:u}"
            : "Arena DB: unavailable");
        if (service.DatabaseDiagnostic is { } diagnostic) text.AppendLine($"Arena DB diagnostic: {diagnostic}");
        text.AppendLine();
        text.AppendLine($"Observations: {evaluation.ObservationCount}");
        text.AppendLine($"Cards observed: {evaluation.CardCount}");
        text.AppendLine($"Raw surviving hypotheses: {evaluation.Survivors.Count} / {evaluation.HypothesisCount}");
        text.AppendLine("Distinct rule classes: " + (evaluation.ClassUniverseSize == 0 ? "n/a (no observed cards)" : $"{evaluation.Classes.Count} (over {evaluation.ClassUniverseSize} cards)"));
        text.AppendLine($"Contradictions: {evaluation.Contradictions}");
        text.AppendLine($"Discriminating observations: {evaluation.DiscriminatingObservations}");
        if (load is not null && (load.SkippedLines.Count > 0 || load.DuplicateLines > 0 || evaluation.UnusableObservations > 0))
            text.AppendLine($"Skipped ledger lines: {load.SkippedLines.Count} | duplicate lines: {load.DuplicateLines}"
                + $" (conflicting {load.ConflictingDuplicates}) | unusable: {evaluation.UnusableObservations}");
        foreach (var line in load?.SkippedLines.Take(5) ?? []) text.AppendLine("  " + line);
        if (evaluation.IsFamilyRefuted)
            text.AppendLine("HYPOTHESIS FAMILY REFUTED: no rule reproduces every observation. Abandon DB order; keep the artwork matcher.");
        text.AppendLine();
        text.AppendLine("Gate progress:");
        text.AppendLine($"Complete drafts: {progress.CompleteDrafts} / {ArenaDisplayOrderGateProgress.RequiredCompleteDrafts} ({progress.DraftScopes} draft scopes)");
        text.AppendLine($"Observations: {progress.Observations} / {ArenaDisplayOrderGateProgress.RequiredObservations}");
        text.AppendLine($"Full P1P1: {YesNo(progress.FullFirstPick)}");
        text.AppendLine($"Rare observed: {YesNo(progress.Rare)}");
        text.AppendLine($"Mythic observed: {YesNo(progress.Mythic)}");
        text.AppendLine($"Multicolor: {YesNo(progress.Multicolor)}");
        text.AppendLine($"Hybrid: {(progress.Hybrid is { } hybrid ? YesNo(hybrid) : "n/a (none in observed formats)")}");
        text.AppendLine($"Bonus sheet at main-set rarity: {YesNo(progress.BonusSheetSameRarity)}");
        text.AppendLine($"Duplicate pack: {YesNo(progress.DuplicatePack)}");
        text.AppendLine($"Nonbasic land: {YesNo(progress.NonbasicLand)}");
        text.AppendLine($"Window sizes: {progress.WindowSizes} / {ArenaDisplayOrderGateProgress.RequiredWindowSizes}");
        text.AppendLine($"Single rule class, zero contradictions: {YesNo(progress.RuleClasses == 1 && progress.Contradictions == 0 && !progress.FamilyRefuted)}");
        text.AppendLine($"Gate 1: {(progress.Satisfied ? "SATISFIED (Phase 9E.2 may be proposed; nothing is promoted automatically)" : "not satisfied")}");
        var observations = service.Observations;
        if (observations.Count > 0)
        {
            text.AppendLine();
            var contradicting = evaluation.ContradictingObservations.ToHashSet(StringComparer.Ordinal);
            foreach (var version in observations.GroupBy(o => o.ArenaDataVersion ?? "unknown").OrderBy(g => g.Key, StringComparer.Ordinal))
                text.AppendLine($"Arena Data {version.Key}: {version.Count()} observations, {version.Count(o => contradicting.Contains(o.ObservationId))} contradictions");
            text.AppendLine("By expansion: " + string.Join(", ", observations.GroupBy(o => o.Expansion ?? "unknown")
                .OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Key} {g.Count()}")));
        }
        if (evaluation.Classes.Count > 0)
            text.AppendLine("Surviving classes: " + string.Join("; ", evaluation.Classes.Take(8)
                .Select(c => $"{c.Representative.Name} ({c.Rules.Count.ToString(CultureInfo.InvariantCulture)} rules)"))
                + (evaluation.Classes.Count > 8 ? $"; ... {evaluation.Classes.Count - 8} more" : ""));
        return text.ToString();
    }
}
