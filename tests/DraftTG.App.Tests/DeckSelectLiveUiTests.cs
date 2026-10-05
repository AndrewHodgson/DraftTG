using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using DraftTG.Application;
using DraftTG.ArenaIntegration;
using DraftTG.Data;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;

namespace DraftTG.App.Tests;

public sealed partial class MainWindowViewModelTests
{
    private static string[] CompletionLines() => File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "deckselect-woe-completion-live.jsonl"));
    private sealed class CompletionUiSource : IArenaLogSource
    {
        private readonly Channel<string> _lines = Channel.CreateUnbounded<string>();
        public void Send(string line) => _lines.Writer.TryWrite(line);
        public async IAsyncEnumerable<ArenaLogSourceEvent> ReadEventsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        { await foreach (var line in _lines.Reader.ReadAllAsync(cancellationToken)) yield return new ArenaLogSourceEvent.Line(line); }
    }
    private sealed class CompletionUiRatings(CardCatalog catalog) : ISeventeenLandsCardRatingsClient
    {
        public Task<SeventeenLandsRatingsResult> LoadAsync(string expansion, SeventeenLandsFormat format,
            bool forceRefresh = false, CancellationToken cancellationToken = default) => Task.FromResult(new SeventeenLandsRatingsResult(expansion, format,
                catalog.Cards.Select(c => new SeventeenLandsRating(c.Name, GameInHandWinRate: .55, GameInHandGameCount: 10000)), SeventeenLandsSource.Cache));
    }
    private static MainWindowViewModel CompletionViewModel(CompletionUiSource source)
    {
        var data = new ScryfallCardCatalogDecoder().DecodeCatalogData(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "deckselect-woe-cards.json")));
        var runtime = new DraftTGRuntime(new(source, new(), new(), new(new(data))), data.Catalog, DraftTGCardDataStatus.Cache, null, null,
            Decks: new(new(new LimitedStatisticsService(new CompletionUiRatings(data.Catalog), data.Catalog))));
        return CreateViewModel(new ImmediateRuntimeFactory(runtime));
    }

    [Fact]
    public async Task RawLiveCompletionAutomaticallyUpdatesRailPoolAndBaselineThenNewDraftClearsThem()
    {
        var source = new CompletionUiSource(); await using var vm = CompletionViewModel(source); vm.Start();
        foreach (var line in CompletionLines()) source.Send(line);
        await WaitUntilAsync(() => vm.BaselineDeckResult?.Availability == DeckBuildAvailability.Ready);
        Assert.Equal("Draft complete", vm.StatusText); Assert.True(vm.HasBaselineDeckPanel);
        Assert.Equal(42, vm.DraftPool!.TotalCardCount); Assert.Equal(DraftPoolCompleteness.Complete, vm.DraftPool.Completeness);
        Assert.Equal(42, vm.PoolAnalysis!.TotalCardCount); Assert.False(vm.IsCurrentPackVisible);
        Assert.Contains("40 cards · 23 spells / 17 lands", vm.BaselineDeckSummaryText); Assert.Empty(vm.DiagnosticText);
        source.Send("""[UnityCrossThreadLogger]==> EventJoin {"id":"entry-2","request":{"EventName":"QuickDraft_WOE_20260929","EntryCurrencyType":"Gold","EntryCurrencyPaid":10000}}""");
        await WaitUntilAsync(() => !vm.HasBaselineDeckPanel);
        Assert.Null(vm.BaselineDeckResult); Assert.Equal(0, vm.DraftPool!.TotalCardCount);
    }

    [Fact]
    public async Task CompletionMismatchStillClearsActionablePackAndDisplaysWarningWithProvisionalPool()
    {
        var source = new CompletionUiSource(); await using var vm = CompletionViewModel(source); vm.Start();
        source.Send(CompletionLines()[0]); await WaitUntilAsync(() => vm.IsCurrentPackVisible);
        var outer = JsonNode.Parse(CompletionLines()[3])!.AsObject();
        var payload = JsonNode.Parse(outer["Payload"]!.GetValue<string>())!.AsObject();
        payload["PickedCards"]![0] = "999999"; outer["Payload"] = payload.ToJsonString(); source.Send(outer.ToJsonString());
        await WaitUntilAsync(() => vm.BaselineDeckResult?.Deck is not null);
        Assert.Equal("Draft complete", vm.StatusText); Assert.False(vm.IsCurrentPackVisible);
        Assert.Contains("DeckSelect PickedCards mismatch", vm.DiagnosticText);
        Assert.Equal(41, vm.DraftPool!.TotalCardCount); Assert.Equal(DraftPoolCompleteness.Unknown, vm.DraftPool.Completeness);
        Assert.Equal(DeckBuildAvailability.ProvisionalUnknownPool, vm.BaselineDeckResult!.Availability);
        Assert.Contains("Provisional", vm.BaselineDeckSummaryText);
    }
}
