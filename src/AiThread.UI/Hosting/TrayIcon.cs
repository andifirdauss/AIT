using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AiThread.UI.Hosting;

// Notification-area icon with a right-click menu, built on plain Win32 so no WinForms/WPF runtime is loaded (low RAM).
// Run() owns a message loop and must be called on the thread that should own the icon.
[SupportedOSPlatform("windows")]
public sealed class TrayIcon : IDisposable
{
    public sealed record Item(string Text, Action OnClick, Func<bool>? IsChecked = null)
    {
        public static Item Separator { get; } = new("-", () => { });
    }

    private const uint WM_DESTROY = 0x0002;
    private const uint WM_CLOSE = 0x0010;
    private const uint WM_NULL = 0x0000;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_CALLBACK = 0x8000 + 1; // WM_APP + 1

    private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2;
    private const uint NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4, NIF_INFO = 0x10;
    private const uint NIIF_INFO = 1;

    private const uint MF_STRING = 0, MF_CHECKED = 8, MF_SEPARATOR = 0x800;
    private const uint TPM_RIGHTBUTTON = 2, TPM_RETURNCMD = 0x100;

    private readonly string _tooltip;
    private readonly string? _balloon;
    private readonly IReadOnlyList<Item> _items;
    private readonly Action _onDefault;
    private readonly WndProc _wndProc; // held for the lifetime of the window: native code keeps calling it
    private readonly uint _taskbarCreated;
    private nint _hwnd;
    private nint _icon;
    private bool _added;

    public TrayIcon(string tooltip, string? balloon, IReadOnlyList<Item> items, Action onDefault)
    {
        _tooltip = tooltip;
        _balloon = balloon;
        _items = items;
        _onDefault = onDefault;
        _wndProc = HandleMessage;
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    }

    // Blocks until Close() or the window is destroyed.
    public void Run()
    {
        var instance = GetModuleHandle(null);
        const string className = "AiThreadTray";
        var windowClass = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = instance,
            lpszClassName = className
        };
        if (RegisterClassEx(ref windowClass) == 0) throw new InvalidOperationException("RegisterClassEx gagal.");

        // A hidden top-level window rather than a message-only one: only top-level windows receive the
        // TaskbarCreated broadcast, which tells us Explorer restarted and the icon must be added again.
        _hwnd = CreateWindowEx(0x80 /* WS_EX_TOOLWINDOW */, className, "AiThread", 0, 0, 0, 0, 0, 0, 0, instance, 0);
        if (_hwnd == 0) throw new InvalidOperationException("CreateWindowEx gagal.");

        _icon = ExtractIcon(instance, AppPaths.ExePath, 0);
        if (_icon <= 1) _icon = LoadIcon(0, 32512 /* IDI_APPLICATION */);

        AddIcon(showBalloon: _balloon is not null);

        while (GetMessage(out var message, 0, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }
    }

    // Safe from any thread.
    public void Close()
    {
        if (_hwnd != 0) PostMessage(_hwnd, WM_CLOSE, 0, 0);
    }

    // Safe from any thread.
    public void SetTooltip(string text)
    {
        if (_hwnd == 0 || !_added) return;
        var data = NewData(text);
        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    private void AddIcon(bool showBalloon)
    {
        var data = NewData(_tooltip);
        if (showBalloon)
        {
            data.uFlags |= NIF_INFO;
            data.szInfo = _balloon!;
            data.szInfoTitle = "AiThread";
            data.dwInfoFlags = NIIF_INFO;
        }
        _added = Shell_NotifyIcon(NIM_ADD, ref data);
    }

    private NOTIFYICONDATA NewData(string tooltip) => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _hwnd,
        uID = 1,
        uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
        uCallbackMessage = WM_CALLBACK,
        hIcon = _icon,
        szTip = tooltip.Length > 127 ? tooltip[..127] : tooltip,
        szInfo = "",
        szInfoTitle = ""
    };

    private nint HandleMessage(nint hwnd, uint message, nint wParam, nint lParam)
    {
        if (message == WM_CALLBACK)
        {
            switch ((uint)(lParam.ToInt64() & 0xFFFF))
            {
                case WM_LBUTTONDBLCLK:
                    SafeInvoke(_onDefault);
                    break;
                case WM_LBUTTONUP:
                case WM_RBUTTONUP:
                    ShowMenu(hwnd);
                    break;
            }
            return 0;
        }

        if (message == _taskbarCreated)
        {
            AddIcon(showBalloon: false);
            return 0;
        }

        if (message == WM_CLOSE)
        {
            DestroyWindow(hwnd);
            return 0;
        }

        if (message == WM_DESTROY)
        {
            RemoveIcon();
            PostQuitMessage(0);
            return 0;
        }

        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    private void ShowMenu(nint hwnd)
    {
        var menu = CreatePopupMenu();
        try
        {
            for (var i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                if (ReferenceEquals(item, Item.Separator))
                {
                    AppendMenu(menu, MF_SEPARATOR, 0, null);
                    continue;
                }
                var flags = MF_STRING | (item.IsChecked?.Invoke() == true ? MF_CHECKED : 0);
                AppendMenu(menu, flags, (nuint)(i + 1), item.Text);
            }

            GetCursorPos(out var point);
            // Without this the menu would not close when the user clicks elsewhere.
            SetForegroundWindow(hwnd);
            var chosen = TrackPopupMenu(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD, point.x, point.y, 0, hwnd, 0);
            PostMessage(hwnd, WM_NULL, 0, 0);

            if (chosen > 0 && chosen <= _items.Count) SafeInvoke(_items[chosen - 1].OnClick);
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    // A failing menu action must not throw through native code.
    private static void SafeInvoke(Action action)
    {
        try { action(); }
        catch (Exception ex) { Console.Error.WriteLine($"Aksi tray gagal: {ex.Message}"); }
    }

    private void RemoveIcon()
    {
        if (!_added) return;
        var data = NewData(_tooltip);
        Shell_NotifyIcon(NIM_DELETE, ref data);
        _added = false;
    }

    public void Dispose() => Close();

    private delegate nint WndProc(nint hwnd, uint message, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x; public int y; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? name);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(uint exStyle, string className, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll")] private static extern nint DefWindowProc(nint hwnd, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int exitCode);
    [DllImport("user32.dll")] private static extern int GetMessage(out MSG message, nint hwnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG message);
    [DllImport("user32.dll")] private static extern nint DispatchMessage(ref MSG message);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint hwnd, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] private static extern nint LoadIcon(nint instance, nint name);
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll")] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(nint menu, uint flags, nuint id, string? text);
    [DllImport("user32.dll")] private static extern int TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint hwnd, nint rect);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern nint ExtractIcon(nint instance, string file, uint index);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(uint message, ref NOTIFYICONDATA data);
}
