using DraftTG.Data;
using DraftTG.Domain;

namespace DraftTG.Application;

public sealed record DraftTGCardDataBootstrapResult(
    CardCatalog Catalog,
    ArenaCardResolver ArenaCardResolver,
    ArenaDraftSnapshotAdapter SnapshotAdapter,
    ScryfallCardDataLoadSource DataSource,
    DateTimeOffset? DatasetUpdatedAt,
    bool RefreshAttempted,
    ScryfallCardDataRefreshDiagnostic? RefreshDiagnostic);

/// <summary>Builds UI-independent runtime card dependencies from Data-layer bootstrap output.</summary>
/// <param name="arenaDatabase">Optional Arena local identity bridge; null keeps Scryfall arena_id resolution only.</param>
public sealed class DraftTGCardDataBootstrapper(IScryfallCardDataProvider cardDataProvider,
    IArenaPrintingIdentitySource? arenaDatabase = null)
{
    private readonly IScryfallCardDataProvider _cardDataProvider =
        cardDataProvider ?? throw new ArgumentNullException(nameof(cardDataProvider));

    public async Task<DraftTGCardDataBootstrapResult> BootstrapAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        var loadResult = await _cardDataProvider
            .LoadAsync(forceRefresh, cancellationToken)
            .ConfigureAwait(false);
        var resolver = new ArenaCardResolver(loadResult.CardData, arenaDatabase);
        var adapter = new ArenaDraftSnapshotAdapter(resolver);

        return new DraftTGCardDataBootstrapResult(
            loadResult.CardData.Catalog,
            resolver,
            adapter,
            loadResult.Source,
            loadResult.DatasetUpdatedAt,
            loadResult.RefreshAttempted,
            loadResult.RefreshDiagnostic);
    }
}
