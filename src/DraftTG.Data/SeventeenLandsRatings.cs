namespace DraftTG.Data;

public enum SeventeenLandsFormat { PremierDraft, TradDraft, QuickDraft }
public enum SeventeenLandsSource { Live, Cache, StaleCache, Unavailable }

/// <summary>Validated, name-keyed boundary value; JSON transport DTOs remain internal.</summary>
public sealed record SeventeenLandsRating(
    string Name,
    int? GameInHandGameCount = null,
    double? PlayRate = null,
    double? GameInHandWinRate = null,
    double? OpeningHandWinRate = null,
    double? DrawnWinRate = null,
    double? DrawnImprovementWinRate = null,
    double? AverageLastSeenAt = null,
    double? AverageTakenAt = null);

public sealed class SeventeenLandsRatingsResult
{
    public SeventeenLandsRatingsResult(string expansion, SeventeenLandsFormat format,
        IEnumerable<SeventeenLandsRating> rows, SeventeenLandsSource source,
        DateTimeOffset? fetchedAt = null, string? diagnostic = null)
    {
        Expansion = expansion;
        Format = format;
        Rows = Array.AsReadOnly(rows.ToArray());
        Source = source;
        FetchedAt = fetchedAt;
        Diagnostic = diagnostic;
    }

    public string Expansion { get; }
    public SeventeenLandsFormat Format { get; }
    public IReadOnlyList<SeventeenLandsRating> Rows { get; }
    public SeventeenLandsSource Source { get; }
    public DateTimeOffset? FetchedAt { get; }
    public string? Diagnostic { get; }
}

public interface ISeventeenLandsCardRatingsClient
{
    Task<SeventeenLandsRatingsResult> LoadAsync(string expansion, SeventeenLandsFormat format,
        bool forceRefresh = false, CancellationToken cancellationToken = default);
}
