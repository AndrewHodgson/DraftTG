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

public sealed class CardBadgeViewModel(int index, CurrentPackCardViewModel card) : PresentationModel
{
    public int Index { get; } = index;
    public string Name { get; } = card.Name;
    private LimitedCardStatisticsPresentation _statistics = card.Statistics;
    private CardRecommendation? _recommendation = card.Recommendation;
    public string WinRate => _statistics.GameInHand + (_statistics.IsLowSample ? "*" : "");
    public string Rank => _recommendation?.StatisticalRank is { } rank ? $"#{rank}" : string.Empty;
    public bool IsStatisticalPick => _recommendation?.IsTopStatisticalCandidate == true;
    public string BorderColor => IsStatisticalPick ? "#FFE2BE64" : "#886D798B";
    public string RankColor => IsStatisticalPick ? "#FFFFD57D" : "#FFB9C6D8";
    public string Secondary => "ALSA " + _statistics.AverageLastSeen;
    public string Detail => $"{Name} · {_statistics.Summary}";
    public double X { get; private set; }
    public double Y { get; private set; }
    public double Width { get; private set; }
    public void UpdateStatistics(CurrentPackCardViewModel row)
    {
        _statistics = row.Statistics;
        _recommendation = row.Recommendation;
        Notify(nameof(WinRate)); Notify(nameof(Secondary)); Notify(nameof(Detail));
        Notify(nameof(Rank)); Notify(nameof(IsStatisticalPick)); Notify(nameof(BorderColor)); Notify(nameof(RankColor));
    }
    public void Place(CardSlot slot, double width, double height)
    {
        var bounds = slot.Scale(width, height);
        Width = Math.Min(86, bounds.Width);
        X = bounds.X + (bounds.Width - Width) / 2;
        Y = Math.Max(bounds.Y, bounds.Y + bounds.Height - 44);
        Notify(nameof(X)); Notify(nameof(Y)); Notify(nameof(Width));
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
    public OverlayViewModel(MainWindowViewModel session)
    {
        Session = session;
        session.PackPresentationChanged += RefreshPack;
        session.PropertyChanged += SessionChanged;
        RefreshPack(true);
    }
    public MainWindowViewModel Session { get; }
    public IReadOnlyList<CardBadgeViewModel> Badges { get; private set; } = [];
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
        Session.StatisticsDiagnosticText, _overlayDiagnostic }.Where(text => !string.IsNullOrWhiteSpace(text)));
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
    private void RefreshPack(bool newPack)
    {
        if (newPack)
        {
            // Replace the immutable list once, after all rows are built. No intermediate stale slots.
            Badges = Array.AsReadOnly(Session.CurrentPackCards.Take(DraftCardLayout.MaximumCards)
                .Select((card, index) => new CardBadgeViewModel(index, card)).ToArray());
            PlaceAll();
            Notify(nameof(Badges)); Notify(nameof(ShowBadges));
        }
        else
        {
            for (var i = 0; i < Badges.Count && i < Session.CurrentPackCards.Count; i++)
                Badges[i].UpdateStatistics(Session.CurrentPackCards[i]);
        }
    }
    private void PlaceAll()
    {
        foreach (var badge in Badges) badge.Place(_layout.Slots[badge.Index], _width, _height);
        Guides = !_isCalibrating ? [] : Array.AsReadOnly(_layout.Slots.Select(slot =>
        {
            var bounds = slot.Scale(_width, _height);
            var name = slot.Index < Badges.Count ? " · " + Badges[slot.Index].Name : "";
            return new CardSlotGuide(bounds.X, bounds.Y, bounds.Width, bounds.Height, $"Slot {slot.Index + 1}{name}");
        }).ToArray());
        Notify(nameof(Guides));
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
