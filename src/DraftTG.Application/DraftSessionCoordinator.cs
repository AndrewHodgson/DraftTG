using System.Runtime.CompilerServices;
using DraftTG.ArenaIntegration;

namespace DraftTG.Application;

public sealed class DraftSessionAlreadyRunningException()
    : InvalidOperationException("This draft session coordinator is already running.");

/// <summary>
/// Sequentially owns source consumption, parsing, reconstructed state mutation,
/// and Domain snapshot adaptation for one monitoring run at a time.
/// </summary>
public sealed class DraftSessionCoordinator
{
    private readonly IArenaLogSource _logSource;
    private readonly ArenaDraftLogParser _parser;
    private readonly ArenaDraftStateEngine _stateEngine;
    private readonly ArenaDraftSnapshotAdapter _snapshotAdapter;
    private int _isRunning;

    public DraftSessionCoordinator(
        IArenaLogSource logSource,
        ArenaDraftLogParser parser,
        ArenaDraftStateEngine stateEngine,
        ArenaDraftSnapshotAdapter snapshotAdapter)
    {
        _logSource = logSource ?? throw new ArgumentNullException(nameof(logSource));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _stateEngine = stateEngine ?? throw new ArgumentNullException(nameof(stateEngine));
        _snapshotAdapter = snapshotAdapter ?? throw new ArgumentNullException(nameof(snapshotAdapter));
    }

    public IAsyncEnumerable<DraftSessionUpdate> RunAsync(
        CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _isRunning) != 0)
        {
            throw new DraftSessionAlreadyRunningException();
        }

        return RunCoreAsync(cancellationToken);
    }

    private async IAsyncEnumerable<DraftSessionUpdate> RunCoreAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0)
        {
            throw new DraftSessionAlreadyRunningException();
        }

        try
        {
            // Every run reconstructs from the source's byte-zero replay rather
            // than combining replayed facts with a prior in-memory session.
            _stateEngine.Apply(new ArenaDraftLogEvent.SourceReset());

            await using var sourceEnumerator = _logSource
                .ReadEventsAsync(cancellationToken)
                .GetAsyncEnumerator(cancellationToken);

            while (true)
            {
                bool hasNext;
                var cancelled = false;
                try
                {
                    hasNext = await sourceEnumerator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    hasNext = false;
                    cancelled = true;
                }

                if (cancelled) yield break;
                if (!hasNext) yield break;

                IReadOnlyList<ArenaDraftLogEvent>? parsedEvents = null;
                DraftSessionDiagnostic? diagnostic = null;
                try
                {
                    parsedEvents = _parser.Parse(sourceEnumerator.Current);
                }
                catch (ArenaDraftLogParseException error)
                {
                    diagnostic = new DraftSessionDiagnostic(
                        DraftSessionDiagnosticKind.ParseError,
                        error.Message,
                        error.Kind);
                }

                if (diagnostic is not null)
                {
                    var current = _stateEngine.Current;
                    yield return new DraftSessionUpdate(
                        current,
                        _snapshotAdapter.Convert(current),
                        diagnostic);
                    continue;
                }

                foreach (var draftEvent in parsedEvents!)
                {
                    var stateUpdate = _stateEngine.Apply(draftEvent);
                    if (!stateUpdate.Changed) continue;

                    yield return new DraftSessionUpdate(
                        stateUpdate.Snapshot,
                        _snapshotAdapter.Convert(stateUpdate.Snapshot),
                        (draftEvent is ArenaDraftLogEvent.PickedCardsObserved
                            or ArenaDraftLogEvent.DraftCompleted { Completion.Origin: ArenaDraftCompletionOrigin.DeckSelection })
                            && stateUpdate.Snapshot.PickedCardsDiagnostic is { } warning
                            ? new(DraftSessionDiagnosticKind.PickedCardsSnapshot, warning.Message,
                                PickedCardsDiagnosticKind: warning.Kind) : null);
                }
            }
        }
        finally
        {
            Volatile.Write(ref _isRunning, 0);
        }
    }
}
