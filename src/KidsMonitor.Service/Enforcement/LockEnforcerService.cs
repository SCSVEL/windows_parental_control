using KidsMonitor.Service.Session;

namespace KidsMonitor.Service.Enforcement;

/// <summary>
/// Polls SessionTracker independently of pipe heartbeat timing, so the lock still engages
/// (and the watchdog still runs) even if Tray stops sending heartbeats.
/// </summary>
public sealed class LockEnforcerService(SessionTracker tracker, LockController lockController, ILogger<LockEnforcerService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // Each tick is isolated so one bad iteration (e.g. a transient Win32 failure while
            // engaging/disengaging the lock) can't throw out of ExecuteAsync -- an unhandled
            // exception here would stop this BackgroundService's host entirely (the Generic
            // Host's default BackgroundServiceExceptionBehavior is StopHost), silently killing
            // the whole Service -- including the daily-reset/unlock logic that's supposed to
            // clear an overnight lock -- with no crash-recovery to bring it back (see
            // ServiceConfig in the installer for that half of the fix).
            try
            {
                Tick();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error in lock-enforcer tick");
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

    private void Tick()
    {
        // Polled here (not just on heartbeats) so the daily reset fires at local midnight
        // even if Tray isn't sending heartbeats (e.g. machine sitting at a Windows lock
        // screen overnight) and even across a lock/unlock or logoff/logon.
        tracker.RolloverIfNewDay();

        if (lockController.IsLocked
            && lockController.CurrentLockReason == LockReason.Break
            && lockController.ElapsedSinceEngaged() >= tracker.BreakDuration)
        {
            tracker.ResetBreak();
        }

        if (tracker.IsOverLimit)
        {
            lockController.EngageLock(LockReason.DailyLimit);
        }
        else if (tracker.IsBreakDue)
        {
            lockController.EngageLock(LockReason.Break);
        }
        else if (lockController.IsLocked)
        {
            // Neither condition holds anymore -- either the break's duration just elapsed
            // above, or the day just rolled over (RolloverIfNewDay reset UsedTime), so this
            // lock (break or daily-limit) can lift without a password.
            lockController.DisengageLock();
        }
    }
}
