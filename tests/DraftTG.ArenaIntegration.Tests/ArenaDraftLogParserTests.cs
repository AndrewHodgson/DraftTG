using System.Text.Json;
using DraftTG.ArenaIntegration;

namespace DraftTG.ArenaIntegration.Tests;

public sealed class ArenaDraftLogParserTests
{
    private const string LiveQuickDraftStatus =
        """{"CurrentModule":"BotDraft","Payload":"{\"Result\":\"Success\",\"EventName\":\"QuickDraft_HOB_20260915\",\"DraftStatus\":\"PickNext\",\"PackNumber\":0,\"PickNumber\":2,\"NumCardsToPick\":1,\"DraftPack\":[\"103441\",\"103501\",\"103421\",\"103401\",\"103411\",\"103513\",\"103480\",\"103457\",\"103535\",\"103464\",\"103554\",\"103581\"],\"PackStyles\":[],\"PickedCards\":[\"103499\",\"103521\"],\"PickedStyles\":[]}"}""";

    private readonly ArenaDraftLogParser _parser = new();

    [Fact]
    public void SourceResetBecomesOneResetEvent() =>
        Assert.IsType<ArenaDraftLogEvent.SourceReset>(Assert.Single(_parser.Parse(new ArenaLogSourceEvent.SourceReset())));

    [Fact]
    public void IrrelevantUnityNoiseReturnsNoEvents() =>
        Assert.Empty(ParseLine("[Unity] Graphics device initialized"));

    [Fact]
    public void DraftNotifyProducesOrderedOneBasedHumanPack()
    {
        var pack = Pack(ParseLine("""[Arena] Draft.Notify {"draftId":"draft-human","SelfPack":3,"SelfPick":11,"PackCards":"104894,105090,104987,105113"}"""));
        Assert.Equal("draft-human", pack.DraftIdentifier?.Value);
        Assert.Equal(ArenaDraftCoordinate.Create(3, 11), pack.Coordinate);
        Assert.Equal([104_894, 105_090, 104_987, 105_113], Values(pack.CardIdentifiers));
    }

    [Fact]
    public void NestedCardsInPackProvidesP1P1Fallback()
    {
        var payload = JsonSerializer.Serialize(new
        {
            DraftId = "draft-p1p1",
            CardsInPack = new object[] { 101, "102", 103 },
            PackNumber = 1,
            PickNumber = 1
        });
        var request = JsonSerializer.Serialize(new { Payload = payload });
        var line = "[BusinessEvent] " + JsonSerializer.Serialize(new { method = "LogBusinessEvent", request });
        var pack = Pack(ParseLine(line));
        Assert.Equal("draft-p1p1", pack.DraftIdentifier?.Value);
        Assert.Equal(ArenaDraftCoordinate.Create(1, 1), pack.Coordinate);
        Assert.Equal([101, 102, 103], Values(pack.CardIdentifiers));
    }

    [Fact]
    public void StringEncodedHumanPickUsesTrueDraftIdAndPreservesMultipleIds()
    {
        var line = """[Arena] {"method":"EventPlayerDraftMakePick","id":"correlation-only","request":"{\"DraftId\":\"draft-human\",\"GrpIds\":[101,\"102\"],\"Pack\":3,\"Pick\":11}"}""";
        var pick = Pick(ParseLine(line));
        Assert.Equal("draft-human", pick.DraftIdentifier?.Value);
        Assert.Equal(ArenaDraftCoordinate.Create(3, 11), pick.Coordinate);
        Assert.Equal([101, 102], Values(pick.CardIdentifiers));
    }

    [Fact]
    public void UnderscoreHumanPickAcceptsDirectObject()
    {
        var pick = Pick(ParseLine("""{"method":"Event_PlayerDraftMakePick","request":{"DraftId":"draft-direct","GrpIds":[201],"Pack":2,"Pick":7}}"""));
        Assert.Equal("draft-direct", pick.DraftIdentifier?.Value);
        Assert.Equal(ArenaDraftCoordinate.Create(2, 7), pick.Coordinate);
        Assert.Equal([201], Values(pick.CardIdentifiers));
    }

