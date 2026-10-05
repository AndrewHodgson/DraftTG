using DraftTG.Domain;

namespace DraftTG.RecommendationEngine.Tests;

public sealed class BaselineDeckBuilderTests
{
    private static Card Card(string id, string colors = "BG", double? manaValue = 3, bool creature = true,
        string? cost = "{1}{B}{G}", string? type = null, IEnumerable<ManaKind>? production = null) =>
        new(CardIdentifier.Create(id), id, new(colors.Select(c => (MagicColor)"WUBRG".IndexOf(c))), CardRarity.Common,
            CardSetCode.Create("WOE"), CollectorNumber.Create(id))
        { GameplayMetadata = new(manaValue, cost, type ?? (creature ? "Creature" : "Instant"), producedMana: production) };
    private static Card[] Cards(string prefix, int count, string colors = "BG", double? mv = 3, bool creature = true,
        string? cost = "{1}{B}{G}") => Enumerable.Range(0, count).Select(i => Card($"{prefix}-{i:D2}", colors, mv, creature, cost)).ToArray();
    private static DeckBuildInput Input(Card[] cards, double rate = .55, DraftPoolCompleteness completeness = DraftPoolCompleteness.Complete,
        IEnumerable<CardIdentifier>? occurrences = null) => new(new(new(occurrences ?? cards.Select(c => c.Identifier)), completeness),
            new(cards), new(cards.Select(c => new LimitedCardStatistics(c.Identifier, 10000, GameInHandWinRate: rate))),
            StatisticsContext: new("WOE", LimitedStatisticsFormat.QuickDraft));
    private static SetArchetypeProfile Set(string pair) => new("WOE", "https://example.test/woe", [new("WOE", ArchetypeColorPair.Create(pair), pair, "Test archetype")]);
    private static BaselineDeck Build(DeckBuildInput input) => Assert.IsType<BaselineDeck>(new BaselineDeckBuilder().Build(input).Deck);

    [Fact]
    public void HealthyPoolProducesExactDeckAndConservesEveryOwnedCopy()
    {
        var input = Input(Cards("healthy", 32)); var deck = Build(input);
        Assert.Equal(40, deck.TotalCardCount); Assert.Equal(23, deck.NonlandCount); Assert.Equal(17, deck.LandCount);
        Assert.Equal(9, deck.Sideboard.Sum(e => e.Count)); Assert.Equal(23, deck.CreatureCount);
        Assert.All(input.Pool.Entries, e => Assert.Equal(e.Count,
            deck.Nonlands.Concat(deck.NonbasicLands).Concat(deck.Sideboard).Where(d => d.CardIdentifier == e.CardIdentifier).Sum(d => d.Count)));
    }

    [Fact]
    public void CopiesAreIndependentAndLimitedHasNoFourCopyCap()
    {
        var a = Card("A"); var b = Card("B");
        var input = Input([a, b], occurrences: Enumerable.Repeat(a.Identifier, 5).Concat(Enumerable.Repeat(b.Identifier, 20)));
        var deck = Build(input);
        Assert.Equal(5, deck.Nonlands.Single(e => e.CardIdentifier == a.Identifier).Count);
        Assert.Equal(18, deck.Nonlands.Single(e => e.CardIdentifier == b.Identifier).Count);
        Assert.Equal(2, Assert.Single(deck.Sideboard).Count);
    }

    [Fact]
    public void ColorEligibilityUsesColorSetsAndKeepsColorlessSpells()
    {
        var pair = ArchetypeColorPair.Create("BG");
        foreach (var (colors, expected) in new[] { ("B", true), ("G", true), ("BG", true), ("", true), ("U", false), ("GU", false), ("WBG", false) })
            Assert.Equal(expected, DeckPlanSelector.ColorEligible(Card(colors == "" ? "colorless" : colors, colors), pair));
        var cards = Cards("main", 23).Concat([Card("off", "U", cost: "{U}")]).ToArray();
        var deck = Build(Input(cards) with { Archetypes = Set("BG") });
        Assert.DoesNotContain(deck.Nonlands, e => e.CardIdentifier.Value == "off");
        Assert.True(deck.Decisions.Single(d => d.CardIdentifier.Value == "off").Reasons.HasFlag(DeckDecisionReason.OffColor));
    }

