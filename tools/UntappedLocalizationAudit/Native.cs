using System.Runtime.InteropServices;
using System.Text;

namespace UntappedLocalizationAudit;

internal static class Native
{
    public delegate bool EnumWindowsProc(nint hwnd, nint lParam);
    public delegate bool MonitorEnumProc(nint monitor, nint hdc, ref RECT rect, nint data);

    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEX
    {
        public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra; public int dmFields;
        public int dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels; public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(nint context);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(nint parent, EnumWindowsProc callback, nint lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(nint hwnd, StringBuilder text, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(nint hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(nint hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool GetClientRect(nint hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(nint hwnd, ref POINT point);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll")] public static extern nint GetWindow(nint hwnd, uint command);
    [DllImport("user32.dll")] public static extern nint GetParent(nint hwnd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] public static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] public static extern bool IsZoomed(nint hwnd);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] public static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfo(nint monitor, ref MONITORINFOEX info);
    [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc callback, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool EnumDisplaySettings(string device, int mode, ref DEVMODE mode2);
    [DllImport("user32.dll")] public static extern bool GetLayeredWindowAttributes(nint hwnd, out uint key, out byte alpha, out uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowDisplayAffinity(nint hwnd, out uint affinity);
    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT point);
    [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);
    [DllImport("shell32.dll")] public static extern int SHQueryUserNotificationState(out int state);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out RECT value, int size);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out int value, int size);
    [DllImport("winmm.dll")] public static extern uint timeBeginPeriod(uint period);
    [DllImport("winmm.dll")] public static extern uint timeEndPeriod(uint period);

    // Handle inspection (read-only): system handle table + duplication into this process for identification only.
    [DllImport("ntdll.dll")] public static extern int NtQuerySystemInformation(int infoClass, nint buffer, int length, out int returnLength);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern nint OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool CloseHandle(nint handle);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern nint GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool DuplicateHandle(nint sourceProcess, nint sourceHandle, nint targetProcess, out nint targetHandle, uint access, bool inherit, uint options);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern uint GetProcessId(nint process);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern uint GetFileType(nint file);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern uint GetFinalPathNameByHandle(nint file, StringBuilder path, uint size, uint flags);

    public const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
    public const uint GW_OWNER = 4;
    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9, DWMWA_CLOAKED = 14;

    public static string Hex(nint value) => $"0x{value.ToInt64():X}";

    public static string StyleFlags(long style)
    {
        var flags = new List<string>();
        void F(long bit, string name) { if ((style & bit) == bit) flags.Add(name); }
        F(0x80000000, "POPUP"); F(0x40000000, "CHILD"); F(0x20000000, "MINIMIZE"); F(0x10000000, "VISIBLE");
        F(0x08000000, "DISABLED"); F(0x04000000, "CLIPSIBLINGS"); F(0x02000000, "CLIPCHILDREN"); F(0x01000000, "MAXIMIZE");
        F(0x00C00000, "CAPTION"); F(0x00080000, "SYSMENU"); F(0x00040000, "THICKFRAME"); F(0x00020000, "MINIMIZEBOX"); F(0x00010000, "MAXIMIZEBOX");
        return string.Join('|', flags);
    }

    public static string ExStyleFlags(long ex)
    {
        var flags = new List<string>();
        void F(long bit, string name) { if ((ex & bit) == bit) flags.Add(name); }
        F(0x00000008, "TOPMOST"); F(0x00000020, "TRANSPARENT"); F(0x00000080, "TOOLWINDOW"); F(0x00000100, "WINDOWEDGE");
        F(0x00000200, "CLIENTEDGE"); F(0x00040000, "APPWINDOW"); F(0x00080000, "LAYERED"); F(0x00200000, "NOREDIRECTIONBITMAP");
        F(0x02000000, "COMPOSITED"); F(0x08000000, "NOACTIVATE"); F(0x00000004, "NOPARENTNOTIFY"); F(0x00010000, "CONTROLPARENT");
        return string.Join('|', flags);
    }

    public static string Text(nint hwnd, bool title)
    {
        var builder = new StringBuilder(512);
        if (title) GetWindowText(hwnd, builder, builder.Capacity); else GetClassName(hwnd, builder, builder.Capacity);
        return builder.ToString();
    }
}

internal sealed record Rect32(int X, int Y, int W, int H)
{
    public static Rect32 From(Native.RECT r) => new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    public override string ToString() => $"({X},{Y}) {W}x{H}";
}
