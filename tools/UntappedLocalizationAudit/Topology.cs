using System.Diagnostics;
using static UntappedLocalizationAudit.Native;

namespace UntappedLocalizationAudit;

internal sealed record WindowInfo(
    int ZIndex, string Hwnd, uint Pid, string Process, string Class, string Title,
    Rect32 Window, Rect32? Client, Rect32? DwmFrame, bool Visible, bool Iconic, bool Zoomed, int Cloaked,
    string Style, string ExStyle, string StyleFlags, string ExStyleFlags, string Owner, string Parent,
    uint Dpi, string Monitor, string? Layered, string DisplayAffinity, int ChildCount);

internal sealed record MonitorInfo(string Handle, string Device, Rect32 Bounds, Rect32 Work, bool Primary,
    uint EffectiveDpi, int ModeWidth, int ModeHeight, int RefreshHz);

internal static class Topology
{
    private static readonly Dictionary<uint, string> Names = new();

    public static string ProcessName(uint pid)
    {
        if (Names.TryGetValue(pid, out var name)) return name;
        try { name = Process.GetProcessById((int)pid).ProcessName; } catch { name = "?"; }
        Names[pid] = name;
        return name;
    }

    public static bool IsUntapped(string process) => process.StartsWith("Untapped", StringComparison.OrdinalIgnoreCase);
    public static bool IsArena(string process) => process.Equals("MTGA", StringComparison.OrdinalIgnoreCase);

    public static List<nint> TopLevel()
    {
        var list = new List<nint>();
        EnumWindows((h, _) => { list.Add(h); return true; }, 0);
        return list;
    }

    public static List<nint> Children(nint root)
    {
        var list = new List<nint>();
        EnumChildWindows(root, (h, _) => { list.Add(h); return true; }, 0);
        return list;
    }

    public static nint ArenaWindow()
    {
        foreach (var h in TopLevel())
        {
            GetWindowThreadProcessId(h, out var pid);
            if (IsArena(ProcessName(pid)) && Text(h, false) == "UnityWndClass") return h;
        }
        return 0;
    }

    public static Rect32? Client(nint hwnd)
    {
        if (!GetClientRect(hwnd, out var c)) return null;
        var origin = new POINT();
        if (!ClientToScreen(hwnd, ref origin)) return null;
        return new Rect32(origin.X, origin.Y, c.Right - c.Left, c.Bottom - c.Top);
    }

    public static Rect32 WindowRect(nint hwnd) => GetWindowRect(hwnd, out var r) ? Rect32.From(r) : new Rect32(0, 0, 0, 0);

    public static WindowInfo Describe(nint h, int z)
    {
        GetWindowThreadProcessId(h, out var pid);
        GetWindowRect(h, out var wr);
        Rect32? dwm = DwmGetWindowAttribute(h, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT fr, 16) == 0 ? Rect32.From(fr) : null;
        DwmGetWindowAttribute(h, DWMWA_CLOAKED, out int cloaked, 4);
        var style = GetWindowLongPtr(h, GWL_STYLE).ToInt64();
        var ex = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
        string? layered = null;
        if ((ex & 0x80000) != 0)
            layered = GetLayeredWindowAttributes(h, out var key, out var alpha, out var flags)
                ? $"alpha={alpha} key=0x{key:X} flags=0x{flags:X} (LWA_COLORKEY=1, LWA_ALPHA=2)"
                : "UpdateLayeredWindow-style (GetLayeredWindowAttributes failed)";
        var affinity = GetWindowDisplayAffinity(h, out var a) ? a switch { 0 => "NONE", 1 => "MONITOR", 0x11 => "EXCLUDEFROMCAPTURE", _ => $"0x{a:X}" } : "unavailable";
        var monitor = MonitorFromWindow(h, 2);
        return new WindowInfo(z, Hex(h), pid, ProcessName(pid), Text(h, false), Text(h, true),
            Rect32.From(wr), Client(h), dwm, IsWindowVisible(h), IsIconic(h), IsZoomed(h), cloaked,
            $"0x{style:X8}", $"0x{ex:X8}", StyleFlags(style), ExStyleFlags(ex), Hex(GetWindow(h, GW_OWNER)), Hex(GetParent(h)),
            GetDpiForWindow(h), Hex(monitor), layered, affinity, Children(h).Count);
    }

    public static List<MonitorInfo> Monitors()
    {
        var list = new List<MonitorInfo>();
        EnumDisplayMonitors(0, 0, (nint m, nint hdc, ref RECT clip, nint data) =>
        {
            var info = new MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFOEX>() };
            GetMonitorInfo(m, ref info);
            GetDpiForMonitor(m, 0, out var dpi, out _);
            var mode = new DEVMODE { dmSize = (short)System.Runtime.InteropServices.Marshal.SizeOf<DEVMODE>() };
            EnumDisplaySettings(info.szDevice, -1, ref mode);
            list.Add(new MonitorInfo(Hex(m), info.szDevice, Rect32.From(info.rcMonitor), Rect32.From(info.rcWork),
                (info.dwFlags & 1) != 0, dpi, mode.dmPelsWidth, mode.dmPelsHeight, mode.dmDisplayFrequency));
            return true;
        }, 0);
        return list;
    }

    private static bool Intersects(Rect32 a, Rect32 b) =>
        a.X < b.X + b.W && b.X < a.X + a.W && a.Y < b.Y + b.H && b.Y < a.Y + a.H;

    public static object Run()
    {
        var all = TopLevel();
        var arena = ArenaWindow();
        var arenaRect = arena != 0 ? WindowRect(arena) : new Rect32(0, 0, 0, 0);
        var described = all.Select((h, i) => Describe(h, i)).ToList();
        var interesting = described.Where(w => IsArena(w.Process) || IsUntapped(w.Process)
            || (w.Visible && w.Cloaked == 0 && Intersects(w.Window, arenaRect) && w.Window.W > 0 && w.Window.H > 0)).ToList();
        var children = interesting.Where(w => IsArena(w.Process) || IsUntapped(w.Process))
            .ToDictionary(w => w.Hwnd, w => Children((nint)Convert.ToInt64(w.Hwnd[2..], 16)).Select(c => Describe(c, -1)).ToList());
        SHQueryUserNotificationState(out var notificationState);
        GetCursorPos(out var cursor);
        GetWindowThreadProcessId(GetForegroundWindow(), out var fgPid);
        return new
        {
            TimestampLocal = DateTime.Now.ToString("o"),
            ArenaHwnd = Hex(arena),
            ArenaPid = arena != 0 ? (GetWindowThreadProcessId(arena, out var apid) > 0 ? apid : 0) : 0,
            ArenaWindow = arena != 0 ? Describe(arena, all.IndexOf(arena)) : null,
            Foreground = new { Hwnd = Hex(GetForegroundWindow()), Pid = fgPid, Process = ProcessName(fgPid) },
            Cursor = new { cursor.X, cursor.Y },
            UserNotificationState = notificationState switch
            {
                1 => "NOT_PRESENT", 2 => "BUSY (fullscreen app)", 3 => "RUNNING_D3D_FULL_SCREEN", 4 => "PRESENTATION_MODE",
                5 => "ACCEPTS_NOTIFICATIONS", 6 => "QUIET_TIME", 7 => "APP", _ => notificationState.ToString()
            },
            Monitors = Monitors(),
            TopLevelCount = all.Count,
            InterestingTopLevel = interesting,
            ChildWindows = children,
            AllVisibleTopLevel = described.Where(w => w.Visible && w.Cloaked == 0).ToList(),
        };
    }
}