    [Fact]
    public void FinalActiveArchetypeWinsWhenViableWithoutAnActivePack()
    {
        var input = Input(Cards("bg", 24).Concat(Cards("wu", 23, "WU", cost: "{W}{U}")).ToArray()) with { Archetypes = Set("BG") };
        var deck = Build(input);
        Assert.Equal("BG", deck.Plan.Pair.Code); Assert.Equal(DeckPlanSource.ActiveArchetype, deck.Plan.Source);
        Assert.NotNull(deck.Plan.FinalArchetypeContext.Active);
    }

    [Fact]
    public void InsufficientActivePairFallsBackToHighestViablePoolEvidence()
    {
        var input = Input(Cards("bg", 18).Concat(Cards("gw", 23, "WG", cost: "{W}{G}")).ToArray()) with { Archetypes = Set("BG") };
        var deck = Build(input);
        Assert.Equal("WG", deck.Plan.Pair.Code); Assert.Equal(DeckPlanSource.PoolEvidenceFallback, deck.Plan.Source);
        Assert.All(deck.Nonlands, e => Assert.True(DeckPlanSelector.ColorEligible(input.Catalog.Find(e.CardIdentifier)!, deck.Plan.Pair)));
    }

    [Fact]
    public void CreatureFloorChangesRawSelectionAndReasonsReflectCounterfactual()
    {
        var creatures = Cards("creature", 18); var instants = Cards("spell", 20, creature: false);
        var input = Input(creatures.Concat(instants).ToArray()) with
            { Statistics = new(creatures.Select(c => new LimitedCardStatistics(c.Identifier, 10000, GameInHandWinRate: .5))
                .Concat(instants.Select(c => new LimitedCardStatistics(c.Identifier, 10000, GameInHandWinRate: .65)))) };
        var deck = Build(input);
        Assert.Equal(14, deck.CreatureCount);
        Assert.Contains(deck.Decisions, d => d.SelectedCount > 0 && d.Reasons.HasFlag(DeckDecisionReason.CreatureRequirement));
        Assert.Contains(deck.Decisions, d => d.SelectedCount == 0 && d.Reasons.HasFlag(DeckDecisionReason.CompositionConstraint));
        Assert.DoesNotContain(deck.Decisions, d => d.SelectedCount == 0 && d.Reasons.HasFlag(DeckDecisionReason.CompositionConstraint)
            && d.Reasons.HasFlag(DeckDecisionReason.LowerSelectionValue));
    }

    [Fact]
    public void FourAvailableEarlyPlaysAreSelectedDespiteLowerStrength()
    {
        var early = Cards("early", 4, mv: 2); var late = Cards("late", 26, mv: 3);
        var input = Input(early.Concat(late).ToArray()) with
        { Statistics = new(early.Select(c => new LimitedCardStatistics(c.Identifier, 10000, GameInHandWinRate: .45))
            .Concat(late.Select(c => new LimitedCardStatistics(c.Identifier, 10000, GameInHandWinRate: .6)))) };
        var deck = Build(input); Assert.Equal(4, deck.Composition.SelectedEarlyPlays);
        Assert.Contains(deck.Decisions, d => d.Reasons.HasFlag(DeckDecisionReason.EarlyCurveRequirement));
    }

