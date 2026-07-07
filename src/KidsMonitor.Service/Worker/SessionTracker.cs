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

    /// <summary>Clears accumulated usage after a verified unlock, giving a fresh session.</summary>
    public void Reset()
    {
        UsedTime = TimeSpan.Zero;
        UsedSinceBreak = TimeSpan.Zero;
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
    /// previous heartbeat is added to UsedTime only if the reported idle time is still under
    /// the reset threshold (i.e. the user was continuously active in between).
    /// </summary>
    public void RecordHeartbeat(TimeSpan idle)
    {
        // If this call itself is the one crossing midnight (e.g. heartbeats had stopped
        // arriving for a while and this is the first one since), _lastHeartbeatTimestamp is
        // stale from before the reset -- treat it like the very first heartbeat of the new day
        // rather than folding that whole pre-reset gap into the fresh UsedTime.
        var rolledOver = RolloverIfNewDay();

        var now = _clock.GetTimestamp();

        if (!rolledOver && _lastHeartbeatTimestamp is long last && idle < IdleResetThreshold)
        {
            var elapsed = _clock.GetElapsedTime(last, now);
            UsedTime += elapsed;
            UsedSinceBreak += elapsed;
        }

        _lastHeartbeatTimestamp = now;
    }
}
