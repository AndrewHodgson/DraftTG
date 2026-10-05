using System.Threading.Channels;
using DraftTG.ArenaIntegration;
using DraftTG.Domain;
using DraftTG.RecommendationEngine;

namespace DraftTG.Application;

/// <summary>Completed-only work. A generation fence rejects old sessions and changed final inventories.</summary>
public sealed class DeckConstructionCoordinator(DeckConstructionService service) : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Channel<DeckBuildUpdate> _updates = Channel.CreateBounded<DeckBuildUpdate>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });
    private readonly List<Task> _workers = [];
    private readonly List<CancellationTokenSource> _tokens = [];
    private CancellationTokenSource? _pending;
    private DraftPoolSnapshot? _pool;
    private ArenaDraftIdentifier? _draftId;
    private string? _eventName;
    private bool _completed;
    private string? _sessionIdentity;
    private long _generation;
    private bool _disposed;

    public IAsyncEnumerable<DeckBuildUpdate> ReadUpdatesAsync(CancellationToken token = default) => _updates.Reader.ReadAllAsync(token);

    public long Observe(DraftSessionUpdate update)
    {
        lock (_gate)
        {
            if (_disposed || update.Diagnostic?.Kind == DraftSessionDiagnosticKind.ParseError) return _generation;
            var pool = update.SnapshotResult.DraftPool;
            var completed = update.ArenaState.IsCompleted;
            if (_draftId == update.ArenaState.DraftIdentifier && _eventName == update.ArenaState.EventName
                && _completed == completed && Equals(_pool, pool)) return _generation;
            _pending?.Cancel();
            var generation = ++_generation;
            if (completed && (!_completed || _draftId != update.ArenaState.DraftIdentifier || _eventName != update.ArenaState.EventName))
                _sessionIdentity = $"completed-session:{generation}";
            if (!completed) _sessionIdentity = null;
            var sessionIdentity = _sessionIdentity!;
            _draftId = update.ArenaState.DraftIdentifier; _eventName = update.ArenaState.EventName; _pool = pool; _completed = completed;
            if (!completed)
            {
                _updates.Writer.TryWrite(new(generation, _draftId, _eventName, pool, false, DeckBuildResult.InProgress));
                return generation;
            }
            var token = new CancellationTokenSource(); _tokens.Add(token); _pending = token;
            _updates.Writer.TryWrite(new(generation, _draftId, _eventName, pool, true, null));
            _workers.Add(Task.Run(async () =>
            {
                try
                {
                    var suggestions = await service.BuildSuggestionsAsync(update, sessionIdentity, token.Token).ConfigureAwait(false);
                    var result = suggestions?.BaselineResult ?? new(DeckBuildAvailability.MissingRequiredMetadata, null, "Completed pool unavailable.");
                    lock (_gate) if (!_disposed && generation == _generation && !token.IsCancellationRequested)
                        _updates.Writer.TryWrite(new(generation, _draftId, _eventName, pool, false, result, suggestions));
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                catch (Exception error)
                {
                    lock (_gate) if (!_disposed && generation == _generation)
                        _updates.Writer.TryWrite(new(generation, _draftId, _eventName, pool, false,
                            new(DeckBuildAvailability.MissingRequiredMetadata, null, $"Suggested builds unavailable: {error.Message}")));
                }
            }));
            return generation;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task[] tasks;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var token in _tokens) token.Cancel();
            tasks = _workers.ToArray(); _updates.Writer.TryComplete();
        }
        await Task.WhenAll(tasks).ConfigureAwait(false);
        foreach (var token in _tokens) token.Dispose();
    }
}
