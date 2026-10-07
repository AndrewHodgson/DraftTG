using System.Text.Json;
using DraftTG.Application;
using DraftTG.Data;
using DraftTG.Domain;
using DraftTG.Tests.Shared;

namespace DraftTG.Application.Tests;

public sealed class ArenaDisplayOrderTests
{
    private static readonly Lazy<IReadOnlyDictionary<int, ArenaCardSortKeys>> LazyKeys = new(() =>
    {
        var directory = SyntheticArenaDatabase.TemporaryDirectory();
        try { return new ArenaCardDatabaseReader(SyntheticArenaDatabase.Create(directory.FullName)).ReadDraftUniverse(["WOE", "WOT"]).Keys; }
        finally { directory.Delete(true); }
    });
    private static IReadOnlyDictionary<int, ArenaCardSortKeys> Keys => LazyKeys.Value;

    private static (int[] Log, int[] Visual) P1P6()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "arena-woe-p1p6-visual-placement.json")));
        return (json.RootElement.GetProperty("LogPack").GetProperty("DraftPack").EnumerateArray().Select(e => int.Parse(e.GetString()!)).ToArray(),
            json.RootElement.GetProperty("VisualArenaIds").EnumerateArray().Select(e => e.GetInt32()).ToArray());
    }

    private static (int[] Log, int[] Visual) P1P7()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "arena-wgc-p1p7", "manifest.json")));
        var cards = json.RootElement.GetProperty("Cards").EnumerateArray().ToArray();
        var byId = cards.ToDictionary(c => c.GetProperty("Id").GetString()!, c => c.GetProperty("ArenaId").GetInt32());
        return (cards.Select(c => c.GetProperty("ArenaId").GetInt32()).ToArray(),
            json.RootElement.GetProperty("ExpectedVisualOrder").EnumerateArray().Select(e => byId[e.GetString()!]).ToArray());
    }

    private static ArenaDisplayOrderObservation Observation(string scope, int pack, int pick, IReadOnlyList<int> log, IReadOnlyList<int> visual,
        int width = 2560) => new()
    {
        ObservationId = ArenaDisplayOrderObservation.CreateId(scope, pack, pick, log), DraftScope = scope, EventName = "QuickDraft_WOE_20260929",
        Expansion = "WOE", Pack = pack, Pick = pick, CardCount = log.Count, LogOrder = log.ToArray(), VisualOrder = visual.ToArray(),
        ClientWidth = width, ClientHeight = 1440, ArenaDataVersion = "synthetic.1", RecordedAt = DateTimeOffset.UnixEpoch,
        SortKeys = log.Distinct().Order().Select(id => Keys[id]).ToArray()
    };

    private static ArenaDisplayOrderRule Rule(string name) => ArenaDisplayOrderModel.Hypotheses.Single(r => r.Name == name);

    [Fact]
    public void KnownPacksReproduceTheProbeFamilyWithoutSelectingARule()
    {
        var (log6, visual6) = P1P6(); var (log7, visual7) = P1P7();
        Assert.NotEqual(log6, visual6); Assert.NotEqual(log7, visual7);
        var observations = new[] { Observation("s", 1, 6, log6, visual6), Observation("s", 1, 7, log7, visual7) };
        var evaluation = ArenaDisplayOrderModel.Evaluate(observations);

        Assert.Equal(400, ArenaDisplayOrderModel.Hypotheses.Count);
        Assert.Equal(400, ArenaDisplayOrderModel.Hypotheses.Select(r => r.Name).Distinct().Count());
        Assert.Equal(120, evaluation.Survivors.Count); // tools/ArenaSortOrderProbe: 120 / 400.
        Assert.Equal((2, 17, 0), (evaluation.ObservationCount, evaluation.CardCount, evaluation.Contradictions));
        foreach (var survivor in new[] { "rarity>color>title", "rarity>color>collector", "rarity>collector" })
            Assert.Contains(evaluation.Survivors, r => r.Name == survivor);
        foreach (var refuted in new[] { "rarity>color>cmc>title", "rarity>title", "rarity>grpId", "rarity>cmc" })
            Assert.DoesNotContain(evaluation.Survivors, r => r.Name == refuted);
        Assert.True(evaluation.Classes.Count > 1); // Still ambiguous: nothing is promoted.
        var again = ArenaDisplayOrderModel.Evaluate(observations, Keys);
        Assert.Equal(evaluation.Survivors.Select(r => r.Name), again.Survivors.Select(r => r.Name));
        Assert.Equal(again.Classes.Select(c => c.Representative.Name), ArenaDisplayOrderModel.Evaluate(observations, Keys).Classes.Select(c => c.Representative.Name));
    }

    [Fact]
    public void FunctionalClassesGroupOnlyRulesThatOrderTheUniverseIdentically()
    {
        var (log6, visual6) = P1P6(); var (log7, visual7) = P1P7();
        var evaluation = ArenaDisplayOrderModel.Evaluate([Observation("s", 1, 6, log6, visual6), Observation("s", 1, 7, log7, visual7)], Keys);
        Assert.Equal(evaluation.Survivors.Count, evaluation.Classes.Sum(c => c.Rules.Count));
        Assert.True(evaluation.Classes.Count < evaluation.Survivors.Count);
        var ids = Keys.Keys.Order().ToArray();
        foreach (var group in evaluation.Classes)
        {
            var expected = ArenaDisplayOrderModel.Sort(group.Representative, ids, Keys)!;
            Assert.All(group.Rules, rule => Assert.Equal(expected, ArenaDisplayOrderModel.Sort(rule, ids, Keys)!));
            Assert.Equal(group.Rules.Min(r => r.Index), group.Representative.Index);
        }
        var representatives = evaluation.Classes.Select(c => ArenaDisplayOrderModel.Sort(c.Representative, ids, Keys)!).ToArray();
        Assert.Equal(representatives.Length, representatives.Select(order => string.Join(",", order)).Distinct().Count());
    }

    [Theory]
    [InlineData("all-rules", ArenaDisplayOrderPredictionStatus.Discriminating, ArenaDisplayOrderAgreement.Undetermined)]
    [InlineData("p1p6-survivors", ArenaDisplayOrderPredictionStatus.Unanimous, ArenaDisplayOrderAgreement.Agrees)]
    [InlineData("reversed", ArenaDisplayOrderPredictionStatus.Unanimous, ArenaDisplayOrderAgreement.Disagrees)]
    [InlineData("missing-key", ArenaDisplayOrderPredictionStatus.Unavailable, ArenaDisplayOrderAgreement.Unavailable)]
    [InlineData("refuted", ArenaDisplayOrderPredictionStatus.Unavailable, ArenaDisplayOrderAgreement.Unavailable)]
    public void PredictionStatusIsDeterministic(string scenario, ArenaDisplayOrderPredictionStatus status, ArenaDisplayOrderAgreement agreement)
    {
        var (log, visual) = P1P6();
        var p1p6Survivors = ArenaDisplayOrderModel.Evaluate([Observation("s", 1, 6, log, visual)]).Survivors;
        var (pack, survivors, actual) = scenario switch
        {
            "all-rules" => (log, ArenaDisplayOrderModel.Hypotheses, visual),
            "p1p6-survivors" => (log, p1p6Survivors, visual),
            "reversed" => (log, p1p6Survivors, visual.Reverse().ToArray()),
            "missing-key" => ([.. log, 123], p1p6Survivors, visual),
            _ => (log, Array.Empty<ArenaDisplayOrderRule>(), visual)
        };
        var prediction = ArenaDisplayOrderModel.Predict(pack, Keys, survivors);
        Assert.Equal(status, prediction.Status);
        Assert.Equal(agreement, prediction.CompareWith(actual));
        Assert.Equal(survivors.Count == 0 ? 0 : survivors.Count, prediction.SurvivingRules);
        Assert.Equal(prediction.Orders.Select(o => string.Join(",", o.Order)), ArenaDisplayOrderModel.Predict(pack, Keys, survivors).Orders.Select(o => string.Join(",", o.Order)));
        if (status != ArenaDisplayOrderPredictionStatus.Unavailable) Assert.Equal(survivors.Count, prediction.Orders.Sum(o => o.RuleCount));
    }

    [Theory]
    [InlineData("full", true, null)]
    [InlineData("partial", false, "partial automatic result")]
    [InlineData("ambiguous", false, "not safe")]
    [InlineData("manual", false, "manual placement")]
    [InlineData("stale", false, "stale")]
    [InlineData("log-mismatch", false, "Arena log pack")]
    public void RecordingGateAcceptsOnlyFullConfidentAutomaticResults(string scenario, bool accepted, string? reason)
    {
        var (log, visual) = P1P6();
        var pack = new DraftPack(new(PackNumber.Create(1), PickNumber.Create(6)), log.Select(id => CardIdentifier.Create("c" + id)));
        var occurrences = pack.AvailableCardIdentifiers.Select((id, i) => new CurrentPackCardPresentation(new(i, id))).ToArray();
        var request = new CardVisualLocalizationRequest(pack, occurrences, DraftCardLayout.Grid(5)) { PackGeneration = 7 };
        var rectangles = visual.Select((_, slot) => new NormalizedDraftRegion(slot % 5 * .2 + .01, slot / 5 * .5 + .01, .18, .48)).ToArray();
        var matches = visual.Select((id, slot) => new CardVisualMatch(occurrences[Array.IndexOf(log, id)].Key, slot, rectangles[slot], .98,
            scenario == "ambiguous" && slot == 3 ? LocalizationConfidence.Ambiguous : LocalizationConfidence.HighConfidence, "test")).Reverse().ToArray();
        var result = new CardVisualLocalizationResult(pack, scenario == "partial" ? matches[1..] : matches, rectangles, 1325, 1131, TimeSpan.Zero, "test")
            { PackGeneration = 7 };

        var gate = ArenaDisplayOrderEvidenceGate.Evaluate(request, result, scenario == "stale" ? 8 : 7, pack, log, 1,
            scenario == "log-mismatch" ? 7 : 6, scenario == "manual");

        Assert.Equal(accepted, gate.Accepted);
        if (accepted) Assert.Equal(visual, gate.VisualOrder);
        else { Assert.Contains(reason!, gate.Reason); Assert.Empty(gate.VisualOrder); }
        // A card straddling two rows has no trustworthy reading position.
        Assert.Null(ArenaDisplayOrderEvidenceGate.ReadingOrder([matches[0] with { Rectangle = new(0, 0, .2, .4) },
            matches[1] with { Rectangle = new(.3, .15, .2, .4) }]));
    }

    [Fact]
    public void LedgerReloadSkipsCorruptRowsSuppressesDuplicatesAndReplaysDeterministically()
    {
        var directory = SyntheticArenaDatabase.TemporaryDirectory();
        try
        {
            var file = new JsonLinesLedgerFile(Path.Combine(directory.FullName, "localization", "order-evidence.jsonl"));
            var ledger = new ArenaDisplayOrderEvidenceLedger(file);
            var (log6, visual6) = P1P6(); var (log7, visual7) = P1P7();
            var first = Observation("s", 1, 6, log6, visual6); var second = Observation("s", 1, 7, log7, visual7);
            Assert.True(ledger.TryAppend(first));
            Assert.False(ledger.TryAppend(first with { RecordedAt = DateTimeOffset.UtcNow })); // UI refresh of the same pack state.
            Assert.True(ledger.TryAppend(second));
            File.AppendAllText(file.Path, "not json\n" + JsonSerializer.Serialize(first with { SchemaVersion = 2 }) + "\n"
                + JsonSerializer.Serialize(first) + "\n" + JsonSerializer.Serialize(first with { VisualOrder = log6 }) + "\n{\"ObservationId\":");

            var reloaded = new ArenaDisplayOrderEvidenceLedger(file).Load();
            Assert.Equal([first.ObservationId, second.ObservationId], reloaded.Observations.Select(o => o.ObservationId));
            Assert.Equal(3, reloaded.SkippedLines.Count);
            Assert.Contains(reloaded.SkippedLines, line => line.Contains("schema version 2"));
            Assert.Equal((2, 1), (reloaded.DuplicateLines, reloaded.ConflictingDuplicates));
            var text = File.ReadAllLines(file.Path)[0];
            Assert.Contains("\"SchemaVersion\":1", text);
            Assert.DoesNotContain("Users", text); Assert.DoesNotContain(".png", text, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(ArenaDisplayOrderModel.Evaluate([first, second]).Survivors.Select(r => r.Name),
                ArenaDisplayOrderModel.Evaluate(reloaded.Observations).Survivors.Select(r => r.Name));
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public void ObservationIdentityIsTheUnorderedPackStateWithinADraftScope()
    {
        var (log, _) = P1P6();
        var id = ArenaDisplayOrderObservation.CreateId("scope", 1, 6, log);
        Assert.Equal(id, ArenaDisplayOrderObservation.CreateId("scope", 1, 6, log.Reverse()));
        Assert.NotEqual(id, ArenaDisplayOrderObservation.CreateId("scope", 1, 7, log));
        Assert.NotEqual(id, ArenaDisplayOrderObservation.CreateId("other", 1, 6, log));
        Assert.NotEqual(id, ArenaDisplayOrderObservation.CreateId("scope", 1, 6, [.. log, log[0]]));
        Assert.DoesNotContain("draft-id-123", ArenaDisplayOrderObservation.ScopeDigest("draft:draft-id-123"));
    }

    [Fact]
    public void RecordPredictsBeforeLearningAndFailsClosedWithoutTheDatabase()
    {
        var directory = SyntheticArenaDatabase.TemporaryDirectory();
        try
        {
            var database = SyntheticArenaDatabase.Create(directory.FullName);
            var file = new JsonLinesLedgerFile(Path.Combine(directory.FullName, "order-evidence.jsonl"));
            var service = new ArenaDisplayOrderEvidenceService(new(file), () => new ArenaCardDatabaseReader(database));
            service.Initialize();
            ArenaDisplayOrderCandidate Candidate((int[] Log, int[] Visual) pack, int pick) => new("runtime:x:QuickDraft_WOE_20260929:1",
                "QuickDraft_WOE_20260929", 1, pick, pack.Log, new(true, "full", pack.Visual, .97, .99), 2560, 1440, 1325, 1131, "test", DateTimeOffset.UnixEpoch);

            var p6 = service.Record(Candidate(P1P6(), 6));
            Assert.Equal((ArenaDisplayOrderRecordStatus.Recorded, ArenaDisplayOrderPredictionStatus.Discriminating, 400, 0),
                (p6.Status, p6.PredictionBefore.Status, p6.SurvivorsBefore, p6.ClassesBefore));
            Assert.Equal(ArenaDisplayOrderAgreement.Undetermined, p6.Agreement);
            var p7 = service.Record(Candidate(P1P7(), 7));
            Assert.Equal(p6.SurvivorsAfter, p7.SurvivorsBefore); // Predicted from P1P6 evidence only.
            Assert.NotEqual(ArenaDisplayOrderAgreement.Disagrees, p7.Agreement);
            Assert.Equal(120, p7.SurvivorsAfter);
            Assert.Equal("WOE", service.Observations[0].Expansion);
            Assert.Equal("synthetic.1", service.Observations[0].ArenaDataVersion);
            Assert.Equal(ArenaDisplayOrderRecordStatus.Duplicate, service.Record(Candidate(P1P7(), 7)).Status);

            var unavailable = new ArenaDisplayOrderEvidenceService(new(file), () => null);
            unavailable.Initialize();
            Assert.Equal(120, unavailable.Evaluation.Survivors.Count); // Replay needs only the ledger's own keys.
            var outcome = unavailable.Record(Candidate(P1P6(), 8));
            Assert.Equal((ArenaDisplayOrderRecordStatus.Unavailable, ArenaDisplayOrderAgreement.Unavailable), (outcome.Status, outcome.Agreement));
            Assert.Equal(2, file.ReadLines().Count);
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public void GateProgressCountsOnlyStrictCompleteDraftsAndNeverPassesWithSeveralClasses()
    {
        var rule = Rule("rarity>color>title");
        int[][] packs = [[900001, 900002, 86988], [87058, 86853, 900003], [86711, 86711, 86834]];
        IEnumerable<ArenaDisplayOrderObservation> Draft(string scope, int width) => packs.SelectMany((cards, p) =>
            Enumerable.Range(1, 3).Select(pick =>
            {
                var log = cards.Skip(pick - 1).ToArray();
                return Observation(scope, p + 1, pick, log, ArenaDisplayOrderModel.Sort(rule, log, Keys)!, width);
            })).ToArray();
        var complete = Draft("a", 2560).ToArray();
        var lateJoin = Draft("b", 1920).Where(o => !(o.Pack == 1 && o.Pick == 1)).ToArray();
        Assert.True(ArenaDisplayOrderGateProgress.IsCompleteDraft(complete));
        Assert.False(ArenaDisplayOrderGateProgress.IsCompleteDraft(lateJoin));
        Assert.False(ArenaDisplayOrderGateProgress.IsCompleteDraft(complete.Where(o => !(o.Pack == 3 && o.Pick == 2))));

        var observations = complete.Concat(lateJoin).ToArray();
        var progress = ArenaDisplayOrderGateProgress.From(observations, ArenaDisplayOrderModel.Evaluate(observations, Keys), Keys);
        Assert.Equal((1, 2, 17, 2, 0), (progress.CompleteDrafts, progress.DraftScopes, progress.Observations, progress.WindowSizes, progress.Contradictions));
        Assert.True(progress is { Rare: true, Mythic: true, Multicolor: true, Hybrid: true, BonusSheetSameRarity: true, DuplicatePack: true, NonbasicLand: true });
        Assert.False(progress.FullFirstPick);
        Assert.True(progress.RuleClasses > 1);
        Assert.False(progress.Satisfied);
    }

    [Fact]
    public void QuickDraftScopesSplitOnNewDraftOrEventButDraftIdsAreStable()
    {
        var tracker = new ArenaDraftEvidenceScopeTracker("runtime");
        var first = tracker.Observe(null, "QuickDraft_WOE", 1, 1);
        Assert.Equal(first, tracker.Observe(null, "QuickDraft_WOE", 1, 2));
        Assert.Equal(first, tracker.Observe(null, "QuickDraft_WOE", 2, 1));
        var second = tracker.Observe(null, "QuickDraft_WOE", 1, 1);
        Assert.NotEqual(first, second);
        Assert.NotEqual(second, tracker.Observe(null, "QuickDraft_FDN", 1, 2));
        Assert.Equal("draft:abc", tracker.Observe("abc", "PremierDraft_WOE", 2, 3));
        Assert.Equal("draft:abc", tracker.Observe("abc", "PremierDraft_WOE", 1, 1));
    }

    [Theory]
    [InlineData(ArenaDisplayOrderRecordStatus.Recorded, ArenaDisplayOrderAgreement.Agrees, "P1P6 predicted order: agrees · rule classes 20 → 18")]
    [InlineData(ArenaDisplayOrderRecordStatus.Recorded, ArenaDisplayOrderAgreement.Undetermined, "P1P6 predicted order: undetermined (2 candidate orders) · rule classes 20 → 18")]
    [InlineData(ArenaDisplayOrderRecordStatus.Recorded, ArenaDisplayOrderAgreement.Disagrees, "P1P6 predicted order: DISAGREES · rule classes 20 → 18")]
    [InlineData(ArenaDisplayOrderRecordStatus.Unavailable, ArenaDisplayOrderAgreement.Unavailable, "P1P6 predicted order: unavailable (Arena card database not found.)")]
    [InlineData(ArenaDisplayOrderRecordStatus.Duplicate, ArenaDisplayOrderAgreement.Agrees, "P1P6 order evidence already recorded")]
    public void RailDiagnosticNamesTheOutcomeOnly(ArenaDisplayOrderRecordStatus status, ArenaDisplayOrderAgreement agreement, string expected)
    {
        var prediction = new ArenaDisplayOrderPrediction(ArenaDisplayOrderPredictionStatus.Discriminating,
            [new([1, 2], 30), new([2, 1], 10)], 40);
        var outcome = new ArenaDisplayOrderRecordOutcome(status, 1, 6, 2, prediction, agreement, 40, 20, 30, 18, [1, 2],
            status == ArenaDisplayOrderRecordStatus.Unavailable ? "Arena card database not found." : null);
        Assert.Equal(expected, ArenaDisplayOrderEvidencePresentation.RailLine(outcome));
        Assert.Contains("actual: 1,2", ArenaDisplayOrderEvidencePresentation.DetailLine(outcome));
    }
}
