using System.ComponentModel;
using System.Runtime.CompilerServices;
using DraftTG.Application;
using DraftTG.RecommendationEngine;

namespace DraftTG.App;

public abstract class PresentationModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed class CardBadgeViewModel : PresentationModel
{
    public CardBadgeViewModel(CurrentPackCardPresentation presentation)
    {
        Presentation = presentation;
        Index = presentation.PackIndex;
        OccurrenceKey = presentation.Key;
    }
    // Retained for standalone formatter callers/tests. The passive overlay never uses this path.
    internal CardBadgeViewModel(int index, CurrentPackCardViewModel card)
    {
        Index = index;
        _legacyCard = card;
        Presentation = card.Presentation;
        OccurrenceKey = card.OccurrenceKey;
    }
    public int Index { get; }
    public int? VisualSlotIndex { get; private set; }
    public bool IsPlaced => VisualSlotIndex is not null && Presentation?.IsIdentityConsistent == true;
    private CurrentPackCardViewModel? _legacyCard;
    public CardOccurrenceKey? OccurrenceKey { get; }
    public CurrentPackCardPresentation? Presentation { get; private set; }
    public string Name => Presentation?.CardName ?? _legacyCard!.Name;
    private LimitedCardStatisticsPresentation Statistics => Presentation?.Statistics ?? _legacyCard!.Statistics;
    private CardRecommendation? Recommendation => Presentation is { } p ? p.Statistical : _legacyCard!.Recommendation;
    public string WinRate => Presentation?.DisplayedGIH ?? Statistics.GameInHand + (Statistics.IsLowSample ? "*" : "");
    public string Rank => Presentation?.DisplayedContextRank
        ?? ((_legacyCard!.LaneRecommendation?.ContextualRank ?? _legacyCard.ContextualRecommendation?.ContextualRank
            ?? Recommendation?.StatisticalRank) is { } rank ? $"#{rank}" : string.Empty);
    public bool IsStatisticalPick => Recommendation?.IsTopStatisticalCandidate == true;
    public bool IsContextPick => Presentation?.IsContextPick ?? _legacyCard!.LaneRecommendation?.IsTopContextualCandidate
        ?? _legacyCard.ContextualRecommendation?.IsTopContextualCandidate ?? IsStatisticalPick;
    public string BorderColor => IsContextPick ? "#FFE2BE64" : "#886D798B";
    public string RankColor => IsContextPick ? "#FFFFD57D" : "#FFB9C6D8";
    public string Secondary => Presentation?.DisplayedALSA ?? "ALSA " + Statistics.AverageLastSeen;
    public string Detail => $"{Name} · {Statistics.Summary}";
    public double X { get; private set; }
    public double Y { get; private set; }
    public double Width { get; private set; }
    internal void UpdateStatistics(CurrentPackCardViewModel row)
    {
        // A slot cannot accept another occurrence, even if its display index overlaps.
        if (row.OccurrenceKey != OccurrenceKey || !string.Equals(row.Name, Name, StringComparison.Ordinal)
            || row.Presentation is { IsIdentityConsistent: false }
            || (row.Presentation is { } occurrence && row.Name != occurrence.CardName)) return;
        if (row.Presentation is { } model) { UpdatePresentation(model); return; }
        _legacyCard = row;
        NotifyValues();
    }
    internal void UpdatePresentation(CurrentPackCardPresentation model)
    {
        if (model.Key != OccurrenceKey || !model.IsIdentityConsistent || model.CardName != Name) return;
        Presentation = model;
        Notify(nameof(Presentation)); // Compiled nested XAML bindings reconnect to the whole immutable item.
        NotifyValues();
    }
    private void NotifyValues()
    {
        Notify(nameof(WinRate)); Notify(nameof(Secondary)); Notify(nameof(Detail));
        Notify(nameof(Rank)); Notify(nameof(IsStatisticalPick)); Notify(nameof(BorderColor)); Notify(nameof(RankColor));
        Notify(nameof(IsContextPick));
    }
    public void Place(CardSlot slot, double width, double height)
    {
        VisualSlotIndex = slot.Index;
        var bounds = slot.Scale(width, height);
        Width = Math.Min(86, bounds.Width);
        X = bounds.X + (bounds.Width - Width) / 2;
        Y = Math.Max(bounds.Y, bounds.Y + bounds.Height - 44);
        Notify(nameof(X)); Notify(nameof(Y)); Notify(nameof(Width)); Notify(nameof(VisualSlotIndex)); Notify(nameof(IsPlaced));
    }
    internal void ClearPlacement()
    {
        VisualSlotIndex = null;
        X = 0; Y = 0; Width = 0;
        Notify(nameof(X)); Notify(nameof(Y)); Notify(nameof(Width)); Notify(nameof(VisualSlotIndex)); Notify(nameof(IsPlaced));
    }
}

