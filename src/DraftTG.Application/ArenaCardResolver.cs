using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using DraftTG.ArenaIntegration;
using DraftTG.Data;
using DraftTG.Domain;

namespace DraftTG.Application;

/// <summary>
/// Resolves Arena card identifiers from one immutable catalog import. The Scryfall arena_id mapping is authoritative,
/// including its ambiguities. Only a Missing direct mapping may consult Arena's local database: its exact
/// ExpansionCode + CollectorNumber must match exactly one catalog printing whose name agrees with Arena's title.
/// </summary>
public sealed class ArenaCardResolver
{
    private readonly FrozenDictionary<int, CardIdentifier> _mappings;
    private readonly FrozenDictionary<int, IReadOnlyList<CardIdentifier>> _ambiguities;
    private readonly IArenaPrintingIdentitySource? _arenaDatabase;
    private readonly Lazy<ILookup<string, Card>>? _printings;

    public ArenaCardResolver(ScryfallCardCatalogData catalogData, IArenaPrintingIdentitySource? arenaDatabase = null)
    {
        ArgumentNullException.ThrowIfNull(catalogData);
        _mappings = catalogData.ArenaIdMappings.ToFrozenDictionary();
        _ambiguities = catalogData.AmbiguousArenaIds.ToFrozenDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<CardIdentifier>)Array.AsReadOnly(pair.Value.ToArray()));
        _arenaDatabase = arenaDatabase;
        if (arenaDatabase is not null)
            _printings = new(() => catalogData.Catalog.Cards
                .Select(card => (Key: ArenaPrintingKey.Create(card.SetCode.Value, card.CollectorNumber.Value), Card: card))
                .Where(p => p.Key is not null).ToLookup(p => p.Key!, p => p.Card, StringComparer.Ordinal));
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
        return _arenaDatabase is null ? ArenaCardResolutionResult.Missing() : ResolveFromArenaDatabase(arenaIdentifier.Value);
    }

    private ArenaCardResolutionResult ResolveFromArenaDatabase(int grpId)
    {
        if (_arenaDatabase!.Find(grpId) is not { } arena)
            return ArenaCardResolutionResult.Missing("Arena database has no row for this GrpId or is unavailable.");
        if (arena.IsToken || arena.IsRebalanced)
            return ArenaCardResolutionResult.Missing("Arena token or rebalanced card; no set/collector fallback.");
        if (ArenaPrintingKey.Create(arena.ExpansionCode, arena.CollectorNumber) is not { } key)
            return ArenaCardResolutionResult.Missing("Arena database row has no set code or collector number.");
        var matches = _printings!.Value[key].ToArray();
        if (matches.Length == 0)
            return ArenaCardResolutionResult.Missing($"No catalog printing for {arena.ExpansionCode} #{arena.CollectorNumber}.");
        if (matches.Length > 1)
            return ArenaCardResolutionResult.Ambiguous(Array.AsReadOnly(matches.Select(c => c.Identifier).ToArray()),
                ArenaCardResolutionSource.ArenaDatabaseSetCollector);
        // A set/collector hit is trusted only when Arena's own English title names the same card.
        if (arena.EnglishTitle is null || !ArenaPrintingKey.NameAgrees(arena.EnglishTitle, matches[0]))
            return ArenaCardResolutionResult.Missing(
                $"Name mismatch for {arena.ExpansionCode} #{arena.CollectorNumber}: Arena \"{arena.EnglishTitle}\" vs catalog \"{matches[0].Name}\".");
        return ArenaCardResolutionResult.Resolved(matches[0].Identifier, ArenaCardResolutionSource.ArenaDatabaseSetCollector);
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

/// <summary>Which evidence produced a resolution; None for a plain Missing result.</summary>
public enum ArenaCardResolutionSource
{
    None,
    ScryfallArenaId,
    ArenaDatabaseSetCollector
}

public sealed class ArenaCardResolutionResult
{
    private static readonly IReadOnlyList<CardIdentifier> NoCandidates =
        Array.AsReadOnly(Array.Empty<CardIdentifier>());

    private ArenaCardResolutionResult(
        ArenaCardResolutionStatus status,
        CardIdentifier? cardIdentifier,
        IReadOnlyList<CardIdentifier> candidates,
        ArenaCardResolutionSource source,
        string? diagnostic = null)
    {
        Status = status;
        CardIdentifier = cardIdentifier;
        Candidates = candidates;
        Source = source;
        Diagnostic = diagnostic;
    }

    public ArenaCardResolutionStatus Status { get; }
    public CardIdentifier? CardIdentifier { get; }
    public IReadOnlyList<CardIdentifier> Candidates { get; }
    public ArenaCardResolutionSource Source { get; }
    /// <summary>Why the Arena database fallback did not resolve, when it was consulted.</summary>
    public string? Diagnostic { get; }

    internal static ArenaCardResolutionResult Resolved(CardIdentifier cardIdentifier,
        ArenaCardResolutionSource source = ArenaCardResolutionSource.ScryfallArenaId) =>
        new(ArenaCardResolutionStatus.Resolved, cardIdentifier, NoCandidates, source);

    internal static ArenaCardResolutionResult Missing(string? diagnostic = null) =>
        new(ArenaCardResolutionStatus.Missing, null, NoCandidates, ArenaCardResolutionSource.None, diagnostic);

    internal static ArenaCardResolutionResult Ambiguous(
        IReadOnlyList<CardIdentifier> candidates,
        ArenaCardResolutionSource source = ArenaCardResolutionSource.ScryfallArenaId) =>
        new(ArenaCardResolutionStatus.Ambiguous, null, candidates, source);
}
