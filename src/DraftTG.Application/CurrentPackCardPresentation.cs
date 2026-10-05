using System.Globalization;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;

namespace DraftTG.Application;

/// <summary>An occurrence is scoped to its owning immutable DraftPack, not a reusable UI slot.</summary>
public readonly record struct CardOccurrenceKey(int PackIndex, CardIdentifier CardIdentifier);

/// <summary>One immutable identity and all evidence displayed for that occurrence.</summary>
public sealed class CurrentPackCardPresentation
{
    public CurrentPackCardPresentation(CardOccurrenceKey key, Card? card = null,
        LimitedCardStatistics? rawStatistics = null, string? resolvedStatisticsName = null,
        CardRecommendation? statistical = null, ContextualCardRecommendation? pool = null,
        LaneCardRecommendation? lane = null, bool isLoading = false, ArchetypeCardRecommendation? archetype = null,
        TrophyCardRecommendation? trophy = null)
    {
        Key = key;
        Card = card;
        ResolvedStatisticsName = resolvedStatisticsName;
        IsIdentityConsistent = key.PackIndex >= 0 && (card is null || card.Identifier == key.CardIdentifier)
            && (rawStatistics is null || rawStatistics.CardIdentifier == key.CardIdentifier)
            && (statistical is null || new CardOccurrenceKey(statistical.PackIndex, statistical.CardIdentifier) == key)
            && (pool is null || new CardOccurrenceKey(pool.PackIndex, pool.CardIdentifier) == key)
            && (lane is null || new CardOccurrenceKey(lane.PackIndex, lane.CardIdentifier) == key)
            && (archetype is null || new CardOccurrenceKey(archetype.PackIndex, archetype.CardIdentifier) == key)
            && (trophy is null || new CardOccurrenceKey(trophy.PackIndex, trophy.CardIdentifier) == key)
            && (card is null || resolvedStatisticsName is null
                || string.Equals(card.Name, resolvedStatisticsName, StringComparison.Ordinal))
            && (pool is null || pool.StatisticalRecommendation == statistical)
            && (lane is null || lane.PoolRecommendation == pool)
            && (archetype is null || archetype.LaneRecommendation == lane)
            && (trophy is null || trophy.ArchetypeRecommendation == archetype);
        // Fail closed as a whole; a partially matched badge could suggest a wrong pick.
        if (IsIdentityConsistent)
        {
            RawStatistics = rawStatistics;
            Statistical = statistical;
            Pool = pool;
            Lane = lane;
            Archetype = archetype;
            Trophy = trophy;
        }
        IsLoading = isLoading;
        Statistics = IsIdentityConsistent && isLoading ? LimitedCardStatisticsPresentation.Loading
            : LimitedCardStatisticsPresentation.From(RawStatistics);
    }

    public CardOccurrenceKey Key { get; }
    public int PackIndex => Key.PackIndex;
    public CardIdentifier CardIdentifier => Key.CardIdentifier;
    public Card? Card { get; }
    public string CardName => Card?.Identifier == CardIdentifier ? Card.Name : "Unknown card";
    public string StatisticsLookupKey => CardName;
    public string? ResolvedStatisticsName { get; }
    public bool IsIdentityConsistent { get; }
    public bool IsLoading { get; }
    public LimitedCardStatistics? RawStatistics { get; }
    public LimitedCardStatisticsPresentation Statistics { get; }
    public CardRecommendation? Statistical { get; }
    public ContextualCardRecommendation? Pool { get; }
    public LaneCardRecommendation? Lane { get; }
    public ArchetypeCardRecommendation? Archetype { get; }
    public TrophyCardRecommendation? Trophy { get; }
    public double? RawGIH => RawStatistics?.GameInHandWinRate;
    public int? GIHSample => RawStatistics?.GameInHandGameCount;
    public double? ALSA => RawStatistics?.AverageLastSeenAt;
    public double? Phase8AdjustedValue => Statistical?.AdjustedValue;
    public int? StatsRank => Statistical?.StatisticalRank;
    public double? Phase9AColorAdjustment => Pool?.ColorAdjustment;
    public double? Phase9AValue => Pool?.ContextualValue;
    public int? PoolRank => Pool?.ContextualRank;
    public double? Phase9BLaneAdjustment => Lane?.LaneAdjustment;
    public double? Phase9BValue => Lane?.ContextualValue;
    public int? LaneRank => Lane?.ContextualRank;
    public double? Phase9CAdjustment => Archetype?.Affinity.Adjustment;
    public double? Phase9CValue => Archetype?.ContextualValue;
    public int? ArchetypeRank => Archetype?.ContextualRank;
    public double? Phase9DAdjustment => Trophy?.Adjustment;
    public double? FinalContextualValue => Trophy?.FinalValue ?? Phase9CValue ?? Phase9BValue;
    public int? ContextRank => Trophy?.FinalRank ?? ArchetypeRank ?? LaneRank ?? PoolRank ?? StatsRank;
    public bool IsContextPick => Trophy?.IsContextPick ?? Archetype?.IsTopContextualCandidate ?? Lane?.IsTopContextualCandidate
        ?? Pool?.IsTopContextualCandidate ?? Statistical?.IsTopStatisticalCandidate ?? false;
    public string DisplayedGIH => Statistics.GameInHand + (Statistics.IsLowSample ? "*" : "");
    public string DisplayedALSA => "ALSA " + Statistics.AverageLastSeen;
    public string DisplayedContextRank => ContextRank is { } rank ? $"#{rank}" : string.Empty;

    public CurrentPackCardPresentation WithCard(Card? card) => IsIdentityConsistent
        ? new(Key, card, RawStatistics, ResolvedStatisticsName, Statistical, Pool, Lane, IsLoading, Archetype, Trophy)
        : this;

    public string DiagnosticText => string.Create(CultureInfo.InvariantCulture,
        $"Slot: {PackIndex}; CardIdentifier: {CardIdentifier.Value}; Card: {CardName}\n"
        + $"Stats lookup: {StatisticsLookupKey}; Stats resolved: {ResolvedStatisticsName ?? "unavailable"}; Identity: {(IsIdentityConsistent ? "consistent" : "REJECTED")}\n"
        + $"GIH: {RawGIH}; GIH n: {GIHSample}; ALSA: {ALSA}\n"
        + $"Phase 8: {Phase8AdjustedValue}; Stats rank: {StatsRank}\n"
        + $"Phase 9A adjustment: {Phase9AColorAdjustment}; Pool value: {Phase9AValue}; Pool rank: {PoolRank}\n"
        + $"Phase 9B adjustment: {Phase9BLaneAdjustment}; Lane value: {Phase9BValue}; Lane rank: {LaneRank}\n"
        + $"Phase 9C adjustment: {Phase9CAdjustment}; Archetype value: {Phase9CValue}; Archetype rank: {ArchetypeRank}\n"
        + $"Phase 9D adjustment: {Phase9DAdjustment}; Final value: {FinalContextualValue}; Context rank: {ContextRank}");
}
