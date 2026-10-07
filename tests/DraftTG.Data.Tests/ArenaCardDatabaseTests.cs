using System.Security.Cryptography;
using DraftTG.Data;
using DraftTG.Tests.Shared;

namespace DraftTG.Data.Tests;

public sealed class ArenaCardDatabaseTests
{
    [Fact]
    public void ReadsExactArenaKeysReadOnlyWithoutChangingTheFile()
    {
        var directory = SyntheticArenaDatabase.TemporaryDirectory();
        try
        {
            var path = SyntheticArenaDatabase.Create(directory.FullName);
            var before = SHA256.HashData(File.ReadAllBytes(path));
            File.SetAttributes(path, FileAttributes.ReadOnly); // A write-mode open would fail here.
            var lookup = new ArenaCardDatabaseReader(path).ReadSortKeys([86853, 87058, 86988, 900002, 900003, 900004, 900005]);

            Assert.Equal(ArenaCardDatabaseStatus.Available, lookup.Status);
            Assert.Equal(("synthetic.1", "synthetic.1"), (lookup.Database!.DataVersion, lookup.Database.GrpVersion));
            Assert.Equal(new ArenaCardSortKeys(86853, 2, 3, "stonesplitterbolt", 1001, 1, 0, 1, "151", "WOE")
                { IsMulticolor = false, IsHybrid = false, IsNonbasicLand = false }, lookup.Keys[86853]);
            Assert.Equal("WOT", lookup.Keys[87058].ExpansionCode);
            Assert.Equal((true, false), (lookup.Keys[86988].IsNonbasicLand!.Value, lookup.Keys[900004].IsNonbasicLand!.Value));
            Assert.True(lookup.Keys[900002].IsMulticolor); Assert.True(lookup.Keys[900003].IsHybrid);
            Assert.Equal(new ArenaCardSortKeys(900005, null, null, null, null, null, null, null, "", "WOE")
                { IsMulticolor = false, IsHybrid = false, IsNonbasicLand = false }, lookup.Keys[900005]);
            Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(path)));
            Assert.Single(directory.GetFiles()); // No journal, WAL or copy left beside Arena's file.
        }
        finally
        {
            foreach (var file in directory.GetFiles()) file.Attributes = FileAttributes.Normal;
            directory.Delete(true);
        }
    }

    [Fact]
    public void MissingIdsAreReportedAndTheDraftUniverseExcludesNonDraftRows()
    {
        var directory = SyntheticArenaDatabase.TemporaryDirectory();
        try
        {
            var reader = new ArenaCardDatabaseReader(SyntheticArenaDatabase.Create(directory.FullName));
            var lookup = reader.ReadSortKeys([86711, 123, 86711]);
            Assert.Equal([123], lookup.MissingGrpIds);
            Assert.Equal([86711], lookup.Keys.Keys);
            Assert.Contains("123", lookup.Diagnostic);
            var universe = reader.ReadDraftUniverse(["WOE", "WOT"]);
            Assert.Equal(21, universe.Keys.Count);
            Assert.DoesNotContain(900005, universe.Keys.Keys);
        }
        finally { directory.Delete(true); }
    }

    [Theory]
    [InlineData("missing-file")]
    [InlineData("not-sqlite")]
    [InlineData("no-cards-table")]
    [InlineData("Order_ColorOrder")]
    [InlineData("Order_Title")]
    public void UnavailableDatabasesAndMissingRequiredColumnsFailClosed(string scenario)
    {
        var directory = SyntheticArenaDatabase.TemporaryDirectory();
        try
        {
            var path = Path.Combine(directory.FullName, "Raw_CardDatabase_x.mtga");
            switch (scenario)
            {
                case "missing-file": break;
                case "not-sqlite": File.WriteAllText(path, "not a database"); break;
                case "no-cards-table": SyntheticArenaDatabase.Create(directory.FullName, "CREATE TABLE Other (Id INT);", Path.GetFileName(path)); break;
                default:
                    SyntheticArenaDatabase.Create(directory.FullName,
                        SyntheticArenaDatabase.Sql + $"ALTER TABLE Cards RENAME COLUMN {scenario} TO Renamed_{scenario};", Path.GetFileName(path));
                    break;
            }
            var lookup = new ArenaCardDatabaseReader(path).ReadSortKeys([86711]);
            Assert.Equal(scenario == "missing-file" ? ArenaCardDatabaseStatus.NotFound : ArenaCardDatabaseStatus.Unavailable, lookup.Status);
            Assert.Empty(lookup.Keys);
            if (scenario.StartsWith("Order_", StringComparison.Ordinal)) Assert.Contains(scenario, lookup.Diagnostic);
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public void LocatorPrefersExplicitPathThenNewestFileAndKnowsPlatformLocations()
    {
        var directory = SyntheticArenaDatabase.TemporaryDirectory();
        try
        {
            var older = Path.Combine(directory.FullName, "Raw_CardDatabase_old.mtga");
            var newer = Path.Combine(directory.FullName, "Raw_CardDatabase_new.mtga");
            File.WriteAllText(older, ""); File.WriteAllText(newer, "");
            File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddDays(-2));
            File.WriteAllText(Path.Combine(directory.FullName, "Raw_ArtCropDatabase_x.mtga"), "");
            Assert.Equal(newer, ArenaCardDatabaseLocator.FindNewest(directory.FullName));
            Assert.Equal(older, ArenaCardDatabaseLocator.FindNewest(older));
            Assert.Equal(newer, ArenaCardDatabaseLocator.FindNewest(null, [Path.Combine(directory.FullName, "absent"), directory.FullName]));
            Assert.Null(ArenaCardDatabaseLocator.FindNewest(Path.Combine(directory.FullName, "absent")));

            string Folder(Environment.SpecialFolder folder) => folder switch
            {
                Environment.SpecialFolder.ProgramFilesX86 => "C:/PF86", Environment.SpecialFolder.ProgramFiles => "C:/PF", _ => "/Users/u"
            };
            var windows = ArenaCardDatabaseLocator.DefaultDirectories(true, false, Folder).Select(p => p.Replace('\\', '/')).ToArray();
            Assert.Contains("C:/PF86/Steam/steamapps/common/MTGA/MTGA_Data/Downloads/Raw", windows);
            Assert.Contains("C:/PF/Wizards of the Coast/MTGA/MTGA_Data/Downloads/Raw", windows);
            Assert.Equal(["/Users/u/Library/Application Support/com.wizards.mtga/Downloads/Raw"],
                ArenaCardDatabaseLocator.DefaultDirectories(false, true, Folder).Select(p => p.Replace('\\', '/')));
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public void LedgerAppendStartsAfterATornTailAndNeverWritesMultilineRecords()
    {
        var directory = SyntheticArenaDatabase.TemporaryDirectory();
        try
        {
            var file = new JsonLinesLedgerFile(Path.Combine(directory.FullName, "localization", "order-evidence.jsonl"));
            Assert.Empty(file.ReadLines());
            file.AppendLine("{\"a\":1}");
            File.AppendAllText(file.Path, "{\"torn\":"); // Simulated exit mid-write.
            file.AppendLine("{\"b\":2}");
            Assert.Equal(["{\"a\":1}", "{\"torn\":", "{\"b\":2}"], file.ReadLines());
            Assert.Throws<ArgumentException>(() => file.AppendLine("{}\n{}"));
        }
        finally { directory.Delete(true); }
    }
}
