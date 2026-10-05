using DraftTG.Domain;

namespace DraftTG.RecommendationEngine;

public sealed record DraftCardObservation(int PackIndex, CardIdentifier CardIdentifier, ColorSet? Colors,
    double? Phase8AdjustedValue, double? RawGamesInHandWinRate, int? GamesInHandSampleCount,
    double? AverageLastSeenAt = null);

public sealed class DraftPackObservation
{
    internal DraftPackObservation(DraftPack pack, IEnumerable<DraftCardObservation> cards,
        CardIdentifier? selectedCardIdentifier = null, bool hasStatisticsSnapshot = false, double? environmentBaseline = null)
    {
        Pack = pack;
        Cards = Array.AsReadOnly(cards.ToArray());
        SelectedCardIdentifier = selectedCardIdentifier;
        HasStatisticsSnapshot = hasStatisticsSnapshot;
        EnvironmentBaseline = environmentBaseline;
        MatchingSelectedPackIndexes = Array.AsReadOnly(Cards.Where(c => c.CardIdentifier == selectedCardIdentifier)
            .Select(c => c.PackIndex).ToArray());
    }
    public DraftPack Pack { get; }
    public DraftPosition Position => Pack.Position;
    public IReadOnlyList<DraftCardObservation> Cards { get; }
    public CardIdentifier? SelectedCardIdentifier { get; }
    public bool HasStatisticsSnapshot { get; }
    public double? EnvironmentBaseline { get; }
    // Arena identifies the selected card, not the slot of an indistinguishable duplicate.
    public IReadOnlyList<int> MatchingSelectedPackIndexes { get; }
    public int? SelectedPackIndex => MatchingSelectedPackIndexes.Count == 1 ? MatchingSelectedPackIndexes[0] : null;
}

/// <summary>Immutable, session-scoped observations, never an input to Phase 9A scoring.</summary>
public sealed class DraftPackObservationHistory
{
    public static DraftPackObservationHistory Empty { get; } = new([], new());
    private DraftPackObservationHistory(IEnumerable<DraftPackObservation> observations, DraftHistory history)
        : this(observations, history.Count, history.Picks.Count(p => observations.Any(o => o.Position == p.Position
            && o.SelectedCardIdentifier == p.SelectedCardIdentifier))) { }

    private DraftPackObservationHistory(IEnumerable<DraftPackObservation> observations, int knownCompletedPickCount,
        int observedCompletedPickCount)
    {
        Observations = Array.AsReadOnly(observations.ToArray());
        KnownCompletedPickCount = knownCompletedPickCount;
        ObservedCompletedPickCount = observedCompletedPickCount;
    }
    public IReadOnlyList<DraftPackObservation> Observations { get; }
    public DraftPosition? FirstObservedPosition => Observations.FirstOrDefault()?.Position;
    public bool StartsAtBeginning => FirstObservedPosition is { } position
        && position.Pack.Value == 1 && position.Pick.Value == 1;
    public int KnownCompletedPickCount { get; }
    public int ObservedCompletedPickCount { get; }
    public double? CompletedPickCoverage => KnownCompletedPickCount == 0 ? null
        : (double)ObservedCompletedPickCount / KnownCompletedPickCount;

    public DraftPackObservationHistory WithStatistics(DraftPack pack, CardCatalog catalog, DraftRecommendation statistical,
        LimitedCardStatisticsCatalog? statistics = null)
    {
        if (!Observations.Any(o => o.Pack.Equals(pack)))
            throw new ArgumentException("Statistics enrichment requires an already observed pack.", nameof(pack));
        var enriched = Observe(pack, catalog, new(), statistical, statistics);
        return new(enriched.Observations, KnownCompletedPickCount, ObservedCompletedPickCount);
    }

    public DraftPackObservationHistory Observe(DraftPack? pack, CardCatalog catalog, DraftHistory? history,
        DraftRecommendation? statistical = null, LimitedCardStatisticsCatalog? statistics = null)
    {
        if (statistical is not null && (pack is null
            || !pack.AvailableCardIdentifiers.SequenceEqual(statistical.Cards.Select(c => c.CardIdentifier))))
            throw new ArgumentException("Statistics must match the observed pack.", nameof(statistical));
        var observations = Observations.ToList();
        if (pack is not null)
        {
            var index = observations.FindIndex(o => o.Pack.Equals(pack));
            var prior = index < 0 ? null : observations[index];
            var cards = pack.AvailableCardIdentifiers.Select((id, slot) =>
            {
                var score = statistical?.Cards[slot];
                var old = prior?.Cards[slot];
                return new DraftCardObservation(slot, id, catalog.Find(id)?.Colors ?? old?.Colors,
                    statistical is not null ? score!.AdjustedValue : old?.Phase8AdjustedValue,
                    statistical is not null ? score!.RawGamesInHandWinRate : old?.RawGamesInHandWinRate,
                    statistical is not null ? score!.GamesInHandSampleCount : old?.GamesInHandSampleCount,
                    statistics is not null ? statistics.StatisticsFor(id)?.AverageLastSeenAt : old?.AverageLastSeenAt);
            });
            var observed = new DraftPackObservation(pack, cards, prior?.SelectedCardIdentifier,
                statistical is not null || prior?.HasStatisticsSnapshot == true,
                statistical is not null ? statistical.EnvironmentBaseline : prior?.EnvironmentBaseline);
            if (index < 0) observations.Add(observed); else observations[index] = observed;
        }
        for (var index = 0; index < observations.Count; index++)
        {
            var observed = observations[index];
            var selected = history?.Picks.FirstOrDefault(p => p.Position == observed.Position)?.SelectedCardIdentifier;
            if (selected is not null)
                observations[index] = new(observed.Pack, observed.Cards, selected, observed.HasStatisticsSnapshot,
                    observed.EnvironmentBaseline);
        }
        return history is null ? new(observations, KnownCompletedPickCount, ObservedCompletedPickCount)
            : new(observations, history);
    }
}
