using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace DraftTG.LocalizationAudit;

internal sealed record PixelRect(int X, int Y, int Width, int Height);
internal sealed record NativeWindow(string Handle, string Parent, string Class, string Title, PixelRect Rectangle,
    PixelRect ClientScreen, bool Visible, bool Minimized, uint Dpi, string? Error);
internal sealed record AccessibleNode(int Index, int ParentIndex, string ControlType, string Name, string AutomationId,
    string Class, string Framework, int ProcessId, string NativeHandle, PixelRect Rectangle, bool Offscreen);
internal sealed record AccessibilityResult(string Handle, int DescendantCount, bool Truncated, IReadOnlyList<AccessibleNode> Nodes, string? Error);

internal static class WindowAudit
{
    public static object Read(int pid)
    {
        var windows = NativeWindows(pid);
        return new { EnumerationDesktop = ObjectName(GetThreadDesktop(GetCurrentThreadId())),
            InputDesktop = InputDesktopName(), NativeWindows = windows,
            Accessibility = windows.Where(w => w.Parent == "0x0").Select(w => Accessibility((nint)System.Convert.ToInt64(w.Handle[2..], 16))).ToArray() };
    }
    internal static IReadOnlyList<NativeWindow> NativeWindows(int pid)
    {
        var handles = new HashSet<nint>();
        bool Collect(nint handle, nint unused)
        { GetWindowThreadProcessId(handle, out var id); if (id == pid) handles.Add(handle); return true; }
        EnumWindows(Collect, 0);
        var desktop = OpenInputDesktop(0, false, 0x41); // DESKTOP_READOBJECTS | DESKTOP_ENUMERATE, no switch/input access.
        if (desktop != 0) { try { EnumDesktopWindows(desktop, Collect, 0); } finally { CloseDesktop(desktop); } }
        foreach (var root in handles.ToArray()) EnumChildWindows(root, Collect, 0);
        return handles.OrderBy(h => h.ToInt64()).Select(h =>
        {
            var title = new StringBuilder(256); var type = new StringBuilder(256);
            GetWindowText(h, title, title.Capacity); GetClassName(h, type, type.Capacity); GetWindowRect(h, out var rectangle);
            PixelRect client; string? error=null;
            try { client = Client(h); }
            catch (Exception ex) { client=new(0,0,0,0); error=$"{ex.GetType().Name}; 0x{ex.HResult:X8}; {ex.Message}"; }
            return new NativeWindow(Hex(h), Hex(GetParent(h)), type.ToString(), title.ToString(), Convert(rectangle), client,
                IsWindowVisible(h), IsIconic(h), GetDpiForWindow(h), error);
        }).ToArray();
    }
    internal static PixelRect Client(nint hwnd)
    {
        if (!GetClientRect(hwnd, out var client)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var origin = new Point(); if (!ClientToScreen(hwnd, ref origin)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return new(origin.X, origin.Y, client.Right-client.Left, client.Bottom-client.Top);
    }
    private static AccessibilityResult Accessibility(nint hwnd)
    {
        var nodes = new List<AccessibleNode>(); var watch = Stopwatch.StartNew(); var truncated = false; string? error = null;
        try
        {
            var root = AutomationElement.FromHandle(hwnd); var walker = TreeWalker.RawViewWalker;
            var pending = new Queue<(AutomationElement Element, int Parent)>(); pending.Enqueue((root, -1));
            while (pending.TryDequeue(out var next))
            {
                if (nodes.Count >= 512 || watch.Elapsed > TimeSpan.FromSeconds(8)) { truncated = true; break; }
                var c = next.Element.Current; var r = c.BoundingRectangle; var index = nodes.Count;
                nodes.Add(new(index, next.Parent, c.ControlType.ProgrammaticName, c.Name, c.AutomationId,
                    c.ClassName, c.FrameworkId, c.ProcessId, $"0x{c.NativeWindowHandle:X}",
                    r.IsEmpty ? new(0,0,0,0) : new((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height), c.IsOffscreen));
                for (var child = walker.GetFirstChild(next.Element); child is not null; child = walker.GetNextSibling(child))
                {
                    pending.Enqueue((child, index));
                    if (pending.Count + nodes.Count >= 512 || watch.Elapsed > TimeSpan.FromSeconds(8)) { truncated = true; break; }
                }
            }
        }
        catch (Exception ex) { error = $"{ex.GetType().Name}; 0x{ex.HResult:X8}; {ex.Message}"; }
        return new(Hex(hwnd), Math.Max(0,nodes.Count-1), truncated, nodes, error);
    }
    internal static string Hex(nint value) => $"0x{value.ToInt64():X}";
    private static PixelRect Convert(Rect r) => new(r.Left,r.Top,r.Right-r.Left,r.Bottom-r.Top);
    private static string ObjectName(nint obj) { var b = new StringBuilder(256); return GetUserObjectInformation(obj, 2, b, b.Capacity*2, out _) ? b.ToString() : "unavailable"; }
    private static string InputDesktopName() { var d = OpenInputDesktop(0, false, 1); if (d == 0) return "unavailable"; try { return ObjectName(d); } finally { CloseDesktop(d); } }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X,Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left,Top,Right,Bottom; }
    private delegate bool EnumWindow(nint handle,nint data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindow callback,nint data);
    [DllImport("user32.dll")] private static extern bool EnumDesktopWindows(nint desktop,EnumWindow callback,nint data);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent,EnumWindow callback,nint data);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint handle,out uint process);
    [DllImport("user32.dll")] private static extern nint GetParent(nint handle);
    [DllImport("user32.dll",EntryPoint="GetWindowTextW",CharSet=CharSet.Unicode)] private static extern int GetWindowText(nint handle,StringBuilder text,int size);
    [DllImport("user32.dll",EntryPoint="GetClassNameW",CharSet=CharSet.Unicode)] private static extern int GetClassName(nint handle,StringBuilder text,int size);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint handle,out Rect rect);
    [DllImport("user32.dll",SetLastError=true)] private static extern bool GetClientRect(nint handle,out Rect rect);
    [DllImport("user32.dll",SetLastError=true)] private static extern bool ClientToScreen(nint handle,ref Point point);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint handle);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint handle);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint handle);
    [DllImport("user32.dll")] private static extern nint OpenInputDesktop(uint flags,bool inherit,uint access);
    [DllImport("user32.dll")] private static extern bool CloseDesktop(nint handle);
    [DllImport("user32.dll")] private static extern nint GetThreadDesktop(uint thread);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll",EntryPoint="GetUserObjectInformationW",CharSet=CharSet.Unicode)] private static extern bool GetUserObjectInformation(nint handle,int index,StringBuilder text,int length,out int needed);
}
