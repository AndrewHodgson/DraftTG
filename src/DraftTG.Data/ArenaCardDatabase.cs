using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace DraftTG.Data;

/// <summary>
/// Arena-authored display-sort keys for one card (Phase 9E.1). Values are copied verbatim from
/// Arena's local <c>Raw_CardDatabase_*.mtga</c> <c>Cards</c> row; NULL stays null and no key is rederived.
/// The three coverage traits are used only for evidence-gate reporting, never for ordering.
/// </summary>
public sealed record ArenaCardSortKeys(
    int GrpId,
    int? MythicToCommon,
    int? ColorOrder,
    string? Title,
    int? CmcWithXLast,
    int? CreaturesFirst,
    int? LandLast,
    int? BasicLandsFirst,
    string? CollectorNumber,
    string? ExpansionCode)
{
    public bool? IsMulticolor { get; init; }
    public bool? IsHybrid { get; init; }
    public bool? IsNonbasicLand { get; init; }
}

/// <summary>
/// Arena's own printing label for one GrpId, used only as a card-identity bridge when Scryfall has no arena_id yet.
/// <see cref="EnglishTitle"/> is the raw enUS localization (it may contain Arena markup such as &lt;nobr&gt;).
/// </summary>
public sealed record ArenaCardPrintingIdentity(int GrpId, string? ExpansionCode, string? CollectorNumber, string? EnglishTitle,
    bool IsRebalanced, bool IsToken);

public sealed record ArenaCardPrintingLookup(ArenaCardDatabaseStatus Status, ArenaCardDatabaseInfo? Database,
    IReadOnlyDictionary<int, ArenaCardPrintingIdentity> Printings, string? Diagnostic);

public enum ArenaCardDatabaseStatus { Available, NotFound, Unavailable }

/// <summary>File identity and Arena's own version rows. The path is diagnostic only and is never persisted.</summary>
public sealed record ArenaCardDatabaseInfo(string Path, DateTimeOffset LastModifiedUtc, string? DataVersion, string? GrpVersion)
{
    public string FileName => System.IO.Path.GetFileName(Path);
}

public sealed record ArenaCardSortKeyLookup(ArenaCardDatabaseStatus Status, ArenaCardDatabaseInfo? Database,
    IReadOnlyDictionary<int, ArenaCardSortKeys> Keys, IReadOnlyList<int> MissingGrpIds, string? Diagnostic)
{
    internal IReadOnlyDictionary<int, ArenaCardPrintingIdentity>? Printings { get; init; }
    public static ArenaCardSortKeyLookup Failed(ArenaCardDatabaseStatus status, ArenaCardDatabaseInfo? database, string diagnostic) =>
        new(status, database, new Dictionary<int, ArenaCardSortKeys>(), [], diagnostic);
}

/// <summary>Finds the newest <c>Raw_CardDatabase_*.mtga</c>. Never copies, moves or opens the file for writing.</summary>
public static class ArenaCardDatabaseLocator
{
    public const string FilePattern = "Raw_CardDatabase_*.mtga";
    public const string OverrideEnvironmentVariable = "DRAFTTG_ARENA_CARD_DATABASE";
    private const string RawDirectory = "MTGA_Data/Downloads/Raw";

    /// <summary>Likely Arena install locations. macOS is documented but not runtime-validated.</summary>
    public static IReadOnlyList<string> DefaultDirectories(bool windows, bool macOS, Func<Environment.SpecialFolder, string> folder)
    {
        var result = new List<string>();
        if (windows)
        {
            foreach (var programFiles in new[] { folder(Environment.SpecialFolder.ProgramFilesX86), folder(Environment.SpecialFolder.ProgramFiles) }
                .Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                result.Add(Path.Combine(programFiles, "Steam", "steamapps", "common", "MTGA", RawDirectory));
                result.Add(Path.Combine(programFiles, "Wizards of the Coast", "MTGA", RawDirectory));
            }
        }
        if (macOS && folder(Environment.SpecialFolder.UserProfile) is { Length: > 0 } home)
            result.Add(Path.Combine(home, "Library", "Application Support", "com.wizards.mtga", "Downloads", "Raw"));
        return result.Select(p => p.Replace('/', Path.DirectorySeparatorChar)).ToArray();
    }

