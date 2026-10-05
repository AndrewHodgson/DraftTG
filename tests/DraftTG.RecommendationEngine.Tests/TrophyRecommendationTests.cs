using DraftTG.Domain;
using static DraftTG.RecommendationEngine.Tests.ColorCommitmentTests;

namespace DraftTG.RecommendationEngine.Tests;

public sealed class TrophyRecommendationTests
{
    private static readonly SuccessfulDeckKey Key = new("WOE", SuccessfulDeckFormat.QuickDraft, ArchetypeColorPair.Create("BG"));
    private static readonly DateTimeOffset Time = new(2026, 10, 4, 0, 0, 0, TimeSpan.Zero);
    private static Dictionary<string, int> Counts(params string[] names) => names.GroupBy(n => n).ToDictionary(g => g.Key, g => g.Count());
    private static SuccessfulDeckSample Sample(string id, string[] main, string[]? pool) =>
        new(Key, id, new(7, 1), 7, 0, Time, Counts(main), pool is null ? null : Counts(pool));
    private static SuccessfulDeckCorpus Corpus(params SuccessfulDeckSample[] samples) => new(Key, samples, Time,
        new("test", "recent", "WOE QuickDraft BG", 100, 100, samples.Length, "final"));
    private static (ArchetypeRecommendationResult Result, CardCatalog Cards) Earlier(Card[] pack, bool active = true)
    {
        var green = Card("G", MagicColor.Green); var black = Card("B", MagicColor.Black);
        var pool = active ? Enumerable.Repeat(green, 7).Concat(Enumerable.Repeat(black, 7)).ToArray() : [];
        var snapshot = Snapshot(pool, pack); var catalog = new CardCatalog(pool.Concat(pack).DistinctBy(c => c.Identifier));
        var stats = new StatisticalRecommendationEngine().Recommend(snapshot.CurrentPack, new(pack.DistinctBy(c => c.Identifier)
            .Select(c => new LimitedCardStatistics(c.Identifier, GameInHandWinRate: .58, GameInHandGameCount: 1000))));
        var colors = new ContextualRecommendationEngine().Recommend(snapshot, catalog, stats);
        var lane = new LaneContextualRecommendationEngine().Recommend(snapshot, colors, DraftPackObservationHistory.Empty, LimitedStatisticsFormat.QuickDraft);
        var set = new SetArchetypeProfile("WOE", "https://example.test/source", new[] { "BG", "GU" }
            .Select(p => new ArchetypeDefinition("WOE", ArchetypeColorPair.Create(p), p, "Description")));
        return (new ArchetypeRecommendationEngine().Recommend(lane, set, new("WOE", LimitedStatisticsFormat.QuickDraft)), catalog);
    }

    [Fact]
    public void SyntheticCorpusAggregatesEventConversionCopiesPresenceAndAverageExactly()
    {
        var catalog = new TrophyEvidenceCatalog(Corpus(Sample("1", ["A", "B"], ["A", "B", "C"]),
            Sample("2", ["A", "A", "C"], ["A", "A", "A", "C", "D"]), Sample("3", ["B", "D"], ["A", "B", "D"])));
        var expected = new[] { ("A", 3, 2, 5, 3), ("B", 2, 2, 2, 2), ("C", 2, 1, 2, 1), ("D", 2, 1, 2, 1) };
        foreach (var (name, pool, main, poolCopies, mainCopies) in expected)
        {
            var card = catalog.ForName(name)!;
            Assert.Equal(pool, card.PoolDecks); Assert.Equal(main, card.MainDeckDecks); Assert.Equal(poolCopies, card.PoolCopies);
            Assert.Equal(mainCopies, card.MainDeckCopies); Assert.Equal(main / (double)pool, card.MainDeckConversion);
            Assert.Equal(mainCopies / (double)poolCopies, card.CopyUtilization);
            Assert.Equal(main / 3.0, card.DeckPresenceRate); Assert.Equal(mainCopies / (double)main, card.AverageCopiesWhenPlayed);
        }
    }

