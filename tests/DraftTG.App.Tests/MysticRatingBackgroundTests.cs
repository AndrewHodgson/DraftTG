using System.Xml.Linq;
using DraftTG.Application;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;

namespace DraftTG.App.Tests;

public sealed class MysticRatingBackgroundTests
{
    [Fact]
    public void ScoreBandsUseAllSixQualityTiersWithSafeUnavailableFallback()
    {
        // Phase 5 tier scheme v2: bands follow the score semantics (0–14 very weak … 45–50 premium).
        foreach (var (score, tier) in new[] { (0, MysticBadgeQualityTier.Graphite), (14, MysticBadgeQualityTier.Graphite),
            (15, MysticBadgeQualityTier.Steel), (24, MysticBadgeQualityTier.Steel), (25, MysticBadgeQualityTier.Silver),
            (34, MysticBadgeQualityTier.Silver), (35, MysticBadgeQualityTier.Gold), (39, MysticBadgeQualityTier.Gold),
            (40, MysticBadgeQualityTier.RichGold), (44, MysticBadgeQualityTier.RichGold), (45, MysticBadgeQualityTier.Premium), (50, MysticBadgeQualityTier.Premium) })
            Assert.Equal(tier, MysticBadgeTierPalette.Tier(score));
        Assert.Equal(MysticBadgeQualityTier.Unavailable, MysticBadgeTierPalette.Tier(null));
        Assert.Equal(MysticBadgeQualityTier.Unavailable, MysticBadgeTierPalette.Tier(51));
    }
    [Fact]
    public void BadgeTierIsTheCanonicalPickScoreTierForEveryScore()
    {
        // Expected: the palette consumes RecommendationEngine's canonical tier; it never keeps its own bands.
        foreach (var score in Enumerable.Range(-1, 53).Select(s => (int?)s).Append(null))
        {
            var canonical = DraftTG.RecommendationEngine.PickScoreTierThresholds.Classify(score);
            Assert.Equal((int)canonical, (int)MysticBadgeTierPalette.Tier(score));
            if (DraftTG.RecommendationEngine.PickScoreTierThresholds.Range(canonical) is var (low, high))
                Assert.EndsWith($"{low}–{high}", MysticBadgeTierPalette.Label(score));
        }
    }
    [Theory]
    [InlineData(47, CardRarity.Common, true)]
    [InlineData(12, CardRarity.Mythic, false)]
    public void PrintedRarityNeverSelectsTheQualityTier(int score, CardRarity rarity, bool premium)
    {
        // Expected: a Common at 47 is premium and a Mythic at 12 is very weak; only the displayed score chooses the tier.
        var sample = MysticBadgePreviewWindow.Sample(3, "Card", score);
        var badge = new CardBadgeViewModel(sample.Presentation!.WithCard(sample.Presentation.Card! with { Rarity = rarity }));
        Assert.Equal(premium ? MysticBadgeQualityTier.Premium : MysticBadgeQualityTier.Graphite, MysticBadgeTierPalette.Tier(badge.Presentation!.PickScore!.Score0To50));
    }
    [Fact]
    public void ExistingQualityTreatmentsShareCharcoalAndSilverWithMorePrestigiousTopAccent()
    {
        var palettes = new[] { MysticBadgeTierPalette.For(15), MysticBadgeTierPalette.For(42) };
        Assert.All(palettes, p =>
        {
            Assert.Equal("#e6171b22", p.BaseBackground.ToString().ToLowerInvariant());
            Assert.InRange(p.HighlightOpacity, .14, .24);
            Assert.InRange(p.NeutralOpacity, .18, .20);
        });
        Assert.Equal("#ff6d798b", palettes[0].AccentPrimary.ToString().ToLowerInvariant());
        Assert.Equal("#ffe2be64", palettes[1].AccentPrimary.ToString().ToLowerInvariant());
        Assert.Equal("#ffa4b7ce", palettes[0].Glow.ToString().ToLowerInvariant());
        Assert.Equal("#ffd8a24f", palettes[1].Glow.ToString().ToLowerInvariant());
        Assert.Equal("#ffcad8ea", palettes[0].Sparkle.ToString().ToLowerInvariant());
        Assert.Equal("#fff3dca2", palettes[1].Sparkle.ToString().ToLowerInvariant());
        Assert.True(palettes[1].SparkleStrength > palettes[0].SparkleStrength);
        Assert.True(palettes[1].HighlightOpacity > palettes[0].HighlightOpacity);
        Assert.Equal("Steel · 15–24", MysticBadgeTierPalette.Label(15));
        Assert.Equal("Rich gold · 40–44", MysticBadgeTierPalette.Label(42));
    }
    [Fact]
    public void MissingRecommendationSafelyUsesTheNeutralTreatment()
    {
        var unknown = new CardBadgeViewModel(new CurrentPackCardPresentation(new(0, CardIdentifier.Create("missing"))));
        Assert.False(unknown.IsContextPick);
        Assert.Equal(MysticBadgeQualityTier.Unavailable, MysticBadgeTierPalette.Tier(unknown.Presentation?.PickScore?.Score0To50));
        Assert.Null(new MysticRatingBackground().PickScore);
    }
    [Fact]
    public void DisabledAnimationRetainsAStaticVersionAcrossTime()
    {
        Assert.Equal(MysticWaveState.At(0, 3, false), MysticWaveState.At(197, 3, false));
        Assert.NotEqual(MysticWaveState.At(0, 3, true), MysticWaveState.At(2, 3, true));
        var control = new MysticRatingBackground { IsAnimated = false };
        Assert.False(control.AnimationActive); Assert.False(control.IsHitTestVisible); Assert.False(control.Focusable);
    }
    [Fact]
    public void OccurrenceOffsetsAreStableDistinctAndDoNotUseCardName()
    {
        var offsets = Enumerable.Range(0, 15).Select(MysticWaveState.PhaseOffset).ToArray();
        Assert.Equal(15, offsets.Distinct().Count());
        Assert.Equal(offsets, Enumerable.Range(0, 15).Select(MysticWaveState.PhaseOffset));
        Assert.Equal(0, MysticWaveState.PhaseOffset(-1));
        Assert.NotEqual(MysticWaveState.At(0, 1, true), MysticWaveState.At(0, 2, true));
    }
    [Fact]
    public void MotionIsBoundedAndContinuousThroughCycleBoundaries()
    {
        foreach (var time in Enumerable.Range(0, 1201).Select(i => i / 10d))
        {
            var state = MysticWaveState.At(time, 4, true);
            Assert.InRange(state.X1, -15, 15); Assert.InRange(state.Y1, -1.8, 1.8);
            Assert.InRange(state.X2, -24, 24); Assert.InRange(state.Y2, -2.4, 2.4);
            Assert.InRange(state.X3, -10, 10); Assert.InRange(state.Y3, -1.4, 1.4);
        }
        var before = MysticWaveState.At(120 - .0001, 4, true); var after = MysticWaveState.At(.0001, 4, true);
        Assert.InRange(Math.Abs(before.X1 - after.X1), 0, .004);
        Assert.InRange(Math.Abs(before.X2 - after.X2), 0, .004);
        Assert.InRange(Math.Abs(before.X3 - after.X3), 0, .004);
        Assert.Equal(MysticWaveState.At(0, 0, true), MysticWaveState.At(double.NaN, 0, true));
    }
    [Fact]
    public void SharedClockFrameUpdatesChangeRenderedOffsetsAtZeroOneAndTwoSeconds()
    {
        var control = new MysticRatingBackground();
        control.AdvanceFrame(0); var zero = control.RenderState;
        control.AdvanceFrame(1); var one = control.RenderState;
        control.AdvanceFrame(2); var two = control.RenderState;
        Assert.NotEqual(zero, one); Assert.NotEqual(one, two); Assert.NotEqual(zero, two);
        Assert.True(Math.Abs(two.X2 - zero.X2) > 15);
        Assert.True(Math.Abs(two.X1 - zero.X1) > 10);
        Assert.Equal(3, control.FrameUpdateCount);
        control.IsAnimated = false; var paused = control.RenderState;
        control.AdvanceFrame(8); Assert.Equal(paused, control.RenderState);
        control.AdvanceFrame(19); Assert.Equal(paused, control.RenderState);
    }
    [Fact]
    public void SparklesFadeAndDriftSlowlyWithinTheBadgeAndFreezeWhenAnimationIsDisabled()
    {
        var control = new MysticRatingBackground { OccurrenceIndex = 3 };
        control.AdvanceFrame(0); var zero = control.SparkleState(0);
        control.AdvanceFrame(1); var one = control.SparkleState(0);
        control.AdvanceFrame(2); var two = control.SparkleState(0);
        Assert.NotEqual(zero, one); Assert.NotEqual(one, two);
        Assert.NotEqual(zero.Opacity, one.Opacity);
        for (var index = 0; index < MysticBadgeTuning.SparkleCount; index++)
        {
            foreach (var time in Enumerable.Range(0, 601).Select(i => i / 10d))
            {
                var state = MysticSparkleState.At(time, 3, index, true);
                Assert.InRange(state.X, 10, 76); Assert.InRange(state.Y, 5, 37);
                Assert.InRange(state.Opacity, 0, MysticBadgeTuning.SparkleOpacity);
                Assert.InRange(state.SizeScale, MysticBadgeTuning.SparkleSizeMin, MysticBadgeTuning.SparkleSizeMax);
                var next = MysticSparkleState.At(time + 1d / 30, 3, index, true);
                Assert.InRange(Math.Abs(next.Opacity - state.Opacity), 0, .02);
            }
            control.IsAnimated = false; control.AdvanceFrame(8); var paused = control.SparkleState(index);
            control.AdvanceFrame(19); Assert.Equal(paused, control.SparkleState(index));
        }
        Assert.Equal(MysticBadgeTuning.SparkleCount, Enumerable.Range(0, MysticBadgeTuning.SparkleCount)
            .Select(i => MysticSparkleState.At(0, 3, i, true).SizeScale).Distinct().Count());
        var warm = new MysticRatingBackground { OccurrenceIndex = 3, PickScore = 42 };
        var cool = new MysticRatingBackground { OccurrenceIndex = 3, PickScore = 15 };
        warm.AdvanceFrame(2); cool.AdvanceFrame(2);
        Assert.True(warm.SparkleState(0).Opacity > cool.SparkleState(0).Opacity);
    }
    [Fact]
    public void QualityComesFromTheSameRecommendationAndRatingsRemainUnchanged()
    {
        var badge = MysticBadgePreviewWindow.Sample(3, "Mystic Card", true);
        var presentation = badge.Presentation!;
        Assert.True(badge.IsContextPick); Assert.Equal(presentation.IsContextPick, badge.IsContextPick);
        Assert.Equal("Mystic Card", badge.DisplayName);
        Assert.Equal(presentation.DisplayedGIH, badge.WinRate); Assert.Equal(presentation.DisplayedALSA, badge.Secondary);
        Assert.Equal(presentation.DisplayedContextRank, badge.Rank);
        Assert.Equal(86, badge.Width); Assert.Equal(180, badge.NameWidth);
        Assert.Equal(47, badge.X); Assert.Equal(56, badge.Y);
        var unknown = new CardBadgeViewModel(new CurrentPackCardPresentation(new(0,CardIdentifier.Create("missing"))));
        Assert.False(unknown.IsContextPick);
        var inconsistent = new CardBadgeViewModel(new CurrentPackCardPresentation(new(0,CardIdentifier.Create("wrong")), presentation.Card));
        Assert.False(inconsistent.IsContextPick);
    }
    private static CardBadgeViewModel[] QualityRegressionPack()
    {
        var ids = new[] { CardIdentifier.Create("rare-low"), CardIdentifier.Create("common-top") };
        var pack = new DraftPack(new(PackNumber.Create(1), PickNumber.Create(1)), ids);
        var stats = new LimitedCardStatisticsCatalog([
            new(ids[0], GameInHandGameCount: 5000, GameInHandWinRate: .49),
            new(ids[1], GameInHandGameCount: 5000, GameInHandWinRate: .64)]);
        var snapshot = new DraftSnapshot(pack, new DraftHistory([]), DraftFormat.BestOfOne);
        var catalog = new CardCatalog(ids.Select((id, i) => new Card(id, i == 0 ? "Low-rated Rare" : "Top-rated Common", ColorSet.Colorless,
            i == 0 ? CardRarity.Rare : CardRarity.Common, CardSetCode.Create("TST"), CollectorNumber.Create((i + 1).ToString()))));
        var recommendation = new StatisticalRecommendationEngine().Recommend(pack, stats);
        var pool = new ContextualRecommendationEngine().Recommend(snapshot, catalog, recommendation);
        var lane = new LaneContextualRecommendationEngine().Recommend(snapshot, pool, DraftPackObservationHistory.Empty, LimitedStatisticsFormat.PremierDraft);
        var archetype = new ArchetypeRecommendationEngine().Recommend(lane, null, new("TST", LimitedStatisticsFormat.PremierDraft));
        var scores = new ContextualPickScoreEngine().Recommend(snapshot, catalog, archetype, stats, stats);
        return ids.Select((id, i) => new CardBadgeViewModel(new CurrentPackCardPresentation(new(i, id),
            new Card(id, i == 0 ? "Low-rated Rare" : "Top-rated Common", ColorSet.Colorless,
                i == 0 ? CardRarity.Rare : CardRarity.Common, CardSetCode.Create("TST"), CollectorNumber.Create((i + 1).ToString())),
            stats.StatisticsFor(id), statistical: recommendation.Cards[i], pickScore: scores.Cards[i]))).ToArray();
    }
    [Fact]
    public void PrintedRareWithLowRatingIsNeutralAndPrintedCommonWithTopRatingIsPremium()
    {
        var badges = QualityRegressionPack();
        Assert.Equal("#2", badges[0].Rank); Assert.False(badges[0].IsContextPick);
        Assert.Equal(MysticBadgeQualityTier.Graphite, MysticBadgeTierPalette.Tier(badges[0].Presentation!.PickScore!.Score0To50));
        Assert.Equal("#1", badges[1].Rank); Assert.True(badges[1].IsContextPick);
        Assert.Equal(MysticBadgeQualityTier.Premium, MysticBadgeTierPalette.Tier(badges[1].Presentation!.PickScore!.Score0To50));
    }
    [Fact]
    public void ChangingPrintedRarityNeverChangesQualityTreatment()
    {
        foreach (var original in QualityRegressionPack())
        foreach (var rarity in Enum.GetValues<CardRarity>())
        {
            var variant = new CardBadgeViewModel(original.Presentation!.WithCard(original.Presentation.Card! with { Rarity = rarity }));
            Assert.Equal(original.IsContextPick, variant.IsContextPick);
            Assert.Equal(original.Presentation!.PickScore!.Score0To50, variant.Presentation!.PickScore!.Score0To50);
            Assert.Same(MysticBadgeTierPalette.For(original.Presentation!.PickScore!.Score0To50), MysticBadgeTierPalette.For(variant.Presentation!.PickScore!.Score0To50));
            Assert.Equal(original.WinRate, variant.WinRate); Assert.Equal(original.Rank, variant.Rank);
        }
    }
    [Fact]
    public void ShippedEffectIsBehindTheOriginalTextInsideTheOriginalBorder()
    {
        using var stream = typeof(MysticRatingBackgroundTests).Assembly.GetManifestResourceStream("CardOverlayWindow.axaml")!;
        var xaml = XElement.Load(stream);
        var effect = Assert.Single(xaml.Descendants(), e => e.Name.LocalName == nameof(MysticRatingBackground));
        Assert.Equal("{Binding Presentation.PickScore.Score0To50}", (string?)effect.Attribute("PickScore"));
        Assert.DoesNotContain(effect.Attributes(), a => a.Name.LocalName.Contains("Rarity", StringComparison.Ordinal));
        Assert.Equal("{Binding Index}", (string?)effect.Attribute("OccurrenceIndex"));
        var grid = effect.Parent!; Assert.Same(effect, grid.Elements().First());
        var border = grid.Parent!; Assert.Equal("Border", border.Name.LocalName);
        Assert.Equal("42", (string?)border.Attribute("Height")); Assert.Equal("5", (string?)border.Attribute("CornerRadius"));
        Assert.Equal("#E6171B22", (string?)border.Attribute("Background"));
        Assert.Equal("{Binding BorderColor}", (string?)border.Attribute("BorderBrush"));
        Assert.Equal("{Binding IsPlaced}", (string?)border.Attribute("IsVisible"));
        // Phase 3 adds the secondary EST marker beside the score; the three value lines are otherwise unchanged.
        Assert.Equal(new[] { "{Binding Presentation.DisplayedPickScore}", "EST", "{Binding Presentation.DisplayedBadgeGIH}", "{Binding Presentation.DisplayedALSA}" },
            grid.Descendants().Where(e => e.Name.LocalName == "TextBlock").Select(e => (string)e.Attribute("Text")!));
        var name = Assert.Single(xaml.Descendants(), e => (string?)e.Attribute("Text") == "{Binding DisplayName}");
        Assert.Equal("CharacterEllipsis", (string?)name.Attribute("TextTrimming"));
    }
}
