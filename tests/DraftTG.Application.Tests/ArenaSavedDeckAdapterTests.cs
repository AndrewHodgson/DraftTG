using System.Text.Json;
using DraftTG.ArenaIntegration;
using DraftTG.Data;
using DraftTG.Domain;

namespace DraftTG.Application.Tests;

public sealed class ArenaSavedDeckAdapterTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedActualWoeDeckResolvesBasicsAndPoolCountsWithoutCertifyingUnsavedEditor(bool missingIdentity)
    {
        var data = new ScryfallCardCatalogDecoder().DecodeCatalogData(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "arena-woe-saved-deck-cards.json")));
        var resolver = new ArenaCardResolver(data);
        var saved = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "arena-woe-saved-deck-live.jsonl"))
            .SelectMany(l => new ArenaDeckLogParser().Parse(new ArenaLogSourceEvent.Line(l))).OfType<ArenaDeckLogEvent.SavedDeckObserved>().Single();
        var nonbasics = saved.MainDeck.Concat(saved.Sideboard).Where(e => resolver.TryResolve(e.CardIdentifier, out var id)
            && data.Catalog.Find(id)!.GameplayMetadata.IsBasicLand != true).SelectMany(e => Enumerable.Repeat(resolver.Resolve(e.CardIdentifier).CardIdentifier!, e.Count));
        var pool = new DraftPoolSnapshot(new(nonbasics), DraftPoolCompleteness.Complete);
        if (missingIdentity) saved = saved with { MainDeck = saved.MainDeck.Concat([new(ArenaCardIdentifier.Create(999999), 1)]).ToArray() };
        var snapshot = new ArenaSavedDeckAdapter(resolver, data.Catalog).Convert(saved, new("session-A", 1), pool);
        Assert.Equal(42, pool.TotalCardCount); Assert.Equal(missingIdentity ? 41 : 40, snapshot.MainDeckCount);
        Assert.Equal(24, snapshot.MainDeck.Sum(e => e.Count)); Assert.Equal(18, snapshot.Sideboard.Sum(e => e.Count));
        Assert.Equal(8, snapshot.Basics.Single(e => e.Type == BasicLandType.Mountain).Count);
        Assert.Equal(8, snapshot.Basics.Single(e => e.Type == BasicLandType.Forest).Count);
        Assert.Equal(missingIdentity ? ArenaDeckCompleteness.Partial : ArenaDeckCompleteness.Complete, snapshot.Completeness);
        Assert.Equal(ArenaDeckSnapshotScope.SavedDeck, snapshot.Scope); Assert.False(snapshot.HasTrustworthyCurrentCounts);
    }
}
