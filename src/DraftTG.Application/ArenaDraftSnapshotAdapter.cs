using DraftTG.ArenaIntegration;
using DraftTG.Domain;

namespace DraftTG.Application;

/// <summary>Converts complete, supported Arena state into a provider-neutral draft snapshot.</summary>
public sealed class ArenaDraftSnapshotAdapter(ArenaCardResolver resolver)
{
    private readonly ArenaCardResolver _resolver =
        resolver ?? throw new ArgumentNullException(nameof(resolver));

    public ArenaDraftSnapshotResult Convert(ArenaDraftStateSnapshot state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var result = ConvertSnapshot(state);
        return result with
        {
            ResolvedHistory = result.Snapshot?.History ?? ResolveCompletedHistory(state),
            ResolvedDraftedPool = result.Snapshot?.DraftedPool ?? ResolveDraftedPool(state),
            DraftPool = ArenaDraftPoolAssembler.Assemble(state, _resolver),
            PoolIdentity = ArenaDraftPoolAssembler.Summarize(state, _resolver)
        };
    }

    private DraftedCardPool? ResolveDraftedPool(ArenaDraftStateSnapshot state)
    {
        var cards = new List<CardIdentifier>();
        foreach (var id in state.DraftedPool.Occurrences)
        {
            var resolution = _resolver.Resolve(id);
            if (resolution.Status != ArenaCardResolutionStatus.Resolved) return null;
            cards.Add(resolution.CardIdentifier!);
        }
        return new(cards);
    }

    private DraftHistory? ResolveCompletedHistory(ArenaDraftStateSnapshot state)
    {
        var picks = new List<DraftPick>();
        foreach (var pick in state.CompletedPicks)
        {
            if (!TryCreatePosition(pick.Coordinate, out var position) || pick.CardIdentifiers.Count != 1) return null;
            var resolution = _resolver.Resolve(pick.CardIdentifiers[0]);
            if (resolution.Status != ArenaCardResolutionStatus.Resolved) return null;
            picks.Add(new(position!, resolution.CardIdentifier!));
        }
        return new(picks);
    }

    private ArenaDraftSnapshotResult ConvertSnapshot(ArenaDraftStateSnapshot state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.Status == ArenaDraftSessionStatus.Idle)
        {
            return ArenaDraftSnapshotResult.Unavailable(DraftSnapshotAvailability.Idle);
        }
        if (state.Status == ArenaDraftSessionStatus.Completed)
        {
            return ArenaDraftSnapshotResult.Unavailable(DraftSnapshotAvailability.Completed);
        }
        if (state.CurrentPack is null)
        {
            return ArenaDraftSnapshotResult.Unavailable(DraftSnapshotAvailability.NoCurrentPack);
        }
        if (!TryMapFormat(state.Mode, out var format))
        {
            return ArenaDraftSnapshotResult.Unavailable(DraftSnapshotAvailability.UnsupportedDraftMode);
        }
        if (!TryCreatePosition(state.CurrentPack.Coordinate, out var currentPosition))
        {
            return ArenaDraftSnapshotResult.Unavailable(DraftSnapshotAvailability.UnsupportedCoordinate);
        }

        var historicalPositions = new DraftPosition[state.CompletedPicks.Count];
        for (var index = 0; index < state.CompletedPicks.Count; index++)
        {
            var pick = state.CompletedPicks[index];
            if (!TryCreatePosition(pick.Coordinate, out var position))
            {
                return ArenaDraftSnapshotResult.Unavailable(DraftSnapshotAvailability.UnsupportedCoordinate);
            }
            if (pick.CardIdentifiers.Count != 1)
            {
                return ArenaDraftSnapshotResult.Unavailable(DraftSnapshotAvailability.UnsupportedMultiCardPick);
            }
            historicalPositions[index] = position!;
        }

        var unresolved = new List<ArenaCardIdentifier>();
        var unresolvedSet = new HashSet<ArenaCardIdentifier>();
        var ambiguous = new List<ArenaCardIdentifier>();
        var ambiguousSet = new HashSet<ArenaCardIdentifier>();
        var currentCards = ResolveAll(
            state.CurrentPack.CardIdentifiers,
            unresolved,
            unresolvedSet,
            ambiguous,
            ambiguousSet);
        var historicalCards = new CardIdentifier?[state.CompletedPicks.Count];

