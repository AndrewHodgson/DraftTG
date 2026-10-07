using System.Runtime.InteropServices;

namespace DraftTG.App.Platform;

internal interface IArenaWindowEvents : IDisposable
{
    event Action? Changed;
    bool IsRegistered { get; }
    void Track(ArenaWindowGeometry? window);
}

/// <summary>Coalesces notifications onto the UI dispatcher; queued callbacks cannot outlive disposal.</summary>
internal sealed class ArenaWindowEventPump : IDisposable
{
    private readonly IArenaWindowEvents _source;
    private readonly Action<Action> _post;
    private readonly Action _refresh;
    private int _pending, _disposed;
    public ArenaWindowEventPump(IArenaWindowEvents source, Action<Action> post, Action refresh)
    { _source = source; _post = post; _refresh = refresh; source.Changed += Changed; }
    public void Track(ArenaWindowGeometry? window) { if (Volatile.Read(ref _disposed) == 0) _source.Track(window); }
    private void Changed()
    {
        if (Volatile.Read(ref _disposed) != 0 || Interlocked.Exchange(ref _pending, 1) != 0) return;
        try
        {
            _post(() =>
            {
                Interlocked.Exchange(ref _pending, 0);
                if (Volatile.Read(ref _disposed) == 0) _refresh();
            });
        }
        catch { Interlocked.Exchange(ref _pending, 0); throw; }
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _source.Changed -= Changed; _source.Dispose();
    }
}

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate void ArenaWinEventCallback(nint hook, uint eventType, nint hwnd, int objectId,
    int childId, uint eventThread, uint eventTime);
internal interface IArenaWinEventApi
{
    nint Register(uint first, uint last, ArenaWinEventCallback callback, uint processId);
    bool Unregister(nint hook);
}

/// <summary>
/// Read-only, out-of-context notifications. Track/Dispose must run on the installing UI thread,
/// which owns a Win32 message loop; callbacks only raise a queued inspection request.
/// </summary>
internal sealed class WindowsArenaWindowEvents : IArenaWindowEvents
{
    internal const uint Foreground = 0x0003, MinimizeStart = 0x0016, MinimizeEnd = 0x0017;
    internal const uint ObjectCreate = 0x8000, ObjectDestroy = 0x8001, ObjectShow = 0x8002,
        ObjectHide = 0x8003, LocationChange = 0x800B;
    private readonly IArenaWinEventApi _api;
    private readonly ArenaWinEventCallback _callback;
    private readonly List<nint> _hooks = [];
    private GCHandle _root;
    private ArenaWindowGeometry? _target;
    private int _ownerThread;
    private bool _disposed;
    public WindowsArenaWindowEvents(IArenaWinEventApi? api = null)
    {
        _api = api ?? new NativeApi(); _callback = OnEvent;
        _root = GCHandle.Alloc(_callback);
    }
    public event Action? Changed;
    public bool IsRegistered => _target is not null && _hooks.Count == 4;
    public void Track(ArenaWindowGeometry? window)
    {
        if (_disposed) return;
        AssertOwnerThread();
        if (_target?.Handle == window?.Handle && _target?.ProcessId == window?.ProcessId && (window is null || IsRegistered)) return;
        Unregister(); _target = null;
        if (_hooks.Count != 0) return; // Keep callback rooted if the OS did not remove an old registration.
        _target = window;
        if (window is null || window.ProcessId <= 0) return;
        try
        {
            foreach (var (first, last) in new[] { (LocationChange, LocationChange), (Foreground, Foreground),
                (MinimizeStart, MinimizeEnd), (ObjectCreate, ObjectHide) })
            {
                var hook = _api.Register(first, last, _callback, (uint)window.ProcessId);
                if (hook == 0) { Unregister(); return; } // Two-second health polling remains available.
                _hooks.Add(hook);
            }
        }
        catch (Exception error) { Unregister(); System.Diagnostics.Trace.WriteLine("Arena WinEvent registration failed: " + error.Message); }
    }
    private void OnEvent(nint hook, uint eventType, nint hwnd, int objectId, int childId, uint thread, uint time)
    {
        // Process-scoped registrations plus exact HWND and window-object filtering.
        if (_disposed || _target is null || hwnd != _target.Handle || !_hooks.Contains(hook)) return;
        if (eventType >= ObjectCreate && (objectId != 0 || childId != 0)) return;
        if (eventType is not (LocationChange or Foreground or MinimizeStart or MinimizeEnd
            or ObjectCreate or ObjectDestroy or ObjectShow or ObjectHide)) return;
        try { Changed?.Invoke(); }
        catch (Exception error) { System.Diagnostics.Trace.WriteLine("Arena WinEvent callback failed: " + error.Message); }
    }
    private void AssertOwnerThread()
    {
        var current = Environment.CurrentManagedThreadId;
        if (_ownerThread == 0) _ownerThread = current;
        if (_ownerThread != current) throw new InvalidOperationException("WinEvent registration/disposal requires its installing thread.");
    }
    private void Unregister()
    {
        for (var i = _hooks.Count - 1; i >= 0; i--)
            if (_api.Unregister(_hooks[i])) _hooks.RemoveAt(i);
            else System.Diagnostics.Trace.WriteLine("Arena WinEvent unregister failed; callback retained and disabled.");
    }
    public void Dispose()
    {
        if (_disposed && _hooks.Count == 0) return;
        AssertOwnerThread(); _disposed = true; Unregister(); _target = null; Changed = null;
        if (_hooks.Count == 0 && _root.IsAllocated) _root.Free();
    }
    private sealed class NativeApi : IArenaWinEventApi
    {
        public nint Register(uint first, uint last, ArenaWinEventCallback callback, uint processId) =>
            SetWinEventHook(first, last, 0, callback, processId, 0, 0x0002); // OUTOFCONTEXT (0) | SKIPOWNPROCESS
        public bool Unregister(nint hook) => UnhookWinEvent(hook);
        [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWinEventHook(uint first, uint last,
            nint module, ArenaWinEventCallback callback, uint processId, uint threadId, uint flags);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWinEvent(nint hook);
    }
}
