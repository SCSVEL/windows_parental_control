namespace KidsMonitor.Service.Session;

/// <summary>
/// Accumulates continuous-use time from Tray's ActivityHeartbeats. Uses TimeProvider's
/// timestamp/elapsed API (backed by Stopwatch on the real clock) rather than DateTime.Now,
/// so wall-clock changes can't be used to reset the timer, and so the elapsed calculation
/// is deterministic and clock-injectable under test via FakeTimeProvider.
/// </summary>
public sealed class SessionTracker
{
    private readonly TimeProvider _clock;
    private long? _lastHeartbeatTimestamp;
    private DateOnly _currentDay;

    public SessionTracker(TimeProvider clock, TimeSpan limit, TimeSpan idleResetThreshold, TimeSpan breakInterval = default, TimeSpan? breakDuration = null)
    {
        _clock = clock;
        Limit = limit;
        IdleResetThreshold = idleResetThreshold;
        BreakInterval = breakInterval;
        BreakDuration = breakDuration ?? TimeSpan.FromMinutes(10);
        _currentDay = DateOnly.FromDateTime(clock.GetLocalNow().Date);
    }

    public TimeSpan Limit { get; private set; }

    public TimeSpan IdleResetThreshold { get; private set; }

    /// <summary>How often a mandatory break is enforced (e.g. every 30 minutes of use). Zero/negative disables breaks.</summary>
    public TimeSpan BreakInterval { get; private set; }

    /// <summary>How long a break lock lasts before it auto-lifts (no password needed).</summary>
    public TimeSpan BreakDuration { get; private set; }

    public TimeSpan UsedTime { get; private set; }

    /// <summary>Usage accumulated since the last break was taken (or since start of day).</summary>
    public TimeSpan UsedSinceBreak { get; private set; }

    public bool IsOverLimit => UsedTime >= Limit;

    /// <summary>True once a break is due -- only meaningful while the daily limit hasn't already been reached.</summary>
    public bool IsBreakDue => BreakInterval > TimeSpan.Zero && !IsOverLimit && UsedSinceBreak >= BreakInterval;

    /// <summary>Applies a parent-approved limit change (SetLimitsRequest).</summary>
    public void UpdateLimit(TimeSpan newLimit) => Limit = newLimit;

    /// <summary>Applies a parent-approved idle-reset threshold change (SetLimitsRequest).</summary>
    public void UpdateIdleResetThreshold(TimeSpan newThreshold) => IdleResetThreshold = newThreshold;

    /// <summary>Applies parent-approved break settings (SetLimitsRequest).</summary>
    public void UpdateBreakSettings(TimeSpan interval, TimeSpan duration)
    {
        BreakInterval = interval;
        BreakDuration = duration;
    }

    /// <summary>
    /// Clears accumulated usage after a verified unlock, giving a fresh session. Also clears the
    /// heartbeat baseline: whoever triggers a reset (this call, RolloverIfNewDay, etc.) may not
    /// be the next thing to call RecordHeartbeat, so the baseline must not survive the reset --
    /// otherwise the next heartbeat would fold whatever gap elapsed since the *pre-reset* baseline
    /// into the fresh usage (e.g. LockEnforcerService's 1s poll rolls the day over first, then
    /// Tray's heartbeat arrives with a baseline from hours earlier, before an overnight sleep).
    /// </summary>
    public void Reset()
    {
        UsedTime = TimeSpan.Zero;
        UsedSinceBreak = TimeSpan.Zero;
        _lastHeartbeatTimestamp = null;
    }

    /// <summary>Clears just the break timer, e.g. once a break's duration has elapsed.</summary>
    public void ResetBreak() => UsedSinceBreak = TimeSpan.Zero;

    /// <summary>
    /// Resets usage for a new calendar day (local time), independent of whether the machine was
    /// locked/unlocked or the Service ever restarted overnight. Returns true if a reset happened.
    /// Must be polled regularly (not just from heartbeats) so the reset fires even if Tray isn't
    /// sending heartbeats (e.g. at a Windows lock screen overnight).
    /// </summary>
    public bool RolloverIfNewDay()
    {
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().Date);
        if (today == _currentDay)
        {
            return false;
        }

        _currentDay = today;
        Reset();
        return true;
    }

    /// <summary>
    /// Records a heartbeat carrying the child's current idle time. Elapsed time since the
    /// previous heartbeat is added to UsedTime only if (a) the reported idle time is still under
    /// the reset threshold -- i.e. the child looks active right now -- AND (b) the gap since the
    /// previous heartbeat is also under the threshold. (b) matters because heartbeats can stop
    /// arriving for reasons that have nothing to do with the child being continuously active --
    /// system sleep/hibernate, the Service being restarted, Tray losing its pipe connection --
    /// and a low *current* idle reading only proves the child touched the input devices recently,
    /// not that they were active for the whole gap. Without this, waking a machine that slept for
    /// hours would fold the entire sleep duration into UsedTime as if it were active use.
    /// </summary>
    public void RecordHeartbeat(TimeSpan idle)
    {
        RolloverIfNewDay();

        var now = _clock.GetTimestamp();

        if (_lastHeartbeatTimestamp is long last)
        {
            var elapsed = _clock.GetElapsedTime(last, now);
            if (idle < IdleResetThreshold && elapsed < IdleResetThreshold)
            {
                UsedTime += elapsed;
                UsedSinceBreak += elapsed;
            }
        }

        _lastHeartbeatTimestamp = now;
    }
}
