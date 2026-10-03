using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;

namespace DraftTG.App.Platform;

/// <summary>Bound only to the badge window, never to the interactive rail or flyouts.</summary>
public interface IClickThroughWindowController
{
    void SetMode(OverlayInteractionMode mode);
}

/// <summary>Mouse pass-through and activation are separate, paired requirements.</summary>
public sealed record OverlayInteractionMode
{
    private OverlayInteractionMode(bool ignoresMouseEvents, bool allowsActivation)
    { IgnoresMouseEvents = ignoresMouseEvents; AllowsActivation = allowsActivation; }
    public bool IgnoresMouseEvents { get; }
    public bool AllowsActivation { get; }
    public static OverlayInteractionMode Passive { get; } = new(true, false);
    public static OverlayInteractionMode Calibration { get; } = new(false, true);
}

public static class ClickThroughWindowControllerFactory
{
    public static IClickThroughWindowController Create(Window badgeWindow)
    {
        if (OperatingSystem.IsWindows()) return new WindowsClickThroughWindowController(badgeWindow);
        if (OperatingSystem.IsMacOS()) return new MacClickThroughWindowController(badgeWindow);
        throw new PlatformNotSupportedException("The badge overlay supports Windows and macOS.");
    }
}

/// <summary>Testable mode switch. Failure must hide the badge window, never intercept game input.</summary>
public sealed class OverlayInteractionController(IClickThroughWindowController native) : IDisposable
{
    public bool TryApply(bool isEditing, out string? diagnostic)
    {
        try { native.SetMode(isEditing ? OverlayInteractionMode.Calibration : OverlayInteractionMode.Passive); diagnostic = null; return true; }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or ExternalException
            or DllNotFoundException or EntryPointNotFoundException or PlatformNotSupportedException)
        {
            diagnostic = "Card overlay hidden: passive/input mode could not be configured. " + error.Message;
            return false;
        }
    }
    public void Dispose() { if (native is IDisposable disposable) disposable.Dispose(); }
}

internal sealed class WindowsClickThroughWindowController(Window window) : IClickThroughWindowController
{
    private const int ExtendedStyle = -20;
    private const long Layered = 0x80000, Transparent = 0x20, NoActivate = 0x08000000;
    private long? _originalBits;
    public void SetMode(OverlayInteractionMode mode)
    {
        var enabled = mode.IgnoresMouseEvents;
        var handle = window.TryGetPlatformHandle();
        if (handle is null || handle.HandleDescriptor != "HWND" || handle.Handle == 0)
            throw new InvalidOperationException("No HWND is available.");
        var hwnd = handle.Handle;
        var style = IntPtr.Size == 8 ? GetWindowLongPtr(hwnd, ExtendedStyle).ToInt64() : GetWindowLong(hwnd, ExtendedStyle);
        if (style == 0 && Marshal.GetLastWin32Error() != 0) throw new Win32Exception();
        _originalBits ??= style & (Layered | Transparent | NoActivate);
        var updated = enabled ? style | Layered | Transparent | (mode.AllowsActivation ? 0 : NoActivate)
            : (style & ~(Layered | Transparent | NoActivate)) | _originalBits.Value;
        var previous = IntPtr.Size == 8 ? SetWindowLongPtr(hwnd, ExtendedStyle, new IntPtr(updated))
            : new IntPtr(SetWindowLong(hwnd, ExtendedStyle, (int)updated));
        if (previous == 0 && Marshal.GetLastWin32Error() != 0) throw new Win32Exception();
        if (enabled && (style & Layered) == 0 && !SetLayeredWindowAttributes(hwnd, 0, 255, 2)) throw new Win32Exception();
        if (!SetWindowPos(hwnd, 0, 0, 0, 0, 0, 0x37)) throw new Win32Exception(); // frame changed; no move/resize/activate/z-order
    }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint color, byte alpha, uint flags);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
}

/// <summary>
/// Per-window subclass preserves Avalonia's NSWindow layout and callbacks. Only the badge
/// instance receives key/main vetoes; the rail and other application windows are untouched.
/// Never change an NSWindow into an NSPanel: their instance layouts are not interchangeable.
/// </summary>
internal sealed class MacClickThroughWindowController(Window window) : IClickThroughWindowController, IDisposable
{
    private IntPtr _window, _originalClass;
    private bool _disposed;
    // Objective-C classes live for the process lifetime, as must their managed IMP delegates.
    private static readonly Dictionary<IntPtr, IntPtr> PassiveClasses = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<IntPtr, byte> PassiveWindows = new();
    private static readonly List<WindowPermission> FocusCallbacks = [];
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte WindowPermission(IntPtr receiver, IntPtr selector);