    [Fact]
    public void MissingPoolDoesNotInventZeroExposureOrPolluteConversionNumerator()
    {
        var card = new TrophyEvidenceCatalog(Corpus(Sample("known", ["B"], ["A", "B"]),
            Sample("unknown", ["A", "A"], null))).ForName("A")!;
        Assert.Equal(1, card.PoolDecks); Assert.Equal(1, card.MainDeckDecks); Assert.Equal(0, card.MainDeckDecksWithKnownPool);
        Assert.Equal(0, card.MainDeckConversion); Assert.Equal(0, card.CopyUtilization); Assert.Equal(.5, card.DeckPresenceRate);
        var unknown = new TrophyEvidenceCatalog(Corpus(Sample("only", ["A"], null))).ForName("A")!;
        Assert.Null(unknown.MainDeckConversion); Assert.Null(unknown.CopyUtilization);
    }

    [Fact]
    public void DuplicateOccurrencesJoinByExactIdentityAndNeverChangeEarlierScoresOrRanks()
    {
        var (earlier, cards) = Earlier([Card("A", MagicColor.Green), Card("B", MagicColor.Black), Card("A", MagicColor.Green)]);
        var trophy = new TrophyRecommendationEngine().Recommend(earlier, new("WOE", LimitedStatisticsFormat.QuickDraft), cards,
            Corpus(Sample("1", ["A"], ["A", "B"])));
        Assert.Same(trophy.Cards[0].Evidence, trophy.Cards[2].Evidence);
        Assert.Equal([0, 1, 2], trophy.Cards.Select(c => c.PackIndex)); Assert.False(trophy.ScoringEnabled);
        Assert.All(trophy.Cards, c => { Assert.Equal(0, c.Adjustment); Assert.Equal(c.ArchetypeRecommendation.ContextualValue, c.FinalValue);
            Assert.Equal(c.ArchetypeRecommendation.ContextualRank, c.FinalRank); });
        Assert.Contains("games and copies", TrophyRecommendationResult.DisabledReason);
    }

    [Fact]
    public void WrongSetFormatPairAndUnsettledArchetypeCannotApplyAnyEvidence()
    {
        var corpus = Corpus(Sample("1", ["A"], ["A"])); var (earlier, cards) = Earlier([Card("A", MagicColor.Green)]);
        var wrongKeys = new[] { new SuccessfulDeckKey("HOB", Key.Format, Key.Pair), new("WOE", SuccessfulDeckFormat.PremierDraft, Key.Pair),
            new("WOE", Key.Format, ArchetypeColorPair.Create("GU")) };
        foreach (var key in wrongKeys)
        {
            var sample = new SuccessfulDeckSample(key, "event", new(7, 1), 7, 0, Time, Counts("A"), Counts("A"));
            var foreign = new SuccessfulDeckCorpus(key, [sample], Time, corpus.Provenance);
            Assert.Null(new TrophyRecommendationEngine().Recommend(earlier, new("WOE", LimitedStatisticsFormat.QuickDraft), cards, foreign).Evidence);
        }
        var unsettled = Earlier([Card("A", MagicColor.Green)], active: false);
        Assert.Null(new TrophyRecommendationEngine().Recommend(unsettled.Result, new("WOE", LimitedStatisticsFormat.QuickDraft), unsettled.Cards, corpus).Evidence);
    }

    [Fact]
    public void ColorlessRequiresEvidenceThirdColorIsIneligibleAndSmallSamplesStayDiagnostic()
    {
        var (earlier, cards) = Earlier([Card("A", MagicColor.Green), Card("C"), Card("D"), Card("R", MagicColor.Red), Card("E")]);
        var corpus = Corpus(Sample("1", ["A", "C", "R"], ["A", "C", "R", "E"]));
        var result = new TrophyRecommendationEngine().Recommend(earlier, new("WOE", LimitedStatisticsFormat.QuickDraft), cards, corpus);
        Assert.Equal([true, true, false, false, false], result.Cards.Select(c => c.Eligible));
        Assert.Equal(1.0 / 50, result.Cards[0].CorpusConfidence); Assert.Equal(1.0 / 15, result.Cards[0].CardExposureConfidence);
        Assert.Contains("Small corpus", result.Cards[0].Diagnostic); Assert.All(result.Cards, c => Assert.Equal(0, c.Adjustment));
        var exposure = new TrophyRecommendationEngine(new(minimumTrophyCorpusDecks: 1)).Recommend(earlier,
            new("WOE", LimitedStatisticsFormat.QuickDraft), cards, corpus);
        Assert.Contains("Small or unknown pool exposure", exposure.Cards[0].Diagnostic);
        Assert.Throws<ArgumentOutOfRangeException>(() => new TrophyRecommendationConfiguration(minimumCardTrophyExposure: 0));
    }
}
