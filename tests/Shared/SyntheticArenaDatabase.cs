using Microsoft.Data.Sqlite;

namespace DraftTG.Tests.Shared;

/// <summary>Materializes the synthetic Arena card-database SQL fixture. Never touches Wizards' real database.</summary>
internal static class SyntheticArenaDatabase
{
    public static string Sql => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "arena-card-database-synthetic.sql"));

    public static string Create(string directory, string? sql = null, string fileName = "Raw_CardDatabase_synthetic.mtga")
    {
        var path = Path.Combine(directory, fileName);
        var builder = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false };
        using var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql ?? Sql;
        command.ExecuteNonQuery();
        return path;
    }

    public static DirectoryInfo TemporaryDirectory() => Directory.CreateTempSubdirectory("drafttg-9e1-");
}
