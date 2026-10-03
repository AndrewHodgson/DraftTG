using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using DraftTG.ArenaIntegration;
using DraftTG.Data;
using DraftTG.Domain;

namespace DraftTG.Application;

/// <summary>Resolves Arena card identifiers from one immutable catalog import.</summary>
public sealed class ArenaCardResolver
{
    private readonly FrozenDictionary<int, CardIdentifier> _mappings;
    private readonly FrozenDictionary<int, IReadOnlyList<CardIdentifier>> _ambiguities;

    public ArenaCardResolver(ScryfallCardCatalogData catalogData)
    {
        ArgumentNullException.ThrowIfNull(catalogData);
        _mappings = catalogData.ArenaIdMappings.ToFrozenDictionary();
        _ambiguities = catalogData.AmbiguousArenaIds.ToFrozenDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<CardIdentifier>)Array.AsReadOnly(pair.Value.ToArray()));
    }

    public ArenaCardResolutionResult Resolve(ArenaCardIdentifier arenaIdentifier)
    {
        ArgumentNullException.ThrowIfNull(arenaIdentifier);
        if (_mappings.TryGetValue(arenaIdentifier.Value, out var cardIdentifier))
        {
            return ArenaCardResolutionResult.Resolved(cardIdentifier);
        }
        if (_ambiguities.TryGetValue(arenaIdentifier.Value, out var candidates))
        {
            return ArenaCardResolutionResult.Ambiguous(candidates);
        }
        return ArenaCardResolutionResult.Missing();
    }

    public bool TryResolve(
        ArenaCardIdentifier arenaIdentifier,
        [NotNullWhen(true)] out CardIdentifier? cardIdentifier)
    {
        var result = Resolve(arenaIdentifier);
        cardIdentifier = result.Status == ArenaCardResolutionStatus.Resolved
            ? result.CardIdentifier
            : null;
        return cardIdentifier is not null;
    }
}

public enum ArenaCardResolutionStatus
{
    Resolved,
    Missing,
    Ambiguous
}

public sealed class ArenaCardResolutionResult
{
    private static readonly IReadOnlyList<CardIdentifier> NoCandidates =
        Array.AsReadOnly(Array.Empty<CardIdentifier>());

    private ArenaCardResolutionResult(
        ArenaCardResolutionStatus status,
        CardIdentifier? cardIdentifier,
        IReadOnlyList<CardIdentifier> candidates)
    {
        Status = status;
        CardIdentifier = cardIdentifier;
        Candidates = candidates;
    }

    public ArenaCardResolutionStatus Status { get; }
    public CardIdentifier? CardIdentifier { get; }
    public IReadOnlyList<CardIdentifier> Candidates { get; }

    internal static ArenaCardResolutionResult Resolved(CardIdentifier cardIdentifier) =>
        new(ArenaCardResolutionStatus.Resolved, cardIdentifier, NoCandidates);

    internal static ArenaCardResolutionResult Missing() =>
        new(ArenaCardResolutionStatus.Missing, null, NoCandidates);

    internal static ArenaCardResolutionResult Ambiguous(
        IReadOnlyList<CardIdentifier> candidates) =>
        new(ArenaCardResolutionStatus.Ambiguous, null, candidates);
}
