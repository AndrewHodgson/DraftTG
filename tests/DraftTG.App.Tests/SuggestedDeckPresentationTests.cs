using DraftTG.Application;
using DraftTG.ArenaIntegration;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;

namespace DraftTG.App.Tests;

public sealed partial class MainWindowViewModelTests
{
    private static (MainWindowViewModel Vm, DraftSessionUpdate Update, DeckBuildInput Input, SuggestedDeckSet Set) SuggestedUi(bool partial = false, bool oneBuild = false)
    {
        var cards = CatalogData().Catalog.Cards.Select((c, i) => c with { Colors = new([(MagicColor)(i == 0 ? 4 : i == 1 ? 3 : 2)]),
            GameplayMetadata = new(2, i == 0 ? "{1}{G}" : i == 1 ? "{1}{R}" : "{1}{B}", "Creature") }).ToArray();
        var catalog = new CardCatalog(cards); var vm = CreateViewModel(); vm.ApplyRuntime(CreateRuntime(new ControlledLogSource(), catalog: catalog));
        var state = State(ArenaDraftSessionStatus.Completed, ArenaDraftMode.Quick, null) with { RecoveredPool = new(null,
            new(Enumerable.Repeat(ArenaCardIdentifier.Create(101), partial ? 19 : 20)
                .Concat(Enumerable.Repeat(ArenaCardIdentifier.Create(102), oneBuild ? 21 : 11))
                .Concat(Enumerable.Repeat(ArenaCardIdentifier.Create(103), oneBuild ? 1 : 11)))) };
        var update = Update(state); vm.ApplySessionUpdate(update);
        var input = new DeckBuildInput(update.SnapshotResult.DraftPool!, catalog,
            new(cards.Select((c, i) => new LimitedCardStatistics(c.Identifier, 10000, GameInHandWinRate: .55 + i * .01))));
        var set = new SuggestedDeckBuilder().Build(input, "session-A");
        vm.ApplyDeckBuildUpdate(new(0, state.DraftIdentifier, state.EventName, input.Pool, false, set.BaselineResult, set));
        return (vm, update, input, set);
    }
    private static void Publish(MainWindowViewModel vm, DraftSessionUpdate update, SuggestedDeckSet set) =>
        vm.ApplyDeckBuildUpdate(new(0, update.ArenaState.DraftIdentifier, update.ArenaState.EventName, update.SnapshotResult.DraftPool, false, set.BaselineResult, set));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectorHasOnlyAvailableBuildsAndRecommendedDefault(bool oneBuild)
    {
        var (vm, _, _, _) = SuggestedUi(oneBuild: oneBuild);
        Assert.Equal(oneBuild ? 1 : 2, vm.SuggestedDecks.Count);
        Assert.True(vm.SelectedSuggestedDeck!.IsRecommended); Assert.Contains("Recommended Baseline", vm.SuggestedDecks[0].Label);
        Assert.False(vm.HasSuggestedDeckDifference); Assert.Empty(vm.SuggestedDeckDifferenceText);
    }

    [Fact]
    public void SelectingAlternativeUpdatesOneMainSideboardAndDifferenceWithoutPackActions()
    {
        var (vm, _, input, _) = SuggestedUi(); var packActions = 0; vm.PackPresentationChanged += _ => packActions++;
        var alternative = vm.SuggestedDecks[1]; vm.SelectedSuggestedDeck = alternative;
        Assert.Same(alternative.Deck, vm.SelectedSuggestedDeck!.Deck);
        Assert.Contains(alternative.PlanLabel, vm.BaselineDeckSummaryText); Assert.Contains("40 cards · 23 spells / 17 lands", vm.BaselineDeckSummaryText);
        Assert.Equal(BaselineDeckPresentation.Cards(alternative.Deck, input.Catalog), vm.BaselineDeckCardsText);
        Assert.Equal(BaselineDeckPresentation.Sideboard(alternative.Deck, input.Catalog), vm.BaselineDeckSideboardText);
        Assert.True(vm.HasSuggestedDeckDifference); Assert.Contains("Compared with Recommended Build 1", vm.SuggestedDeckDifferenceText);
        Assert.Contains("Measured cards: 23 / 23", vm.BaselineDeckSummaryText); Assert.Equal(0, packActions); Assert.Empty(vm.CurrentPackPresentations);
    }

