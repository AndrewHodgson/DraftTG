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
    private readonly List<Task> _workers = [];
    private readonly List<CancellationTokenSource> _cancellations = [];
    private CancellationTokenSource? _pending;
    private LimitedStatisticsContext? _context;
    private DraftSnapshot? _snapshot;
    private string? _eventName;
    private ArenaDraftIdentifier? _draftId;
    private long _generation;
    private bool _disposed;

    public IAsyncEnumerable<LimitedStatisticsUpdate> ReadUpdatesAsync(CancellationToken cancellationToken = default) =>
        _updates.Reader.ReadAllAsync(cancellationToken);

    public void Observe(DraftSessionUpdate update)
    {
        if (update.Diagnostic is not null) return;
        lock (_gate)
        {
            if (_disposed) return;
            _snapshot = update.SnapshotResult.Snapshot;
            var newSession = _draftId != update.ArenaState.DraftIdentifier || _eventName != update.ArenaState.EventName;
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
                if (newSession) _loaded.Clear();
                _pending?.Cancel();
                _pending = null;
                _context = context;
            }
            if (context is null)
            {
                Publish(new(_snapshot, false, null, "Draft expansion or format is unknown or unsupported."));
                return;
            }
            if (_loaded.TryGetValue(context, out var loaded))
            {
                Publish(new(_snapshot, false, service.Map(loaded, _snapshot)));
                return;
            }
            Publish(new(_snapshot, true, null));
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
                Publish(new(_snapshot, false, service.Map(loaded, _snapshot)));
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
                    Publish(new(_snapshot, false, service.Map(failed, _snapshot)));
                }
        }
    }

    private void Publish(LimitedStatisticsUpdate update) => _updates.Writer.TryWrite(update);

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
