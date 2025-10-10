#if WINDOWS
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using WinRT.Interop;
using MauiApplication = Microsoft.Maui.Controls.Application;

namespace ShuffleTask.Presentation.Services;

internal sealed class WindowsTrayIconManager : IDisposable
{
    private readonly Icon _icon;
    private readonly bool _ownsIcon;
    private readonly uint _trayCallbackMessage;
    private readonly WndProc _wndProcDelegate;
    private nint _windowHandle;
    private nint _previousWndProc;
    private bool _iconAdded;
    private MauiWinUIWindow? _window;
    private AppWindow? _appWindow;
    private bool _initialized;
    private bool _allowClose;
    private bool _hasShownBackgroundTip;

    public WindowsTrayIconManager()
    {
        (_icon, _ownsIcon) = LoadIcon();
        _trayCallbackMessage = WM_APP + 1000;
        _wndProcDelegate = WindowProcedure;
    }

    public void Initialize(MauiWinUIWindow window)
    {
        if (_initialized)
        {
            return;
        }

        _window = window ?? throw new ArgumentNullException(nameof(window));
        _appWindow = window.AppWindow;

        if (_appWindow != null)
        {
            _appWindow.Closing += OnAppWindowClosing;
        }

        _windowHandle = WindowNative.GetWindowHandle(window);
        if (_windowHandle == 0)
        {
            throw new InvalidOperationException("Failed to retrieve the native window handle.");
        }

        HookWindowProcedure();
        AddTrayIcon();

        _initialized = true;
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose)
        {
            Dispose();
            return;
        }

