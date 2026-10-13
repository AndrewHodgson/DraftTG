using DraftTG.ArenaIntegration;
using DraftTG.ArenaIntegration.Memory;

namespace DraftTG.Application;

public sealed record CaptureFreeReconciliation(string Status, bool CanScore, ArenaDraftStateSnapshot State, string Reason);

/// <summary>Memory owns offered membership/order; logs own durable picks/pool and format. No comparator.</summary>
public static class CaptureFreeDraftReconciler
{
    public static CaptureFreeReconciliation Reconcile(ArenaDraftMemorySnapshot memory, ArenaDraftStateSnapshot? log)
    {
        var empty = log is null ? new ArenaDraftStateSnapshot(ArenaDraftSessionStatus.Idle, null, null, null, null, new([]))
            : log with { CurrentPack = null };
        CaptureFreeReconciliation Decline(string status, string reason) => new(status, false, empty, reason);
        if (memory.Status != ArenaMemoryStatus.Success) return Decline("MemoryUnavailable", memory.Detail);
        if (log is null || log.Status != ArenaDraftSessionStatus.Active || log.Mode is null)
            return Decline("LogContextUnavailable", "Memory identities are current; durable draft history/format is not available.");
        if (memory.DraftSessionId is { } draft && log.DraftIdentifier is { } logDraft && draft != logDraft.Value)
            return Decline("SessionDisagreement", "Memory and Player.log identify different drafts.");
        if (memory.EventName is { Length: > 0 } eventName && log.EventName is { } logEvent && eventName != logEvent)
            return Decline("SessionDisagreement", "Memory and Player.log event names differ.");
        if (memory.PackNumber is not { } pack || memory.PickNumber is not { } pick)
            return Decline("MemoryCoordinateUnavailable", "No independently checked memory pack/pick coordinate; identities remain available in diagnostics, badges withheld.");
        var coordinate = ArenaDraftCoordinate.Create(pack, pick);
        if (log.CompletedPicks.Any(p => Compare(p.Coordinate, coordinate) >= 0))
            return Decline("PlayerLogAhead", "Pick already accepted in Player.log; outgoing memory pack withheld.");
        if (log.CurrentPack is { } current)
        {
            var comparison = Compare(current.Coordinate, coordinate);
            if (comparison > 0) return Decline("PlayerLogAhead", "Player.log is ahead of the memory scene.");
            if (comparison < 0) return Decline("MemoryAhead", "Memory pack is current; waiting for durable log history to catch up.");
            if (!current.CardIdentifiers.Select(c => (uint)c.Value).Order().SequenceEqual(memory.OrderedOfferedGrpIds.Order()))
                return Decline("PackSetDisagreement", "Memory/log multiset differs at the same coordinate; no badges.");
        }
        else return Decline("InTransition", "Log history has a pick/pack gap; memory identities retained but badges withheld.");
        var sessionId = log.DraftIdentifier ?? (memory.CourseId is { } course ? ArenaDraftIdentifier.Create("course:" + course) : null);
        var state = log with { DraftIdentifier = sessionId, CurrentPack = new(sessionId, coordinate,
            new(memory.OrderedOfferedGrpIds.Select(id => ArenaCardIdentifier.Create((int)id)))) };
        return new("PackSetAgreement", true, state, "Memory order retained exactly; history/pool/format retained from Player.log.");
    }
    private static int Compare(ArenaDraftCoordinate a, ArenaDraftCoordinate b) => a.Pack != b.Pack ? a.Pack.CompareTo(b.Pack) : a.Pick.CompareTo(b.Pick);
}