    [Fact]
    public void SingularHumanGrpIdIsSupported() =>
        Assert.Equal(
            [301],
            Values(Pick(ParseLine("""{"method":"EventPlayerDraftMakePick","request":{"DraftId":"draft-single","GrpId":"301","Pack":1,"Pick":2}}""")).CardIdentifiers));

    [Fact]
    public void QuickStatusNormalizesP1P1AndMixedIdEncodings()
    {
        var events = ParseLine("""{"method":"BotDraftDraftStatus","response":{"EventName":"QuickDraft_TST","DraftId":"quick-draft","DraftPack":[401,"402",403],"PackNumber":0,"PickNumber":0,"DraftStatus":"Drafting"}}""");
        var pack = Pack(events);
        Assert.Equal(2, events.Count);
        Assert.Equal(ArenaDraftCoordinate.Create(1, 1), pack.Coordinate);
        Assert.Equal([401, 402, 403], Values(pack.CardIdentifiers));
    }

    [Fact]
    public void IncomingBotDraftStatusMarkerIsIgnoredWithoutParseError()
    {
        const string marker =
            "<== BotDraftDraftStatus(25e66e3f-1686-471b-b06a-2e4983a28adf)";

        Assert.Empty(ParseLine(marker));
    }

    [Fact]
    public void LiveOutgoingBotDraftStatusRequestClassifiesQuickDraft()
    {
        const string line =
            """[UnityCrossThreadLogger]==> BotDraftDraftStatus {"id":"correlation","request":"{\"EventName\":\"QuickDraft_HOB_20260915\"}"}""";

        var start = Start(ParseLine(line));

        Assert.Equal(ArenaDraftModeKind.Quick, start.Mode.Kind);
        Assert.Equal("QuickDraft_HOB_20260915", start.EventName);
    }

    [Fact]
    public void StandaloneLiveQuickStatusRecoversHistoryThenOrderedCurrentPack()
    {
        var events = ParseLine(LiveQuickDraftStatus);

        Assert.Equal(4, events.Count);
        Assert.Equal(ArenaDraftModeKind.Quick,
            Assert.IsType<ArenaDraftLogEvent.DraftStarted>(events[0]).Start.Mode.Kind);
        var firstPick = Assert.IsType<ArenaDraftLogEvent.PickSubmitted>(events[1]).Pick;
        var secondPick = Assert.IsType<ArenaDraftLogEvent.PickSubmitted>(events[2]).Pick;
        var pack = Assert.IsType<ArenaDraftLogEvent.PackPresented>(events[3]).Pack;
        Assert.Equal(ArenaDraftCoordinate.Create(1, 1), firstPick.Coordinate);
        Assert.Equal([103499], Values(firstPick.CardIdentifiers));
        Assert.Equal(ArenaDraftCoordinate.Create(1, 2), secondPick.Coordinate);
        Assert.Equal([103521], Values(secondPick.CardIdentifiers));
        Assert.Equal(ArenaDraftCoordinate.Create(1, 3), pack.Coordinate);
        Assert.Equal(
            [103441, 103501, 103421, 103401, 103411, 103513,
             103480, 103457, 103535, 103464, 103554, 103581],
            Values(pack.CardIdentifiers));
    }

