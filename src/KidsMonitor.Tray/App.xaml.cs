using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;

namespace KidsMonitor_Tray;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private readonly StatusViewModel _status = new();

    private TaskbarIcon? _trayIcon;
    private MenuFlyoutItem? _statusMenuItem;
    private HeartbeatWorker? _heartbeatWorker;
    private DispatcherQueue? _dispatcherQueue;
    private FirstRunSetupWindow? _setupWindow;
    private SettingsWindow? _settingsWindow;
    private Window? _keepAliveWindow;

    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        // WinUI3 (Windows App SDK) ends the app's message loop -- and the process -- the moment
        // the last Window is closed, same as it would for a normal windowed app. This app has no
        // "main window": FirstRunSetupWindow/SettingsWindow are opened on demand and closed by the
        // user, which would otherwise kill the tray icon along with them. Keeping one Window alive
        // for the app's whole lifetime (created here, never closed) keeps the process running even
        // when every user-facing window is closed. Parked off-screen and shrunk to 1x1 rather than
        // AppWindow.Hide(): Hide() left the window activated-but-invisible in a way that also
        // blocked later windows (Settings/Setup) from ever coming to the foreground when activated.
        //
        // IsShownInSwitchers only hides it from Alt+Tab -- it still gets a taskbar button, which
        // the user can click and close, killing the whole Tray process along with it (the exact
        // bug this window exists to prevent). HideFromTaskbar strips the taskbar button itself
        // via the same raw Win32 styling approach the Overlay uses for its own window chrome.
        _keepAliveWindow = new Window();
        _keepAliveWindow.AppWindow.IsShownInSwitchers = false;
        _keepAliveWindow.AppWindow.Resize(new Windows.Graphics.SizeInt32(1, 1));
        _keepAliveWindow.AppWindow.Move(new Windows.Graphics.PointInt32(-32000, -32000));
        _keepAliveWindow.Activate();
        HideFromTaskbar(WindowNative.GetWindowHandle(_keepAliveWindow));

        _trayIcon = (TaskbarIcon)Resources["TrayIcon"];

        // Set here rather than via IconSource in App.xaml: IconSource resolves through a
        // ms-appx:/// URI, which needs package identity and reliably crashes (0xc000027b in
        // Microsoft.UI.Xaml.dll) on ForceCreate() in this unpackaged app. The classic Icon
        // property loads straight from disk instead.
        _trayIcon.Icon = new System.Drawing.Icon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));

        _statusMenuItem = new MenuFlyoutItem { Text = _status.StatusText, IsEnabled = false };
        // ContextMenuMode is left at its default (PopupMenu, a native Win32 menu -- see App.xaml),
        // which invokes each item's bound Command on selection rather than raising the WinUI
        // Click event (Click only fires for a real XAML flyout, which this isn't). A Click-only
        // handler here silently never runs when the user picks the item from the native menu.
        var settingsItem = new MenuFlyoutItem { Text = "Settings...", Command = new RelayCommand(ShowSettingsWindow) };
        _trayIcon.ContextFlyout = new MenuFlyout { Items = { _statusMenuItem, settingsItem } };

        _status.PropertyChanged += (_, _) =>
        {
            var text = _status.StatusText;
            var setupRequired = _status.SetupRequired;
            _dispatcherQueue.TryEnqueue(() =>
            {
                _statusMenuItem.Text = text;
                if (setupRequired)
                {
                    ShowSetupWindowIfNeeded();
                }
            });
        };

        _heartbeatWorker = new HeartbeatWorker(_status);
        _heartbeatWorker.Start();

        // Deferred rather than called inline: calling ForceCreate() synchronously during
        // OnLaunched races WinUI's message pump/WindowsAppSDK bootstrap in Release builds
        // (0xc000027b fail-fast, intermittent -- Debug's slower startup masks it). Posting
        // it as a dispatcher callback lets the pump take at least one turn first.
        _dispatcherQueue.TryEnqueue(() => _trayIcon.ForceCreate());
    }

    private void ShowSetupWindowIfNeeded()
    {
        if (_setupWindow is not null)
        {
            return;
        }

        _setupWindow = new FirstRunSetupWindow();
        _setupWindow.Closed += (_, _) => _setupWindow = null;
        _setupWindow.Activate();
    }

    private void ShowSettingsWindow()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        try
        {
            _settingsWindow = new SettingsWindow(_status.LimitMinutes, _status.BreakIntervalMinutes, _status.BreakDurationMinutes, _status.IdleResetSeconds);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Activate();
        }
        catch (Exception ex)
        {
            _settingsWindow = null;
            ReportError("ShowSettingsWindow", ex);
        }
    }

    /// <summary>
    /// Logs to a user-writable path (Tray runs as the logged-in user, and
    /// C:\ProgramData\KidsMonitor is ACL-locked to SYSTEM/Administrators by the service) and
    /// shows a plain Win32 message box, which doesn't depend on WinUI window creation working.
    /// </summary>
    private static void ReportError(string context, Exception ex)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KidsMonitor", "logs");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "tray.log"), $"{DateTime.Now:O} [{context}] {ex}{Environment.NewLine}");
        }
        catch
        {
            // Best-effort logging only.
        }

        MessageBoxW(IntPtr.Zero, $"{context}:\n{ex}", "KidsMonitor", 0);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    /// <summary>
    /// Strips the taskbar button from an already-shown window. WS_EX_TOOLWINDOW/WS_EX_APPWINDOW
    /// alone only take effect for a window's *first* show, so SetWindowPos with SWP_FRAMECHANGED
    /// (no move/size/z-order/activation) is needed afterward to make the taskbar actually drop
    /// the button for a window that's already been Activate()d.
    /// </summary>
    private static void HideFromTaskbar(IntPtr hwnd)
    {
        var exStyle = (long)GetWindowLongPtrW(hwnd, GWL_EXSTYLE);
        exStyle = (exStyle & ~WS_EX_APPWINDOW) | WS_EX_TOOLWINDOW;
        SetWindowLongPtrW(hwnd, GWL_EXSTYLE, (IntPtr)exStyle);

        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
    }

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x00000080;
    private const long WS_EX_APPWINDOW = 0x00040000;

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtrW(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
}
