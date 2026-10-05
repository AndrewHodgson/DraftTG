using DraftTG.ArenaIntegration;
using DraftTG.Data;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;

namespace DraftTG.Application;

public sealed class DeckConstructionService(LimitedStatisticsService statistics,
    ISetArchetypeProfileCatalog? archetypes = null, BaselineDeckConfiguration? configuration = null,
    SuggestedDeckConfiguration? suggestedConfiguration = null)
{
    private readonly ISetArchetypeProfileCatalog _archetypes = archetypes ?? SetArchetypeProfileCatalog.Default;
    private readonly BaselineDeckBuilder _builder = new(configuration, statistics.ArchetypeConfiguration);

    public async Task<DeckBuildResult> BuildAsync(DraftSessionUpdate update, CancellationToken token = default)
    {
        if (!update.ArenaState.IsCompleted) return DeckBuildResult.InProgress;
        if (update.SnapshotResult.DraftPool is not { } pool)
            return new(DeckBuildAvailability.MissingRequiredMetadata, null, "Completed pool unavailable.");
        return _builder.Build(await PrepareInputAsync(update, pool, token).ConfigureAwait(false), token);
    }

    public async Task<SuggestedDeckSet?> BuildSuggestionsAsync(DraftSessionUpdate update, string sessionIdentity,
        CancellationToken token = default)
    {
        if (!update.ArenaState.IsCompleted || update.SnapshotResult.DraftPool is not { } pool) return null;
        var input = await PrepareInputAsync(update, pool, token).ConfigureAwait(false);
        // No additional pair requests: alternative selection uses overall values under Phase 10B's affinity rules.
        return new SuggestedDeckBuilder(_builder, suggestedConfiguration).Build(input, sessionIdentity, token);
    }

    private async Task<DeckBuildInput> PrepareInputAsync(DraftSessionUpdate update, DraftPoolSnapshot pool, CancellationToken token)
    {
        var catalog = statistics.CardCatalog;
        var context = statistics.Resolve(update);
        var input = new DeckBuildInput(pool, catalog, new(), new());
        if (context is not null)
        {
            // Existing mapper's identity scope only: no live pack or fabricated pick history is used by deck construction.
            var lookup = new DraftSnapshot(new(new(PackNumber.Create(1), PickNumber.Create(1)), pool.Entries.Select(e => e.CardIdentifier)),
                new(), context.Format == LimitedStatisticsFormat.TraditionalDraft ? DraftFormat.BestOfThree : DraftFormat.BestOfOne);
            try
            {
                var loaded = await statistics.LoadEnvironmentAsync(context, token).ConfigureAwait(false);
                var mapped = statistics.Map(loaded, lookup);
                input = input with { Statistics = mapped.Catalog, EnvironmentStatistics = mapped.EnvironmentCatalog,
                    StatisticsContext = context, Archetypes = _archetypes.Find(context.Expansion) };
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception) { input = input with { StatisticsContext = context, Archetypes = _archetypes.Find(context.Expansion) }; }
            var plan = DeckPlanSelector.Select(input, _builder.Configuration, statistics.ArchetypeConfiguration);
            if (plan?.Source == DeckPlanSource.ActiveArchetype && statistics.CanLoadPairStatistics)
            {
                try
                {
                    var loaded = await statistics.LoadPairAsync(new(context, plan.Pair), token).ConfigureAwait(false);
                    if (loaded.Ratings.Source != SeventeenLandsSource.Unavailable)
                    {
                        var mapped = LimitedStatisticsMapper.Map(loaded.Ratings.Rows, catalog, context, lookup);
                        input = input with { PairStatistics = new(context, plan.Pair, mapped.Catalog,
                            loaded.Ratings.Rows.Select(r => new PairGihStatistics(r.GameInHandWinRate, r.GameInHandGameCount))) };
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception) { /* Overall strength remains usable when optional pair evidence fails. */ }
            }
        }
        return input;
    }
}

public sealed record DeckBuildUpdate(long Generation, ArenaDraftIdentifier? DraftIdentifier, string? EventName,
    DraftPoolSnapshot? Pool, bool IsLoading, DeckBuildResult? Result, SuggestedDeckSet? Suggestions = null);
