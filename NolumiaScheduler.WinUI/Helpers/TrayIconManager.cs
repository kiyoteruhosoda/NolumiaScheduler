using System.Drawing;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace NolumiaScheduler.WinUI.Helpers;

/// <summary>
/// Manages a system tray (notification area) icon using Shell_NotifyIcon Win32 API.
/// </summary>
internal partial class TrayIconManager : IDisposable
{
    private const int WM_USER = 0x0400;
    private const int WM_TRAYICON = WM_USER + 1;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;

    private const int NIF_ICON = 0x00000002;
    private const int NIF_TIP = 0x00000004;
    private const int NIF_MESSAGE = 0x00000001;
    private const int NIM_ADD = 0x00000000;
    private const int NIM_DELETE = 0x00000002;

    private const int TPM_RIGHTBUTTON = 0x0002;
    private const int TPM_NONOTIFY = 0x0080;
    private const int TPM_RETURNCMD = 0x0100;

    private const int MF_STRING = 0x00000000;

    private nint _hWnd;
    private NOTIFYICONDATA _nid;
    private bool _added;
    private nint _hIcon;
    private readonly Window _window;
    private WNDPROC? _wndProc;
    private nint _messageWindow;

    private const int ID_SHOW = 1000;
    private const int ID_EXIT = 1001;

    public event Action? ShowRequested;
    public event Action? ExitRequested;

    private readonly string _tooltip;

    public TrayIconManager(Window window, string tooltip)
    {
        _window = window;
        _tooltip = tooltip;
        CreateMessageWindow();
        LoadIcon();
    }

    public void Show()
    {
        if (!_added)
            AddTrayIcon(_tooltip);
    }

    public void Hide()
    {
        if (_added)
        {
            Shell_NotifyIcon(NIM_DELETE, ref _nid);
            _added = false;
        }
    }

    private void CreateMessageWindow()
    {
        _wndProc = WndProc;
        var wc = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = GetModuleHandle(null),
            lpszClassName = "NolumiaSchedulerTrayMsg"
        };
        RegisterClassEx(ref wc);
        _messageWindow = CreateWindowEx(0, wc.lpszClassName, "", 0, 0, 0, 0, 0, nint.Zero, nint.Zero, wc.hInstance, nint.Zero);
        _hWnd = _messageWindow;
    }

    private void LoadIcon()
    {
        var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
        if (System.IO.File.Exists(iconPath))
        {
            _hIcon = LoadImage(nint.Zero, iconPath, 1 /* IMAGE_ICON */, 16, 16, 0x00000010 /* LR_LOADFROMFILE */);
        }
        if (_hIcon == nint.Zero)
        {
            _hIcon = LoadIcon(nint.Zero, (nint)32512 /* IDI_APPLICATION */);
        }
    }

    private void AddTrayIcon(string tooltip)
    {
        _nid = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hWnd,
            uID = 1,
            uFlags = NIF_ICON | NIF_TIP | NIF_MESSAGE,
            uCallbackMessage = WM_TRAYICON,
            hIcon = _hIcon,
            szTip = tooltip.Length > 127 ? tooltip[..127] : tooltip
        };
        Shell_NotifyIcon(NIM_ADD, ref _nid);
        _added = true;
    }

    private nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == WM_TRAYICON)
        {
            var mouseMsg = (int)(lParam & 0xFFFF);
            if (mouseMsg == WM_LBUTTONDBLCLK)
            {
                ShowRequested?.Invoke();
            }
            else if (mouseMsg == WM_RBUTTONUP)
            {
                ShowContextMenu();
            }
        }
        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        var hMenu = CreatePopupMenu();
        AppendMenu(hMenu, MF_STRING, ID_SHOW, "表示");
        AppendMenu(hMenu, MF_STRING, ID_EXIT, "終了");

        GetCursorPos(out var pt);
        SetForegroundWindow(_hWnd);
        var cmd = TrackPopupMenu(hMenu, TPM_RIGHTBUTTON | TPM_NONOTIFY | TPM_RETURNCMD, pt.X, pt.Y, 0, _hWnd, nint.Zero);
        DestroyMenu(hMenu);

        if (cmd == ID_SHOW) ShowRequested?.Invoke();
        else if (cmd == ID_EXIT) ExitRequested?.Invoke();
    }

    public void Dispose()
    {
        if (_added)
        {
            Shell_NotifyIcon(NIM_DELETE, ref _nid);
            _added = false;
        }
        if (_messageWindow != nint.Zero)
        {
            DestroyWindow(_messageWindow);
            _messageWindow = nint.Zero;
        }
    }

    // P/Invoke declarations
    private delegate nint WNDPROC(nint hWnd, uint msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public int uFlags;
        public int uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
    }

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

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowEx(uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle, int x, int y, int nWidth, int nHeight, nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(nint hWnd);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint LoadImage(nint hInst, string name, uint type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll")]
    private static extern nint LoadIcon(nint hInstance, nint lpIconName);

    [DllImport("user32.dll")]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(nint hMenu, int uFlags, int uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(nint hMenu, int uFlags, int x, int y, int nReserved, nint hWnd, nint prcRect);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(nint hMenu);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);
}
