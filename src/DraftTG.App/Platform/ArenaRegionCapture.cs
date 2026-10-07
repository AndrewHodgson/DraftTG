using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using DraftTG.Application;
using SkiaSharp;

namespace DraftTG.App.Platform;

internal sealed record ArenaWindowGeometry(nint Handle, int X, int Y, int Width, int Height, double Scaling, bool IsForeground)
{
    public int ProcessId { get; init; }
    public string Title { get; init; } = "unknown";
    public string WindowClass { get; init; } = "unknown";
    public bool IsVisible { get; init; } = true;
    public bool IsMinimized { get; init; }
    public string ArenaIntegrity { get; init; } = "unknown";
    public string DraftTGIntegrity { get; init; } = "unknown";
    public static bool SameCaptureGeometry(ArenaWindowGeometry? a, ArenaWindowGeometry? b) => a is null ? b is null
        : b is not null && a.Handle == b.Handle && a.ProcessId == b.ProcessId
        && Math.Abs(a.X - b.X) <= 1 && Math.Abs(a.Y - b.Y) <= 1
        && Math.Abs(a.Width - b.Width) <= 1 && Math.Abs(a.Height - b.Height) <= 1 && Math.Abs(a.Scaling - b.Scaling) < .001;
    // A completed placement is local to the client. Origin and foreground never change that layout.
    // Unlike capture jitter tolerance, even a one-pixel client resize invalidates completed placement.
    public static bool SamePlacementGeometry(ArenaWindowGeometry? a, ArenaWindowGeometry? b) => a is null ? b is null
        : b is not null && a.Handle == b.Handle && a.ProcessId == b.ProcessId
        && a.Width == b.Width && a.Height == b.Height && a.Scaling == b.Scaling
        && a.IsVisible == b.IsVisible && a.IsMinimized == b.IsMinimized;
}
internal sealed record ArenaWindowInspection(bool ProcessFound, ArenaWindowGeometry? Window, string? Diagnostic = null);
internal sealed record ArenaRegionFrame(SKBitmap Image, int X, int Y, double Scaling) : IDisposable
{
    public nint WindowHandle { get; init; }
    public string Backend { get; init; } = "Unspecified/test capture";
    public TimeSpan CaptureDuration { get; init; }
    public TimeSpan? PresentationTime { get; init; }
    public long Generation { get; init; }
    public long CaptureRequestGeneration { get; init; }
    public long PackGeneration { get; init; }
    public string? ImageHash { get; init; }
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;
    public void Dispose() => Image.Dispose();
}
internal sealed record ArenaDraftCrop(int X, int Y, int Width, int Height)
{
    public static ArenaDraftCrop Calculate(ArenaWindowGeometry window, NormalizedDraftRegion region)
    {
        var x = (int)Math.Round(region.X * window.Width); var y = (int)Math.Round(region.Y * window.Height);
        var width = (int)Math.Round(region.Width * window.Width); var height = (int)Math.Round(region.Height * window.Height);
        if (!region.IsValid || x < 0 || y < 0 || width < 64 || height < 64
            || x + (long)width > window.Width || y + (long)height > window.Height || (long)width * height > 8_000_000)
            throw new InvalidOperationException("Draft-region crop is outside Arena client bounds or capture limits; no regional frame was requested.");
        return new(x, y, width, height);
    }
}
internal interface IWindowFrameCapture
{
    string Backend => "Unspecified/test capture";
    ArenaWindowInspection InspectWindow();
    Task<ArenaRegionFrame> CaptureAsync(ArenaWindowGeometry window, NormalizedDraftRegion region, long generation,
        Action<string> stage, CancellationToken cancellationToken);
}
internal interface IArenaRegionCapture : IWindowFrameCapture { }

