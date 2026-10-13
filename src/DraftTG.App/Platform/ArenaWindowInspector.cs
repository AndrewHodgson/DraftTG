using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace DraftTG.App.Platform;

// Shared read-only HWND/client inspection. No capture backend, DC or bitmap is created.
internal static class ArenaWindowInspector
{
    internal static bool IsWindowedStyle(long style) => (style & 0xC00000) == 0xC00000;
    public static ArenaWindowInspection InspectWindow()
    {
        var processes = DraftTG.ArenaIntegration.ArenaProcessDiscovery.GetCandidates();
        try
        {
            if (processes.Length == 0) return new(false, null, "MTGA process not found.");
            var ids = processes.Select(p => p.Id).ToHashSet();
            var candidates = new List<ArenaWindowGeometry>();
            var rejected = new List<string>();
            Exception? failure = null;
            EnumWindow callback = (hwnd, _) =>
            {
                try
                {
                    GetWindowThreadProcessId(hwnd, out var pid);
                    if (!ids.Contains((int)pid)) return true;
                    var title = new StringBuilder(512); var type = new StringBuilder(256);
                    GetWindowText(hwnd, title, title.Capacity); GetClassName(hwnd, type, type.Capacity);
                    if (GetWindow(hwnd, 4) != 0 || type.ToString() != "UnityWndClass")
                    { rejected.Add($"0x{hwnd.ToInt64():X} PID {pid} '{title}' class '{type}' (owned/helper window)"); return true; }
                    if (!GetClientRect(hwnd, out var rect)) throw NativeFailure("GetClientRect");
                    var origin = new Point();
                    if (!ClientToScreen(hwnd, ref origin)) throw NativeFailure("ClientToScreen");
                    var dpi = GetDpiForWindow(hwnd);
                    if (dpi == 0) throw NativeFailure("GetDpiForWindow");
                    candidates.Add(new(hwnd, origin.X, origin.Y, rect.Right - rect.Left, rect.Bottom - rect.Top,
                        dpi / 96d, GetForegroundWindow() == hwnd)
                    { ProcessId = (int)pid, Title = title.ToString(), WindowClass = type.ToString(),
                        IsVisible = IsWindowVisible(hwnd), IsMinimized = IsIconic(hwnd) });
                    return true;
                }
                catch (Exception ex) { failure = ex; return false; }
            };
            var enumerated = EnumWindows(callback, 0);
            if (failure is not null) throw failure;
            if (!enumerated) throw NativeFailure("EnumWindows");
            var eligible = candidates.Where(c => c.IsVisible && !c.IsMinimized && c.Width >= 100 && c.Height >= 100).ToArray();
            if (eligible.Length > 1) return new(true, null, "Multiple MTGA Unity rendering HWNDs found; refusing ambiguous capture: "
                + string.Join(", ", eligible.Select(w => $"0x{w.Handle.ToInt64():X} PID {w.ProcessId} '{w.Title}'")));
            var selected = eligible.SingleOrDefault() ?? candidates.OrderByDescending(c => (long)c.Width * c.Height).FirstOrDefault();
            if (selected is null) return new(true, null, "MTGA PID(s) " + string.Join(",", ids)
                + "; no Unity rendering HWND found. " + string.Join("; ", rejected));
            selected = selected with { IsWindowed = IsWindowedStyle(GetWindowLongPtr(selected.Handle, -16).ToInt64()) };
            return new(true, selected);
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    private delegate bool EnumWindow(nint hwnd, nint parameter);
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    private static Win32Exception NativeFailure(string operation) => new(Marshal.GetLastWin32Error(), operation);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool EnumWindows(EnumWindow callback, nint parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint hwnd, StringBuilder text, int length);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint hwnd, StringBuilder text, int length);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetClientRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool ClientToScreen(nint hwnd, ref Point point);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint hwnd, uint command);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint window, int index);
}
