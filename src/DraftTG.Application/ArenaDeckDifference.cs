using DraftTG.Domain;
using DraftTG.RecommendationEngine;

namespace DraftTG.Application;

public enum ArenaDeckComparisonStatus { MatchesSuggestedDeck, NeedsChanges, CurrentDeckUnknown, SuggestedDeckUnavailable }
public sealed record CardDeckDifference(CardIdentifier CardIdentifier, int CurrentCount, int TargetCount)
{
    public int AddCount => Math.Max(TargetCount - CurrentCount, 0);
    public int RemoveCount => Math.Max(CurrentCount - TargetCount, 0);
}
public sealed record BasicLandDifference(BasicLandType Type, int CurrentCount, int TargetCount)
{
    public int Change => TargetCount - CurrentCount;
}

/// <summary>An immutable comparison target; no deck optimization or inferred Arena membership.</summary>
public sealed class ArenaDeckComparisonTarget
{
    public ArenaDeckComparisonTarget(SuggestedDeckId buildIdentity, string sessionIdentity,
        IEnumerable<ArenaDeckEntry> cards, IEnumerable<ArenaBasicLandEntry> basics)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(buildIdentity.Value); ArgumentException.ThrowIfNullOrWhiteSpace(sessionIdentity);
        var entries = cards.ToArray(); var lands = basics.ToArray();
        if (entries.Any(e => e.CardIdentifier is null || e.Count is < 1 or > 1000)
            || lands.Any(e => !Enum.IsDefined(e.Type) || e.Count is < 0 or > 1000)) throw new ArgumentOutOfRangeException(nameof(cards));
        BuildIdentity = buildIdentity; SessionIdentity = sessionIdentity;
        Cards = Array.AsReadOnly(entries.GroupBy(e => e.CardIdentifier).OrderBy(g => g.Key.Value, StringComparer.Ordinal)
            .Select(g => new ArenaDeckEntry(g.Key, checked(g.Sum(e => e.Count)))).ToArray());
        Basics = Array.AsReadOnly(lands.GroupBy(e => e.Type).OrderBy(g => g.Key)
            .Select(g => new ArenaBasicLandEntry(g.Key, checked(g.Sum(e => e.Count)))).Where(e => e.Count > 0).ToArray());
    }
    public SuggestedDeckId BuildIdentity { get; }
    public string SessionIdentity { get; }
    public IReadOnlyList<ArenaDeckEntry> Cards { get; }
    public IReadOnlyList<ArenaBasicLandEntry> Basics { get; }
    public int DeckSize => Cards.Sum(e => e.Count) + Basics.Sum(e => e.Count);
    public static ArenaDeckComparisonTarget? From(SuggestedDeckSet? set) => set?.Selected is { } build
        ? new(build.Id, set.SessionIdentity,
            build.Deck.Nonlands.Concat(build.Deck.NonbasicLands).Select(e => new ArenaDeckEntry(e.CardIdentifier, e.Count)),
            build.Deck.GeneratedBasics.Select(e => new ArenaBasicLandEntry(e.Type, e.Count))) : null;
}

/// <summary>Exact current-editor multiset difference. Unknown/stale/saved-only observations fail closed.</summary>
public sealed class ArenaDeckDifference
{
    private ArenaDeckDifference(ArenaDeckComparisonStatus status, ArenaDeckComparisonTarget? target, ArenaDeckSnapshot? current,
        IEnumerable<CardDeckDifference> cards, IEnumerable<BasicLandDifference> basics, string diagnostic)
    {
        Status = status; TargetBuildIdentity = target?.BuildIdentity; CurrentRevision = current?.Revision;
        Cards = Array.AsReadOnly(cards.ToArray()); BasicLands = Array.AsReadOnly(basics.ToArray());
        Add = Array.AsReadOnly(Cards.Where(c => c.AddCount > 0).ToArray()); Remove = Array.AsReadOnly(Cards.Where(c => c.RemoveCount > 0).ToArray());
        AlreadyCorrect = Array.AsReadOnly(Cards.Where(c => c.CurrentCount == c.TargetCount && c.TargetCount > 0).ToArray());
        TargetDeckSize = target?.DeckSize; CurrentDeckSize = current?.MainDeckCount; Diagnostic = diagnostic;
    }
    public ArenaDeckComparisonStatus Status { get; }
    public SuggestedDeckId? TargetBuildIdentity { get; }
    public ArenaDeckRevision? CurrentRevision { get; }
    public IReadOnlyList<CardDeckDifference> Cards { get; }
    public IReadOnlyList<CardDeckDifference> Add { get; }
    public IReadOnlyList<CardDeckDifference> Remove { get; }
    public IReadOnlyList<CardDeckDifference> AlreadyCorrect { get; }
    public IReadOnlyList<BasicLandDifference> BasicLands { get; }
    public int? TargetDeckSize { get; }
    public int? CurrentDeckSize { get; }
    public int AddCount => Add.Sum(e => e.AddCount);
    public int RemoveCount => Remove.Sum(e => e.RemoveCount);
    public string Diagnostic { get; }

    public static ArenaDeckDifference Compare(ArenaDeckSnapshot? current, ArenaDeckComparisonTarget? target)
    {
        if (target is null) return new(ArenaDeckComparisonStatus.SuggestedDeckUnavailable, null, current, [], [], "Selected suggested build unavailable.");
        if (current is null || !current.HasTrustworthyCurrentCounts || current.Revision.SessionIdentity != target.SessionIdentity)
            return new(ArenaDeckComparisonStatus.CurrentDeckUnknown, target, current, [], [],
                current?.Scope == ArenaDeckSnapshotScope.SavedDeck
                    ? "Saved deck counts do not establish the current unsaved editor contents."
                    : "Current deck counts or completed-session identity are not sufficiently trustworthy.");
        var before = current.MainDeck.ToDictionary(e => e.CardIdentifier, e => e.Count);
        var after = target.Cards.ToDictionary(e => e.CardIdentifier, e => e.Count);
        var cards = before.Keys.Concat(after.Keys).Distinct().OrderBy(id => id.Value, StringComparer.Ordinal)
            .Select(id => new CardDeckDifference(id, before.GetValueOrDefault(id), after.GetValueOrDefault(id))).ToArray();
        var basicBefore = current.Basics.ToDictionary(e => e.Type, e => e.Count);
        var basicAfter = target.Basics.ToDictionary(e => e.Type, e => e.Count);
        var basics = Enum.GetValues<BasicLandType>().Select(t => new BasicLandDifference(t, basicBefore.GetValueOrDefault(t), basicAfter.GetValueOrDefault(t))).ToArray();
        var matches = cards.All(c => c.AddCount == 0 && c.RemoveCount == 0) && basics.All(b => b.Change == 0);
        return new(matches ? ArenaDeckComparisonStatus.MatchesSuggestedDeck : ArenaDeckComparisonStatus.NeedsChanges,
            target, current, cards, basics, matches ? "Deck matches selected build." : "Exact copy changes against the selected build.");
    }
}
