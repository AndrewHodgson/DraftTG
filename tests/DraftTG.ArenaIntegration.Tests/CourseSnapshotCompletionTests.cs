using System.Text.Json.Nodes;
using DraftTG.ArenaIntegration;

namespace DraftTG.ArenaIntegration.Tests;

/// <summary>Premier FRA after an Arena restart: the EventGetCoursesV2 course list is the only surviving draft evidence.</summary>
public sealed class CourseSnapshotCompletionTests
{
    private static string[] Lines() => File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "premier-fra-course-snapshot-live.jsonl"));
    private static IReadOnlyList<ArenaDraftLogEvent> Parse(string line) => new ArenaDraftLogParser().Parse(new ArenaLogSourceEvent.Line(line));
    private static string ChangedCourses(Action<JsonArray> change)
    {
        var root = JsonNode.Parse(Lines()[1])!.AsObject();
        change(root["Courses"]!.AsArray());
        return root.ToJsonString();
    }
    private static JsonObject Fra(JsonArray courses) =>
        courses.Select(c => c!.AsObject()).Single(c => c["InternalEventName"]!.GetValue<string>() == "PremierDraft_FRA_20260929");

    [Fact]
    public void RealPremierCourseListCompletesAnIdleSessionWithTheFullDuplicatePreservingPool()
    {
        var lines = Lines();
        Assert.Empty(Parse(lines[0])); Assert.Empty(Parse(lines[2])); // Response marker and scene change are not draft facts.
        var fact = Assert.IsType<ArenaDraftLogEvent.DraftCompleted>(Assert.Single(Parse(lines[1])));
        Assert.Equal((ArenaDraftCompletionOrigin.CourseSnapshot, "PremierDraft_FRA_20260929"), (fact.Completion.Origin, fact.Completion.EventName));
        Assert.Equal(ArenaDraftMode.Premier, fact.Completion.Mode);
        Assert.Null(fact.Completion.DraftIdentifier); Assert.Null(fact.Completion.FinalPickedCards); // CourseId is not a draft ID.

        var engine = new ArenaDraftStateEngine();
        var completed = engine.Apply(fact);
        Assert.True(completed.Changed);
        Assert.Equal((ArenaDraftSessionStatus.Completed, 42, 33), (completed.Snapshot.Status, completed.Snapshot.DraftedPool.Count,
            completed.Snapshot.DraftedPool.Counts.Count));
        Assert.Equal(3, completed.Snapshot.DraftedPool.CountOf(ArenaCardIdentifier.Create(106315)));
        Assert.Equal(8, completed.Snapshot.DraftedPool.Counts.Count(p => p.Value > 1));
        Assert.Null(completed.Snapshot.PickedCardsDiagnostic); Assert.Empty(completed.Snapshot.CompletedPicks);
        Assert.False(engine.Apply(fact).Changed); // Arena repeats the course list on every EventLanding visit.
    }

    [Theory]
    [InlineData("second-draft-course")]
    [InlineData("deck-submitted")]
    [InlineData("short-pool")]
    [InlineData("only-non-draft-courses")]
    [InlineData("malformed-pool")]
    public void AmbiguousSubmittedPartialOrNonDraftCourseListsNeverBecomeDraftState(string scenario)
    {
        var line = ChangedCourses(courses =>
        {
            var fra = Fra(courses);
            switch (scenario)
            {
                case "second-draft-course":
                    var other = fra.DeepClone().AsObject(); other["InternalEventName"] = "QuickDraft_WOE_20260929"; courses.Add(other); break;
                case "deck-submitted": fra["CurrentModule"] = "CreateMatch"; break;
                case "short-pool": fra["CardPool"]!.AsArray().RemoveAt(0); break;
                case "only-non-draft-courses": courses.Remove(fra); break;
                default: fra["CardPool"]!.AsArray()[0] = "not-a-card"; break;
            }
        });
        var engine = new ArenaDraftStateEngine();
        foreach (var fact in Parse(line)) engine.Apply(fact);
        Assert.Equal(ArenaDraftSessionStatus.Idle, engine.Current.Status);
        Assert.Equal(0, engine.Current.DraftedPool.Count);
    }

    [Fact]
    public void CourseListNeverReplacesADifferentLiveDraftAndOnlyConfirmsTheSameEvent()
    {
        var engine = new ArenaDraftStateEngine(); var parser = new ArenaDraftLogParser();
        foreach (var line in File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "deckselect-woe-completion-live.jsonl")).Take(1))
            foreach (var fact in parser.Parse(new ArenaLogSourceEvent.Line(line))) engine.Apply(fact);
        var live = engine.Current;
        Assert.Equal("QuickDraft_WOE_20260929", live.EventName); Assert.NotNull(live.CurrentPack);
        Assert.False(engine.Apply(Assert.Single(Parse(Lines()[1]))).Changed); // An older FRA course must not end the live WOE draft.
        Assert.Equal(live, engine.Current);
    }
}
