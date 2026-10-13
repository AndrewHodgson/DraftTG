using System.Runtime.CompilerServices;
using System.Threading.Channels;
using DraftTG.ArenaIntegration;
using DraftTG.ArenaIntegration.Memory;

namespace DraftTG.Application;

/// <summary>Opt-in composition over the existing log coordinator and snapshot adapter. Scoring stays unchanged.</summary>
public sealed class CaptureFreeDraftSession(DraftSessionCoordinator logs, ArenaDraftSnapshotAdapter adapter,
    ArenaDraftMemoryWatcher watcher) : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly Channel<DraftSessionUpdate> updates = Channel.CreateBounded<DraftSessionUpdate>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private ArenaDraftStateSnapshot? logState;
    private ArenaDraftMemoryUpdate? memory;
    private DraftSessionUpdate? latest;
    private long generation;
    public ArenaDraftMemoryWatcher Watcher => watcher;
    public ArenaDraftMemoryUpdate? MemoryUpdate { get { lock (gate) return memory; } }
    public CaptureFreeReconciliation? Reconciliation { get; private set; }
    public event Action<ArenaDraftMemoryUpdate, CaptureFreeReconciliation?>? Observed;
    public event Func<Task>? Invalidated;
    public event Action<string>? Diagnostic;
    public event Action? PickRetired;
    public long Generation => Interlocked.Read(ref generation);
    public bool IsCurrent(DraftSessionUpdate update) { lock (gate) return ReferenceEquals(latest, update); }

    private async Task ObserveMemory(ArenaDraftMemoryUpdate value)
    {
        bool invalidate;
        lock (gate)
        {
            memory = value;
            invalidate = value.CurrentPack is null || latest?.SnapshotResult.Snapshot is not null;
            Publish();
        }
        if (invalidate) await Retire().ConfigureAwait(false);
        Observed?.Invoke(value, Reconciliation);
    }
    private async Task Retire()
    {
        if (Invalidated is { } handlers)
            foreach (Func<Task> handler in handlers.GetInvocationList()) await handler().ConfigureAwait(false);
    }
    private async Task AcceptedPick(ArenaDraftStateSnapshot state)
    {
        lock (gate)
        {
            logState = state;
            Publish(); // Invalidate the ready token before waiting on UI or further identity resolution.
        }
        PickRetired?.Invoke();
        await Retire().ConfigureAwait(false);
    }
    private void Publish()
    {
        var current = memory?.CurrentPack ?? memory?.CurrentRead;
        if (current is null) return;
        Reconciliation = CaptureFreeDraftReconciler.Reconcile(memory!.CurrentPack is null
            ? current with { Status = ArenaMemoryStatus.TransientReadFailure, Detail = memory.CurrentRead.Status == ArenaMemoryStatus.Success
                ? "Awaiting a second matching coherent read" : memory.CurrentRead.Detail } : current, logState);
        var state = Reconciliation.State;
        var result = Reconciliation.CanScore ? adapter.Convert(state) : ArenaDraftSnapshotResult.Unavailable(DraftSnapshotAvailability.NoCurrentPack);
        // A log update that changes neither semantic state nor history must not restart scoring.
        if (latest is not null && Equals(latest.ArenaState, state) && latest.SnapshotResult.Availability == result.Availability) return;
        Interlocked.Increment(ref generation);
        latest = new(state, result);
        updates.Writer.TryWrite(latest);
    }
    private async Task ReadLogs(CancellationToken token)
    {
        try
        {
            await foreach (var update in logs.RunAsync(token).ConfigureAwait(false))
            {
                if (update.Diagnostic is not null) { Diagnostic?.Invoke(update.Diagnostic.Message); continue; }
                lock (gate) { logState = update.ArenaState; Publish(); }
                if (Reconciliation?.CanScore != true) await Retire().ConfigureAwait(false);
                if (memory is { } m) Observed?.Invoke(m, Reconciliation);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { Diagnostic?.Invoke("Player.log unavailable: " + ex.Message); lock (gate) { logState = null; Publish(); } await Retire(); }
    }
    private async Task ReadMemory(CancellationToken token)
    {
        try { await foreach (var value in watcher.WatchAsync(token: token).ConfigureAwait(false)) await ObserveMemory(value); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { updates.Writer.TryComplete(); }
    }
    public async IAsyncEnumerable<DraftSessionUpdate> RunAsync([EnumeratorCancellation] CancellationToken token = default)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        logs.CurrentPackPickAccepted += AcceptedPick;
        var logTask = ReadLogs(lifetime.Token); var memoryTask = Task.Run(() => ReadMemory(lifetime.Token), lifetime.Token);
        try { await foreach (var value in updates.Reader.ReadAllAsync(token).ConfigureAwait(false)) yield return value; }
        finally { logs.CurrentPackPickAccepted -= AcceptedPick; await lifetime.CancelAsync(); await Task.WhenAll(logTask, memoryTask); }
    }
    public ValueTask DisposeAsync() { watcher.Dispose(); return ValueTask.CompletedTask; }
}
