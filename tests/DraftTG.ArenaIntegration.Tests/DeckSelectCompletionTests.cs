using System.Text.Json;
using System.Text.Json.Nodes;
using DraftTG.ArenaIntegration;

namespace DraftTG.ArenaIntegration.Tests;

public sealed class DeckSelectCompletionTests
{
    private static string[] Lines() => File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "deckselect-woe-completion-live.jsonl"));
    private static ArenaDraftLogEvent.DraftCompleted Completion(string? line = null) => Assert.IsType<ArenaDraftLogEvent.DraftCompleted>(
        Assert.Single(new ArenaDraftLogParser().Parse(new ArenaLogSourceEvent.Line(line ?? Lines()[3]))));
    private static ArenaDraftStateEngine BeforeCompletion(bool includeFinalPick = true)
    {
        var engine = new ArenaDraftStateEngine(); var parser = new ArenaDraftLogParser();
        foreach (var line in Lines().Take(includeFinalPick ? 3 : 1))
            foreach (var fact in parser.Parse(new ArenaLogSourceEvent.Line(line))) engine.Apply(fact);
        return engine;
    }
    private static string ChangedPayload(Action<JsonObject> change)
    {
        var outer = JsonNode.Parse(Lines()[3])!.AsObject();
        var payload = JsonNode.Parse(outer["Payload"]!.GetValue<string>())!.AsObject();
        change(payload); outer["Payload"] = payload.ToJsonString(); return outer.ToJsonString();
    }

    [Fact]
    public void RealSequenceEmitsOneCanonicalCompletionAndPreservesRecoveredExactInventory()
    {
        var engine = BeforeCompletion(); var before = engine.Current;
        Assert.Equal(42, before.DraftedPool.Count); Assert.Equal(41, before.RecoveredPool!.Cards.Count);
        Assert.Equal(1, before.ExactHistoryCardCount);
        var fact = Completion();
        Assert.Equal(ArenaDraftCompletionOrigin.DeckSelection, fact.Completion.Origin);
        Assert.Equal(ArenaDraftMode.Quick, fact.Completion.Mode); Assert.Null(fact.Completion.DraftIdentifier);
        Assert.Equal(42, fact.Completion.FinalPickedCards!.Count);
        var completed = engine.Apply(fact); Assert.True(completed.Changed);
        Assert.True(completed.Snapshot.IsCompleted); Assert.Null(completed.Snapshot.CurrentPack);
        Assert.Same(before.RecoveredPool, completed.Snapshot.RecoveredPool);
        Assert.Equal(before.DraftedPool, completed.Snapshot.DraftedPool);
        Assert.Equal(before.CompletedPicks, completed.Snapshot.CompletedPicks);
        Assert.Null(completed.Snapshot.PickedCardsDiagnostic);
        Assert.False(engine.Apply(fact).Changed); Assert.Equal(42, engine.Current.DraftedPool.Count);
        Assert.Empty(new ArenaDraftLogParser().Parse(new ArenaLogSourceEvent.Line(Lines()[4])));
    }

    [Fact]
    public void TrustworthyFullPickedCardsCanRecoverMissingFinalPickOrBootstrapCompletedReplay()
    {
        foreach (var engine in new[] { BeforeCompletion(includeFinalPick: false), new ArenaDraftStateEngine() })
        {
            var completed = engine.Apply(Completion()).Snapshot;
            Assert.True(completed.IsCompleted); Assert.Equal(ArenaDraftMode.Quick, completed.Mode);
            Assert.Equal(42, completed.DraftedPool.Count); Assert.Equal(42, completed.RecoveredPool!.Cards.Count);
            Assert.Empty(completed.CompletedPicks); Assert.Null(completed.RecoveredPool.CurrentCoordinate);
            Assert.Null(completed.PickedCardsDiagnostic); Assert.False(engine.Apply(Completion()).Changed);
        }
    }

    [Fact]
    public void MismatchedMultiplicityOrSizeRetainsAuthoritativePoolAndReportsCountsWithoutMerge()
    {
        foreach (var change in new Action<JsonArray>[]
        {
            cards => cards[0] = cards[1]!.DeepClone(),
            cards => cards.Add("999999"),
            cards => cards.RemoveAt(0)
        })
        {
            var engine = BeforeCompletion(); var before = engine.Current;
            var bad = Completion(ChangedPayload(p => change(p["PickedCards"]!.AsArray())));
            var completed = engine.Apply(bad).Snapshot;
            Assert.True(completed.IsCompleted); Assert.Null(completed.CurrentPack);
            Assert.Equal(before.DraftedPool, completed.DraftedPool); Assert.Same(before.RecoveredPool, completed.RecoveredPool);
            Assert.Equal(ArenaPickedCardsDiagnosticKind.FinalPoolMismatch, completed.PickedCardsDiagnostic!.Kind);
            Assert.Contains("recovered/exact pool 42", completed.PickedCardsDiagnostic.Message);
            Assert.Contains($"completion pool {bad.Completion.FinalPickedCards!.Count}", completed.PickedCardsDiagnostic.Message);
            Assert.False(engine.Apply(bad).Changed);
            Assert.True(engine.Apply(Completion()).Changed); Assert.Null(engine.Current.PickedCardsDiagnostic);
            Assert.Equal(before.DraftedPool, engine.Current.DraftedPool);
        }
    }

    [Fact]
    public void ModuleOrDeckSelectionAloneIsNotCompletionAndNestedCoursesAreNotTheDraftPool()
    {
        var parser = new ArenaDraftLogParser();
        var records = new[]
        {
            """{"CurrentModule":"DeckSelect","ModulePayload":"{}","CardPool":[101,102],"CourseDeckSummary":{}}""",
            """{"CurrentModule":"DeckSelect","Payload":{"Deck":[101],"Sideboard":[102]}}""",
            "{\"Courses\":[" + Lines()[3] + "]}",
            ChangedPayload(p => p["DraftStatus"] = "PickNext"),
            ChangedPayload(p => p["Result"] = "Failure"),
            ChangedPayload(p => p["EventName"] = "Sealed_WOE")
        };
        foreach (var line in records) Assert.Empty(parser.Parse(new ArenaLogSourceEvent.Line(line)));
        var deckOnly = Completion(ChangedPayload(p =>
        {
            p.Remove("PickedCards"); p["Deck"] = new JsonArray(101, 102); p["Sideboard"] = new JsonArray(103);
            p["CardPool"] = new JsonArray(104, 105);
        }));
        var state = new ArenaDraftStateEngine().Apply(deckOnly).Snapshot;
        Assert.True(state.IsCompleted); Assert.Equal(0, state.DraftedPool.Count); Assert.Null(state.RecoveredPool);
        Assert.Null(Completion(ChangedPayload(p => p["NumCardsToPick"] = 2)).Completion.FinalPickedCards);
    }

    [Fact]
    public void MalformedNestedPayloadAndPickedCardsHaveFocusedErrors()
    {
        var parser = new ArenaDraftLogParser();
        var nested = Assert.Throws<ArenaDraftLogParseException>(() => parser.Parse(new ArenaLogSourceEvent.Line(
            """{"CurrentModule":"DeckSelect","Payload":"not-json"}""")));
        Assert.Equal(ArenaDraftLogParseErrorKind.MalformedNestedJson, nested.Kind);
        var cards = Assert.Throws<ArenaDraftLogParseException>(() => Completion(ChangedPayload(p => p["PickedCards"] = "not-an-array")));
        Assert.Equal(ArenaDraftLogParseErrorKind.MalformedCardList, cards.Kind);
    }

    [Fact]
    public void ExistingBotAndHumanCompletionStillWorkWithoutDeckSelect()
    {
        var outer = JsonNode.Parse(Lines()[3])!.AsObject(); outer["CurrentModule"] = "BotDraft";
        var parser = new ArenaDraftLogParser(); var engine = BeforeCompletion();
        foreach (var fact in parser.Parse(new ArenaLogSourceEvent.Line(outer.ToJsonString()))) engine.Apply(fact);
        Assert.True(engine.Current.IsCompleted); Assert.Equal(42, engine.Current.DraftedPool.Count);
        Assert.Null(engine.Current.CurrentPack);
        var human = parser.Parse(new ArenaLogSourceEvent.Line("""DraftCompleteDraft {"EventName":"PremierDraft_WOE","DraftId":"human"}"""));
        var humanEngine = new ArenaDraftStateEngine();
        foreach (var fact in human) humanEngine.Apply(fact);
        Assert.True(humanEngine.Current.IsCompleted);
        Assert.Equal(ArenaDraftCompletionOrigin.DraftProtocol, Assert.IsType<ArenaDraftLogEvent.DraftCompleted>(Assert.Single(human)).Completion.Origin);
    }

    [Fact]
    public void GenuinelyNewSessionClearsPoolAndRejectsLateOldDeckSelectionCompletion()
    {
        var engine = BeforeCompletion(); engine.Apply(Completion());
        var id = ArenaDraftIdentifier.Create("next-draft");
        engine.Apply(new ArenaDraftLogEvent.DraftStarted(new("QuickDraft_WOE_next", ArenaDraftMode.Quick, id)));
        engine.Apply(new ArenaDraftLogEvent.PackPresented(new(id, ArenaDraftCoordinate.Create(1, 1), new([ArenaCardIdentifier.Create(86978)]))));
        Assert.Equal(ArenaDraftSessionStatus.Active, engine.Current.Status); Assert.Equal(0, engine.Current.DraftedPool.Count);
        Assert.Empty(engine.Current.CompletedPicks); Assert.Null(engine.Current.RecoveredPool);
        Assert.False(engine.Apply(Completion()).Changed); Assert.Equal(ArenaDraftSessionStatus.Active, engine.Current.Status);
        Assert.NotNull(engine.Current.CurrentPack); Assert.Null(engine.Current.PickedCardsDiagnostic);

        // The observed entry-request shape establishes a new anonymous session even for the same event.
        var anonymous = BeforeCompletion(); anonymous.Apply(Completion());
        const string join = """[UnityCrossThreadLogger]==> EventJoin {"id":"entry-2","request":{"EventName":"QuickDraft_WOE_20260929","EntryCurrencyType":"Gold","EntryCurrencyPaid":10000}}""";
        var start = Assert.IsType<ArenaDraftLogEvent.DraftStarted>(Assert.Single(new ArenaDraftLogParser().Parse(new ArenaLogSourceEvent.Line(join))));
        Assert.Equal("entry-2", start.Start.EntryRequestIdentifier); Assert.Null(start.Start.DraftIdentifier);
        Assert.True(anonymous.Apply(start).Changed); Assert.Equal(ArenaDraftSessionStatus.Active, anonymous.Current.Status);
        Assert.Equal(0, anonymous.Current.DraftedPool.Count); Assert.Null(anonymous.Current.DraftIdentifier);
        Assert.Empty(anonymous.Current.CompletedPicks);
        anonymous.Apply(new ArenaDraftLogEvent.DraftCompleted(new("QuickDraft_WOE_20260929")));
        Assert.False(anonymous.Apply(start).Changed); Assert.True(anonymous.Current.IsCompleted);
        Assert.True(anonymous.Apply(start with { Start = start.Start with { EntryRequestIdentifier = "entry-3" } }).Changed);
        Assert.Equal(ArenaDraftSessionStatus.Active, anonymous.Current.Status);
    }
}
