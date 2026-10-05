using DraftTG.Domain;
using DraftTG.RecommendationEngine;

namespace DraftTG.Application.Tests;

public sealed class ArenaDeckDifferenceTests
{
    private static ArenaDeckEntry Entry(string id, int count) => new(CardIdentifier.Create(id), count);
    private static ArenaDeckComparisonTarget Target(IEnumerable<ArenaDeckEntry> cards, IEnumerable<ArenaBasicLandEntry>? basics = null,
        string build = "build-1", string session = "draft-A") => new(new SuggestedDeckId(build), session, cards, basics ?? []);
    private static ArenaDeckSnapshot Current(IEnumerable<ArenaDeckEntry> cards, IEnumerable<ArenaBasicLandEntry>? basics = null,
        long revision = 1, string session = "draft-A", ArenaDeckSnapshotScope scope = ArenaDeckSnapshotScope.CurrentEditor) =>
        new(new(session, revision), cards, basics ?? [], ArenaDeckCompleteness.Complete, scope,
            ArenaDeckSnapshotSource.ConfirmedVisualSnapshot, true);

    [Fact]
    public void CopyDifferenceUsesCountsInsteadOfPresenceAndKeepsCorrectCopies()
    {
        var current = Current([Entry("A", 2), Entry("B", 1), Entry("C", 2)]);
        var target = Target([Entry("A", 1), Entry("B", 2), Entry("D", 1), Entry("C", 2)]);
        var diff = ArenaDeckDifference.Compare(current, target);
        Assert.Equal(ArenaDeckComparisonStatus.NeedsChanges, diff.Status);
        Assert.Equal([new(CardIdentifier.Create("B"), 1, 2), new CardDeckDifference(CardIdentifier.Create("D"), 0, 1)], diff.Add);
        Assert.Equal(new CardDeckDifference(CardIdentifier.Create("A"), 2, 1), Assert.Single(diff.Remove));
        Assert.Equal("C", Assert.Single(diff.AlreadyCorrect).CardIdentifier.Value);
        Assert.Equal(2, diff.AddCount); Assert.Equal(1, diff.RemoveCount);
        Assert.Equal(5, diff.CurrentDeckSize); Assert.Equal(6, diff.TargetDeckSize);
    }

    [Fact]
    public void BasicsAreAggregatedSeparatelyFromOwnedCardIdentity()
    {
        var current = Current([Entry("nonbasic", 1)], [new(BasicLandType.Mountain, 8), new(BasicLandType.Forest, 9)]);
        var target = Target([Entry("nonbasic", 1)], [new(BasicLandType.Mountain, 9), new(BasicLandType.Forest, 8)]);
        var diff = ArenaDeckDifference.Compare(current, target);
        Assert.Empty(diff.Add); Assert.Empty(diff.Remove); Assert.Equal(ArenaDeckComparisonStatus.NeedsChanges, diff.Status);
        Assert.Equal(1, diff.BasicLands.Single(b => b.Type == BasicLandType.Mountain).Change);
        Assert.Equal(-1, diff.BasicLands.Single(b => b.Type == BasicLandType.Forest).Change);
        Assert.Equal(3, diff.BasicLands.Count(b => b.Change == 0));
    }

    [Fact]
    public void ExactMatchHasNoChangesAndRetainsTargetAndCurrentRevision()
    {
        var current = Current([Entry("A", 2)], [new(BasicLandType.Forest, 17)]);
        var target = Target([Entry("A", 2)], [new(BasicLandType.Forest, 17)]);
        var diff = ArenaDeckDifference.Compare(current, target);
        Assert.Equal(ArenaDeckComparisonStatus.MatchesSuggestedDeck, diff.Status); Assert.Empty(diff.Add); Assert.Empty(diff.Remove);
        Assert.All(diff.BasicLands, b => Assert.Equal(0, b.Change));
        Assert.Equal(target.BuildIdentity, diff.TargetBuildIdentity); Assert.Equal(current.Revision, diff.CurrentRevision);
    }

    [Fact]
    public void SelectedBuildChangesTargetIdentityAndExactChangesWithoutChangingCurrentDeck()
    {
        var current = Current([Entry("A", 2)]);
        var first = ArenaDeckDifference.Compare(current, Target([Entry("A", 2)]));
        var second = ArenaDeckDifference.Compare(current, Target([Entry("B", 2)], build: "build-2"));
        Assert.Equal(ArenaDeckComparisonStatus.MatchesSuggestedDeck, first.Status);
        Assert.Equal("build-2", second.TargetBuildIdentity!.Value.Value);
        Assert.Equal(2, second.AddCount); Assert.Equal(2, second.RemoveCount); Assert.Equal(first.CurrentRevision, second.CurrentRevision);
    }

    [Fact]
    public void NewObservedDeckRevisionCompletesGuidanceWithoutGuessingClicks()
    {
        var target = Target([Entry("A", 2)]);
        var before = ArenaDeckDifference.Compare(Current([Entry("A", 1)], revision: 10), target);
        var after = ArenaDeckDifference.Compare(Current([Entry("A", 2)], revision: 11), target);
        Assert.Equal(1, before.AddCount); Assert.Equal(0, after.AddCount);
        Assert.Equal(ArenaDeckComparisonStatus.MatchesSuggestedDeck, after.Status);
        Assert.Equal(11, after.CurrentRevision!.Number);
    }