    [Fact]
    public void LiveQuickStatusReconstructsStateAndExactReplayIsIdempotent()
    {
        var events = ParseLine(LiveQuickDraftStatus);
        var engine = new ArenaDraftStateEngine();

        foreach (var draftEvent in events) engine.Apply(draftEvent);
        var state = engine.Current;

        Assert.Equal(ArenaDraftSessionStatus.Active, state.Status);
        Assert.Equal(ArenaDraftModeKind.Quick, state.Mode!.Kind);
        Assert.Equal([103499, 103521], state.CompletedPicks.Select(
            pick => Assert.Single(pick.CardIdentifiers).Value));
        Assert.Equal(
            [ArenaDraftCoordinate.Create(1, 1), ArenaDraftCoordinate.Create(1, 2)],
            state.CompletedPicks.Select(pick => pick.Coordinate));
        Assert.Equal(ArenaDraftCoordinate.Create(1, 3), state.CurrentPack!.Coordinate);
        Assert.Equal(12, state.CurrentPack.CardIdentifiers.Count);

        var replay = events.Select(engine.Apply).ToArray();
        Assert.All(replay, update => Assert.False(update.Changed));
        Assert.Equal(2, engine.Current.CompletedPicks.Count);
    }

    [Fact]
    public void CoherentPickedCardsReconstructAcrossPackBoundary()
    {
        var pickedCards = Enumerable.Range(1, 15).Select(number => number.ToString()).ToArray();
        var line = BotStatusEnvelope(pack: 1, pick: 1, numCardsToPick: 1, pickedCards);

        var picks = ParseLine(line)
            .OfType<ArenaDraftLogEvent.PickSubmitted>()
            .Select(draftEvent => draftEvent.Pick)
            .ToArray();

        Assert.Equal(15, picks.Length);
        Assert.Equal(ArenaDraftCoordinate.Create(1, 14), picks[13].Coordinate);
        Assert.Equal(ArenaDraftCoordinate.Create(2, 1), picks[14].Coordinate);
    }

    [Fact]
    public void IncoherentPickedCardsDoNotDiscardValidCurrentPack()
    {
        var events = ParseLine(BotStatusEnvelope(
            pack: 0,
            pick: 2,
            numCardsToPick: 1,
            pickedCards: ["901"]));

        Assert.Empty(events.OfType<ArenaDraftLogEvent.PickSubmitted>());
        Assert.Equal(ArenaDraftCoordinate.Create(1, 3), Pack(events).Coordinate);
        Start(events);
    }

    [Fact]
    public void MultiCardBotStatusDoesNotSynthesizeSingleCardHistory()
    {
        var events = ParseLine(BotStatusEnvelope(
            pack: 0,
            pick: 2,
            numCardsToPick: 2,
            pickedCards: ["901", "902"]));

        Assert.Empty(events.OfType<ArenaDraftLogEvent.PickSubmitted>());
        Pack(events);
    }

    [Fact]
    public void LaterQuickCoordinateNormalizesIndependently()
    {
        var pack = Pack(ParseLine("""{"method":"BotDraft_DraftStatus","response":{"DraftPack":[501,502],"PackNumber":1,"PickNumber":6,"DraftStatus":"Drafting"}}"""));
        Assert.Equal(ArenaDraftCoordinate.Create(2, 7), pack.Coordinate);
    }

    [Fact]
    public void QuickPickPreservesNumericStringMultiCardIds()
    {
        var line = """{"method":"BotDraftDraftPick","request":"{\"DraftId\":\"quick-draft\",\"PickInfo\":{\"PackNumber\":1,\"PickNumber\":6,\"CardIds\":[\"601\",602]}}"}""";
        var pick = Pick(ParseLine(line));
        Assert.Equal("quick-draft", pick.DraftIdentifier?.Value);
        Assert.Equal(ArenaDraftCoordinate.Create(2, 7), pick.Coordinate);
        Assert.Equal([601, 602], Values(pick.CardIdentifiers));
    }

    [Fact]
    public void SingularQuickCardIdIsSupported()
    {
        var pick = Pick(ParseLine("""{"method":"BotDraft_DraftPick","request":{"PickInfo":{"PackNumber":0,"PickNumber":2,"CardId":"701"}}}"""));
        Assert.Equal(ArenaDraftCoordinate.Create(1, 3), pick.Coordinate);
        Assert.Equal([701], Values(pick.CardIdentifiers));
    }

