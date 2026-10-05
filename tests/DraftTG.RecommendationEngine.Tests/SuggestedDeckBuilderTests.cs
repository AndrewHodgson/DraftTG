using DraftTG.Domain;

namespace DraftTG.RecommendationEngine.Tests;

public sealed class SuggestedDeckBuilderTests
{
    private static Card Card(string id, string colors, string type = "Creature", IEnumerable<ManaKind>? production = null) =>
        new(CardIdentifier.Create(id), id, new(colors.Select(c => (MagicColor)"WUBRG".IndexOf(c))), CardRarity.Common,
            CardSetCode.Create("WOE"), CollectorNumber.Create(id))
        { GameplayMetadata = new(type == "Land" ? 0 : 2, "{1}" + string.Concat(colors.Select(c => $"{{{c}}}")), type, producedMana: production) };
    private static SetArchetypeProfile Set() => new("WOE", "https://example.test/woe",
        [new("WOE", ArchetypeColorPair.Create("RG"), "RG", "Test beatdown"),
         new("WOE", ArchetypeColorPair.Create("BG"), "BG", "Test midrange"),
         new("WOE", ArchetypeColorPair.Create("UG"), "UG", "Test ramp")]);
    private static DeckBuildInput Input(params (Card Card, int Count, double Rate)[] rows) => new(
        new(new(rows.SelectMany(r => Enumerable.Repeat(r.Card.Identifier, r.Count))), DraftPoolCompleteness.Complete),
        new(rows.Select(r => r.Card)), new(rows.Select(r => new LimitedCardStatistics(r.Card.Identifier, 10000, GameInHandWinRate: r.Rate))),
        StatisticsContext: new("WOE", LimitedStatisticsFormat.QuickDraft));
    private static DeckBuildInput Pool(int pairs = 3) => Input(
        (Card("green", "G"), 11, .55), (Card("red", "R"), 10, .57),
        (Card("black", "B"), pairs >= 2 ? 10 : 1, .61), (Card("blue", "U"), pairs >= 3 ? 10 : 1, .63),
        (Card("shared", ""), 2, .6)) with
        { Archetypes = new("WOE", "https://example.test/woe", [Set().Archetypes.Single(a => a.Pair.Code == "RG")]) };
    private static void SameDeck(BaselineDeck before, BaselineDeck after)
    {
        Assert.Equal(before.Plan.Pair, after.Plan.Pair); Assert.Equal(before.Plan.Source, after.Plan.Source);
        Assert.Equal(before.Nonlands, after.Nonlands); Assert.Equal(before.NonbasicLands, after.NonbasicLands);
        Assert.Equal(before.GeneratedBasics, after.GeneratedBasics); Assert.Equal(before.Sideboard, after.Sideboard);
        Assert.Equal(before.NonlandCount, after.NonlandCount); Assert.Equal(before.LandCount, after.LandCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BuildOneExactlyPreservesBaselineIncludingOptionalAffinity(bool affinity)
    {
        var input = Pool(); var builder = new BaselineDeckBuilder();
        if (affinity) input = input with { PairStatistics = new(input.StatisticsContext!, ArchetypeColorPair.Create("RG"),
            new([new(CardIdentifier.Create("red"), 10000, GameInHandWinRate: .7)]), [new(.55, 10000)]) };
        var baseline = builder.Build(input).Deck!;
        var set = new SuggestedDeckBuilder(builder).Build(input);
        SameDeck(baseline, set.Recommended!.Deck);
        Assert.Same(set.BaselineResult.Deck, set.Recommended.Deck);
        Assert.Equal(DeckPlanSource.ActiveArchetype, baseline.Plan.Source);
        Assert.Equal("RG", baseline.Plan.Pair.Code); Assert.Equal(40, baseline.TotalCardCount);
        if (affinity) Assert.Contains(baseline.Decisions, d => d.Strength?.ArchetypeAdjustment > 0);
        var expected = baseline.Nonlands.Sum(e => baseline.Decisions.Single(d => d.CardIdentifier == e.CardIdentifier).Strength!.Phase8AdjustedValue!.Value * e.Count) / 23;
        Assert.Equal(expected, set.Recommended.Comparison.CommonSpellQuality!.Value, 12);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ReturnsOnlyTheDistinctViablePairsWithoutPadding(int pairs)
    {
        var set = new SuggestedDeckBuilder().Build(Pool(pairs));
        Assert.Equal(pairs, set.Builds.Count); Assert.Equal(pairs, set.ViablePairCandidatesEvaluated);
        Assert.Equal(pairs, set.Builds.Select(b => b.Deck.Plan.Pair).Distinct().Count());
        Assert.Equal(Enumerable.Range(1, pairs), set.Builds.Select(b => b.BuildRank.Value));
        Assert.Equal(10, set.Candidates.Count); Assert.Equal(10 - pairs, set.ExcludedCandidates.Count);
        Assert.All(set.Builds, b => { Assert.Equal(40, b.Deck.TotalCardCount); Assert.Equal(23, b.Deck.NonlandCount); Assert.Equal(17, b.Deck.LandCount); });
    }

    [Fact]
    public void ThreePairOutputIsDeterministicAndUsesKnownArchetypesWithoutOffColorCopies()
    {
        var input = Pool(); var set = new SuggestedDeckBuilder().Build(input);
        var reordered = input with { Pool = new(new(input.Pool.Inventory.CardIdentifiers.Reverse()), DraftPoolCompleteness.Complete) };
        var again = new SuggestedDeckBuilder().Build(reordered);
        Assert.Equal(set.Builds.Select(b => b.Id), again.Builds.Select(b => b.Id));
        foreach (var build in set.Builds)
        {
            SameDeck(build.Deck, again.Builds.Single(b => b.Id == build.Id).Deck);
            Assert.All(build.Deck.Nonlands, e => Assert.True(DeckPlanSelector.ColorEligible(input.Catalog.Find(e.CardIdentifier)!, build.Deck.Plan.Pair)));
            if (!build.IsRecommended) Assert.Equal(DeckPlanSource.AlternativeColorPair, build.Deck.Plan.Source);
        }
        var labelled = new SuggestedDeckBuilder().Build(input with { Archetypes = Set() });
        Assert.All(labelled.Builds, b => Assert.Contains(b.Deck.Plan.Archetype!.Name, b.PlanLabel));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AlternativeOrderingUsesCommonQualityThenPoolFit(bool tie)
    {
        var input = Pool();
        if (tie)
        {
            input = input with { Statistics = new(input.Statistics.Entries.Select(r => r with { GameInHandWinRate = .55 })),
                Pool = new(new(input.Pool.Inventory.CardIdentifiers.Append(CardIdentifier.Create("black"))), DraftPoolCompleteness.Complete) };
        }
        var set = new SuggestedDeckBuilder().Build(input);
        Assert.Equal("RG", set.Builds[0].Deck.Plan.Pair.Code);
        Assert.Equal(tie ? "BG" : "UG", set.Builds[1].Deck.Plan.Pair.Code);
        if (tie)
        {
            Assert.Equal(set.Builds[1].Comparison.CommonSpellQuality!.Value, set.Builds[2].Comparison.CommonSpellQuality!.Value, 12);
            Assert.True(set.Builds[1].Comparison.PairPoolFit > set.Builds[2].Comparison.PairPoolFit);
        }
        else Assert.True(set.Builds[1].Comparison.CommonSpellQuality > set.Builds[2].Comparison.CommonSpellQuality);
    }

    [Fact]
    public void EvaluatesAllTenViablePairsEvenWhenOutputLimitIsOne()
    {
        var input = Input((Card("artifact", ""), 23, .55));
        var set = new SuggestedDeckBuilder(configuration: new(maximumSuggestedBuilds: 1)).Build(input);
        Assert.Single(set.Builds); Assert.Equal(10, set.ViablePairCandidatesEvaluated);
        Assert.All(set.Candidates, c => Assert.True(c.ValidCandidate)); Assert.Empty(set.ExcludedCandidates);
    }

    [Fact]
    public void ManaAndNonbasicSelectionAreIndependentAndSharedInventoryIsNotReserved()
    {
        var input = Pool(2); var rg = Card("rg-land", "", "Land", [ManaKind.Red, ManaKind.Green]);
        var bg = Card("bg-land", "", "Land", [ManaKind.Black, ManaKind.Green]);
        input = input with { Catalog = new(input.Catalog.Cards.Concat([rg, bg])),
            Pool = new(new(input.Pool.Inventory.CardIdentifiers.Concat([rg.Identifier, bg.Identifier])), DraftPoolCompleteness.Complete) };
        var set = new SuggestedDeckBuilder(new(new(maximumDraftedNonbasicLands: 1))).Build(input);
        Assert.Equal(rg.Identifier, Assert.Single(set.Builds[0].Deck.NonbasicLands).CardIdentifier);
        Assert.Equal(bg.Identifier, Assert.Single(set.Builds[1].Deck.NonbasicLands).CardIdentifier);
        Assert.Contains(set.Builds[0].Deck.GeneratedBasics, e => e.Type == BasicLandType.Mountain);
        Assert.Contains(set.Builds[1].Deck.GeneratedBasics, e => e.Type == BasicLandType.Swamp);
        Assert.Equal(0, set.Builds[1].Deck.ColoredDemand[MagicColor.Red]);
        Assert.Equal(0, set.Builds[0].Deck.ColoredDemand[MagicColor.Black]);
        foreach (var build in set.Builds)
        {
            Assert.Equal(11, build.Deck.Nonlands.Single(e => e.CardIdentifier.Value == "green").Count);
            Assert.Equal(2, build.Deck.Nonlands.Single(e => e.CardIdentifier.Value == "shared").Count);
            Assert.All(input.Pool.Entries, e => Assert.Equal(e.Count, build.Deck.Nonlands.Concat(build.Deck.NonbasicLands).Concat(build.Deck.Sideboard)
                .Where(d => d.CardIdentifier == e.CardIdentifier).Sum(d => d.Count)));
        }
    }

    [Fact]
    public void DifferenceUsesCountedCopiesAndSeparatesLandChanges()
    {
        var input = Input((Card("A", "G"), 2, .5), (Card("B", ""), 1, .6), (Card("C", "R"), 1, .55), (Card("D", "B"), 2, .65));
        var builder = new BaselineDeckBuilder(new(7, 3, 4, 0, 0, 0, 5, 4, 0, 1));
        var baseline = builder.BuildForPair(input, ArchetypeColorPair.Create("RG")).Deck!;
        var other = builder.BuildForPair(input, ArchetypeColorPair.Create("BG")).Deck!;
        var diff = SuggestedDeckDifference.Compare(baseline, other);
        Assert.Equal(new DeckCardEntry(CardIdentifier.Create("D"), 2), Assert.Single(diff.CardsAdded));
        Assert.Equal([new(CardIdentifier.Create("A"), 1), new DeckCardEntry(CardIdentifier.Create("C"), 1)], diff.CardsRemoved);
        Assert.Equal(2, diff.SharedNonlandCount); Assert.Equal(2, diff.ChangedNonlandCount);
        Assert.Equal(2, diff.Lands.AddedCount); Assert.Equal(2, diff.Lands.RemovedCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingDataDoesNotExcludeViableAlternativesAndCoverageIsExplicit(bool baselineAvailable)
    {
        var input = Pool() with { Statistics = new(), EnvironmentStatistics = baselineAvailable ? Pool().Statistics : new() };
        var set = new SuggestedDeckBuilder().Build(input);
        Assert.Equal(3, set.Builds.Count);
        Assert.All(set.Builds, b =>
        {
            Assert.Equal(0, b.Comparison.MeasuredStatCards);
            Assert.Equal(baselineAvailable ? 23 : 0, b.Comparison.NeutralFallbackCards);
            Assert.Equal(baselineAvailable ? 0 : 23, b.Comparison.UnscoredCards);
            Assert.Equal(baselineAvailable, b.Comparison.CommonSpellQuality.HasValue);
        });
    }

    [Theory]
    [InlineData(DraftPoolCompleteness.Partial)]
    [InlineData(DraftPoolCompleteness.Unknown)]
    public void EverySuggestionInheritsProvisionalPoolAndCompositionRelaxations(DraftPoolCompleteness completeness)
    {
        var input = Pool(); input = input with { Pool = new(input.Pool.Inventory, completeness),
            Catalog = new(input.Catalog.Cards.Select(c => c with { GameplayMetadata = new(6, c.GameplayMetadata.ManaCost, "Instant") })) };
        var set = new SuggestedDeckBuilder().Build(input);
        Assert.Equal(3, set.Builds.Count);
        Assert.All(set.Builds, b =>
        {
            Assert.NotEqual(DeckBuildAvailability.Ready, b.Availability);
            Assert.Equal(3, b.Comparison.CompositionRelaxations);
            Assert.Contains(b.Deck.Diagnostics, d => d.StartsWith("Provisional", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void IdentityDependsOnSessionAndPairRatherThanRankAndSelectionFallsBackWhenAbsent()
    {
        var builder = new SuggestedDeckBuilder(); var input = Pool();
        var first = builder.Build(input, "session-A"); var selected = first.Builds[1];
        Assert.Equal(selected.Id, first.Select(selected.Id).Selected!.Id);
        var reordered = builder.Build(input with { Statistics = new(input.Statistics.Entries.Select(r => r with { GameInHandWinRate = .55 })) }, "session-A");
        Assert.Equal(selected.Id, reordered.Select(selected.Id).Selected!.Id);
        Assert.NotEqual(first.Recommended!.Id, builder.Build(input, "session-B").Recommended!.Id);
        Assert.Equal(first.Recommended.Id, first.Select(new("missing")).Selected!.Id);
    }

    [Fact]
    public void ConfigurationBoundsAndImmutableOutputsAreExplicit()
    {
        var config = new SuggestedDeckConfiguration(); Assert.Equal(3, config.MaximumSuggestedBuilds); Assert.Equal(2, config.MaximumAdditionalPairDataRequests);
        Assert.Throws<ArgumentOutOfRangeException>(() => new SuggestedDeckConfiguration(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SuggestedDeckConfiguration(maximumAdditionalPairDataRequests: 3));
        var set = new SuggestedDeckBuilder().Build(Pool());
        Assert.Throws<NotSupportedException>(() => ((IList<SuggestedDeck>)set.Builds).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<SuggestedDeckCandidateDiagnostic>)set.Candidates).Clear());
    }
}