    [Theory]
    [InlineData(ArenaDeckCompleteness.Unknown)]
    [InlineData(ArenaDeckCompleteness.Partial)]
    public void IncompleteCurrentCountsNeverProduceAddRemoveGuidance(ArenaDeckCompleteness completeness)
    {
        var current = new ArenaDeckSnapshot(new("draft-A", 1), [Entry("A", 1)], [], completeness,
            ArenaDeckSnapshotScope.CurrentEditor, ArenaDeckSnapshotSource.StructuredLog, false);
        var diff = ArenaDeckDifference.Compare(current, Target([Entry("B", 2)]));
        Assert.Equal(ArenaDeckComparisonStatus.CurrentDeckUnknown, diff.Status); Assert.Empty(diff.Cards); Assert.Empty(diff.BasicLands);
    }

    [Fact]
    public void ExactSavedLogSnapshotDoesNotCertifyCurrentUnsavedEditorCounts()
    {
        var current = Current([Entry("A", 1)], scope: ArenaDeckSnapshotScope.SavedDeck);
        var diff = ArenaDeckDifference.Compare(current, Target([Entry("B", 1)]));
        Assert.Equal(ArenaDeckComparisonStatus.CurrentDeckUnknown, diff.Status); Assert.Empty(diff.Add); Assert.Empty(diff.Remove);
        Assert.Contains("unsaved", diff.Diagnostic);
    }

    [Fact]
    public void PreviousCompletedSessionAndMissingTargetFailClosed()
    {
        var current = Current([Entry("A", 1)], session: "draft-A");
        Assert.Equal(ArenaDeckComparisonStatus.CurrentDeckUnknown, ArenaDeckDifference.Compare(current, Target([Entry("B", 1)], session: "draft-B")).Status);
        Assert.Equal(ArenaDeckComparisonStatus.SuggestedDeckUnavailable, ArenaDeckDifference.Compare(current, null).Status);
        Assert.Equal(ArenaDeckComparisonStatus.CurrentDeckUnknown, ArenaDeckDifference.Compare(null, Target([])).Status);
    }

    [Fact]
    public void NonbasicCopiesParticipateAndSideboardDoesNotBecomeMainDeck()
    {
        var current = new ArenaDeckSnapshot(new("draft-A", 1), [Entry("dual", 2)], [], ArenaDeckCompleteness.Complete,
            ArenaDeckSnapshotScope.CurrentEditor, ArenaDeckSnapshotSource.StructuredLog, true, sideboard: [Entry("A", 3)]);
        var diff = ArenaDeckDifference.Compare(current, Target([Entry("dual", 1), Entry("A", 1)]));
        Assert.Equal(1, Assert.Single(diff.Remove).RemoveCount); Assert.Equal(1, Assert.Single(diff.Add).AddCount);
    }

    [Fact]
    public void SuggestedSetConversionUsesSelectedBuildAndKeepsGeneratedBasicsSeparate()
    {
        Card Card(string id, MagicColor color, string cost) => new(CardIdentifier.Create(id), id, new([color]), CardRarity.Common,
            CardSetCode.Create("WOE"), CollectorNumber.Create(id)) { GameplayMetadata = new(2, cost, "Creature") };
        var cards = new[] { Card("G", MagicColor.Green, "{1}{G}"), Card("R", MagicColor.Red, "{1}{R}"), Card("B", MagicColor.Black, "{1}{B}") };
        var pool = new DraftPoolSnapshot(new(Enumerable.Repeat(cards[0].Identifier, 20).Concat(Enumerable.Repeat(cards[1].Identifier, 11))
            .Concat(Enumerable.Repeat(cards[2].Identifier, 11))), DraftPoolCompleteness.Complete);
        var set = new SuggestedDeckBuilder().Build(new(pool, new(cards), new()), "draft-A");
        var selected = set.Select(set.Builds[1].Id); var target = ArenaDeckComparisonTarget.From(selected)!;
        Assert.Equal(selected.Selected!.Id, target.BuildIdentity); Assert.Equal("draft-A", target.SessionIdentity);
        Assert.Equal(selected.Selected.Deck.Nonlands.Select(e => new ArenaDeckEntry(e.CardIdentifier, e.Count)), target.Cards);
        Assert.Equal(17, target.Basics.Sum(e => e.Count)); Assert.Equal(40, target.DeckSize);
        Assert.Null(ArenaDeckComparisonTarget.From(null));
    }

    [Fact]
    public void SnapshotAndDifferenceAggregateDuplicateRowsAndAreImmutable()
    {
        var current = Current([Entry("A", 1), Entry("A", 2)], [new(BasicLandType.Forest, 4), new(BasicLandType.Forest, 5)]);
        Assert.Equal(3, Assert.Single(current.MainDeck).Count); Assert.Equal(9, Assert.Single(current.Basics).Count);
        var diff = ArenaDeckDifference.Compare(current, Target([Entry("A", 1)]));
        Assert.Equal(2, Assert.Single(diff.Remove).RemoveCount);
        Assert.Throws<NotSupportedException>(() => ((IList<CardDeckDifference>)diff.Cards).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<ArenaDeckEntry>)current.MainDeck).Clear());
        Assert.Throws<ArgumentException>(() => new ArenaDeckSnapshot(new("draft-A", 1), [], [], ArenaDeckCompleteness.Complete,
            ArenaDeckSnapshotScope.CurrentEditor, ArenaDeckSnapshotSource.StructuredLog, false));
    }
}
