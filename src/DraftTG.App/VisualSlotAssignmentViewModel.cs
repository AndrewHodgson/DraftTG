using DraftTG.Application;
using DraftTG.Domain;

namespace DraftTG.App;

/// <summary>Reference identity rejects callbacks from an earlier pack, even if pack values match.</summary>
public sealed record VisualPlacementContext(DraftPack Pack, long Generation)
{
    public DateTimeOffset ChangedAt { get; init; } = DateTimeOffset.UtcNow;
}
public sealed record VisualCardChoice(CardOccurrenceKey Key, string Label);

public sealed class VisualSlotAssignmentViewModel(int visualSlot, IReadOnlyList<VisualCardChoice> choices,
    Action selectionChanged) : PresentationModel
{
    private VisualCardChoice? _selectedCard;
    public int VisualSlot { get; } = visualSlot;
    public string Label => $"Slot {VisualSlot + 1}";
    public IReadOnlyList<VisualCardChoice> Choices { get; } = choices;
    public VisualCardChoice? SelectedCard
    {
        get => _selectedCard;
        set
        {
            if (_selectedCard == value) return;
            _selectedCard = value;
            Notify();
            selectionChanged();
        }
    }
    internal void SelectWithoutInvalidating(VisualCardChoice? card)
    {
        _selectedCard = card;
        Notify(nameof(SelectedCard));
    }
}
