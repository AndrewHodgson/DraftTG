#if WINDOWS
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DraftTG.App.Platform;

/// <summary>A harmless developer-owned, normally composed window; never targets or sends input to Arena.</summary>
internal sealed class WgcStressFixture : IDisposable
{
    private readonly Thread _thread;
    private readonly TaskCompletionSource<nint> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private nint _handle;
    public WgcStressFixture()
    {
        _thread = new Thread(() =>
        {
            var hwnd = CreateWindowEx(0, "STATIC", "DraftTG WGC lifecycle fixture", 0x10CF0000, 40, 40, 960, 700, 0, 0, 0, 0);
            if (hwnd == 0) { _ready.TrySetException(new Win32Exception(Marshal.GetLastWin32Error())); return; }
            _ready.TrySetResult(hwnd);
            try { while (GetMessage(out var message, 0, 0, 0) > 0) { TranslateMessage(ref message); DispatchMessage(ref message); } }
            finally { DestroyWindow(hwnd); }
        }) { IsBackground = true, Name = "DraftTG WGC stress fixture" };
        _thread.Start(); _handle = _ready.Task.GetAwaiter().GetResult();
    }
    public void Present(int cycle) { SetWindowText(_handle, $"DraftTG WGC lifecycle fixture — cycle {cycle}"); InvalidateRect(_handle, 0, true); }
    public ArenaWindowInspection Inspect()
    {
        if (!GetClientRect(_handle, out var rect)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var point = new NativePoint(); if (!ClientToScreen(_handle, ref point)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return new(true, new(_handle, point.X, point.Y, rect.Right, rect.Bottom, GetDpiForWindow(_handle) / 96d, false)
            { ProcessId = Environment.ProcessId, Title = "DraftTG stress fixture", WindowClass = "STATIC" });
    }
    public void Dispose()
    {
        if (_handle == 0) return;
        var threadId = GetWindowThreadProcessId(_handle, out _); PostThreadMessage(threadId, 0x12, 0, 0);
        _thread.Join(TimeSpan.FromSeconds(3)); _handle = 0;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeMessage { public nint Window; public uint Message; public nuint WParam; public nint LParam; public uint Time; public NativePoint Point; public uint Private; }
    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWindowEx(uint extended, string type, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint data);
    [DllImport("user32.dll", EntryPoint = "GetMessageW")] private static extern int GetMessage(out NativeMessage message, nint hwnd, uint minimum, uint maximum);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref NativeMessage message);
    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")] private static extern nint DispatchMessage(ref NativeMessage message);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll", EntryPoint = "PostThreadMessageW")] private static extern bool PostThreadMessage(uint thread, uint message, nuint wparam, nint lparam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint process);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetClientRect(nint hwnd, out NativeRect rect);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool ClientToScreen(nint hwnd, ref NativePoint point);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll", EntryPoint = "SetWindowTextW", CharSet = CharSet.Unicode)] private static extern bool SetWindowText(nint hwnd, string text);
    [DllImport("user32.dll")] private static extern bool InvalidateRect(nint hwnd, nint rect, bool erase);
}
#endif