    [Theory]
    [InlineData("PremierDraft_TST", ArenaDraftModeKind.Premier)]
    [InlineData("TradDraft_TST", ArenaDraftModeKind.Traditional)]
    [InlineData("PickTwoDraft_TST", ArenaDraftModeKind.PickTwo)]
    [InlineData("ExperimentalDraft_TST", ArenaDraftModeKind.Unknown)]
    public void HumanDraftStartsAreClassified(string eventName, ArenaDraftModeKind expected)
    {
        var line = JsonSerializer.Serialize(new
        {
            method = "EventJoin",
            id = "correlation",
            request = new { EventName = eventName, DraftId = "true-draft" }
        });
        var start = Start(ParseLine(line));
        Assert.Equal(eventName, start.EventName);
        Assert.Equal(expected, start.Mode.Kind);
        Assert.Equal("true-draft", start.DraftIdentifier?.Value);
        if (expected == ArenaDraftModeKind.Unknown) Assert.Equal(eventName, start.Mode.UnknownEventName);
    }

    [Fact]
    public void QuickStartIsClassifiedFromBotStatus()
    {
        var start = Start(ParseLine("""{"method":"BotDraftDraftStatus","response":{"EventName":"QuickDraft_TST","DraftId":"quick-draft","DraftStatus":"Drafting"}}"""));
        Assert.Equal(ArenaDraftModeKind.Quick, start.Mode.Kind);
        Assert.Equal("QuickDraft_TST", start.EventName);
    }

    [Fact]
    public void CorrelationIdIsNotUsedAsDraftIdentifier()
    {
        var start = Start(ParseLine("""{"method":"EventJoin","id":"not-a-draft-id","request":"{\"EventName\":\"PremierDraft_TST\"}"}"""));
        Assert.Null(start.DraftIdentifier);
    }

    [Fact]
    public void SealedJoinIsIgnored() =>
        Assert.Empty(ParseLine("""{"method":"EventJoin","request":{"EventName":"Sealed_TST"}}"""));

    [Fact]
    public void HumanCompletionProducesOneFact()
    {
        var events = ParseLine("""{"method":"DraftCompleteDraft","response":{"EventName":"PremierDraft_TST","DraftId":"draft-complete"}}""");
        var completion = Completion(events);
        Assert.Single(events);
        Assert.Equal("PremierDraft_TST", completion.EventName);
        Assert.Equal("draft-complete", completion.DraftIdentifier?.Value);
    }

    [Fact]
    public void QuickCompletionProducesOneFact()
    {
        var events = ParseLine("""{"method":"BotDraftDraftStatus","response":{"EventName":"QuickDraft_TST","DraftId":"quick-complete","DraftStatus":"Completed"}}""");
        Assert.Single(events);
        Assert.Equal("quick-complete", Completion(events).DraftIdentifier?.Value);
    }

    [Fact]
    public void StandaloneBotDraftEnvelopePreservesCompletionHandling()
    {
        var payload = JsonSerializer.Serialize(new
        {
            Result = "Success",
            EventName = "QuickDraft_TST",
            DraftStatus = "Completed"
        });
        var line = JsonSerializer.Serialize(new { CurrentModule = "BotDraft", Payload = payload });

        var events = ParseLine(line);

        Assert.Single(events);
        Assert.Equal("QuickDraft_TST", Completion(events).EventName);
    }

    [Fact]
    public void CompletedQuickPickDoesNotAlsoEmitPick()
    {
        var events = ParseLine("""{"method":"BotDraftDraftPick","response":{"EventName":"QuickDraft_TST","DraftId":"quick-complete","DraftStatus":"Completed","PickInfo":{"PackNumber":2,"PickNumber":13,"CardIds":[801]}}}""");
        Assert.Single(events);
        Completion(events);
    }

    [Fact]
    public void MalformedOuterJsonProducesTypedError() =>
        AssertError("Draft.Notify {not-json}", ArenaDraftLogParseErrorKind.MalformedOuterJson);