public sealed record CardSlotGuide(double X, double Y, double Width, double Height, string Label);
public enum RailPanel { None, Status, History, Calibration }

/// <summary>Two surfaces, one existing runtime owner. Contains presentation and calibration only.</summary>
public sealed class OverlayViewModel : PresentationModel, IDisposable
{
    private double _width = 1000;
    private double _height = 500;
    private bool _statisticsVisible = true;
    private bool _isCalibrating;
    private bool _hasCalibration;
    private string _overlayDiagnostic = string.Empty;
    private RailPanel _panel;
    private DraftCardLayout _layout = DraftCardLayout.Grid(7);
    private VisualCardPlacement? _visualPlacement;
    private CardVisualLocalizationResult? _automaticPlacement;
    private long _visualGeneration;
    private string? _placementError;
    public OverlayViewModel(MainWindowViewModel session)
    {
        Session = session;
        session.PackPresentationChanged += RefreshPack;
        session.PropertyChanged += SessionChanged;
        RefreshPack(true);
    }
    public MainWindowViewModel Session { get; }
    public IReadOnlyList<CardBadgeViewModel> Badges { get; private set; } = [];
    public string BadgeBindingDiagnosticsText { get; private set; } = string.Empty;
    public VisualPlacementContext? PlacementContext { get; private set; }
    public IReadOnlyList<VisualSlotAssignmentViewModel> VisualSlots { get; private set; } = [];
    public bool HasVisualSlots => VisualSlots.Count > 0;
    public bool CanConfirmVisualPlacement => HasVisualSlots && VisualSlots.All(slot => slot.SelectedCard is not null);
    public bool HasConfirmedVisualPlacement => _visualPlacement is not null;
    public bool HasAutomaticVisualPlacement => _automaticPlacement?.Matches.Count == Badges.Count && Badges.Count > 0;
    public bool HasManualVisualEdits { get; private set; }
    public string LocalizationStatus { get; private set; } = "Card placement: Waiting";
    public string LocalizationDiagnostics { get; private set; } = string.Empty;
    public string CaptureRuntimeDiagnosticsText { get; private set; } = "Capture runtime has not started.";
    internal void SetCaptureRuntimeDiagnostics(string text)
    {
        CaptureRuntimeDiagnosticsText = text;
        Notify(nameof(CaptureRuntimeDiagnosticsText));
    }
    public string PlacementDiagnostic => !HasVisualSlots ? string.Empty : _placementError
        ?? (_automaticPlacement is { } auto ? $"Card placement: {auto.Matches.Count} / {Badges.Count}. Resolve remaining positions if needed." : null)
        ?? (_visualPlacement is null ? "Card positions unconfirmed. Match cards to numbered slots in the status panel; badges are hidden."
            : "Card positions confirmed for this pack. Confirm again after the next pick.");
    public IReadOnlyList<CardSlotGuide> Guides { get; private set; } = [];
    public DraftCardLayout Layout => _layout;
    public bool IsCalibrating => _isCalibrating;
    public bool HasCalibration => _hasCalibration;
    public bool NeedsCalibration => !_hasCalibration;
    public bool StatisticsVisible => _statisticsVisible;
    public bool ShowBadges => (_hasCalibration || _isCalibrating) && _statisticsVisible && Badges.Count > 0;
    public string VisibilityLabel => _statisticsVisible ? "Hide statistics" : "Show statistics";
    public string CalibrationLabel => _isCalibrating ? "Editing overlay" : _hasCalibration ? "Edit overlay position" : "Set overlay position";
    public RailPanel Panel => _panel;
    public bool IsStatusPanel => _panel == RailPanel.Status;
    public bool IsHistoryPanel => _panel == RailPanel.History;
    public bool IsCalibrationPanel => _isCalibrating && _panel == RailPanel.Calibration;
    public bool HasWarning => Diagnostics.Length > 0;
    public string Diagnostics => string.Join("\n\n", new[] { Session.DiagnosticText, Session.CardDataWarningText,
        Session.StatisticsDiagnosticText, _overlayDiagnostic,
        HasConfirmedVisualPlacement || HasAutomaticVisualPlacement ? null : PlacementDiagnostic }.Where(text => !string.IsNullOrWhiteSpace(text)));
    public string CompactStatus => Session.StatusText.StartsWith("Unable", StringComparison.Ordinal) ? "Error"
        : Session.StatusText == "Draft complete" ? "Done"
        : Session.ModeText.StartsWith("Quick", StringComparison.Ordinal) ? "Quick"
        : Session.ModeText.StartsWith("Premier", StringComparison.Ordinal) ? "Prem."
        : Session.ModeText.StartsWith("Traditional", StringComparison.Ordinal) ? "Trad."
        : Session.StatusText.Contains("Loading", StringComparison.Ordinal) ? "Load" : "Wait";