    [Fact]
    public void HighCostCapRelaxesOnlyToMinimumJointFeasibility()
    {
        foreach (var (lowCount, lowCreature, expectedCap) in new[] { (25, true, 6), (15, true, 8), (23, false, 14) })
        {
            var low = Cards("low", lowCount, mv: 3, creature: lowCreature); var high = Cards("high", 14, mv: 6);
            var input = Input(low.Concat(high).ToArray()) with
            { Statistics = new(low.Select(c => new LimitedCardStatistics(c.Identifier, 10000, GameInHandWinRate: .5))
                .Concat(high.Select(c => new LimitedCardStatistics(c.Identifier, 10000, GameInHandWinRate: .65)))) };
            var deck = Build(input);
            Assert.Equal(expectedCap, deck.Composition.EffectiveHighCostCap);
            Assert.Equal(expectedCap, deck.Composition.SelectedHighCostCards);
            if (expectedCap > 6) Assert.Contains(deck.Composition.Relaxations, s => s.StartsWith("High-cost", StringComparison.Ordinal));
            else Assert.Contains(deck.Decisions, d => d.Reasons.HasFlag(DeckDecisionReason.HighCostConstraint));
        }
    }

    [Fact]
    public void MissingStatisticsUseNeutralPriorAndNoBaselineUsesExplicitUnscoredOrdering()
    {
        var cards = Cards("prior", 23); var input = Input(cards);
        input = input with { Statistics = new(input.Statistics.Entries.Skip(1)), EnvironmentStatistics = input.Statistics };
        var deck = Build(input);
        var fallback = deck.Decisions.Single(d => !d.Strength!.IsMeasured).Strength!;
        Assert.Equal(DeckCardStrengthSource.NeutralBaselineFallback, fallback.Source);
        Assert.Equal(.55, fallback.SelectionValue!.Value, 10); Assert.Equal(1, deck.Strength.NeutralFallbackCards);
        var noData = Build(input with { Statistics = new(), EnvironmentStatistics = new() });
        Assert.Equal(DeckBuildConfidence.InsufficientStatistics, noData.Confidence);
        Assert.All(noData.Decisions, d => Assert.Null(d.Strength!.SelectionValue));
        Assert.Contains(noData.Diagnostics, d => d.Contains("InsufficientStatistics", StringComparison.Ordinal));
    }

    [Fact]
    public void NoViablePairDoesNotAddThirdColorAndMissingMetadataIsExplicit()
    {
        var cards = Cards("b", 8, "B", cost: "{B}").Concat(Cards("g", 8, "G", cost: "{G}"))
            .Concat(Cards("u", 8, "U", cost: "{U}")).ToArray();
        var result = new BaselineDeckBuilder().Build(Input(cards));
        Assert.Null(result.Deck); Assert.Equal(DeckBuildAvailability.InsufficientEligibleCards, result.Availability);
        result = new BaselineDeckBuilder().Build(Input(Cards("unknown", 24).Select(c => c with { GameplayMetadata = CardGameplayMetadata.Unknown }).ToArray()));
        Assert.Null(result.Deck); Assert.Equal(DeckBuildAvailability.MissingRequiredMetadata, result.Availability);
    }

    [Fact]
    public void PartialAndUnknownPoolsAreProvisionalAndShortPoolsAreUnavailable()
    {
        foreach (var completeness in new[] { DraftPoolCompleteness.Partial, DraftPoolCompleteness.Unknown })
        {
            var result = new BaselineDeckBuilder().Build(Input(Cards("partial", 25), completeness: completeness));
            Assert.NotNull(result.Deck); Assert.NotEqual(DeckBuildAvailability.Ready, result.Availability);
            Assert.Contains(result.Deck.Diagnostics, d => d.StartsWith("Provisional", StringComparison.Ordinal));
        }
        Assert.Null(new BaselineDeckBuilder().Build(Input(Cards("short", 22), completeness: DraftPoolCompleteness.Partial)).Deck);
    }

