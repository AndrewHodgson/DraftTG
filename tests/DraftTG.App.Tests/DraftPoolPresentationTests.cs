using DraftTG.ArenaIntegration;
using DraftTG.Domain;

namespace DraftTG.App.Tests;

public sealed partial class MainWindowViewModelTests
{
    [Fact]
    public void CompletedDraftKeepsPoolAnalysisAndEntryDiagnosticsWhileNewDraftReplacesIt()
    {
        var viewModel = ReadyViewModel();
        var state = State(ArenaDraftSessionStatus.Active, ArenaDraftMode.Quick, [101], pack: 3, pick: 14)
            with { RecoveredPool = new(ArenaDraftCoordinate.Create(3, 14), new(Enumerable.Repeat(ArenaCardIdentifier.Create(101), 41))) };
        viewModel.ApplySessionUpdate(Update(state));
        Assert.Equal(41, viewModel.PoolAnalysis!.TotalCardCount);
        state = state with { Status = ArenaDraftSessionStatus.Completed, CurrentPack = null,
            RecoveredPool = new(null, new(Enumerable.Repeat(ArenaCardIdentifier.Create(101), 42))) };
        viewModel.ApplySessionUpdate(Update(state));
        Assert.True(viewModel.HasDraftPool);
        Assert.False(viewModel.IsCurrentPackVisible);
        Assert.Equal(42, viewModel.PoolAnalysis!.TotalCardCount);
        Assert.Equal(DraftPoolCompleteness.Complete, viewModel.DraftPool!.Completeness);
        Assert.Contains("Draft complete", viewModel.PoolSummaryText);
        Assert.Contains("Cards: 42", viewModel.PoolSummaryText);
        Assert.Contains("Alpha ×42", viewModel.PoolEntryDiagnosticsText);
        Assert.Contains("ID: domain-a", viewModel.PoolEntryDiagnosticsText);
        Assert.Contains("MV: Unknown", viewModel.PoolEntryDiagnosticsText);
        Assert.Contains("Creature: False", viewModel.PoolEntryDiagnosticsText);
        Assert.Contains("P/T: Unknown/Unknown", viewModel.PoolEntryDiagnosticsText);
        viewModel.ApplySessionUpdate(Update(State(ArenaDraftSessionStatus.Active, ArenaDraftMode.Quick, null)
            with { DraftIdentifier = ArenaDraftIdentifier.Create("draft-2") }));
        Assert.Equal(0, viewModel.PoolAnalysis!.TotalCardCount);
        Assert.Empty(viewModel.PoolEntryDiagnosticsText);
        viewModel.ApplySessionUpdate(Update(State(ArenaDraftSessionStatus.Idle, ArenaDraftMode.Quick, null)));
        Assert.False(viewModel.HasDraftPool);
        Assert.Null(viewModel.PoolAnalysis);
    }
}
