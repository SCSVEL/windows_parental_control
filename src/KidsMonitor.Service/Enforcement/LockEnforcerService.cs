using KidsMonitor.Service.Session;

namespace KidsMonitor.Service.Enforcement;

/// <summary>
/// Polls SessionTracker independently of pipe heartbeat timing, so the lock still engages
/// (and the watchdog still runs) even if Tray stops sending heartbeats.
/// </summary>
public sealed class LockEnforcerService(SessionTracker tracker, LockController lockController) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (tracker.IsOverLimit)
            {
                lockController.EngageLock();
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
