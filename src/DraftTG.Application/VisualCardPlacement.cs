using System.Collections.ObjectModel;
using DraftTG.Domain;

namespace DraftTG.Application;

/// <summary>A verified visual order for one immutable pack. It never changes log or rank order.</summary>
public sealed class VisualCardPlacement
{
    private VisualCardPlacement(DraftPack pack, Dictionary<CardOccurrenceKey, int> slots)
    {
        Pack = pack;
        Slots = new ReadOnlyDictionary<CardOccurrenceKey, int>(slots);
    }
    public DraftPack Pack { get; }
    public IReadOnlyDictionary<CardOccurrenceKey, int> Slots { get; }

    public static bool TryCreate(DraftPack pack, IEnumerable<CardOccurrenceKey> confirmedVisualOrder,
        out VisualCardPlacement? placement, out string? diagnostic)
    {
        var order = confirmedVisualOrder.ToArray();
        var expected = pack.AvailableCardIdentifiers.Select((id, index) => new CardOccurrenceKey(index, id)).ToHashSet();
        placement = null;
        if (expected.Count is < 1 or > DraftCardLayout.MaximumCards || order.Length != expected.Count
            || order.Distinct().Count() != order.Length || !expected.SetEquals(order))
        {
            diagnostic = "Assign each current card once to a numbered visual slot. Badges remain hidden.";
            return false;
        }
        // Array order is explicit visual evidence supplied by the user, never provider/log/rank order.
        placement = new(pack, order.Select((key, visualSlot) => (key, visualSlot))
            .ToDictionary(item => item.key, item => item.visualSlot));
        diagnostic = null;
        return true;
    }
}
