using KidsMonitor.Service.Enforcement;

namespace KidsMonitor.Service;

public sealed record TrayLauncherOptions(string? TrayExecutablePath);

/// <summary>
/// Launches the Tray icon in the active interactive session once at Service startup, via the
/// same CreateProcessAsUser chain <see cref="ProcessLauncher"/> uses for the Overlay. This
/// covers "Tray isn't running yet in the session that's active right after install", since
/// Tray's own HKLM Run key (see KidsMonitor.Installer) only fires at the next logon/reboot.
/// (An MSI custom action was tried for this first and was initially blamed for a Tray crash on
/// launch -- that turned out to be unrelated: `dotnet publish` was dropping Tray's own Assets\
/// and XAML-compiler outputs from the publish folder regardless of how Tray was launched, fixed
/// via a post-publish copy target in KidsMonitor.Tray.csproj. The custom action was still
/// replaced with this Service-driven launch since it's simpler to retry/log from here.)
/// </summary>
public sealed class TrayLauncherService(TrayLauncherOptions options, ILogger<TrayLauncherService> logger) : BackgroundService
{
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(5);
    private const int MaxAttempts = 12;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.TrayExecutablePath is null)
        {
            logger.LogInformation("Tray executable not found next to the Service (expected in the installed layout); skipping auto-launch.");
            return;
        }

        for (var attempt = 0; attempt < MaxAttempts && !stoppingToken.IsCancellationRequested; attempt++)
        {
            if (System.Diagnostics.Process.GetProcessesByName("KidsMonitor.Tray").Length > 0)
            {
                return;
            }

            if (ProcessLauncher.TryLaunchInActiveSession(options.TrayExecutablePath, out _, out var win32Error))
            {
                logger.LogInformation("Launched KidsMonitor.Tray in the active session");
                return;
            }

            logger.LogWarning("Could not launch Tray yet (Win32 error {Error}), will retry", win32Error);

            try
            {
                await Task.Delay(RetryInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
