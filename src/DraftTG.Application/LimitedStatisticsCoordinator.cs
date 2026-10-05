using System.Threading.Channels;
using DraftTG.ArenaIntegration;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;

namespace DraftTG.Application;

/// <summary>Observe never waits for I/O; a separate worker owns each environment load.</summary>
public sealed class LimitedStatisticsCoordinator(LimitedStatisticsService service, IDisposable? ownedResource = null)
    : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Channel<LimitedStatisticsUpdate> _updates = Channel.CreateBounded<LimitedStatisticsUpdate>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });
    private readonly Dictionary<LimitedStatisticsContext, LoadedLimitedStatistics> _loaded = [];
    private readonly Dictionary<ArchetypeStatisticsKey, LoadedArchetypeStatistics> _pairs = [];
    private readonly Dictionary<ArchetypeStatisticsKey, CancellationTokenSource> _pairPending = [];
    private int _pairRequests;
    private readonly Dictionary<SuccessfulDeckKey, SuccessfulDeckLoadResult> _trophies = [];
    private readonly Dictionary<SuccessfulDeckKey, CancellationTokenSource> _trophyPending = [];
    private int _trophyRequests;
    private readonly List<Task> _workers = [];
    private readonly List<CancellationTokenSource> _cancellations = [];
    private CancellationTokenSource? _pending;
    private LimitedStatisticsContext? _context;
    private DraftSnapshot? _snapshot;
    private DraftPackObservationHistory _observations = DraftPackObservationHistory.Empty;
    private string? _eventName;
    private ArenaDraftIdentifier? _draftId;
    private long _generation;
    private long _revision;
    private bool _disposed;

    public IAsyncEnumerable<LimitedStatisticsUpdate> ReadUpdatesAsync(CancellationToken cancellationToken = default) =>
        _updates.Reader.ReadAllAsync(cancellationToken);

    public void Observe(DraftSessionUpdate update)
    {
        if (update.Diagnostic is not null) return;
        lock (_gate)
        {
            if (_disposed) return;
            _revision++;
            _snapshot = update.SnapshotResult.Snapshot;
            var newSession = _draftId != update.ArenaState.DraftIdentifier || _eventName != update.ArenaState.EventName;
            if (newSession) _observations = DraftPackObservationHistory.Empty;
            _observations = _observations.Observe(_snapshot?.CurrentPack, service.CardCatalog,
                update.SnapshotResult.ResolvedHistory ?? _snapshot?.History);
            _draftId = update.ArenaState.DraftIdentifier;
            _eventName = update.ArenaState.EventName;
            var context = update.ArenaState.Status == ArenaDraftSessionStatus.Active ? service.Resolve(update) : null;
            // A temporary gap between packs must not forget an inferred environment.
            if (!newSession && _snapshot is null && update.ArenaState.Status == ArenaDraftSessionStatus.Active
                && DraftExpansionResolver.FormatFor(update.ArenaState.Mode?.Kind) is not null)
                context ??= _context;
            if (context != _context || newSession)
            {
                _generation++;
                if (newSession) { _loaded.Clear(); _pairs.Clear(); _pairRequests = 0; }
                if (newSession) { _trophies.Clear(); _trophyRequests = 0; }
                foreach (var trophy in _trophyPending.Values) trophy.Cancel();
                _trophyPending.Clear();
                foreach (var pair in _pairPending.Values) pair.Cancel();
                _pairPending.Clear();
                _pending?.Cancel();
                _pending = null;
                _context = context;
            }
            if (context is null)
            {
                Publish(new(_snapshot, false, null, "Draft expansion or format is unknown or unsupported.", _observations));
                return;
            }
            if (_loaded.TryGetValue(context, out var loaded))
            {
                // Scoring runs off the monitoring/UI thread, even for cached environments.
                QueueLoadedUpdate(loaded);
                return;
            }
            Publish(new(_snapshot, true, null, observationHistory: _observations));
            if (_pending is not null) return;
            var cancellation = new CancellationTokenSource();
            _pending = cancellation;
            _cancellations.Add(cancellation);
            var generation = _generation;
            _workers.Add(Task.Run(() => LoadAsync(context, generation, cancellation.Token)));
        }
    }

    private async Task LoadAsync(LimitedStatisticsContext context, long generation, CancellationToken cancellationToken)
    {
        try
        {
            var loaded = await service.LoadEnvironmentAsync(context, cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                if (_disposed || generation != _generation || cancellationToken.IsCancellationRequested) return;
                _loaded[context] = loaded;
                QueueLoadedUpdate(loaded);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception)
        {
            lock (_gate)
                if (!_disposed && generation == _generation)
                {
                    var failed = new LoadedLimitedStatistics(context, context,
                        new(context.Expansion, LimitedStatisticsService.ProviderFormat(context.Format), [],
                            DraftTG.Data.SeventeenLandsSource.Unavailable,
                            diagnostic: "Limited statistics unavailable; draft tracking continues."));
                    _loaded[context] = failed;
                    QueueLoadedUpdate(failed);
                }
        }
    }

    // Called under _gate. Capture both generation and snapshot; a late calculation must
    // never publish a recommendation for a pack that has already advanced.
    private void QueueLoadedUpdate(LoadedLimitedStatistics loaded)
    {
        var snapshot = _snapshot;
        var generation = _generation;
        var observations = _observations;
        var revision = _revision;
        _workers.Add(Task.Run(() =>
        {
            var initial = service.CreateUpdate(loaded, snapshot, observations);
            LoadedArchetypeStatistics? pair = null;
            SuccessfulDeckLoadResult? trophy = null;
            var trophyStatus = TrophyDataStatus.Unavailable;
            var status = ArchetypeDataStatus.Unavailable;
            lock (_gate)
            {
                if (!IsCurrent(generation, revision, snapshot)) return;
                if (initial.ArchetypeRecommendation?.Profile.Active is { } active)
                {
                    var key = new ArchetypeStatisticsKey(loaded.Requested, active.Definition.Pair);
                    if (_pairs.TryGetValue(key, out pair))
                        status = new(false, pair.Ratings.Source switch
                        {
                            DraftTG.Data.SeventeenLandsSource.Live => LimitedStatisticsSource.Live,
                            DraftTG.Data.SeventeenLandsSource.Cache => LimitedStatisticsSource.Cache,
                            DraftTG.Data.SeventeenLandsSource.StaleCache => LimitedStatisticsSource.StaleCache,
                            _ => LimitedStatisticsSource.Unavailable
                        }, pair.Ratings.Diagnostic);
                    else if (service.CanLoadPairStatistics)
                    {
                        if (!_pairPending.ContainsKey(key) && _pairRequests < service.ArchetypeConfiguration.MaxPairDatasetsPerDraft)
                        {
                            _pairRequests++;
                            var cancellation = new CancellationTokenSource();
                            _cancellations.Add(cancellation); _pairPending[key] = cancellation;
                            _workers.Add(Task.Run(() => LoadPairAsync(key, generation, cancellation.Token)));
                        }
                        status = _pairPending.ContainsKey(key) ? new(true, LimitedStatisticsSource.Unavailable)
                            : new(false, LimitedStatisticsSource.Unavailable, "Pair request budget exhausted for this draft.");
                    }
                    else status = new(false, LimitedStatisticsSource.Unavailable, "Pair statistics provider unavailable.");
                    var trophyKey = new SuccessfulDeckKey(loaded.Requested.Expansion,
                        TrophyRecommendationEngine.Format(loaded.Requested.Format), active.Definition.Pair);
                    if (_trophies.TryGetValue(trophyKey, out trophy))
                        trophyStatus = new(false, trophy.Source, trophy.Diagnostic);
                    else if (service.CanLoadTrophyEvidence)
                    {
                        if (!_trophyPending.ContainsKey(trophyKey) && _trophyRequests < service.TrophyConfiguration.MaxCorporaPerDraft)
                        {
                            _trophyRequests++;
                            var cancellation = new CancellationTokenSource();
                            _cancellations.Add(cancellation); _trophyPending[trophyKey] = cancellation;
                            _workers.Add(Task.Run(() => LoadTrophyAsync(trophyKey, generation, cancellation.Token)));
                        }
                        trophyStatus = _trophyPending.ContainsKey(trophyKey) ? new(true, SuccessfulDeckSource.Unavailable)
                            : new(false, SuccessfulDeckSource.Unavailable, "Trophy request budget exhausted for this draft.");
                    }
                    else trophyStatus = new(false, SuccessfulDeckSource.Unavailable, "Successful-deck provider unavailable.");
                }
            }
            // Mapping/scoring stays off the monitor thread and outside the coordinator gate.
            var update = service.CreateUpdate(loaded, snapshot, initial.ObservationHistory, pair, status, trophy, trophyStatus);
            lock (_gate)
                if (IsCurrent(generation, revision, snapshot))
                {
                    _observations = update.ObservationHistory;
                    Publish(update);
                }
        }));
    }

    private bool IsCurrent(long generation, long revision, DraftSnapshot? snapshot) =>
        !_disposed && generation == _generation && revision == _revision && ReferenceEquals(snapshot, _snapshot);

    private async Task LoadPairAsync(ArchetypeStatisticsKey key, long generation, CancellationToken token)
    {
        LoadedArchetypeStatistics? loaded = null;
        try { loaded = await service.LoadPairAsync(key, token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        catch (Exception)
        {
            loaded = new(key, new(key.Context.Expansion, LimitedStatisticsService.ProviderFormat(key.Context.Format), [],
                DraftTG.Data.SeventeenLandsSource.Unavailable, diagnostic: "Exact pair statistics unavailable; using Lane result.", colorPair: key.Pair));
        }
        lock (_gate)
        {
            if (_disposed || generation != _generation || token.IsCancellationRequested) return;
            _pairPending.Remove(key); _pairs[key] = loaded;
            // Supersede a queued pre-affinity calculation even if the Arena snapshot did not change.
            _revision++;
            if (_context is { } context && _loaded.TryGetValue(context, out var environment)) QueueLoadedUpdate(environment);
        }
    }

    private void Publish(LimitedStatisticsUpdate update) => _updates.Writer.TryWrite(update);

    private async Task LoadTrophyAsync(SuccessfulDeckKey key, long generation, CancellationToken token)
    {
        SuccessfulDeckLoadResult loaded;
        try { loaded = await service.LoadTrophyAsync(key, token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        catch (Exception)
        { loaded = new(key, null, SuccessfulDeckSource.Unavailable, "Exact-format trophy evidence unavailable; using Archetype result."); }
        lock (_gate)
        {
            if (_disposed || generation != _generation || token.IsCancellationRequested) return;
            _trophyPending.Remove(key); _trophies[key] = loaded;
            // Queue the current snapshot, and choose its current active pair again. An older
            // pair may remain cached, but can never contaminate the new pair or draft.
            _revision++;
            if (_context is { } context && _loaded.TryGetValue(context, out var environment)) QueueLoadedUpdate(environment);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task[] workers;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var cancellation in _cancellations) cancellation.Cancel();
            workers = _workers.ToArray();
            _updates.Writer.TryComplete();
        }
        await Task.WhenAll(workers).ConfigureAwait(false);
        foreach (var cancellation in _cancellations) cancellation.Dispose();
        ownedResource?.Dispose();
    }
}