        args.Cancel = true;
        HideWindow();
        ShowBackgroundTip();
    }

    private void HideWindow()
    {
        if (_window == null)
        {
            return;
        }

        _ = _window.DispatcherQueue.TryEnqueue(() =>
        {
            _appWindow?.Hide();
        });
    }

    private void ShowWindow()
    {
        if (_window == null)
        {
            return;
        }

        _ = _window.DispatcherQueue.TryEnqueue(() =>
        {
            _appWindow?.Show();
            _window.Activate();
        });
    }

    private void ExitApplication()
    {
        if (_window == null)
        {
            MauiApplication.Current?.Quit();
            Dispose();
            return;
        }

        _allowClose = true;
        _ = _window.DispatcherQueue.TryEnqueue(() =>
        {
            RemoveTrayIcon();
            _window?.Close();
        });
    }

    private void ShowBackgroundTip()
    {
        if (_hasShownBackgroundTip)
        {
            return;
        }

        var data = CreateNotifyIconData();
        data.uFlags = NIF_INFO;
        data.szInfoTitle = "ShuffleTask";
        data.szInfo = "ShuffleTask will keep running in the background. Use the tray icon to reopen or exit.";
        data.dwInfoFlags = NIIF_INFO;

        if (Shell_NotifyIcon(NIM_MODIFY, ref data))
        {
            _hasShownBackgroundTip = true;
        }
    }

    private static (Icon icon, bool ownsIcon) LoadIcon()
    {
        try
        {
            string? executablePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(executablePath))
            {
                Icon? associatedIcon = Icon.ExtractAssociatedIcon(executablePath);
                if (associatedIcon != null)
                {
                    return (associatedIcon, true);
                }
            }
        }
        catch
        {
            // Ignore icon extraction failures and fall back to the default application icon.
        }

        return (SystemIcons.Application, false);
    }

    public void Dispose()
    {
        RemoveTrayIcon();

        if (_appWindow != null)
        {
            _appWindow.Closing -= OnAppWindowClosing;
            _appWindow = null;
        }

        UnhookWindowProcedure();

        _window = null;
        _initialized = false;

        if (_ownsIcon)
        {
            _icon.Dispose();
        }
    }

    private void HookWindowProcedure()
    {
        if (_previousWndProc != 0)
        {
            return;
        }

        nint newWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate);
        _previousWndProc = SetWindowLongPtr(_windowHandle, GWL_WNDPROC, newWndProc);
        if (_previousWndProc == 0)
        {
            int error = Marshal.GetLastWin32Error();
            if (error != 0)
            {
                throw new InvalidOperationException($"Failed to hook the window procedure. Win32 error: {error}.");
            }
        }
    }

    private void UnhookWindowProcedure()
    {
        if (_previousWndProc == 0 || _windowHandle == 0)
        {
            return;
        }

        SetWindowLongPtr(_windowHandle, GWL_WNDPROC, _previousWndProc);
        _previousWndProc = 0;
        _windowHandle = 0;
    }

    private void AddTrayIcon()
    {
        if (_iconAdded)
        {
            return;
        }

        var data = CreateNotifyIconData();
        data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
        data.szTip = "ShuffleTask";

        if (Shell_NotifyIcon(NIM_ADD, ref data))
        {
            _iconAdded = true;
        }
        else
        {
            throw new InvalidOperationException("Failed to create the tray icon.");
        }
    }

    private void RemoveTrayIcon()
    {
        if (!_iconAdded)
        {
            return;
        }

        var data = CreateNotifyIconData();
        Shell_NotifyIcon(NIM_DELETE, ref data);
        _iconAdded = false;
    }

    private NOTIFYICONDATA CreateNotifyIconData()
    {
        return new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _windowHandle,
            uID = TrayIconId,
            uCallbackMessage = _trayCallbackMessage,
            hIcon = _icon.Handle,
            szTip = string.Empty,
            szInfo = string.Empty,
            szInfoTitle = string.Empty,
        };
    }

    private nint WindowProcedure(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == _trayCallbackMessage)
        {
            switch ((uint)lParam)
            {
                case WM_LBUTTONUP:
                case WM_LBUTTONDBLCLK:
                    ShowWindow();
                    break;
                case WM_RBUTTONUP:
                case WM_CONTEXTMENU:
                    ShowContextMenu();
                    break;
            }

            return 0;
        }

        return _previousWndProc != 0
            ? CallWindowProc(_previousWndProc, hWnd, msg, wParam, lParam)
            : DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        if (_windowHandle == 0)
        {
            return;
        }

        if (!GetCursorPos(out POINT cursor))
        {
            return;
        }

        nint menuHandle = CreatePopupMenu();
        if (menuHandle == 0)
        {
            return;
        }

        try
        {
            AppendMenu(menuHandle, MF_STRING, OPEN_COMMAND_ID, "Open ShuffleTask");
            AppendMenu(menuHandle, MF_SEPARATOR, 0, null);
            AppendMenu(menuHandle, MF_STRING, EXIT_COMMAND_ID, "Exit");

            SetForegroundWindow(_windowHandle);
            uint command = (uint)TrackPopupMenuEx(menuHandle, TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_BOTTOMALIGN, cursor.X, cursor.Y, _windowHandle, 0);

            switch (command)
            {
                case OPEN_COMMAND_ID:
                    ShowWindow();
                    break;
                case EXIT_COMMAND_ID:
                    ExitApplication();
                    break;
            }
        }
        finally
        {
            DestroyMenu(menuHandle);
        }
    }

    private const uint TrayIconId = 1;
    private const uint OPEN_COMMAND_ID = 1;
    private const uint EXIT_COMMAND_ID = 2;

    private delegate nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam);

    private const int GWL_WNDPROC = -4;
    private const uint WM_CONTEXTMENU = 0x007B;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_APP = 0x8000;

    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint NIF_INFO = 0x00000010;

    private const uint NIIF_INFO = 0x00000001;

    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;

    private const uint MF_STRING = 0x00000000;
    private const uint MF_SEPARATOR = 0x00000800;

    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_BOTTOMALIGN = 0x0020;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint newProc);

    [DllImport("user32.dll", EntryPoint = "CallWindowProcW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CallWindowProc(nint lpPrevWndFunc, nint hWnd, uint Msg, nint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW", CharSet = CharSet.Unicode)]
    private static extern nint DefWindowProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(nint hMenu, uint uFlags, uint uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(nint hMenu);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int TrackPopupMenuEx(nint hMenu, uint uFlags, int x, int y, nint hWnd, nint lptpm);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);
}
#endif
