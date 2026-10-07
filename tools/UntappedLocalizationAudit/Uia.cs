using System.Diagnostics;
using System.Windows.Automation;

namespace UntappedLocalizationAudit;

internal sealed record UiaNode(int Index, int Parent, int Depth, string ControlType, string Name, string AutomationId,
    string Class, string Framework, int Pid, string NativeHandle, Rect32? Rect, bool Offscreen);

/// <summary>Bounded raw-view UI Automation walk. Read-only: no patterns are invoked, no input is sent.</summary>
internal static class Uia
{
    /// <summary>One cross-process round trip: cache the whole raw-view subtree, then walk the cached copy locally.</summary>
    public static object DumpCached(nint hwnd)
    {
        var nodes = new List<UiaNode>();
        var watch = Stopwatch.StartNew();
        string? error = null;
        try
        {
            var request = new CacheRequest { TreeScope = TreeScope.Subtree, TreeFilter = Automation.RawViewCondition, AutomationElementMode = AutomationElementMode.None };
            foreach (var p in new AutomationProperty[] { AutomationElement.NameProperty, AutomationElement.ControlTypeProperty, AutomationElement.BoundingRectangleProperty,
                         AutomationElement.AutomationIdProperty, AutomationElement.ClassNameProperty, AutomationElement.FrameworkIdProperty,
                         AutomationElement.ProcessIdProperty, AutomationElement.NativeWindowHandleProperty, AutomationElement.IsOffscreenProperty })
                request.Add(p);
            AutomationElement root;
            using (request.Activate()) root = AutomationElement.FromHandle(hwnd);
            void Walk(AutomationElement e, int parent, int depth)
            {
                var c = e.Cached;
                var r = c.BoundingRectangle;
                var name = c.Name ?? "";
                if (name.Length > 160) name = name[..160] + "…";
                var index = nodes.Count;
                nodes.Add(new UiaNode(index, parent, depth, c.ControlType.ProgrammaticName.Replace("ControlType.", ""), name, c.AutomationId, c.ClassName,
                    c.FrameworkId, c.ProcessId, $"0x{c.NativeWindowHandle:X}",
                    r.IsEmpty || double.IsInfinity(r.X) ? null : new Rect32((int)Math.Round(r.X), (int)Math.Round(r.Y), (int)Math.Round(r.Width), (int)Math.Round(r.Height)),
                    c.IsOffscreen));
                foreach (AutomationElement child in e.CachedChildren) Walk(child, index, depth + 1);
            }
            Walk(root, -1, 0);
        }
        catch (Exception ex) { error = $"{ex.GetType().Name}; 0x{ex.HResult:X8}; {ex.Message}"; }
        return new { Hwnd = Native.Hex(hwnd), TimestampLocal = DateTime.Now.ToString("o"), ElapsedMs = watch.ElapsedMilliseconds, NodeCount = nodes.Count, Error = error, Nodes = nodes };
    }

    public static object Dump(nint hwnd, int maxNodes, TimeSpan budget)
    {
        var nodes = new List<UiaNode>();
        var watch = Stopwatch.StartNew();
        var truncated = false;
        string? error = null;
        try
        {
            var walker = TreeWalker.RawViewWalker;
            var pending = new Stack<(AutomationElement Element, int Parent, int Depth)>();
            pending.Push((AutomationElement.FromHandle(hwnd), -1, 0));
            while (pending.Count > 0)
            {
                if (nodes.Count >= maxNodes || watch.Elapsed > budget) { truncated = true; break; }
                var (element, parent, depth) = pending.Pop();
                UiaNode node;
                try
                {
                    var c = element.Current;
                    var r = c.BoundingRectangle;
                    var name = c.Name ?? "";
                    if (name.Length > 160) name = name[..160] + "…";
                    node = new UiaNode(nodes.Count, parent, depth, c.ControlType.ProgrammaticName.Replace("ControlType.", ""), name,
                        c.AutomationId, c.ClassName, c.FrameworkId, c.ProcessId, $"0x{c.NativeWindowHandle:X}",
                        r.IsEmpty || double.IsInfinity(r.X) ? null : new Rect32((int)Math.Round(r.X), (int)Math.Round(r.Y), (int)Math.Round(r.Width), (int)Math.Round(r.Height)),
                        c.IsOffscreen);
                }
                catch (Exception ex) { node = new UiaNode(nodes.Count, parent, depth, "ERROR", ex.GetType().Name, "", "", "", 0, "", null, true); }
                nodes.Add(node);
                var children = new List<AutomationElement>();
                try { for (var child = walker.GetFirstChild(element); child is not null; child = walker.GetNextSibling(child)) children.Add(child); }
                catch (Exception) { }
                for (var i = children.Count - 1; i >= 0; i--) pending.Push((children[i], node.Index, depth + 1));
            }
        }
        catch (Exception ex) { error = $"{ex.GetType().Name}; 0x{ex.HResult:X8}; {ex.Message}"; }
        return new { Hwnd = Native.Hex(hwnd), ElapsedMs = watch.ElapsedMilliseconds, NodeCount = nodes.Count, Truncated = truncated, Error = error, Nodes = nodes };
    }
}