    [Fact]
    public void NonbasicsNeedUsefulStructureOrMeasuredSupportAndBasicsRemainUnlimited()
    {
        var on = Card("dual", "", 0, false, "", "Land", [ManaKind.Black, ManaKind.Green]);
        var off = Card("off-land", "", 0, false, "", "Land", [ManaKind.White, ManaKind.Blue]);
        var unknown = Card("unknown-land", "", 0, false, "", "Land");
        var basic = Card("forest", "", 0, false, "", "Basic Land — Forest", [ManaKind.Green]);
        var input = Input(Cards("spells", 25).Concat([on, off, unknown, basic]).ToArray());
        input = input with { Statistics = new(input.Statistics.Entries.Where(r => r.CardIdentifier != unknown.Identifier)) };
        var deck = Build(input);
        Assert.Equal(on.Identifier, Assert.Single(deck.NonbasicLands).CardIdentifier);
        Assert.Equal(16, deck.GeneratedBasics.Sum(e => e.Count)); Assert.Equal(17, deck.LandCount);
        Assert.Contains(deck.Sideboard, e => e.CardIdentifier == basic.Identifier);
        Assert.Contains(deck.Sideboard, e => e.CardIdentifier == off.Identifier);
        Assert.Equal(9, deck.KnownColoredSources[MagicColor.Black]);
    }

    [Fact]
    public void BasicAllocationReservesMinimumThenUsesDemandAndDoesNotForceUnusedColor()
    {
        var pair = ArchetypeColorPair.Create("BG");
        var demand = new Dictionary<MagicColor, double> { [MagicColor.Black] = 12, [MagicColor.Green] = 8 };
        var basics = BaselineBasicLandAllocator.Allocate(pair, demand, 17, 6);
        Assert.Equal(17, basics.Sum(e => e.Count));
        Assert.Equal(9, basics.Single(e => e.Type == BasicLandType.Swamp).Count);
        Assert.Equal(8, basics.Single(e => e.Type == BasicLandType.Forest).Count);
        basics = BaselineBasicLandAllocator.Allocate(pair, demand, 9, 6); Assert.Equal(9, basics.Sum(e => e.Count));
        Assert.All(basics, e => Assert.True(e.Count >= 4));
        demand[MagicColor.Green] = 0;
        basics = BaselineBasicLandAllocator.Allocate(pair, demand, 17, 6);
        Assert.Equal(new GeneratedBasicLandEntry(BasicLandType.Swamp, 17), Assert.Single(basics));
    }

    [Theory]
    [InlineData("{2}{G}", 0, 1, 0)]
    [InlineData("{1}{B}{B}", 2, 0, 0)]
    [InlineData("{G/U}", 0, .5, .5)]
    [InlineData("{X}{G}", 0, 1, 0)]
    [InlineData("{4}{C}", 0, 0, 0)]
    public void ManaSymbolsExtractColoredDemand(string cost, double black, double green, double blue)
    {
        var demand = DeckManaDemand.Parse(cost); Assert.True(demand.IsKnown);
        Assert.Equal(black, demand.Colors[MagicColor.Black]); Assert.Equal(green, demand.Colors[MagicColor.Green]); Assert.Equal(blue, demand.Colors[MagicColor.Blue]);
    }

    [Fact]
    public void AdventureDemandUsesPrimaryFaceOnceAndAffinityUsesOnlyOverallPlusMatchingPair()
    {
        var c = Card("adventure", "G", cost: "{G} // {U}") with
        { GameplayMetadata = new(3, "{G} // {U}", "Creature — Beast // Instant — Adventure", CardLayout.Adventure,
            [new("Creature", "{G}", 3, null, "Creature", null, null, null), new("Adventure", "{U}", null, null, "Instant", null, null, null)]) };
        Assert.Equal(1, DeckManaDemand.For(c).Colors[MagicColor.Green]); Assert.Equal(0, DeckManaDemand.For(c).Colors[MagicColor.Blue]);
        var input = Input(Cards("affinity", 23).Concat([c]).ToArray()) with { Archetypes = Set("BG") };
        var row = input.Catalog.Cards[0];
        input = input with { PairStatistics = new(input.StatisticsContext!, ArchetypeColorPair.Create("BG"),
            new([new(row.Identifier, 10000, GameInHandWinRate: .65)]), [new(.55, 10000)]) };
        var deck = Build(input); var strength = deck.Decisions.Single(d => d.CardIdentifier == row.Identifier).Strength!;
        Assert.Equal(DeckCardStrengthSource.ArchetypeAdjusted, strength.Source);
        Assert.Equal(strength.Phase8AdjustedValue!.Value + strength.ArchetypeAdjustment, strength.SelectionValue!.Value, 10);
        Assert.True(strength.ArchetypeAdjustment > 0);
        var mismatch = Build(input with { PairStatistics = new(new("WOE", LimitedStatisticsFormat.PremierDraft), ArchetypeColorPair.Create("BG"),
            input.PairStatistics.Catalog, [new(.55, 10000)]) });
        Assert.Equal(DeckCardStrengthSource.OverallStatistical, mismatch.Decisions.Single(d => d.CardIdentifier == row.Identifier).Strength!.Source);
    }

