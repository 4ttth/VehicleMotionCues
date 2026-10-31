#if WINDOWS
using System.Runtime.InteropServices;

namespace VehicleMotionCues.Platform;

/// <summary>
/// System-wide shortcut via RegisterHotKey on a hidden message-only window. Avalonia's
/// dispatcher already pumps this thread's messages, so WM_HOTKEY reaches our WndProc.
/// </summary>
public sealed partial class GlobalHotkey : IDisposable
{
    public const uint ModAlt = 0x1, ModControl = 0x2, ModShift = 0x4;
    const uint ModNoRepeat = 0x4000;
    const uint WM_HOTKEY = 0x0312;
    const int HotkeyId = 1;
    const string ClassName = "VehicleMotionCues.Hotkey";
    static readonly IntPtr HWND_MESSAGE = new(-3);

    delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern ushort RegisterClassEx(ref WNDCLASSEX wc);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateWindowEx(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(IntPtr hWnd);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static partial IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(IntPtr hWnd, int id);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr GetModuleHandle(string? name);

    readonly WndProc _wndProc; // must stay referenced for the lifetime of the window
    readonly Action _onPressed;
    readonly IntPtr _hwnd;

    public bool IsRegistered { get; }

    public GlobalHotkey(uint modifiers, uint virtualKey, Action onPressed)
    {
        _onPressed = onPressed;
        _wndProc = Proc;
        var instance = GetModuleHandle(null);
        var wc = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = instance,
            lpszClassName = ClassName,
        };
        if (RegisterClassEx(ref wc) == 0)
            Error = $"RegisterClassEx failed ({Marshal.GetLastWin32Error()})";
        _hwnd = CreateWindowEx(0, ClassName, "", 0, 0, 0, 0, 0, HWND_MESSAGE, IntPtr.Zero, instance, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero)
        {
            Error += $" CreateWindowEx failed ({Marshal.GetLastWin32Error()})";
            return;
        }
        IsRegistered = RegisterHotKey(_hwnd, HotkeyId, modifiers | ModNoRepeat, virtualKey);
        if (!IsRegistered)
            Error += $" RegisterHotKey failed ({Marshal.GetLastWin32Error()})";
    }

    public string Error { get; } = "";

    IntPtr Proc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            Log.Write("Hotkey pressed");
            _onPressed();
            return IntPtr.Zero;
        }
        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hwnd == IntPtr.Zero)
            return;
        UnregisterHotKey(_hwnd, HotkeyId);
        DestroyWindow(_hwnd);
    }
}
#endif
