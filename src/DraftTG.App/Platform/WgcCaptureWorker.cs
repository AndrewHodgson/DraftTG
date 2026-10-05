#if WINDOWS
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace DraftTG.App.Platform;

/// <summary>One stable MTA lifetime, including WinRT projection collection, rather than tearing down a thread-pool apartment per frame.</summary>
internal sealed class WgcCaptureWorker : IDisposable
{
    private readonly BlockingCollection<(Func<ArenaRegionFrame> Work, TaskCompletionSource<ArenaRegionFrame> Result)> _queue = new();
    private readonly Thread _thread;
    private int _disposed;
    public CaptureFailure? ShutdownFailure { get; private set; }
    public WgcCaptureWorker(Action? releaseOwnedResources = null)
    {
        _thread = new Thread(() =>
        {
            var initialized = false;
            try
            {
                foreach (var job in _queue.GetConsumingEnumerable())
                {
                    try
                    {
                        if (!initialized)
                        {
                            var result = RoInitialize(1);
                            if (result < 0) throw new CaptureBackendInitializationException("Cannot initialize WGC worker apartment.",
                                Marshal.GetExceptionForHR(result), "WGC worker apartment initialization");
                            initialized = true;
                        }
                        job.Result.TrySetResult(job.Work());
                    }
                    catch (Exception ex) { job.Result.TrySetException(ex); }
                }
            }
            finally
            {
                try { releaseOwnedResources?.Invoke(); }
                catch (Exception ex) { ShutdownFailure = CaptureFailure.From("WGC worker resource shutdown", ex); System.Diagnostics.Trace.TraceError(ShutdownFailure.Text); }
                finally { if (initialized) RoUninitialize(); _queue.Dispose(); }
            }
        }) { IsBackground = true, Name = "DraftTG WGC owner" };
        _thread.SetApartmentState(ApartmentState.MTA); _thread.Start();
    }
    public Task<ArenaRegionFrame> RunAsync(Func<ArenaRegionFrame> work)
    {
        var result = new TaskCompletionSource<ArenaRegionFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        try { _queue.Add((work, result)); }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException) { result.SetException(new ObjectDisposedException(nameof(WgcCaptureWorker))); }
        return result.Task;
    }
    public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) _queue.CompleteAdding(); }
    [DllImport("combase.dll")] private static extern int RoInitialize(uint type);
    [DllImport("combase.dll")] private static extern void RoUninitialize();
}
#endif
