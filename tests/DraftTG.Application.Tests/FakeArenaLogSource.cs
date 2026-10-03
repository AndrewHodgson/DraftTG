using System.Runtime.CompilerServices;
using DraftTG.ArenaIntegration;

namespace DraftTG.Application.Tests;

internal abstract record FakeArenaLogStep
{
    private FakeArenaLogStep() { }

    internal sealed record Emit(ArenaLogSourceEvent SourceEvent) : FakeArenaLogStep;
    internal sealed record Fail(Exception Exception) : FakeArenaLogStep;
    internal sealed record WaitForCancellation : FakeArenaLogStep;
}

internal sealed class FakeArenaLogSource : IArenaLogSource
{
    private readonly Queue<IReadOnlyList<FakeArenaLogStep>> _runs = [];
    private readonly TaskCompletionSource _firstRunStarted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _waitReached =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task FirstRunStarted => _firstRunStarted.Task;
    public Task WaitReached => _waitReached.Task;

    public void EnqueueRun(params FakeArenaLogStep[] steps) => _runs.Enqueue(steps);

    public IAsyncEnumerable<ArenaLogSourceEvent> ReadEventsAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_runs.TryDequeue(out var steps))
        {
            throw new InvalidOperationException("No fake Arena log run was configured.");
        }

        _firstRunStarted.TrySetResult();
        return ReadRunAsync(steps, cancellationToken);
    }

    public static FakeArenaLogStep Line(string text) =>
        new FakeArenaLogStep.Emit(new ArenaLogSourceEvent.Line(text));

    public static FakeArenaLogStep Reset() =>
        new FakeArenaLogStep.Emit(new ArenaLogSourceEvent.SourceReset());

    public static FakeArenaLogStep Error(Exception exception) =>
        new FakeArenaLogStep.Fail(exception);

    public static FakeArenaLogStep Wait() => new FakeArenaLogStep.WaitForCancellation();

    private async IAsyncEnumerable<ArenaLogSourceEvent> ReadRunAsync(
        IReadOnlyList<FakeArenaLogStep> steps,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var step in steps)
        {
            switch (step)
            {
                case FakeArenaLogStep.Emit emit:
                    yield return emit.SourceEvent;
                    break;
                case FakeArenaLogStep.Fail fail:
                    throw fail.Exception;
                case FakeArenaLogStep.WaitForCancellation:
                    _waitReached.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
                        .ConfigureAwait(false);
                    yield break;
            }
        }
    }
}
