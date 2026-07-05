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

    public SessionTracker(TimeProvider clock, TimeSpan limit, TimeSpan idleResetThreshold)
    {
        _clock = clock;
        Limit = limit;
        IdleResetThreshold = idleResetThreshold;
    }

    public TimeSpan Limit { get; }

    public TimeSpan IdleResetThreshold { get; }

    public TimeSpan UsedTime { get; private set; }

    public bool IsOverLimit => UsedTime >= Limit;

    /// <summary>
    /// Records a heartbeat carrying the child's current idle time. Elapsed time since the
    /// previous heartbeat is added to UsedTime only if the reported idle time is still under
    /// the reset threshold (i.e. the user was continuously active in between).
    /// </summary>
    public void RecordHeartbeat(TimeSpan idle)
    {
        var now = _clock.GetTimestamp();

        if (_lastHeartbeatTimestamp is long last && idle < IdleResetThreshold)
        {
            UsedTime += _clock.GetElapsedTime(last, now);
        }

        _lastHeartbeatTimestamp = now;
    }
}
