using DraftTG.Application;
using DraftTG.Data;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;

namespace DraftTG.App.Tests;

public sealed partial class MainWindowViewModelTests
{
    private static MainWindowViewModel PremierCourseViewModel(CompletionUiSource source, bool resolvable,
        IArenaPrintingIdentitySource? arenaDatabase = null)
    {
        var json = resolvable ? File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "premier-fra-course-cards.json")) : "[]";
        // With an Arena database bridge, use the real FRA printings exactly as Scryfall ships them today: no arena_id.
        if (arenaDatabase is not null)
        {
            var cards = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsArray();
            foreach (var card in cards) card!.AsObject().Remove("arena_id");
            json = cards.ToJsonString();
        }
        var data = new ScryfallCardCatalogDecoder().DecodeCatalogData(json);
        var runtime = new DraftTGRuntime(new(source, new(), new(), new(new(data, arenaDatabase))), data.Catalog, DraftTGCardDataStatus.Cache, null, null,
            Decks: new(new(new LimitedStatisticsService(new CompletionUiRatings(data.Catalog), data.Catalog))));
        return CreateViewModel(new ImmediateRuntimeFactory(runtime));
    }

    [Fact]
    public async Task PremierCourseListAfterArenaRestartAutomaticallyActivatesSuggestedDecks()
    {
        var source = new CompletionUiSource(); await using var vm = PremierCourseViewModel(source, resolvable: true); vm.Start();
        foreach (var line in File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "premier-fra-course-snapshot-live.jsonl")))
            source.Send(line);
        await WaitUntilAsync(() => vm.SuggestedDeckSet is not null);
        Assert.Equal("Draft complete", vm.StatusText); Assert.True(vm.HasBaselineDeckPanel);
        Assert.Equal((42, 33, DraftPoolCompleteness.Complete), (vm.DraftPool!.TotalCardCount, vm.DraftPool.UniqueCardCount, vm.DraftPool.Completeness));
        Assert.Contains(vm.DraftPool.Entries, e => e.Count == 3);
        Assert.Equal(DeckBuildAvailability.Ready, vm.BaselineDeckResult!.Availability);
        Assert.NotEmpty(vm.SuggestedDecks); Assert.Empty(vm.DiagnosticText);
    }

    [Fact]
    public async Task PremierCourseListWithUnmappedArenaIdsReportsCompletionInsteadOfWaiting()
    {
        // Scryfall bulk data currently has no arena_id for FRA printings; the pool must stay honest, not wait.
        var source = new CompletionUiSource(); await using var vm = PremierCourseViewModel(source, resolvable: false); vm.Start();
        source.Send(File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "premier-fra-course-snapshot-live.jsonl"))[1]);
        await WaitUntilAsync(() => vm.StatusText == "Draft complete");
        Assert.Equal((0, 42, DraftPoolCompleteness.Partial), (vm.DraftPool!.KnownCardCount, vm.DraftPool.UnresolvedOccurrenceCount, vm.DraftPool.Completeness));
        Assert.Empty(vm.SuggestedDecks);
    }

    [Fact]
    public async Task PremierCourseListWithoutScryfallArenaIdsResolvesThroughArenaDatabaseAndActivatesSuggestedDecks()
    {
        var directory = DraftTG.Tests.Shared.SyntheticArenaDatabase.TemporaryDirectory();
        try
        {
            var path = DraftTG.Tests.Shared.SyntheticArenaDatabase.Create(directory.FullName,
                File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "arena-card-database-fra-identity.sql")));
            var source = new CompletionUiSource();
            await using var vm = PremierCourseViewModel(source, resolvable: true, new ArenaDatabasePrintingIdentitySource(() => path));
            vm.Start();
            foreach (var line in File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "premier-fra-course-snapshot-live.jsonl")))
                source.Send(line);
            await WaitUntilAsync(() => vm.SuggestedDeckSet is not null);
            Assert.Equal((42, 0, DraftPoolCompleteness.Complete), (vm.DraftPool!.KnownCardCount, vm.DraftPool.UnresolvedOccurrenceCount, vm.DraftPool.Completeness));
            Assert.Equal(new ArenaCardIdentitySummary(42, 0, 42, 0, 0), vm.PoolIdentity);
            Assert.StartsWith("Card identity: 42 / 42 resolved\nDirect Arena ID: 0\nArena DB fallback: 42", vm.PoolEntryDiagnosticsText);
            Assert.Equal(DeckBuildAvailability.Ready, vm.BaselineDeckResult!.Availability);
            Assert.NotEmpty(vm.SuggestedDecks);
        }
        finally { directory.Delete(true); }
    }
}
