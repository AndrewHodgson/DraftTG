using System.Text.Json.Nodes;
using DraftTG.Application;
using DraftTG.ArenaIntegration;
using DraftTG.Data;
using DraftTG.Domain;
using DraftTG.Tests.Shared;

namespace DraftTG.Application.Tests;

public sealed class ArenaCardIdentityFallbackTests
{
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    /// <summary>The real FRA printings exactly as Scryfall ships them today: no arena_id. Also returns the expected pairing.</summary>
    private static (ScryfallCardCatalogData Data, Dictionary<int, string> Expected) FraWithoutArenaIds()
    {
        var cards = JsonNode.Parse(Fixture("premier-fra-course-cards.json"))!.AsArray();
        var expected = cards.ToDictionary(c => c!["arena_id"]!.GetValue<int>(), c => c!["id"]!.GetValue<string>());
        foreach (var card in cards) card!.AsObject().Remove("arena_id");
        return (new ScryfallCardCatalogDecoder().DecodeCatalogData(cards.ToJsonString()), expected);
    }

    private sealed class FakeArenaDatabase(params ArenaCardPrintingIdentity[] rows) : IArenaPrintingIdentitySource
    {
        public int Calls { get; private set; }
        public ArenaCardPrintingIdentity? Find(int grpId) { Calls++; return rows.FirstOrDefault(r => r.GrpId == grpId); }
    }

    private static ScryfallCardCatalogData Catalog(params (string Id, int? ArenaId, string Name, string Set, string Number)[] cards) =>
        new ScryfallCardCatalogDecoder().DecodeCatalogData(new JsonArray(cards.Select(c => (JsonNode)new JsonObject
        {
            ["id"] = c.Id, ["arena_id"] = c.ArenaId, ["name"] = c.Name, ["colors"] = new JsonArray(), ["rarity"] = "common",
            ["set"] = c.Set, ["collector_number"] = c.Number
        }).ToArray()).ToJsonString());

    [Fact]
    public void RealFraPoolResolvesEveryDistinctCardAndAll42OccurrencesThroughTheArenaDatabase()
    {
        var directory = SyntheticArenaDatabase.TemporaryDirectory();
        try
        {
            var path = SyntheticArenaDatabase.Create(directory.FullName, Fixture("arena-card-database-fra-identity.sql"));
            var (data, expected) = FraWithoutArenaIds();
            Assert.Empty(data.ArenaIdMappings);
            var resolver = new ArenaCardResolver(data, new ArenaDatabasePrintingIdentitySource(() => path));
            Assert.Equal(33, expected.Count);
            foreach (var (grpId, printing) in expected)
            {
                var result = resolver.Resolve(ArenaCardIdentifier.Create(grpId));
                Assert.Equal((ArenaCardResolutionStatus.Resolved, ArenaCardResolutionSource.ArenaDatabaseSetCollector, printing),
                    (result.Status, result.Source, result.CardIdentifier!.Value));
            }

            var courses = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "premier-fra-course-snapshot-live.jsonl"));
            var engine = new ArenaDraftStateEngine();
            foreach (var fact in new ArenaDraftLogParser().Parse(new ArenaLogSourceEvent.Line(courses[1]))) engine.Apply(fact);
            var snapshot = new ArenaDraftSnapshotAdapter(resolver).Convert(engine.Current);
            Assert.Equal((42, 0, DraftPoolCompleteness.Complete), (snapshot.DraftPool!.KnownCardCount, snapshot.DraftPool.UnresolvedOccurrenceCount,
                snapshot.DraftPool.Completeness));
            Assert.Equal(3, snapshot.DraftPool.Entries.Single(e => e.CardIdentifier.Value == expected[106315]).Count); // Not collapsed.
            Assert.Equal(new ArenaCardIdentitySummary(42, 0, 42, 0, 0), snapshot.PoolIdentity);
            Assert.StartsWith("Card identity: 42 / 42 resolved", snapshot.PoolIdentity!.Text);
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public void DirectScryfallMappingAndDirectAmbiguityNeverConsultTheArenaDatabase()
    {
        var data = Catalog(("direct", 501, "Direct Card", "fra", "1"), ("other", null, "Other Card", "fra", "2"),
            ("amb-a", 502, "Twin", "fra", "3"), ("amb-b", 502, "Twin", "fra", "4"));
        var database = new FakeArenaDatabase(new(501, "FRA", "2", "Other Card", false, false), new(502, "FRA", "3", "Twin", false, false));
        var resolver = new ArenaCardResolver(data, database);

        var direct = resolver.Resolve(ArenaCardIdentifier.Create(501));
        Assert.Equal(("direct", ArenaCardResolutionSource.ScryfallArenaId), (direct.CardIdentifier!.Value, direct.Source));
        var ambiguous = resolver.Resolve(ArenaCardIdentifier.Create(502));
        Assert.Equal((ArenaCardResolutionStatus.Ambiguous, ArenaCardResolutionSource.ScryfallArenaId, 2),
            (ambiguous.Status, ambiguous.Source, ambiguous.Candidates.Count));
        Assert.Equal(0, database.Calls);
    }

