using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DraftTG.Data;
using DraftTG.Domain;

namespace DraftTG.Application;

/// <summary>Arena's own printing label for a GrpId. Consulted only when Scryfall has no arena_id for it.</summary>
public interface IArenaPrintingIdentitySource
{
    ArenaCardPrintingIdentity? Find(int grpId);
}

/// <summary>
/// The one normalization for the set/collector identity bridge. Set codes compare case-insensitively and are stored
/// uppercase. Collector numbers stay strings: letters, suffixes, symbols and leading zeros are kept; only surrounding
/// whitespace and letter case are normalized. Names lose Arena markup (&lt;nobr&gt;, sprites) and typographic apostrophes.
/// </summary>
public static partial class ArenaPrintingKey
{
    public static string? Create(string? setCode, string? collectorNumber) =>
        string.IsNullOrWhiteSpace(setCode) || string.IsNullOrWhiteSpace(collectorNumber) ? null
            : setCode.Trim().ToUpperInvariant() + "|" + collectorNumber.Trim().ToUpperInvariant();

    public static string NormalizeName(string name)
    {
        var text = Markup().Replace(name, "").Normalize(NormalizationForm.FormKC).Replace('’', '\'');
        return Whitespace().Replace(text, " ").Trim().ToLowerInvariant();
    }

    /// <summary>Arena titles name one face; Scryfall names a multiface printing "A // B". Any face may match.</summary>
    public static bool NameAgrees(string arenaTitle, Card card)
    {
        var arena = NormalizeName(arenaTitle);
        if (arena.Length == 0) return false;
        return card.Name.Split(" // ").Prepend(card.Name)
            .Concat(card.GameplayMetadata.Faces.Select(f => f.Name).OfType<string>())
            .Any(name => NormalizeName(name) == arena);
    }

    [GeneratedRegex("<[^>]*>")] private static partial Regex Markup();
    [GeneratedRegex(@"\s+")] private static partial Regex Whitespace();
}

/// <summary>
/// Read-only Arena local database bridge (Phase 9E.1 locator and reader). Results are cached in memory per database
/// identity (file, modification time, Data version); a replaced database clears the cache. Any failure is a miss.
/// </summary>
public sealed class ArenaDatabasePrintingIdentitySource(Func<string?> locate, TimeSpan? recheckInterval = null) : IArenaPrintingIdentitySource
{
    private readonly TimeSpan _recheck = recheckInterval ?? TimeSpan.FromSeconds(30);
    private readonly Lock _gate = new();
    private readonly Dictionary<int, ArenaCardPrintingIdentity?> _cache = [];
    private string? _path;
    private string? _identity;
    private long _checkedAt = long.MinValue;

    public static ArenaDatabasePrintingIdentitySource CreateDefault() => new(() => ArenaCardDatabaseLocator.FindNewest());

    public string? DatabaseIdentity { get { lock (_gate) return _identity; } }

    public ArenaCardPrintingIdentity? Find(int grpId)
    {
        lock (_gate)
        {
            try
            {
                Refresh();
                if (_path is null) return null;
                if (_cache.TryGetValue(grpId, out var cached)) return cached;
                var lookup = new ArenaCardDatabaseReader(_path).ReadPrintingIdentities([grpId]);
                if (lookup.Status != ArenaCardDatabaseStatus.Available) return null; // Unavailable now: do not cache a negative.
                return _cache[grpId] = lookup.Printings.GetValueOrDefault(grpId);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
        }
    }

    private void Refresh()
    {
        var now = Environment.TickCount64;
        if (_checkedAt != long.MinValue && now - _checkedAt < _recheck.TotalMilliseconds) return;
        _checkedAt = now;
        var path = locate();
        string? identity = null;
        if (path is not null)
        {
            var info = new ArenaCardDatabaseReader(path).ReadPrintingIdentities([]); // Validates only identity columns.
            if (info.Status == ArenaCardDatabaseStatus.Available && info.Database is { } db)
                identity = string.Create(CultureInfo.InvariantCulture, $"{db.FileName}|{db.LastModifiedUtc:O}|{db.DataVersion}|{db.GrpVersion}");
        }
        if (identity != _identity) _cache.Clear();
        _identity = identity;
        _path = identity is null ? null : path;
    }
}