    /// <summary>An explicit file or directory override wins; otherwise the newest file across the default directories.</summary>
    public static string? FindNewest(string? explicitPath = null, IEnumerable<string>? directories = null)
    {
        explicitPath ??= Environment.GetEnvironmentVariable(OverrideEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            if (File.Exists(explicitPath)) return Path.GetFullPath(explicitPath);
            if (!Directory.Exists(explicitPath)) return null;
            directories = [explicitPath];
        }
        directories ??= DefaultDirectories(OperatingSystem.IsWindows(), OperatingSystem.IsMacOS(), Environment.GetFolderPath);
        return directories.Where(Directory.Exists)
            .SelectMany(d => SafeFiles(d))
            .OrderByDescending(File.GetLastWriteTimeUtc).ThenBy(p => p, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static IEnumerable<string> SafeFiles(string directory)
    {
        try { return Directory.GetFiles(directory, FilePattern); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }
}

/// <summary>
/// Read-only access to Arena's local SQLite card database. Every call opens with <c>Mode=ReadOnly</c>,
/// pooling disabled, runs one bounded query and closes, so no lock is held between queries.
/// Missing required columns return <see cref="ArenaCardDatabaseStatus.Unavailable"/>; nothing throws to callers.
/// </summary>
public sealed partial class ArenaCardDatabaseReader(string path)
{
    public static readonly IReadOnlyList<string> RequiredColumns =
    [
        "GrpId", "ExpansionCode", "CollectorNumber", "Order_MythicToCommon", "Order_ColorOrder", "Order_Title",
        "Order_CMCWithXLast", "Order_CreaturesFirst", "Order_LandLast", "Order_BasicLandsFirst"
    ];
    // Optional coverage columns. Enum values verified against the DB's own Enums table (CardType 5 = Land, SuperType 1 = Basic).
    private static readonly string[] TraitColumns = ["Colors", "Types", "Supertypes", "OldSchoolManaText"];
    private static readonly string[] UniverseColumns = ["DraftContent", "IsToken", "IsPrimaryCard"];

    public string Path { get; } = path;

    public ArenaCardSortKeyLookup ReadSortKeys(IEnumerable<int> grpIds)
    {
        var ids = grpIds.Where(id => id > 0).Distinct().Order().ToArray();
        return Query((connection, info, columns) =>
        {
            var keys = new Dictionary<int, ArenaCardSortKeys>();
            foreach (var chunk in ids.Chunk(400))
            {
                using var command = connection.CreateCommand();
                command.CommandText = $"select {SelectList(columns)} from Cards where GrpId in ({string.Join(",", chunk.Select((_, i) => "$g" + i))})";
                for (var i = 0; i < chunk.Length; i++) command.Parameters.AddWithValue("$g" + i, chunk[i]);
                ReadRows(command, columns, keys);
            }
            var missing = ids.Where(id => !keys.ContainsKey(id)).ToArray();
            return new(ArenaCardDatabaseStatus.Available, info, keys, missing,
                missing.Length == 0 ? null : $"{missing.Length} GrpId(s) not in Arena card database: {string.Join(", ", missing)}");
        });
    }

    /// <summary>Draftable primary non-token cards of the given expansions; used only to group functionally equivalent rules.</summary>
    public ArenaCardSortKeyLookup ReadDraftUniverse(IEnumerable<string> expansionCodes)
    {
        var codes = expansionCodes.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return Query((connection, info, columns) =>
        {
            if (!UniverseColumns.All(columns.Contains))
                return ArenaCardSortKeyLookup.Failed(ArenaCardDatabaseStatus.Unavailable, info, "Draft-universe columns are unavailable.");
            var keys = new Dictionary<int, ArenaCardSortKeys>();
            if (codes.Length > 0)
            {
                using var command = connection.CreateCommand();
                command.CommandText = $"select {SelectList(columns)} from Cards where ExpansionCode in ({string.Join(",", codes.Select((_, i) => "$e" + i))}) "
                    + "and DraftContent = 1 and IsToken = 0 and IsPrimaryCard = 1 and Order_MythicToCommon is not null";
                for (var i = 0; i < codes.Length; i++) command.Parameters.AddWithValue("$e" + i, codes[i]);
                ReadRows(command, columns, keys);
            }
            return new(ArenaCardDatabaseStatus.Available, info, keys, [], null);
        });
    }

    public ArenaCardSortKeyLookup Inspect() => Query((_, info, _) => new(ArenaCardDatabaseStatus.Available, info, new Dictionary<int, ArenaCardSortKeys>(), [], null));

    /// <summary>GrpId → ExpansionCode, CollectorNumber and enUS title. Needs only the identity columns, not the Order_* keys.</summary>
    public ArenaCardPrintingLookup ReadPrintingIdentities(IEnumerable<int> grpIds)
    {
        var ids = grpIds.Where(id => id > 0).Distinct().Order().ToArray();
        var result = Query((connection, info, columns) =>
        {
            var printings = new Dictionary<int, ArenaCardPrintingIdentity>();
            var hasTitles = HasTable(connection, "Localizations_enUS");
            string Flag(string column) => columns.Contains(column) ? column : "0";
            foreach (var chunk in ids.Chunk(400))
            {
                using var command = connection.CreateCommand();
                var title = hasTitles
                    ? "(select l.Loc from Localizations_enUS l where l.LocId = c.TitleId order by l.Formatted desc limit 1)" : "NULL";
                command.CommandText = $"select c.GrpId, c.ExpansionCode, c.CollectorNumber, {title}, {Flag("IsRebalanced")}, {Flag("IsToken")} "
                    + $"from Cards c where c.GrpId in ({string.Join(",", chunk.Select((_, i) => "$g" + i))})";
                for (var i = 0; i < chunk.Length; i++) command.Parameters.AddWithValue("$g" + i, chunk[i]);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    string? Text(int i) => reader.IsDBNull(i) ? null : reader.GetValue(i).ToString();
                    bool Bool(int i) => !reader.IsDBNull(i) && Convert.ToInt64(reader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture) != 0;
                    var grpId = checked((int)reader.GetInt64(0));
                    printings[grpId] = new(grpId, Text(1), Text(2), Text(3), Bool(4), Bool(5));
                }
            }
            return new ArenaCardSortKeyLookup(ArenaCardDatabaseStatus.Available, info, new Dictionary<int, ArenaCardSortKeys>(), [], null)
            { Printings = printings };
        }, PrintingColumns);
        return new(result.Status, result.Database, result.Printings ?? new Dictionary<int, ArenaCardPrintingIdentity>(), result.Diagnostic);
    }

    private static readonly IReadOnlyList<string> PrintingColumns = ["GrpId", "ExpansionCode", "CollectorNumber", "TitleId"];

    private static bool HasTable(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "select count(*) from sqlite_master where type = 'table' and name = $name";
        command.Parameters.AddWithValue("$name", table);
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) > 0;
    }

    private ArenaCardSortKeyLookup Query(Func<SqliteConnection, ArenaCardDatabaseInfo, HashSet<string>, ArenaCardSortKeyLookup> query,
        IReadOnlyList<string>? requiredColumns = null)
    {
        requiredColumns ??= RequiredColumns;
        if (!File.Exists(Path)) return ArenaCardSortKeyLookup.Failed(ArenaCardDatabaseStatus.NotFound, null, "Arena card database not found.");
        ArenaCardDatabaseInfo? info = null;
        try
        {
            var modified = new DateTimeOffset(File.GetLastWriteTimeUtc(Path), TimeSpan.Zero);
            var builder = new SqliteConnectionStringBuilder { DataSource = Path, Mode = SqliteOpenMode.ReadOnly, Pooling = false };
            using var connection = new SqliteConnection(builder.ToString());
            connection.Open();
            var versions = ReadVersions(connection);
            info = new(Path, modified, versions.GetValueOrDefault("Data"), versions.GetValueOrDefault("GRP"));
            var columns = ReadColumns(connection);
            var absent = requiredColumns.Where(c => !columns.Contains(c)).ToArray();
            if (absent.Length > 0)
                return ArenaCardSortKeyLookup.Failed(ArenaCardDatabaseStatus.Unavailable, info,
                    "Arena card database schema is missing required column(s): " + string.Join(", ", absent));
            return query(connection, info, columns);
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or InvalidCastException or FormatException)
        {
            return ArenaCardSortKeyLookup.Failed(ArenaCardDatabaseStatus.Unavailable, info, $"Arena card database unavailable: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static Dictionary<string, string> ReadVersions(SqliteConnection connection)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        using var probe = connection.CreateCommand();
        probe.CommandText = "select count(*) from sqlite_master where type = 'table' and name = 'Versions'";
        if (Convert.ToInt64(probe.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) == 0) return result;
        using var command = connection.CreateCommand();
        command.CommandText = "select Type, Version from Versions";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            if (!reader.IsDBNull(0) && !reader.IsDBNull(1)) result[reader.GetString(0)] = reader.GetValue(1).ToString()!;
        return result;
    }

    private static HashSet<string> ReadColumns(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "select name from pragma_table_info('Cards')";
        using var reader = command.ExecuteReader();
        var columns = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read()) columns.Add(reader.GetString(0));
        return columns;
    }

    private static string SelectList(HashSet<string> columns) =>
        string.Join(", ", RequiredColumns.Concat(TraitColumns.Select(c => columns.Contains(c) ? c : "NULL")));

    private static void ReadRows(SqliteCommand command, HashSet<string> columns, Dictionary<int, ArenaCardSortKeys> keys)
    {
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            int? Int(int i) => reader.IsDBNull(i) ? null : checked((int)reader.GetInt64(i));
            string? Text(int i) => reader.IsDBNull(i) ? null : reader.GetValue(i).ToString();
            var grpId = checked((int)reader.GetInt64(0));
            var colors = Text(10); var types = Text(11); var supertypes = Text(12); var mana = Text(13);
            keys[grpId] = new(grpId, Int(3), Int(4), Text(5), Int(6), Int(7), Int(8), Int(9), Text(2), Text(1))
            {
                IsMulticolor = columns.Contains("Colors") ? List(colors).Distinct().Count() >= 2 : null,
                IsHybrid = columns.Contains("OldSchoolManaText") ? mana is not null && HybridSymbol().IsMatch(mana) : null,
                IsNonbasicLand = columns.Contains("Types") && columns.Contains("Supertypes")
                    ? List(types).Contains("5") && !List(supertypes).Contains("1") : null
            };
        }
    }

    private static string[] List(string? text) =>
        string.IsNullOrWhiteSpace(text) ? [] : text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // Two-color hybrid symbols such as (G/W) or (G/W/P); excludes (2/R), (C/W) and Phyrexian-only (B/P).
    [GeneratedRegex(@"\([WUBRG]/[WUBRG]")]
    private static partial Regex HybridSymbol();
}
