using DraftTG.Data;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;

namespace DraftTG.Application.Tests;

public sealed class ContextualPickScorePresentationTests
{
    private static readonly Card A = new(CardIdentifier.Create("a"), "Alpha", ColorSet.Colorless, CardRarity.Common, CardSetCode.Create("WOE"), CollectorNumber.Create("1"));
    private static readonly Card B = A with { Identifier = CardIdentifier.Create("b"), Name = "Beta" };
    private static readonly LimitedStatisticsContext Context = new("WOE", LimitedStatisticsFormat.QuickDraft);
    private static DraftSnapshot Snapshot() => new(new(new(PackNumber.Create(1), PickNumber.Create(1)), [A.Identifier, B.Identifier, A.Identifier]), new(), DraftFormat.BestOfOne);
    private static LimitedStatisticsLoadResult Result(bool missing = false) => new(Context, Context,
        new([new(A.Identifier, 10000, GameInHandWinRate: .64, AverageLastSeenAt: 3), new(B.Identifier, 10000, GameInHandWinRate: missing ? null : .5)]),
        LimitedStatisticsSource.Cache, null, null)
    { CardCatalog = new([A, B]), EnvironmentCatalog = new([new(CardIdentifier.Create("env"), 10000, GameInHandWinRate: .56)]) };
    [Fact] public void PipelinePreservesOccurrenceIdentityRankRawStatsAndExplainsNewScore()
    {
        var update = new LimitedStatisticsUpdate(Snapshot(), false, Result());
        var a = update.Occurrences[new(0, A.Identifier)]; var duplicate = update.Occurrences[new(2, A.Identifier)];
        Assert.Equal(a.DisplayedPickScore, duplicate.DisplayedPickScore); Assert.NotEqual(a.Key, duplicate.Key);
        Assert.True(a.IsContextPick); Assert.Equal("64.0%", a.DisplayedGIH); Assert.Equal("ALSA 3.00", a.DisplayedALSA);
        Assert.Contains("Pick-score ordinal rank: 1", a.DiagnosticText); Assert.Contains("Contributions from neutral 25", a.DiagnosticText);
        Assert.Contains("contextual-pick-score-v1", PickScorePresentation.Summary(update.PickScores, "Alpha"));
        var wrong = new CurrentPackCardPresentation(new(1, A.Identifier), A, pickScore: a.PickScore);
        Assert.False(wrong.IsIdentityConsistent); Assert.Null(wrong.PickScore);
    }
    [Fact] public void TopPickDesignationFollowsTheUnroundedContextualWinner()
    {
        // Expected: the badge highlight (IsContextPick) goes to exactly one occurrence, the strongest contextual card:
        // the 72% mythic, not the 65% uncommon with more games that V1 preferred when both saturated at 50.
        var mythic = A with { Identifier = CardIdentifier.Create("mythic"), Name = "Mythic" };
        var uncommon = A with { Identifier = CardIdentifier.Create("uncommon"), Name = "Uncommon" };
        var snapshot = new DraftSnapshot(new(new(PackNumber.Create(1), PickNumber.Create(1)), [uncommon.Identifier, mythic.Identifier, uncommon.Identifier]), new(), DraftFormat.BestOfOne);
        var result = new LimitedStatisticsLoadResult(Context, Context, new([new(mythic.Identifier, 2500, GameInHandWinRate: .72),
            new(uncommon.Identifier, 40000, GameInHandWinRate: .65)]), LimitedStatisticsSource.Cache, null, null)
        { CardCatalog = new([mythic, uncommon]), EnvironmentCatalog = new([new(CardIdentifier.Create("env"), 10000, GameInHandWinRate: .56)]) };
        var update = new LimitedStatisticsUpdate(snapshot, false, result);
        var picks = update.Occurrences.Values.Where(o => o.IsContextPick).ToArray();
        Assert.Equal(new CardOccurrenceKey(1, mythic.Identifier), Assert.Single(picks).Key);
        Assert.Equal(1, update.PickScores!.TopRecommendedPackIndex);
        Assert.True(int.Parse(picks[0].DisplayedPickScore) >= int.Parse(update.Occurrences[new(0, uncommon.Identifier)].DisplayedPickScore));
        Assert.Contains("contextual-pick-score-v1.6", PickScorePresentation.Summary(update.PickScores, "Mythic"));
    }
    [Fact] public void LateDiagnosticsExplainDeckNeedWithLabelAndSignedReasons()
    {
        // Expected: late in a draft the expanded diagnostics label deck need (strong/neutral/weak) and list the concrete
        // +/- reasons (here: making the deck and filling missing early plays). The badge itself gains no lines.
        Card Make(string id, string cost, int mv) => new(CardIdentifier.Create(id), id, new(cost.Where("WUBRG".Contains).Select(c => (MagicColor)"WUBRG".IndexOf(c))),
            CardRarity.Common, CardSetCode.Create("WOE"), CollectorNumber.Create(id)) { GameplayMetadata = new(mv, cost, "Creature") };
        var pool = Enumerable.Range(0, 24).Select(i => Make($"p{i}", i % 2 == 0 ? "{2}{R}" : "{2}{G}", 3))
            .Concat(Enumerable.Range(0, 9).Select(i => Make($"o{i}", "{2}{" + "WUB"[i % 3] + "}", 3))).ToArray();
        var twoDrop = Make("two-drop", "{1}{G}", 2); var other = Make("other", "{2}{U}", 3);
        var snapshot = new DraftSnapshot(new(new(PackNumber.Create(3), PickNumber.Create(6)), [twoDrop.Identifier, other.Identifier]), new(), DraftFormat.BestOfOne)
            { RecoveredPool = new(pool.Select(c => c.Identifier)) };
        var all = pool.Append(twoDrop).Append(other).ToArray();
        var result = new LimitedStatisticsLoadResult(Context, Context, new(all.Select(c => new LimitedCardStatistics(c.Identifier, 10000, GameInHandWinRate: .56, AverageLastSeenAt: 4))),
            LimitedStatisticsSource.Cache, null, null) { CardCatalog = new(all), EnvironmentCatalog = new([new(CardIdentifier.Create("env"), 100000, GameInHandWinRate: .56)]) };
        var occurrence = new LimitedStatisticsUpdate(snapshot, false, result).Occurrences[new(0, twoDrop.Identifier)];
        Assert.Contains("Deck need: strong", occurrence.DiagnosticText);
        Assert.Contains("\n  + Fills missing early plays", occurrence.DiagnosticText);
        Assert.Contains("contextual-pick-score-v1.6", occurrence.DiagnosticText);
        Assert.Contains($"tier {PickScoreTierThresholds.Describe(occurrence.PickScore!.Tier)} (pick-score-tiers-v2)", occurrence.DiagnosticText);
        // Phase 7: a set without its own supply calibration (WOE here) is labelled as using the provisional default.
        Assert.Contains("Supply profile: provisional-default (provisional: FRA-derived values", occurrence.DiagnosticText);
        Assert.False(occurrence.IsPickScoreEstimate);
    }
    [Fact] public void LateOffColourBombExplainsPlayabilitySeparatelyFromIntrinsicQuality()
    {
        // Expected (phase 6): a blue bomb late in an established red-green draft still reads as intrinsically excellent,
        // while a separate playability line and the diagnostics explain the reduced quality relevance. No badge lines.
        Card Make(string id, string cost, int mv) => new(CardIdentifier.Create(id), id, new(cost.Where("WUBRG".Contains).Select(c => (MagicColor)"WUBRG".IndexOf(c))),
            CardRarity.Common, CardSetCode.Create("WOE"), CollectorNumber.Create(id)) { GameplayMetadata = new(mv, cost, "Creature") };
        var pool = Enumerable.Range(0, 24).Select(i => Make($"p{i}", i % 2 == 0 ? "{2}{R}" : "{2}{G}", 3))
            .Concat(Enumerable.Range(0, 9).Select(i => Make($"o{i}", "{2}{" + "WUB"[i % 3] + "}", 3))).ToArray();
        var bomb = Make("blue-bomb", "{2}{U}", 3); var other = Make("other", "{2}{W}", 3);
        var snapshot = new DraftSnapshot(new(new(PackNumber.Create(3), PickNumber.Create(6)), [bomb.Identifier, other.Identifier]), new(), DraftFormat.BestOfOne)
            { RecoveredPool = new(pool.Select(c => c.Identifier)) };
        var all = pool.Append(bomb).Append(other).ToArray();
        var result = new LimitedStatisticsLoadResult(Context, Context, new(all.Select(c => new LimitedCardStatistics(c.Identifier, 10000,
            GameInHandWinRate: c == bomb ? .66 : .56, AverageLastSeenAt: 4))),
            LimitedStatisticsSource.Cache, null, null) { CardCatalog = new(all), EnvironmentCatalog = new([new(CardIdentifier.Create("env"), 100000, GameInHandWinRate: .56)]) };
        var occurrence = new LimitedStatisticsUpdate(snapshot, false, result).Occurrences[new(0, bomb.Identifier)];
        Assert.Contains("Intrinsic quality: excellent · Direct", occurrence.DiagnosticText);
        Assert.Contains("Playability: unlikely to make your current RG deck", occurrence.DiagnosticText);
        Assert.Contains("Quality raw ", occurrence.DiagnosticText); Assert.Contains("relevance R ", occurrence.DiagnosticText);
        Assert.InRange(int.Parse(occurrence.DisplayedPickScore), 10, 20);
    }
    [Fact] public void LoadingMissingAndUnavailableRemainExplicitWithoutFakeGih()
    {
        var loading = new LimitedStatisticsUpdate(Snapshot(), true, null);
        Assert.Equal("…", loading.Occurrences[new(0, A.Identifier)].DisplayedPickScore);
        Assert.Null(loading.PickScores);
        var missing = new LimitedStatisticsUpdate(Snapshot(), false, Result(true)).Occurrences[new(1, B.Identifier)];
        Assert.True(missing.IsPickScoreEstimate); Assert.Equal("—", missing.DisplayedGIH);
        Assert.Contains("EstimatedMissingStatistics", missing.DiagnosticText);
        var unavailable = new LimitedStatisticsUpdate(Snapshot(), false, Result() with { EnvironmentCatalog = new() });
        Assert.Equal("—", unavailable.Occurrences[new(0, A.Identifier)].DisplayedPickScore);
    }
    [Fact] public void StatisticsMappingIncludesRecoveredPoolWithoutInventingChronology()
    {
        var poolCard = B with { Identifier = CardIdentifier.Create("pool-only"), Name = "Recovered" };
        var snapshot = Snapshot() with { RecoveredPool = new([poolCard.Identifier, poolCard.Identifier]) };
        var mapping = LimitedStatisticsMapper.Map([new("Alpha", GameInHandWinRate: .6), new("Recovered", GameInHandWinRate: .57)],
            new([A, B, poolCard]), Context, snapshot);
        Assert.Equal(.57, mapping.Catalog.StatisticsFor(poolCard.Identifier)!.GameInHandWinRate);
        Assert.Empty(snapshot.History.Picks); Assert.Equal(2, snapshot.DraftedPool.Count);
    }
}
