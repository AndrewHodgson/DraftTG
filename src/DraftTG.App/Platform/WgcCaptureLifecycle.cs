using DraftTG.Application;

namespace DraftTG.App.Platform;

internal enum CaptureLifecycleState { Idle, Queued, Initializing, Capturing, FrameReady, Disposing, Faulted }
internal sealed record CaptureFailure(string Operation, string ExceptionType, int HResult, string Message)
{
    public static CaptureFailure From(string operation, Exception exception)
    {
        if (exception is ICaptureFailureLocation initialization && initialization.Operation is { } exact)
            operation = exact;
        while (exception.InnerException is { } inner) exception = inner;
        return new(operation, exception.GetType().FullName ?? exception.GetType().Name, exception.HResult, exception.Message);
    }
    public string Text => $"{Operation}; {ExceptionType}; HRESULT 0x{HResult:X8}; {Message}";
}
internal sealed record CaptureLifecycleSnapshot(CaptureLifecycleState State, string Operation, int OwnedRequests,
    long CompletedRequests, CaptureFailure? LastFailure = null, int PersistentDevices = 0, int PersistentItems = 0);
internal interface ICaptureLifecycleSource { CaptureLifecycleSnapshot Lifecycle { get; } }
internal interface IWgcCaptureRequest : IDisposable
{
    ArenaRegionFrame Capture(ArenaWindowGeometry window, NormalizedDraftRegion region, long generation,
        Action<string> stage, CancellationToken token);
}

/// <summary>One owner through teardown, even after the caller times out. Sessions/pools are request scoped; the backend may retain its device/item.</summary>
internal sealed class WgcCaptureLifecycle(Func<IWgcCaptureRequest> factory,
    Func<Func<ArenaRegionFrame>, Task<ArenaRegionFrame>>? execute = null) : ICaptureLifecycleSource
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _stateGate = new();
    private CaptureLifecycleSnapshot _state = new(CaptureLifecycleState.Idle, "No capture requested", 0, 0);
    public CaptureLifecycleSnapshot Lifecycle { get { lock (_stateGate) return _state; } }

    public async Task<ArenaRegionFrame> CaptureAsync(ArenaWindowGeometry window, NormalizedDraftRegion region,
        long generation, Action<string> stage, CancellationToken token)
    {
        stage("Waiting for previous WGC request cleanup / capture ownership");
        await _gate.WaitAsync(token).ConfigureAwait(false);
        var started = false;
        try
        {
            ArenaRegionFrame Run()
            {
                started = true;
                IWgcCaptureRequest? request = null;
                ArenaRegionFrame? frame = null;
                Exception? failure = null;
                var operation = "WGC request creation";
                void Progress(string next)
                {
                    operation = next;
                    lock (_stateGate) _state = _state with { Operation = next,
                        State = next.StartsWith("Disposing", StringComparison.Ordinal) ? CaptureLifecycleState.Disposing
                            : next == "Capture source initialized" || next == "Session StartCapture"
                            || next.StartsWith("Waiting for Windows.Graphics.Capture frame", StringComparison.Ordinal)
                            || next.StartsWith("Texture readback", StringComparison.Ordinal)
                            ? CaptureLifecycleState.Capturing : CaptureLifecycleState.Initializing };
                    stage(next);
                }
                lock (_stateGate) _state = _state with { State = CaptureLifecycleState.Initializing, Operation = operation, OwnedRequests = 1 };
                try
                {
                    token.ThrowIfCancellationRequested();
                    request = factory();
                    frame = request.Capture(window, region, generation, Progress, token);
                    token.ThrowIfCancellationRequested();
                }
                catch (Exception ex) { failure = ex; }
                finally
                {
                    lock (_stateGate) _state = _state with { State = CaptureLifecycleState.Disposing };
                    try { stage("Disposing WGC request resources"); }
                    catch (Exception ex) { failure ??= ex; }
                    try { request?.Dispose(); }
                    catch (Exception ex)
                    {
                        if (failure is null) { failure = new IOException("WGC request cleanup failed.", ex); operation = "WGC resource disposal"; }
                        else System.Diagnostics.Trace.TraceError(CaptureFailure.From("Secondary WGC resource disposal failure", ex).Text);
                    }
                    lock (_stateGate) _state = _state with { OwnedRequests = 0, CompletedRequests = _state.CompletedRequests + 1,
                        State = failure is null ? CaptureLifecycleState.FrameReady : failure is OperationCanceledException ? CaptureLifecycleState.Idle : CaptureLifecycleState.Faulted,
                        Operation = failure is null ? "Frame ready; native request disposed" : operation,
                        LastFailure = failure is not null && failure is not OperationCanceledException ? CaptureFailure.From(operation, failure) : _state.LastFailure };
                }
                if (failure is not null)
                {
                    frame?.Dispose();
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
                }
                return frame!;
            }
            return await (execute is null ? Task.Run(Run, CancellationToken.None) : execute(Run)).ConfigureAwait(false);
        }
        catch (Exception ex) when (!started)
        {
            var failure = CaptureFailure.From("WGC worker dispatch", ex);
            lock (_stateGate) _state = _state with { State = CaptureLifecycleState.Faulted, Operation = failure.Operation,
                OwnedRequests = 0, CompletedRequests = _state.CompletedRequests + 1, LastFailure = failure };
            throw;
        }
        finally { _gate.Release(); }
    }
}