        for (var index = 0; index < state.CompletedPicks.Count; index++)
        {
            var arenaIdentifier = state.CompletedPicks[index].CardIdentifiers[0];
            historicalCards[index] = Resolve(
                arenaIdentifier,
                unresolved,
                unresolvedSet,
                ambiguous,
                ambiguousSet);
        }
        var poolCards = state.RecoveredPool is null ? null : ResolveAll(state.DraftedPool.Occurrences,
            unresolved, unresolvedSet, ambiguous, ambiguousSet);

        // Ambiguity takes presentation precedence because it is a known
        // one-to-many mapping, while both diagnostic lists remain available.
        if (ambiguous.Count > 0)
        {
            return ArenaDraftSnapshotResult.Unavailable(
                DraftSnapshotAvailability.AmbiguousArenaCard,
                unresolved,
                ambiguous);
        }

        if (unresolved.Count > 0)
        {
            return ArenaDraftSnapshotResult.Unavailable(
                DraftSnapshotAvailability.UnresolvedArenaCard,
                unresolved);
        }

        var history = state.CompletedPicks.Select((_, index) =>
            new DraftPick(historicalPositions[index], historicalCards[index]!));
        var snapshot = new DraftSnapshot(
            new DraftPack(currentPosition!, currentCards!),
            new DraftHistory(history),
            format)
        { RecoveredPool = poolCards is null ? null : new DraftedCardPool(poolCards.Select(c => c!)) };
        return ArenaDraftSnapshotResult.Ready(snapshot);
    }

    private CardIdentifier?[] ResolveAll(
        IEnumerable<ArenaCardIdentifier> arenaIdentifiers,
        ICollection<ArenaCardIdentifier> unresolved,
        ISet<ArenaCardIdentifier> unresolvedSet,
        ICollection<ArenaCardIdentifier> ambiguous,
        ISet<ArenaCardIdentifier> ambiguousSet)
    {
        return arenaIdentifiers.Select(arenaIdentifier => Resolve(
            arenaIdentifier,
            unresolved,
            unresolvedSet,
            ambiguous,
            ambiguousSet)).ToArray();
    }

    private CardIdentifier? Resolve(
        ArenaCardIdentifier arenaIdentifier,
        ICollection<ArenaCardIdentifier> unresolved,
        ISet<ArenaCardIdentifier> unresolvedSet,
        ICollection<ArenaCardIdentifier> ambiguous,
        ISet<ArenaCardIdentifier> ambiguousSet)
    {
        var resolution = _resolver.Resolve(arenaIdentifier);
        switch (resolution.Status)
        {
            case ArenaCardResolutionStatus.Resolved:
                return resolution.CardIdentifier;
            case ArenaCardResolutionStatus.Missing when unresolvedSet.Add(arenaIdentifier):
                unresolved.Add(arenaIdentifier);
                return null;
            case ArenaCardResolutionStatus.Ambiguous when ambiguousSet.Add(arenaIdentifier):
                ambiguous.Add(arenaIdentifier);
                return null;
            case ArenaCardResolutionStatus.Missing:
            case ArenaCardResolutionStatus.Ambiguous:
                return null;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private static bool TryMapFormat(ArenaDraftMode? mode, out DraftFormat format)
    {
        switch (mode?.Kind)
        {
            case ArenaDraftModeKind.Premier:
            case ArenaDraftModeKind.Quick:
                format = DraftFormat.BestOfOne;
                return true;
            case ArenaDraftModeKind.Traditional:
                format = DraftFormat.BestOfThree;
                return true;
            default:
                format = default;
                return false;
        }
    }

    private static bool TryCreatePosition(
        ArenaDraftCoordinate coordinate,
        out DraftPosition? position)
    {
        if (PackNumber.TryCreate(coordinate.Pack, out var pack)
            && PickNumber.TryCreate(coordinate.Pick, out var pick))
        {
            position = new DraftPosition(pack!, pick!);
            return true;
        }

        position = null;
        return false;
    }
}
