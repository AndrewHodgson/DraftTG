namespace DraftTG.ArenaIntegration.Tests;

public sealed class ArenaDeckLogParserTests
{
    private static ArenaLogSourceEvent.Line Line(string text) => new(text);
    [Fact]
    public void RealPublicSavedSequenceHasScenesAndExactCopyCounts()
    {
        var parser = new ArenaDeckLogParser();
        var events = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "arena-woe-saved-deck-live.jsonl"))
            .SelectMany(l => parser.Parse(Line(l))).ToArray();
        Assert.Equal("DeckBuilder", Assert.IsType<ArenaDeckLogEvent.SceneChanged>(events[0]).Scene);
        var saved = Assert.IsType<ArenaDeckLogEvent.SavedDeckObserved>(events[1]);
        Assert.Equal("QuickDraft_WOE_20260929", saved.EventName); Assert.Equal(40, saved.MainDeck.Sum(e => e.Count));
        Assert.Equal(18, saved.Sideboard.Sum(e => e.Count));
        Assert.Contains(saved.MainDeck, e => e.CardIdentifier.Value == 75900 && e.Count == 8);
        Assert.Contains(saved.MainDeck, e => e.CardIdentifier.Value == 75903 && e.Count == 8);
        Assert.Equal("EventLanding", Assert.IsType<ArenaDeckLogEvent.SceneChanged>(events[2]).Scene);
    }

    [Fact]
    public void DraftCompletionPickedCardsNeverBecomeCurrentDeckMembership()
    {
        var line = """{"CurrentModule":"DeckSelect","Payload":{"EventName":"QuickDraft_WOE","DraftStatus":"Completed","PickedCards":["101","102"]}}""";
        Assert.Empty(new ArenaDeckLogParser().Parse(Line(line)));
    }

    [Fact]
    public void SaveRequestAndNestedHistoricalCourseDecksAreNotAcceptedCurrentSnapshots()
    {
        var parser = new ArenaDeckLogParser();
        Assert.Empty(parser.Parse(Line("""[UnityCrossThreadLogger]==> EventSetDeckV3 {"request":{"EventName":"QuickDraft_WOE","Deck":{"MainDeck":[{"cardId":101,"quantity":1}],"Sideboard":[]}}}""")));
        Assert.Empty(parser.Parse(Line("""{"Courses":[{"InternalEventName":"QuickDraft_WOE","CourseDeck":{"MainDeck":[],"Sideboard":[]}}]}""")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MalformedOrIncompleteDeckObservationFailsClosedWithoutThrowingIntoDraftParsing(bool missingSideboard)
    {
        var text = missingSideboard ? """{"InternalEventName":"QuickDraft_WOE","CourseDeck":{"MainDeck":[]}}"""
            : """{"InternalEventName":"QuickDraft_WOE","CourseDeck":{"MainDeck":[{"cardId":101,"quantity":0}],"Sideboard":[]}}""";
        Assert.IsType<ArenaDeckLogEvent.InvalidObservation>(Assert.Single(new ArenaDeckLogParser().Parse(Line(text))));
    }

    [Fact]
    public void SourceResetIsExplicitAndUnrelatedJsonOrSceneTextDoesNotChangeEditorState()
    {
        var parser = new ArenaDeckLogParser();
        Assert.IsType<ArenaDeckLogEvent.SourceReset>(Assert.Single(parser.Parse(new ArenaLogSourceEvent.SourceReset())));
        Assert.Empty(parser.Parse(Line("""{"toSceneName":"DeckBuilder","unrelated":true}""")));
        Assert.Empty(parser.Parse(Line("unrelated log text")));
    }
}
