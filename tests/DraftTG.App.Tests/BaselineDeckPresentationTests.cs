using DraftTG.Application;
using DraftTG.ArenaIntegration;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;

namespace DraftTG.App.Tests;

public sealed partial class MainWindowViewModelTests
{
    private static (MainWindowViewModel ViewModel, DraftSessionUpdate Session, DeckBuildResult Build) CompletedDeckUi(bool partial = false)
    {
        var cards = CatalogData().Catalog.Cards.Select((c, i) => c with
        { Colors = new([(MagicColor)(i == 0 ? 4 : i == 1 ? 2 : 1)]),
            GameplayMetadata = new(i == 0 ? 2 : 3, i == 0 ? "{1}{G}" : i == 1 ? "{2}{B}" : "{2}{U}", "Creature") }).ToArray();
        var catalog = new CardCatalog(cards); var vm = CreateViewModel(); vm.ApplyRuntime(CreateRuntime(new ControlledLogSource(), catalog: catalog));
        var state = State(ArenaDraftSessionStatus.Completed, ArenaDraftMode.Quick, null) with
        { RecoveredPool = new(null, new(Enumerable.Repeat(ArenaCardIdentifier.Create(101), partial ? 24 : 25)
            .Concat(Enumerable.Repeat(ArenaCardIdentifier.Create(102), 16)).Append(ArenaCardIdentifier.Create(103)))) };
        var update = Update(state); vm.ApplySessionUpdate(update);
        var build = new BaselineDeckBuilder().Build(new(update.SnapshotResult.DraftPool!, catalog,
            new(cards.Select(c => new LimitedCardStatistics(c.Identifier, 10000, GameInHandWinRate: .55)))));
        return (vm, update, build);
    }

    [Fact]
    public void CompletedPanelShowsCountsManaAndSideboardWithoutChangingBadgePresentation()
    {
        var (vm, update, build) = CompletedDeckUi(); var packEvents = 0; vm.PackPresentationChanged += _ => packEvents++;
        vm.ApplyDeckBuildUpdate(new(0, update.ArenaState.DraftIdentifier, update.ArenaState.EventName, update.SnapshotResult.DraftPool, false, build));
        Assert.True(vm.HasBaselineDeckPanel); Assert.False(vm.IsCurrentPackVisible);
        Assert.Contains("40 cards · 23 spells / 17 lands", vm.BaselineDeckSummaryText);
        Assert.Contains("(generated)", vm.BaselineDeckCardsText); Assert.Contains("Alpha ×", vm.BaselineDeckCardsText);
        Assert.Contains("Gamma ×1", vm.BaselineDeckSideboardText); Assert.Equal(0, packEvents);
        Assert.Empty(vm.CurrentPackPresentations);
    }

    [Fact]
    public void ProvisionalBuildIsLabelledAndResetOrStaleGenerationCannotRestoreOldDeck()
    {
        var (vm, update, build) = CompletedDeckUi(partial: true);
        vm.ApplyDeckBuildUpdate(new(0, update.ArenaState.DraftIdentifier, update.ArenaState.EventName, update.SnapshotResult.DraftPool, false, build));
        Assert.Contains("Provisional", vm.BaselineDeckSummaryText);
        vm.ApplyDeckBuildUpdate(new(999, update.ArenaState.DraftIdentifier, update.ArenaState.EventName, update.SnapshotResult.DraftPool, false, null));
        Assert.Same(build, vm.BaselineDeckResult);
        vm.ApplySessionUpdate(Update(State(ArenaDraftSessionStatus.Active, ArenaDraftMode.Quick, [101]) with { DraftIdentifier = ArenaDraftIdentifier.Create("draft-2") }));
        vm.ApplyDeckBuildUpdate(new(0, update.ArenaState.DraftIdentifier, update.ArenaState.EventName, update.SnapshotResult.DraftPool, false, build));
        Assert.False(vm.HasBaselineDeckPanel); Assert.Null(vm.BaselineDeckResult); Assert.Empty(vm.BaselineDeckCardsText);
    }
}