    public void SetPanel(RailPanel panel)
    {
        _panel = panel;
        Notify(nameof(Panel)); Notify(nameof(IsStatusPanel)); Notify(nameof(IsHistoryPanel)); Notify(nameof(IsCalibrationPanel));
    }
    public void ToggleStatistics()
    {
        _statisticsVisible = !_statisticsVisible;
        Notify(nameof(StatisticsVisible)); Notify(nameof(ShowBadges)); Notify(nameof(VisibilityLabel));
    }
    public void SetCalibrationState(bool editing, bool valid)
    {
        _isCalibrating = editing; _hasCalibration = valid;
        if (!editing && _panel == RailPanel.Calibration) SetPanel(RailPanel.None);
        PlaceAll();
        Notify(nameof(IsCalibrationPanel));
        Notify(nameof(IsCalibrating)); Notify(nameof(HasCalibration)); Notify(nameof(NeedsCalibration));
        Notify(nameof(ShowBadges)); Notify(nameof(CalibrationLabel));
    }
    public void SetDiagnostic(string? message)
    {
        _overlayDiagnostic = message ?? string.Empty;
        Notify(nameof(Diagnostics)); Notify(nameof(HasWarning));
    }
    public void SetLayout(DraftCardLayout layout)
    {
        _layout = layout;
        PlaceAll();
    }
    public void SetViewport(double width, double height)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0) return;
        _width = width; _height = height;
        PlaceAll();
    }
    public void SetLocalizationStatus(string status, string? diagnostic = null)
    {
        LocalizationStatus = status;
        LocalizationDiagnostics = diagnostic ?? string.Empty;
        Notify(nameof(LocalizationStatus)); Notify(nameof(LocalizationDiagnostics));
    }
    public bool ApplyAutomaticLocalization(VisualPlacementContext context, CardVisualLocalizationResult result)
    {
        if (!ReferenceEquals(context, PlacementContext) || _visualPlacement is not null || HasManualVisualEdits
            || !result.IsSafeFor(new(context.Pack, Session.CurrentPackPresentations, _layout) { PackGeneration = context.Generation })) return false;
        _automaticPlacement = result with { Matches = Array.AsReadOnly(result.Matches.ToArray()),
            Rectangles = Array.AsReadOnly(result.Rectangles.ToArray()) };
        _placementError = null;
        foreach (var row in VisualSlots)
        {
            var match = result.Matches.SingleOrDefault(m => m.VisualSlot == row.VisualSlot);
            row.SelectWithoutInvalidating(match is null ? null : row.Choices.Single(c => c.Key == match.Key));
        }
        SetLocalizationStatus(result.Matches.Count == Badges.Count ? $"Card placement: Automatic ({Badges.Count} / {Badges.Count})"
            : $"Card placement: {result.Matches.Count} / {Badges.Count}; {Badges.Count - result.Matches.Count} need confirmation",
            $"Method: {result.Method}; Capture: {result.CaptureWidth} × {result.CaptureHeight}; Rectangles: {result.Rectangles.Count}; "
            + $"Candidates: {Badges.Count}; Matched: {result.Matches.Count}; Duration: {result.Duration.TotalMilliseconds:F0} ms\n"
            + string.Join("\n", result.Matches.Select(m => $"Slot {m.VisualSlot + 1}: {Badges.Single(b => b.OccurrenceKey == m.Key).Name}; similarity {m.Confidence:F3}; {m.State}"))
            + (result.Diagnostic is null ? "" : "\n" + result.Diagnostic));
        PlaceAll(); NotifyPlacement();
        return true;
    }
    public void InvalidateAutomaticLocalization(string diagnostic, bool invalidateManual = false)
    {
        _automaticPlacement = null;
        if (invalidateManual)
        {
            _visualPlacement = null; HasManualVisualEdits = false;
            foreach (var row in VisualSlots) row.SelectWithoutInvalidating(null);
        }
        SetLocalizationStatus(_visualPlacement is null ? "Card placement: Waiting" : "Card placement: Confirmed", diagnostic);
        PlaceAll(); NotifyPlacement();
    }
    public bool ConfirmVisualOrder(VisualPlacementContext context, IEnumerable<CardOccurrenceKey> visualOrder)
    {
        // A stale UI selection or callback cannot attach a map to the next pack or another session.
        if (!ReferenceEquals(context, PlacementContext) || !Equals(context.Pack, Session.CurrentPackIdentity)) return false;
        if (!VisualCardPlacement.TryCreate(context.Pack, visualOrder, out var placement, out var error))
        {
            _visualPlacement = null;
            _automaticPlacement = null;
            _placementError = error;
            PlaceAll(); NotifyPlacement();
            return false;
        }
        _visualPlacement = placement;
        _placementError = null;
        foreach (var row in VisualSlots)
            row.SelectWithoutInvalidating(row.Choices.Single(c => placement!.Slots[c.Key] == row.VisualSlot));
        PlaceAll(); NotifyPlacement();
        SetLocalizationStatus($"Card placement: Confirmed ({Badges.Count} / {Badges.Count})");
        return true;
    }
    public bool ConfirmVisualSelections() => PlacementContext is { } context && CanConfirmVisualPlacement
        && ConfirmVisualOrder(context, VisualSlots.Select(slot => slot.SelectedCard!.Key));
    private void ResetVisualPlacement()
    {
        _visualPlacement = null;
        _automaticPlacement = null;
        HasManualVisualEdits = false;
        _placementError = null;
        var context = Session.CurrentPackIdentity is { } pack ? new VisualPlacementContext(pack, ++_visualGeneration) : null;
        PlacementContext = context;
        var counts = Badges.GroupBy(b => b.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var copies = new Dictionary<string, int>(StringComparer.Ordinal);
        var choices = Array.AsReadOnly(Badges.Select(b =>
        {
            var copy = copies[b.Name] = copies.GetValueOrDefault(b.Name) + 1;
            return new VisualCardChoice(b.Presentation!.Key, b.Name + (counts[b.Name] > 1 ? $" (copy {copy})" : ""));
        }).ToArray());
        VisualSlots = context is null ? [] : Array.AsReadOnly(Enumerable.Range(0, Badges.Count)
            .Select(index => new VisualSlotAssignmentViewModel(index, choices, () =>
            {
                if (!ReferenceEquals(context, PlacementContext)) return;
                _visualPlacement = null; _placementError = null;
                HasManualVisualEdits = true;
                SetLocalizationStatus("Card placement: Manual confirmation needed");
                PlaceAll(); NotifyPlacement();
            })).ToArray());
        SetLocalizationStatus("Card placement: Waiting");
        Notify(nameof(PlacementContext)); Notify(nameof(VisualSlots)); NotifyPlacement();
    }
    private void NotifyPlacement()
    {
        Notify(nameof(HasVisualSlots)); Notify(nameof(CanConfirmVisualPlacement)); Notify(nameof(HasConfirmedVisualPlacement));
        Notify(nameof(PlacementDiagnostic)); Notify(nameof(Diagnostics)); Notify(nameof(HasWarning));
        Notify(nameof(HasAutomaticVisualPlacement));
        Notify(nameof(HasManualVisualEdits));
    }
    private void RefreshPack(bool newPack)
    {
        // This is the exact immutable snapshot that supplies rail names. Never read CurrentPackCards.
        var existing = newPack ? new Dictionary<CardOccurrenceKey, CardBadgeViewModel>()
            : Badges.Where(b => b.OccurrenceKey is not null).ToDictionary(b => b.OccurrenceKey!.Value);
        var next = Session.CurrentPackPresentations.Take(DraftCardLayout.MaximumCards).Select(model =>
        {
            if (!existing.TryGetValue(model.Key, out var badge)) return new CardBadgeViewModel(model);
            badge.UpdatePresentation(model);
            return badge;
        }).ToArray();
        Badges = Array.AsReadOnly(next);
        if (newPack) ResetVisualPlacement();
        PlaceAll();
        Notify(nameof(Badges)); Notify(nameof(ShowBadges));
    }
    private void CaptureBadgeDiagnostics()
    {
        // Diagnose final XAML items, including their actual geometric slot, rather than earlier engine data.
        BadgeBindingDiagnosticsText = Session.PositionText + " passive badge bindings:\n"
            + string.Join("\n", Badges.Select((badge, visualIndex) =>
                FormattableString.Invariant($"Display index: {visualIndex}; PackIndex: {badge.Presentation!.PackIndex}; CardIdentifier: {badge.Presentation.CardIdentifier.Value}; CardName: {badge.Name}; RawGIH: {badge.Presentation.RawGIH}; ALSA: {badge.Presentation.ALSA}; ContextRank: {badge.Presentation.ContextRank}; IsContextPick: {badge.Presentation.IsContextPick}; Log occurrence index: {badge.Index}; Visual slot: {badge.VisualSlotIndex?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unmapped"}; X: {badge.X}; Y: {badge.Y}")));
        System.Diagnostics.Trace.WriteLine(BadgeBindingDiagnosticsText);
        Notify(nameof(BadgeBindingDiagnosticsText));
    }
    private void PlaceAll()
    {
        foreach (var badge in Badges)
            if (_visualPlacement is { } mapping && Equals(mapping.Pack, Session.CurrentPackIdentity)
                && badge.OccurrenceKey is { } key && mapping.Slots.TryGetValue(key, out var visualSlot))
            {
                var rectangle = _automaticPlacement?.Rectangles.ElementAtOrDefault(visualSlot);
                badge.Place(rectangle is null ? _layout.Slots[visualSlot]
                    : new CardSlot(visualSlot, rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height), _width, _height);
            }
            else if (_automaticPlacement is { } auto && Equals(auto.Pack, Session.CurrentPackIdentity)
                && auto.Matches.SingleOrDefault(m => m.Key == badge.OccurrenceKey) is { } match)
                badge.Place(new(match.VisualSlot, match.Rectangle.X, match.Rectangle.Y, match.Rectangle.Width, match.Rectangle.Height), _width, _height);
            else badge.ClearPlacement();
        Guides = !_isCalibrating ? [] : Array.AsReadOnly(_layout.Slots.Select(slot =>
        {
            var bounds = slot.Scale(_width, _height);
            var card = Badges.FirstOrDefault(b => b.VisualSlotIndex == slot.Index);
            var name = card is null ? "" : " · " + card.Name;
            return new CardSlotGuide(bounds.X, bounds.Y, bounds.Width, bounds.Height, $"Slot {slot.Index + 1}{name}");
        }).ToArray());
        Notify(nameof(Guides));
        CaptureBadgeDiagnostics();
    }
    private void SessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        Notify(nameof(CompactStatus)); Notify(nameof(Diagnostics)); Notify(nameof(HasWarning));
    }
    public void Dispose()
    {
        Session.PackPresentationChanged -= RefreshPack;
        Session.PropertyChanged -= SessionChanged;
    }
}
