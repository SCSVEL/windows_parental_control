using H.NotifyIcon;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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
        _trayIcon = (TaskbarIcon)Resources["TrayIcon"];

        _statusMenuItem = new MenuFlyoutItem { Text = _status.StatusText, IsEnabled = false };
        _trayIcon.ContextFlyout = new MenuFlyout { Items = { _statusMenuItem } };

        _status.PropertyChanged += (_, _) =>
        {
            var text = _status.StatusText;
            _dispatcherQueue.TryEnqueue(() => _statusMenuItem.Text = text);
        };

        _heartbeatWorker = new HeartbeatWorker(_status);
        _heartbeatWorker.Start();

        _trayIcon.ForceCreate();
    }
}