/// <summary>Windows-only bounded regional capture. Foreground status is diagnostic, never an acquisition prerequisite.</summary>
internal sealed class GdiBitBltFrameCapture : IArenaRegionCapture
{
    public string Backend => "GDI fallback / BitBlt SRCCOPY";
    public ArenaWindowInspection InspectWindow()
    {
        var processes = Process.GetProcessesByName("MTGA");
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
            selected = selected with { ArenaIntegrity = Integrity(selected.ProcessId), DraftTGIntegrity = Integrity(Environment.ProcessId) };
            var diagnostic = selected.ArenaIntegrity != selected.DraftTGIntegrity
                ? "Integrity levels differ or are unavailable; capture will still be attempted. Elevation is not required by this backend." : null;
            return new(true, selected, diagnostic);
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    private readonly SemaphoreSlim _captureGate = new(1, 1);
    public async Task<ArenaRegionFrame> CaptureAsync(ArenaWindowGeometry window, NormalizedDraftRegion region, long generation,
        Action<string> stage, CancellationToken cancellationToken)
    {
        // A timed-out native call may still be unwinding; never accumulate concurrent GDI allocations.
        await _captureGate.WaitAsync(cancellationToken);
        try { return await Task.Run(() => CaptureCore(window, region, generation, stage, cancellationToken), cancellationToken); }
        finally { _captureGate.Release(); }
    }

    private static ArenaRegionFrame CaptureCore(ArenaWindowGeometry window, NormalizedDraftRegion region, long generation,
        Action<string> stage, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        stage("Validating rendering HWND and draft crop");
        ValidateWindow(window);
        var crop = ArenaDraftCrop.Calculate(window, region);
        var target = new Rect { Left = window.X + crop.X, Top = window.Y + crop.Y,
            Right = window.X + crop.X + crop.Width, Bottom = window.Y + crop.Y + crop.Height };
        EnsureUnobstructed(window.Handle, target);
        token.ThrowIfCancellationRequested();
        stage("Initializing GDI capture source");
        var dc = GetDC(window.Handle);
        if (dc == 0) throw NativeFailure("GetDC");
        nint memory = 0, bitmap = 0, old = 0;
        SKBitmap? image = null;
        try
        {
            memory = CreateCompatibleDC(dc);
            if (memory == 0) throw NativeFailure("CreateCompatibleDC");
            var info = new BitmapInfo { Size = 40, Width = crop.Width, Height = -crop.Height, Planes = 1, Bits = 32 };
            bitmap = CreateDIBSection(dc, ref info, 0, out var pixels, 0, 0);
            if (bitmap == 0 || pixels == 0) throw NativeFailure("CreateDIBSection");
            old = SelectObject(memory, bitmap);
            if (old == 0 || old == -1) { old = 0; throw NativeFailure("SelectObject"); }
            stage("Capture source initialized");
            token.ThrowIfCancellationRequested();
            stage("Acquiring fresh BitBlt regional frame");
            if (!BitBlt(memory, 0, 0, crop.Width, crop.Height, dc, crop.X, crop.Y, 0x00CC0020)) throw NativeFailure("BitBlt");
            stage("Converting capture bitmap");
            token.ThrowIfCancellationRequested();
            image = new SKBitmap(crop.Width, crop.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
            var bytes = new byte[checked(crop.Width * crop.Height * 4)];
            Marshal.Copy(pixels, bytes, 0, bytes.Length);
            Marshal.Copy(bytes, 0, image.GetPixels(), bytes.Length);
            ValidateWindow(window);
            EnsureUnobstructed(window.Handle, target);
            token.ThrowIfCancellationRequested();
            var frame = new ArenaRegionFrame(image, target.Left, target.Top, window.Scaling)
                { Generation = generation, CapturedAt = DateTimeOffset.UtcNow, WindowHandle = window.Handle,
                    Backend = "GDI fallback / BitBlt SRCCOPY", CaptureDuration = watch.Elapsed };
            image = null;
            return frame;
        }
        finally
        {
            image?.Dispose();
            if (old != 0) SelectObject(memory, old);
            if (bitmap != 0) DeleteObject(bitmap);
            if (memory != 0) DeleteDC(memory);
            ReleaseDC(window.Handle, dc);
        }
    }

    internal static void ValidateWindow(ArenaWindowGeometry expected)
    {
        GetWindowThreadProcessId(expected.Handle, out var pid);
        if (!IsWindow(expected.Handle) || pid != expected.ProcessId || !IsWindowVisible(expected.Handle) || IsIconic(expected.Handle))
            throw new InvalidOperationException("Arena rendering HWND disappeared, changed process, or became hidden/minimized.");
        if (!GetClientRect(expected.Handle, out var rect)) throw NativeFailure("GetClientRect");
        var origin = new Point();
        if (!ClientToScreen(expected.Handle, ref origin)) throw NativeFailure("ClientToScreen");
        var current = expected with { X = origin.X, Y = origin.Y, Width = rect.Right - rect.Left,
            Height = rect.Bottom - rect.Top, Scaling = GetDpiForWindow(expected.Handle) / 96d };
        if (!ArenaWindowGeometry.SameCaptureGeometry(expected, current)) throw new InvalidOperationException("Arena client geometry changed during capture.");
    }

    internal static void EnsureUnobstructed(ArenaWindowGeometry window, ArenaDraftCrop crop) =>
        EnsureUnobstructed(window.Handle, new Rect { Left = window.X + crop.X, Top = window.Y + crop.Y,
            Right = window.X + crop.X + crop.Width, Bottom = window.Y + crop.Y + crop.Height });
    private static void EnsureUnobstructed(nint arena, Rect region)
    {
        var hwnd = GetTopWindow(0);
        for (var count = 0; hwnd != 0 && count < 4096; count++, hwnd = GetWindow(hwnd, 2))
        {
            if (hwnd == arena) return;
            if (!IsWindowVisible(hwnd) || IsIconic(hwnd)) continue;
            if (!GetWindowRect(hwnd, out var r)) throw NativeFailure("GetWindowRect(occlusion check)");
            if (DwmGetWindowAttribute(hwnd, 14, out var cloaked, sizeof(int)) == 0 && cloaked != 0) continue;
            if (r.Right > region.Left && r.Left < region.Right && r.Bottom > region.Top && r.Top < region.Bottom)
                throw new InvalidOperationException($"Arena draft region is covered by HWND 0x{hwnd.ToInt64():X}; move the rail/covering window clear of the region.");
        }
        throw new InvalidOperationException("Arena window order changed; capture skipped.");
    }

    private static Win32Exception NativeFailure(string api) => new(Marshal.GetLastWin32Error(), api + " failed.");
    private static string Integrity(int pid)
    {
        nint process = 0, token = 0, buffer = 0;
        try
        {
            process = OpenProcess(0x1000, false, pid);
            if (process == 0) throw NativeFailure("OpenProcess(TOKEN_QUERY)");
            if (!OpenProcessToken(process, 8, out token)) throw NativeFailure("OpenProcessToken");
            GetTokenInformation(token, 25, 0, 0, out var size);
            if (size <= 0) throw NativeFailure("GetTokenInformation(size)");
            buffer = Marshal.AllocHGlobal(size);
            if (!GetTokenInformation(token, 25, buffer, size, out _)) throw NativeFailure("GetTokenInformation(integrity)");
            var sid = Marshal.ReadIntPtr(buffer);
            var count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
            var rid = Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(count - 1)));
            return rid < 0x1000 ? "Untrusted" : rid < 0x2000 ? "Low" : rid < 0x3000 ? "Medium" : rid < 0x4000 ? "High" : "System";
        }
        catch (Win32Exception ex) { return $"unknown (Win32 {ex.NativeErrorCode}: {ex.Message})"; }
        finally { if (buffer != 0) Marshal.FreeHGlobal(buffer); if (token != 0) CloseHandle(token); if (process != 0) CloseHandle(process); }
    }

    private delegate bool EnumWindow(nint hwnd, nint parameter);
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo
    { public uint Size; public int Width, Height; public ushort Planes, Bits; public uint Compression, ImageSize; public int XPels, YPels; public uint Used, Important; }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool EnumWindows(EnumWindow callback, nint parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint hwnd, StringBuilder text, int length);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint hwnd, StringBuilder text, int length);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetClientRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool ClientToScreen(nint hwnd, ref Point point);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetTopWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint hwnd, uint command);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(nint hwnd, out Rect rectangle);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out int value, int size);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hwnd, nint dc);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern nint CreateDIBSection(nint dc, ref BitmapInfo info, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern nint SelectObject(nint dc, nint value);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern bool BitBlt(nint target, int x, int y, int width, int height, nint source, int sx, int sy, uint operation);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(nint process, uint access, out nint token);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(nint token, int type, nint information, int length, out int size);
    [DllImport("advapi32.dll")] private static extern nint GetSidSubAuthorityCount(nint sid);
    [DllImport("advapi32.dll")] private static extern nint GetSidSubAuthority(nint sid, uint index);
}