    public void SetMode(OverlayInteractionMode mode)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var passive = !mode.AllowsActivation;
        EnsureHandle();
        if (passive) PassiveWindows[_window] = 0;
        else PassiveWindows.TryRemove(_window, out _);
        // Avalonia 12.1.3's macOS BeginResizeDrag is a no-op. BorderOnly keeps AppKit's
        // native resizable frame available in edit mode; None strips that style bit.
        window.WindowDecorations = passive ? WindowDecorations.None : WindowDecorations.BorderOnly;
        window.CanResize = true;
        window.SizeToContent = SizeToContent.Manual;
        SendBool(_window, Selector("setIgnoresMouseEvents:"), mode.IgnoresMouseEvents ? (byte)1 : (byte)0);
        if ((ReadBool(_window, Selector("ignoresMouseEvents")) != 0) != mode.IgnoresMouseEvents
            || (passive && (ReadBool(_window, Selector("canBecomeKeyWindow")) != 0
                || ReadBool(_window, Selector("canBecomeMainWindow")) != 0)))
            throw new InvalidOperationException("NSWindow did not accept the requested input/focus policy.");
    }
    private void EnsureHandle()
    {
        if (_window != 0) return;
        var platform = window.TryGetPlatformHandle();
        if (platform is IMacOSTopLevelPlatformHandle mac) _window = mac.GetNSWindowRetained();
        else if (platform is { HandleDescriptor: "NSWindow", Handle: not 0 })
            _window = SendPointer(platform.Handle, Selector("retain"));
        if (_window == 0) throw new InvalidOperationException("No NSWindow is available.");
        _originalClass = GetClass(_window);
        // Install once, before creating AppKit titlebar observers. Never overwrite a later
        // KVO-generated subclass by swapping isa on every mode change.
        SetClass(_window, PassiveClass(_originalClass));
    }
    private static IntPtr PassiveClass(IntPtr original)
    {
        lock (PassiveClasses)
        {
            if (PassiveClasses.TryGetValue(original, out var existing)) return existing;
            var subclass = AllocateClass(original, "DraftTGPassiveBadge_" + original.ToString("x"), 0);
            if (subclass == 0) throw new InvalidOperationException("Cannot create passive badge window class.");
            var keySelector = Selector("canBecomeKeyWindow");
            var mainSelector = Selector("canBecomeMainWindow");
            var originalKey = Marshal.GetDelegateForFunctionPointer<WindowPermission>(GetImplementation(original, keySelector));
            var originalMain = Marshal.GetDelegateForFunctionPointer<WindowPermission>(GetImplementation(original, mainSelector));
            WindowPermission key = (receiver, selector) => PassiveWindows.ContainsKey(receiver) ? (byte)0 : originalKey(receiver, selector);
            WindowPermission main = (receiver, selector) => PassiveWindows.ContainsKey(receiver) ? (byte)0 : originalMain(receiver, selector);
            if (AddMethod(subclass, keySelector, Marshal.GetFunctionPointerForDelegate(key), "c@:") == 0
                || AddMethod(subclass, mainSelector, Marshal.GetFunctionPointerForDelegate(main), "c@:") == 0)
            {
                DisposeClass(subclass);
                throw new InvalidOperationException("Cannot install badge window focus policy.");
            }
            FocusCallbacks.Add(key);
            FocusCallbacks.Add(main);
            RegisterClass(subclass);
            PassiveClasses.Add(original, subclass);
            return subclass;
        }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_window == 0) return;
        // Keep the compatible subclass until native destruction so KVO teardown remains
        // intact. Removing its policy restores the original key/main implementations.
        PassiveWindows.TryRemove(_window, out _);
        SendVoid(_window, Selector("release"));
        _window = 0;
    }
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    [DllImport(ObjC, EntryPoint = "class_getMethodImplementation")] private static extern IntPtr GetImplementation(IntPtr type, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "object_getClass")] private static extern IntPtr GetClass(IntPtr instance);
    [DllImport(ObjC, EntryPoint = "object_setClass")] private static extern IntPtr SetClass(IntPtr instance, IntPtr type);
    [DllImport(ObjC, EntryPoint = "objc_allocateClassPair")] private static extern IntPtr AllocateClass(IntPtr parent, string name, nuint extraBytes);
    [DllImport(ObjC, EntryPoint = "objc_registerClassPair")] private static extern void RegisterClass(IntPtr type);
    [DllImport(ObjC, EntryPoint = "objc_disposeClassPair")] private static extern void DisposeClass(IntPtr type);
    [DllImport(ObjC, EntryPoint = "class_addMethod")] private static extern byte AddMethod(IntPtr type, IntPtr selector, IntPtr implementation, string encoding);
    [DllImport(ObjC, EntryPoint = "sel_registerName")] private static extern IntPtr Selector(string name);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SendBool(IntPtr receiver, IntPtr selector, byte value);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern byte ReadBool(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SendVoid(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendPointer(IntPtr receiver, IntPtr selector);
}
