#if WINDOWS
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DraftTG.App.Platform;

/// <summary>Only the caller's thread DPI context changes; no Arena DPI or window mutation.</summary>
internal sealed class PhysicalPixelContext : IDisposable
{
    private readonly nint _previous;
    private PhysicalPixelContext()
    {
        _previous = SetThreadDpiAwarenessContext(-4); // PER_MONITOR_AWARE_V2
        if (_previous == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot establish physical-pixel coordinate context.");
    }
    public static PhysicalPixelContext Enter() => new();
    public static void ConfigureBenchmarkProcess()
    {
        if (!SetProcessDpiAwarenessContext(-4) && Marshal.GetLastWin32Error() != 5)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot establish benchmark DPI awareness.");
        if (GetAwarenessFromDpiAwarenessContext(GetThreadDpiAwarenessContext()) != 2)
            throw new InvalidOperationException("Benchmark process is not per-monitor DPI aware; WGC/DD coordinates cannot be compared safely.");
    }
    public void Dispose() { if (_previous != 0) SetThreadDpiAwarenessContext(_previous); }
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetThreadDpiAwarenessContext(nint context);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetProcessDpiAwarenessContext(nint context);
    [DllImport("user32.dll")] private static extern nint GetThreadDpiAwarenessContext();
    [DllImport("user32.dll")] private static extern int GetAwarenessFromDpiAwarenessContext(nint context);
}
#endif
