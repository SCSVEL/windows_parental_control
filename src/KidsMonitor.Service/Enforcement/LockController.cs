using System.Diagnostics;

namespace KidsMonitor.Service.Enforcement;

/// <summary>
/// Owns the locked/unlocked state: launches the Overlay on the child's interactive desktop,
/// watches it and respawns it if killed, and toggles the DisableTaskMgr policy for the
/// duration of the lock. Unlock (password-verified) arrives in a later milestone; for now
/// DisengageLock exists for symmetry/testing but nothing calls it automatically.
/// </summary>
public sealed class LockController(EnforcementOptions options, ILogger<LockController> logger)
{
    private static readonly TimeSpan WatchdogInterval = TimeSpan.FromSeconds(1);

    private readonly object _gate = new();

    private CancellationTokenSource? _watchdogCts;
    private Process? _overlayProcess;
    private string? _lockedUserSid;

    public bool IsLocked { get; private set; }

    public void EngageLock()
    {
        lock (_gate)
        {
            if (IsLocked)
            {
                return;
            }

            IsLocked = true;
            logger.LogInformation("Lock engaged");

            _lockedUserSid = ProcessLauncher.GetActiveSessionUserSid();
            if (_lockedUserSid is not null)
            {
                TaskManagerPolicy.Disable(_lockedUserSid);
            }

            _watchdogCts = new CancellationTokenSource();
            _ = WatchdogLoopAsync(_watchdogCts.Token);
        }
    }

    public void DisengageLock()
    {
        lock (_gate)
        {
            if (!IsLocked)
            {
                return;
            }

            IsLocked = false;
            logger.LogInformation("Lock disengaged");

            _watchdogCts?.Cancel();
            _watchdogCts = null;

            KillOverlay();

            if (_lockedUserSid is not null)
            {
                TaskManagerPolicy.Enable(_lockedUserSid);
                _lockedUserSid = null;
            }
        }
    }

    private async Task WatchdogLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            EnsureOverlayRunning();

            try
            {
                await Task.Delay(WatchdogInterval, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void EnsureOverlayRunning()
    {
        lock (_gate)
        {
            if (_overlayProcess is { HasExited: false })
            {
                return;
            }

            if (ProcessLauncher.TryLaunchInActiveSession(options.OverlayExecutablePath, out var processId, out var win32Error))
            {
                logger.LogInformation("Overlay launched (pid {ProcessId})", processId);
                try
                {
                    _overlayProcess = Process.GetProcessById(processId);
                }
                catch (ArgumentException)
                {
                    _overlayProcess = null;
                }
            }
            else
            {
                logger.LogError("Failed to launch Overlay (Win32 error {Win32Error})", win32Error);
            }
        }
    }

    private void KillOverlay()
    {
        if (_overlayProcess is { HasExited: false })
        {
            try
            {
                _overlayProcess.Kill();
            }
            catch (InvalidOperationException)
            {
                // already exiting
            }
        }

        _overlayProcess = null;
    }
}