    [Fact]
    public void DeterminismIncludesRarityIndependenceAndPreferredCreatureTies()
    {
        var input = Input(Cards("a-creature", 20).Concat(Cards("b-spell", 12, creature: false)).ToArray());
        var first = Build(input); Assert.Equal(16, first.CreatureCount);
        for (var i = 0; i < 3; i++)
        {
            var reordered = input with { Pool = new(new(input.Pool.Inventory.CardIdentifiers.Reverse()), DraftPoolCompleteness.Complete),
                Catalog = new(input.Catalog.Cards.Reverse().Select(c => c with { Rarity = CardRarity.Mythic })) };
            var other = Build(reordered);
            Assert.Equal(first.Nonlands, other.Nonlands); Assert.Equal(first.Sideboard, other.Sideboard); Assert.Equal(first.GeneratedBasics, other.GeneratedBasics);
            Assert.Equal(first.Plan.Pair, other.Plan.Pair);
        }
    }

    [Fact]
    public void OptimizerObjectiveAgreesWithSmallExhaustiveOracle()
    {
        var cards = Enumerable.Range(0, 7).Select(i => Card($"oracle-{i}", manaValue: i % 3 == 0 ? 6 : i % 3 + 1, creature: i % 2 == 0)).ToArray();
        var input = Input(cards) with { Statistics = new(cards.Select((c, i) => new LimitedCardStatistics(c.Identifier, 10000, GameInHandWinRate: .4 + i * .03))) };
        var config = new BaselineDeckConfiguration(7, 3, 4, 2, 3, 1, 5, 2, 0, 1);
        var result = new BaselineDeckBuilder(config).Build(input).Deck!;
        var strengths = result.Decisions.ToDictionary(d => d.CardIdentifier, d => d.Strength!.SelectionValue!.Value);
        var feasible = Enumerable.Range(0, 1 << cards.Length).Select(mask => cards.Where((_, i) => (mask & (1 << i)) != 0).ToArray())
            .Where(s => s.Length == 4 && s.Count(c => c.GameplayMetadata.IsCreature) >= 2 && s.Count(c => c.GameplayMetadata.ManaValue <= 2) >= 1
                && s.Count(c => c.GameplayMetadata.ManaValue >= 5) <= 2);
        Assert.Equal(feasible.Max(s => s.Sum(c => strengths[c.Identifier])), result.Strength.TotalSelectionObjective, 10);
    }

    [Fact]
    public void ConfigurationAndOutputsAreExplicitAndReadOnly()
    {
        var config = new BaselineDeckConfiguration(); Assert.Equal(40, config.TargetDeckSize); Assert.Equal(17, config.TargetLandCount);
        Assert.Equal(14, config.MinimumCreatures); Assert.Equal(4, config.MinimumEarlyPlays);
        Assert.Throws<ArgumentOutOfRangeException>(() => new BaselineDeckConfiguration(targetLandCount: 18));
        var deck = Build(Input(Cards("immutable", 25)));
        Assert.Throws<NotSupportedException>(() => ((IList<DeckCardEntry>)deck.Nonlands).Clear());
    }
}