    [Theory]
    [InlineData("unique", ArenaCardResolutionStatus.Resolved, null)]
    [InlineData("case-and-suffix", ArenaCardResolutionStatus.Resolved, null)]
    [InlineData("markup-and-face-name", ArenaCardResolutionStatus.Resolved, null)]
    [InlineData("zero", ArenaCardResolutionStatus.Missing, "No catalog printing")]
    [InlineData("leading-zero-not-coerced", ArenaCardResolutionStatus.Missing, "No catalog printing")]
    [InlineData("multiple", ArenaCardResolutionStatus.Ambiguous, null)]
    [InlineData("name-mismatch", ArenaCardResolutionStatus.Missing, "Name mismatch")]
    [InlineData("no-title", ArenaCardResolutionStatus.Missing, "Name mismatch")]
    [InlineData("rebalanced", ArenaCardResolutionStatus.Missing, "rebalanced")]
    [InlineData("token", ArenaCardResolutionStatus.Missing, "token")]
    [InlineData("not-in-database", ArenaCardResolutionStatus.Missing, "no row")]
    public void FallbackRequiresOneExactPrintingWithAnAgreeingName(string scenario, ArenaCardResolutionStatus status, string? diagnostic)
    {
        var data = Catalog(("unique", null, "Hallway Heckler // Vicious Verse", "fra", "85"), ("suffix", null, "Starry Card", "fra", "12A★"),
            ("hyphen", null, "Blossom-Blessed Angel", "fra", "3"), ("dup-a", null, "Twin", "fra", "7"), ("dup-b", null, "Twin", "fra", "7"),
            ("twelve", null, "Twelve", "fra", "12"));
        ArenaCardPrintingIdentity? row = scenario switch
        {
            "unique" => new(9, "FRA", "85", "Hallway Heckler", false, false),
            "case-and-suffix" => new(9, " fra ", "12a★", "Starry Card", false, false),
            "markup-and-face-name" => new(9, "FRA", "3", "<nobr>Blossom-Blessed</nobr>  Angel", false, false),
            "zero" => new(9, "FRA", "999", "Hallway Heckler", false, false),
            "leading-zero-not-coerced" => new(9, "FRA", "012", "Twelve", false, false),
            "multiple" => new(9, "FRA", "7", "Twin", false, false),
            "name-mismatch" => new(9, "FRA", "85", "Somebody Else", false, false),
            "no-title" => new(9, "FRA", "85", null, false, false),
            "rebalanced" => new(9, "FRA", "85", "<sprite=\"SpriteSheet_MiscIcons\" name=\"arena_a\">Hallway Heckler", true, false),
            "token" => new(9, "FRA", "85", "Hallway Heckler", false, true),
            _ => null
        };
        var result = new ArenaCardResolver(data, row is null ? new FakeArenaDatabase() : new FakeArenaDatabase(row))
            .Resolve(ArenaCardIdentifier.Create(9));

        Assert.Equal(status, result.Status);
        if (status == ArenaCardResolutionStatus.Resolved)
            Assert.Equal(ArenaCardResolutionSource.ArenaDatabaseSetCollector, result.Source);
        if (status == ArenaCardResolutionStatus.Ambiguous) Assert.Equal(["dup-a", "dup-b"], result.Candidates.Select(c => c.Value).Order());
        if (diagnostic is not null) Assert.Contains(diagnostic, result.Diagnostic);
        Assert.Null(status == ArenaCardResolutionStatus.Resolved ? null : result.CardIdentifier); // Never an arbitrary pick.
    }

