using DraftTG.ArenaIntegration;
using DraftTG.Domain;

namespace DraftTG.Application;

/// <summary>Adapts the already-merged state inventory without adding exact history a second time.</summary>
internal static class ArenaDraftPoolAssembler
{
    internal static DraftPoolSnapshot? Assemble(ArenaDraftStateSnapshot state, ArenaCardResolver resolver)
    {
        if (state.Status == ArenaDraftSessionStatus.Idle) return null;
        var cards = new List<CardIdentifier>();
        var unresolved = 0;
        foreach (var id in state.DraftedPool.Occurrences)
        {
            var resolution = resolver.Resolve(id);
            if (resolution.Status == ArenaCardResolutionStatus.Resolved) cards.Add(resolution.CardIdentifier!);
            else unresolved++;
        }
        var completeness = DetermineCompleteness(state);
        if (unresolved > 0 && completeness == DraftPoolCompleteness.Complete) completeness = DraftPoolCompleteness.Partial;
        return new(new(cards), completeness, unresolved);
    }

    private static DraftPoolCompleteness DetermineCompleteness(ArenaDraftStateSnapshot state)
    {
        if (state.PickedCardsDiagnostic is not null || state.Mode?.Kind is not
            (ArenaDraftModeKind.Quick or ArenaDraftModeKind.Premier or ArenaDraftModeKind.Traditional))
            return DraftPoolCompleteness.Unknown;
        var coordinates = state.CompletedPicks.Select(p => p.Coordinate).ToArray();
        if (coordinates.Any(c => !ArenaQuickDraftCoordinates.IsSupported(c)) ||
            state.CompletedPicks.Any(p => p.CardIdentifiers.Count != 1)) return DraftPoolCompleteness.Unknown;
        var indices = coordinates.Select(ArenaQuickDraftCoordinates.CompletedPicksBefore).Order().ToArray();
        int expected;
        if (state.Status == ArenaDraftSessionStatus.Completed)
            expected = ArenaQuickDraftCoordinates.PackCount * ArenaQuickDraftCoordinates.PicksPerPack;
        else if (state.CurrentPack is { } pack)
        {
            if (!ArenaQuickDraftCoordinates.IsSupported(pack.Coordinate)) return DraftPoolCompleteness.Unknown;
            expected = ArenaQuickDraftCoordinates.CompletedPicksBefore(pack.Coordinate);
        }
        else if (indices.Length > 0)
            expected = Math.Max(indices[^1] + 1, state.RecoveredPool?.Cards.Count ?? 0);
        else if (state.RecoveredPool?.CurrentCoordinate is { } current && ArenaQuickDraftCoordinates.IsSupported(current))
            expected = ArenaQuickDraftCoordinates.CompletedPicksBefore(current);
        else
            return DraftPoolCompleteness.Unknown;

        // An accepted Quick Draft snapshot establishes all current counts even without chronology.
        var recovered = state.RecoveredPool;
        var recoveredValidated = recovered is not null &&
            (recovered.CurrentCoordinate is { } c
                ? ArenaQuickDraftCoordinates.IsSupported(c) && recovered.Cards.Count == ArenaQuickDraftCoordinates.CompletedPicksBefore(c)
                : state.Status == ArenaDraftSessionStatus.Completed && recovered.Cards.Count == expected);
        var contiguousExact = indices.SequenceEqual(Enumerable.Range(0, expected));
        return state.DraftedPool.Count == expected && (recoveredValidated || contiguousExact)
            ? DraftPoolCompleteness.Complete : DraftPoolCompleteness.Partial;
    }
}
