#if WINDOWS
using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Platform;
using Microsoft.UI.Windowing;

namespace ShuffleTask.Presentation.Services;

internal sealed class WindowsTrayIconManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly Icon _icon;
    private readonly bool _ownsIcon;
    private MauiWinUIWindow? _window;
    private AppWindow? _appWindow;
    private bool _initialized;
    private bool _allowClose;
    private bool _hasShownBackgroundTip;

    public WindowsTrayIconManager()
    {
        (_icon, _ownsIcon) = LoadIcon();

        _notifyIcon = new NotifyIcon
        {
            Text = "ShuffleTask",
            Icon = _icon,
            Visible = false,
        };

        var contextMenu = new ContextMenuStrip();
        var openItem = new ToolStripMenuItem("Open ShuffleTask");
        openItem.Click += (_, _) => ShowWindow();
        contextMenu.Items.Add(openItem);

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitApplication();
        contextMenu.Items.Add(exitItem);

        _notifyIcon.ContextMenuStrip = contextMenu;
        _notifyIcon.DoubleClick += (_, _) => ShowWindow();
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

        _notifyIcon.Visible = true;
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
            Application.Current?.Quit();
            Dispose();
            return;
        }

        _allowClose = true;
        _ = _window.DispatcherQueue.TryEnqueue(() =>
        {
            _notifyIcon.Visible = false;
            _appWindow?.Close();
        });
    }

    private void ShowBackgroundTip()
    {
        if (_hasShownBackgroundTip)
        {
            return;
        }

        _notifyIcon.BalloonTipTitle = "ShuffleTask";
        _notifyIcon.BalloonTipText = "ShuffleTask will keep running in the background. Use the tray icon to reopen or exit.";
        _notifyIcon.ShowBalloonTip(3000);
        _hasShownBackgroundTip = true;
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
        if (_appWindow != null)
        {
            _appWindow.Closing -= OnAppWindowClosing;
            _appWindow = null;
        }

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();

        if (_ownsIcon)
        {
            _icon.Dispose();
        }
    }
}
#endif