    [Fact]
    public void SelectionSurvivesPanelReopeningUnrelatedRefreshAndSameSessionRecomputation()
    {
        var (vm, update, input, _) = SuggestedUi(); using var hud = new OverlayViewModel(vm);
        vm.SelectedSuggestedDeck = vm.SuggestedDecks[1]; var selected = vm.SelectedSuggestedDeck!.Id;
        hud.SetPanel(RailPanel.None); hud.SetPanel(RailPanel.Status); vm.ApplySessionUpdate(update);
        Assert.Equal(selected, vm.SelectedSuggestedDeck!.Id);
        var refreshed = new SuggestedDeckBuilder().Build(input with { Statistics = new() }, "session-A"); Publish(vm, update, refreshed);
        Assert.Equal(selected, vm.SelectedSuggestedDeck!.Id);
        var correctedPool = new DraftPoolSnapshot(new(input.Pool.Inventory.CardIdentifiers.Skip(1)), DraftPoolCompleteness.Partial);
        var corrected = update with { SnapshotResult = update.SnapshotResult with { DraftPool = correctedPool } };
        vm.ApplySessionUpdate(corrected);
        Assert.Null(vm.SuggestedDeckSet); Assert.Empty(vm.BaselineDeckCardsText);
        Publish(vm, corrected, new SuggestedDeckBuilder().Build(input with { Pool = correctedPool }, "session-A"));
        Assert.Equal(selected, vm.SelectedSuggestedDeck!.Id); Assert.Contains("Provisional", vm.BaselineDeckSummaryText);
    }

    [Fact]
    public void MissingSelectedBuildOrChangedCompletedSessionDefaultsToRecommended()
    {
        var (vm, update, input, _) = SuggestedUi(); vm.SelectedSuggestedDeck = vm.SuggestedDecks[1];
        var smaller = new SuggestedDeckBuilder(configuration: new(maximumSuggestedBuilds: 1)).Build(input, "session-A"); Publish(vm, update, smaller);
        Assert.True(vm.SelectedSuggestedDeck!.IsRecommended);
        Publish(vm, update, new SuggestedDeckBuilder().Build(input, "session-A")); vm.SelectedSuggestedDeck = vm.SuggestedDecks[1];
        Publish(vm, update, new SuggestedDeckBuilder().Build(input, "session-B")); Assert.True(vm.SelectedSuggestedDeck!.IsRecommended);
    }

    [Fact]
    public void NewDraftClearsSuggestionsAndStaleSetCannotRestoreThem()
    {
        var (vm, update, _, set) = SuggestedUi(); vm.SelectedSuggestedDeck = vm.SuggestedDecks[1];
        vm.ApplySessionUpdate(Update(State(ArenaDraftSessionStatus.Active, ArenaDraftMode.Quick, [101]) with { DraftIdentifier = ArenaDraftIdentifier.Create("next") }));
        Publish(vm, update, set);
        Assert.Null(vm.SuggestedDeckSet); Assert.Null(vm.SelectedSuggestedDeck); Assert.Empty(vm.SuggestedDecks); Assert.False(vm.HasBaselineDeckPanel);
    }

    [Fact]
    public void ProvisionalAndRelaxationLabelsApplyToTheSelectedAlternative()
    {
        var (vm, update, input, _) = SuggestedUi(partial: true);
        input = input with { Catalog = new(input.Catalog.Cards.Select(c => c with { GameplayMetadata = new(6, c.GameplayMetadata.ManaCost, "Instant") })) };
        var set = new SuggestedDeckBuilder().Build(input, "session-A"); Publish(vm, update, set); vm.SelectedSuggestedDeck = vm.SuggestedDecks[1];
        Assert.Contains("Provisional", vm.BaselineDeckSummaryText); Assert.Contains("Composition relaxations: 3", vm.BaselineDeckSummaryText);
        Assert.Contains("High-cost cap relaxed", vm.BaselineDeckDiagnosticsText);
    }
}