    [Fact]
    public void MalformedBotDraftJsonStillFailsClosed() =>
        AssertError(
            "BotDraftDraftStatus {not-json}",
            ArenaDraftLogParseErrorKind.MalformedOuterJson);

    [Fact]
    public void MalformedNestedJsonProducesTypedError()
    {
        var error = AssertError("""{"method":"EventPlayerDraftMakePick","request":"not-json"}""", ArenaDraftLogParseErrorKind.MalformedNestedJson);
        Assert.Equal("request", error.Field);
    }

    [Fact]
    public void InvalidCardIdentifierProducesTypedError() =>
        AssertError("""Draft.Notify {"draftId":"draft","SelfPack":1,"SelfPick":1,"PackCards":"101,0"}""", ArenaDraftLogParseErrorKind.InvalidArenaCardIdentifier);

    [Fact]
    public void MissingCoordinateProducesTypedError()
    {
        var error = AssertError("""Draft.Notify {"draftId":"draft","SelfPack":1,"PackCards":"101,102"}""", ArenaDraftLogParseErrorKind.MissingRequiredField);
        Assert.Equal("SelfPick", error.Field);
    }

    [Fact]
    public void MalformedCommaListProducesTypedError() =>
        AssertError("""Draft.Notify {"draftId":"draft","SelfPack":1,"SelfPick":1,"PackCards":"101,,102"}""", ArenaDraftLogParseErrorKind.MalformedCardList);

    [Fact]
    public void GenericDraftNoiseIsIgnored() => Assert.Empty(ParseLine("Draft queue status changed"));

    [Fact]
    public void DuplicateParserCallsProduceEquivalentEvents()
    {
        const string line = """Draft.Notify {"draftId":"draft-repeat","SelfPack":1,"SelfPick":2,"PackCards":"901,902"}""";
        Assert.Equal(ParseLine(line), ParseLine(line));
    }

    private IReadOnlyList<ArenaDraftLogEvent> ParseLine(string line) =>
        _parser.Parse(new ArenaLogSourceEvent.Line(line));

    private ArenaDraftLogParseException AssertError(string line, ArenaDraftLogParseErrorKind kind)
    {
        var error = Assert.Throws<ArenaDraftLogParseException>(() => ParseLine(line));
        Assert.Equal(kind, error.Kind);
        Assert.NotEmpty(error.Detail);
        return error;
    }

    private static ArenaDraftPackPresentation Pack(IEnumerable<ArenaDraftLogEvent> events) =>
        Assert.Single(events.OfType<ArenaDraftLogEvent.PackPresented>()).Pack;
    private static ArenaDraftPickSubmission Pick(IEnumerable<ArenaDraftLogEvent> events) =>
        Assert.Single(events.OfType<ArenaDraftLogEvent.PickSubmitted>()).Pick;
    private static ArenaDraftStart Start(IEnumerable<ArenaDraftLogEvent> events) =>
        Assert.Single(events.OfType<ArenaDraftLogEvent.DraftStarted>()).Start;
    private static ArenaDraftCompletion Completion(IEnumerable<ArenaDraftLogEvent> events) =>
        Assert.Single(events.OfType<ArenaDraftLogEvent.DraftCompleted>()).Completion;
    private static int[] Values(IEnumerable<ArenaCardIdentifier> identifiers) =>
        identifiers.Select(identifier => identifier.Value).ToArray();

    private static string BotStatusEnvelope(
        int pack,
        int pick,
        int numCardsToPick,
        string[] pickedCards)
    {
        var payload = JsonSerializer.Serialize(new
        {
            Result = "Success",
            EventName = "QuickDraft_TST",
            DraftStatus = "PickNext",
            PackNumber = pack,
            PickNumber = pick,
            NumCardsToPick = numCardsToPick,
            DraftPack = new[] { "9901", "9902" },
            PickedCards = pickedCards
        });
        return JsonSerializer.Serialize(new { CurrentModule = "BotDraft", Payload = payload });
    }
}
