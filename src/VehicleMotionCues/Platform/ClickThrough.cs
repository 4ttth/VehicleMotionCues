using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace VehicleMotionCues.Platform;

/// <summary>Makes a window ignore the mouse so clicks land on whatever is underneath.</summary>
public static partial class ClickThrough
{
    public static void Apply(Window window)
    {
        var handle = window.TryGetPlatformHandle();
        if (handle is null)
            return;

        if (OperatingSystem.IsWindows())
            ApplyWindows(handle.Handle);
        else if (OperatingSystem.IsMacOS())
            ApplyMac(handle.Handle, handle.HandleDescriptor);
    }

    // ---- Windows ----

    const int GWL_EXSTYLE = -20;
    const long WS_EX_TRANSPARENT = 0x20;
    const long WS_EX_TOOLWINDOW = 0x80;
    const long WS_EX_LAYERED = 0x80000;
    const long WS_EX_NOACTIVATE = 0x08000000;
    const long WS_EX_APPWINDOW = 0x40000;
    static readonly IntPtr HWND_TOPMOST = new(-1);
    const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10, SWP_FRAMECHANGED = 0x20;

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static partial IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetLayeredWindowAttributes(IntPtr hWnd, uint colorKey, byte alpha, uint flags);

    const uint LWA_ALPHA = 0x2;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    static void ApplyWindows(IntPtr hwnd)
    {
        long style = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        style |= WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
        style &= ~WS_EX_APPWINDOW;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style));
        // A layered window stays invisible until it is given attributes; full alpha keeps
        // Avalonia's own per-pixel transparency intact while making the window mouse-transparent.
        SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA);
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED);
    }

    // ---- macOS (untested until the Mac build lands) ----

    const string ObjC = "/usr/lib/libobjc.A.dylib";

    [LibraryImport(ObjC, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr Selector(string name);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial IntPtr SendIntPtr(IntPtr receiver, IntPtr selector);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial void SendBool(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.I1)] bool value);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial void SendNInt(IntPtr receiver, IntPtr selector, nint value);

    static void ApplyMac(IntPtr handle, string? descriptor)
    {
        IntPtr nsWindow = descriptor == "NSView" ? SendIntPtr(handle, Selector("window")) : handle;
        if (nsWindow == IntPtr.Zero)
            return;
        SendBool(nsWindow, Selector("setIgnoresMouseEvents:"), true);
        // Show on every Space and over full-screen apps.
        const nint canJoinAllSpaces = 1 << 0, fullScreenAuxiliary = 1 << 8;
        SendNInt(nsWindow, Selector("setCollectionBehavior:"), canJoinAllSpaces | fullScreenAuxiliary);
    }
}