    [Fact]
    public async Task UnavailableArenaDatabaseKeepsDirectResolutionAndExistingMissingBehavior()
    {
        var directory = SyntheticArenaDatabase.TemporaryDirectory();
        try
        {
            var corrupt = Path.Combine(directory.FullName, "Raw_CardDatabase_corrupt.mtga");
            File.WriteAllText(corrupt, "not sqlite");
            var data = Catalog(("direct", 501, "Direct Card", "fra", "1"), ("new", null, "New Card", "fra", "2"));
            foreach (var source in new[] { new ArenaDatabasePrintingIdentitySource(() => null), new ArenaDatabasePrintingIdentitySource(() => corrupt) })
            {
                var resolver = new ArenaCardResolver(data, source);
                Assert.Equal("direct", resolver.Resolve(ArenaCardIdentifier.Create(501)).CardIdentifier!.Value);
                Assert.Equal(ArenaCardResolutionStatus.Missing, resolver.Resolve(ArenaCardIdentifier.Create(502)).Status);
                Assert.Null(source.DatabaseIdentity);
            }
            var bootstrap = await new DraftTGCardDataBootstrapper(new StaticCardData(data), new ArenaDatabasePrintingIdentitySource(() => null))
                .BootstrapAsync();
            Assert.Equal(ArenaCardResolutionStatus.Missing, bootstrap.ArenaCardResolver.Resolve(ArenaCardIdentifier.Create(502)).Status);
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public void ReplacedArenaDatabaseClearsCachedIdentities()
    {
        var directory = SyntheticArenaDatabase.TemporaryDirectory();
        try
        {
            var schema = "CREATE TABLE Versions (Type TEXT, Version TEXT); CREATE TABLE Cards (GrpId INT, ExpansionCode TEXT, CollectorNumber TEXT, TitleId INT);"
                + "CREATE TABLE Localizations_enUS (LocId INT NOT NULL, Formatted INT NOT NULL, Loc TEXT, PRIMARY KEY (LocId, Formatted));";
            var before = SyntheticArenaDatabase.Create(directory.FullName, schema + "INSERT INTO Versions VALUES ('Data','1');", "Raw_CardDatabase_a.mtga");
            var after = SyntheticArenaDatabase.Create(directory.FullName, schema + "INSERT INTO Versions VALUES ('Data','2');"
                + "INSERT INTO Cards VALUES (9, 'FRA', '2', 1); INSERT INTO Localizations_enUS VALUES (1, 1, 'New Card');", "Raw_CardDatabase_b.mtga");
            var current = before;
            var source = new ArenaDatabasePrintingIdentitySource(() => current, TimeSpan.Zero);
            Assert.Null(source.Find(9)); // Negative result cached for database version 1 only.
            current = after;
            Assert.Equal(new ArenaCardPrintingIdentity(9, "FRA", "2", "New Card", false, false), source.Find(9));
            Assert.Contains("|2|", source.DatabaseIdentity);
        }
        finally { directory.Delete(true); }
    }

    private sealed class StaticCardData(ScryfallCardCatalogData data) : IScryfallCardDataProvider
    {
        public Task<ScryfallCardDataLoadResult> LoadAsync(bool forceRefresh = false, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ScryfallCardDataLoadResult(data, ScryfallCardDataLoadSource.Cache, null, false, null));
    }
}
