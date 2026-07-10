using KidsMonitor.Service.Session;
using Microsoft.Extensions.Time.Testing;

namespace KidsMonitor.Service.Tests;

public class SessionTrackerTests
{
    private static SessionTracker CreateTracker(FakeTimeProvider clock, out TimeSpan limit)
    {
        limit = TimeSpan.FromMinutes(30);
        return new SessionTracker(clock, limit, idleResetThreshold: TimeSpan.FromSeconds(60));
    }

    /// <summary>
    /// Stands in for a real Tray sending a heartbeat every few seconds: advances the clock and
    /// calls RecordHeartbeat repeatedly in small steps (always under the idle-reset threshold, so
    /// each step is trusted) until <paramref name="duration"/> has elapsed, rather than a single
    /// large Advance() -- which after the heartbeat-gap safeguard would no longer be trusted as
    /// continuous use (see RecordHeartbeat_AfterLongGapSameDay_DoesNotCountTheGapAsUsage).
    /// </summary>
    private static void SimulateActiveUse(SessionTracker tracker, FakeTimeProvider clock, TimeSpan duration, TimeSpan step)
    {
        var remaining = duration;
        while (remaining > TimeSpan.Zero)
        {
            var advance = remaining < step ? remaining : step;
            clock.Advance(advance);
            tracker.RecordHeartbeat(idle: TimeSpan.Zero);
            remaining -= advance;
        }
    }

    [Fact]
    public void FirstHeartbeat_DoesNotAccumulateTime()
    {
        var clock = new FakeTimeProvider();
        var tracker = CreateTracker(clock, out _);

        tracker.RecordHeartbeat(idle: TimeSpan.Zero);

        Assert.Equal(TimeSpan.Zero, tracker.UsedTime);
    }

    [Fact]
    public void ActiveHeartbeats_AccumulateElapsedTime()
    {
        var clock = new FakeTimeProvider();
        var tracker = CreateTracker(clock, out _);

        tracker.RecordHeartbeat(idle: TimeSpan.Zero);
        clock.Advance(TimeSpan.FromSeconds(10));
        tracker.RecordHeartbeat(idle: TimeSpan.Zero);
        clock.Advance(TimeSpan.FromSeconds(10));
        tracker.RecordHeartbeat(idle: TimeSpan.Zero);

        Assert.Equal(TimeSpan.FromSeconds(20), tracker.UsedTime);
    }

    [Fact]
    public void IdleAboveThreshold_DoesNotAccumulateTime()
    {
        var clock = new FakeTimeProvider();
        var tracker = CreateTracker(clock, out _);

        tracker.RecordHeartbeat(idle: TimeSpan.Zero);
        clock.Advance(TimeSpan.FromSeconds(30));
        tracker.RecordHeartbeat(idle: TimeSpan.FromSeconds(90));

        Assert.Equal(TimeSpan.Zero, tracker.UsedTime);
    }

    [Fact]
    public void IdleReset_ThenActiveAgain_ResumesCountingFromNextHeartbeat()
    {
        var clock = new FakeTimeProvider();
        var tracker = CreateTracker(clock, out _);

        tracker.RecordHeartbeat(idle: TimeSpan.Zero);
        clock.Advance(TimeSpan.FromSeconds(10));
        tracker.RecordHeartbeat(idle: TimeSpan.Zero); // +10s active
        clock.Advance(TimeSpan.FromSeconds(90));
        tracker.RecordHeartbeat(idle: TimeSpan.FromSeconds(90)); // gap ignored (was idle)
        clock.Advance(TimeSpan.FromSeconds(10));
        tracker.RecordHeartbeat(idle: TimeSpan.Zero); // +10s active again

        Assert.Equal(TimeSpan.FromSeconds(20), tracker.UsedTime);
    }

    [Fact]
    public void IsOverLimit_TrueOnceUsedTimeReachesLimit()
    {
        var clock = new FakeTimeProvider();
        var tracker = new SessionTracker(clock, limit: TimeSpan.FromSeconds(20), idleResetThreshold: TimeSpan.FromSeconds(60));

        tracker.RecordHeartbeat(idle: TimeSpan.Zero);
        clock.Advance(TimeSpan.FromSeconds(20));
        tracker.RecordHeartbeat(idle: TimeSpan.Zero);

        Assert.True(tracker.IsOverLimit);
    }

    [Fact]
    public void NoHeartbeats_DoesNotThrowOrAccumulate()
    {
        var clock = new FakeTimeProvider();
        var tracker = CreateTracker(clock, out _);

        clock.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal(TimeSpan.Zero, tracker.UsedTime);
        Assert.False(tracker.IsOverLimit);
    }

    [Fact]
    public void Reset_ClearsUsedTimeAndLiftsOverLimit()
    {
        var clock = new FakeTimeProvider();
        var tracker = new SessionTracker(clock, limit: TimeSpan.FromSeconds(20), idleResetThreshold: TimeSpan.FromSeconds(60));

        tracker.RecordHeartbeat(idle: TimeSpan.Zero);
        clock.Advance(TimeSpan.FromSeconds(20));
        tracker.RecordHeartbeat(idle: TimeSpan.Zero);
        Assert.True(tracker.IsOverLimit);

        tracker.Reset();

        Assert.Equal(TimeSpan.Zero, tracker.UsedTime);
        Assert.False(tracker.IsOverLimit);
    }

    [Fact]
    public void UpdateLimit_ChangesIsOverLimitImmediately()
    {
        var clock = new FakeTimeProvider();
        var tracker = CreateTracker(clock, out _);

        tracker.RecordHeartbeat(idle: TimeSpan.Zero);
        SimulateActiveUse(tracker, clock, TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(30));
        Assert.False(tracker.IsOverLimit);

        tracker.UpdateLimit(TimeSpan.FromMinutes(5));

        Assert.True(tracker.IsOverLimit);
    }

    [Fact]
    public void RolloverIfNewDay_SameDay_DoesNothing()
    {
        var clock = new FakeTimeProvider();
        var tracker = CreateTracker(clock, out _);

        tracker.RecordHeartbeat(idle: TimeSpan.Zero);
        SimulateActiveUse(tracker, clock, TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(30));

        var rolledOver = tracker.RolloverIfNewDay();

        Assert.False(rolledOver);
        Assert.Equal(TimeSpan.FromMinutes(10), tracker.UsedTime);
    }

    [Fact]
    public void RolloverIfNewDay_AfterMidnight_ResetsUsedTimeEvenWithoutUnlockOrRestart()
    {
        var clock = new FakeTimeProvider();
        clock.SetUtcNow(new DateTimeOffset(2024, 1, 1, 20, 0, 0, TimeSpan.Zero)); // 8pm, well before midnight
        var tracker = CreateTracker(clock, out _);

        tracker.RecordHeartbeat(idle: TimeSpan.Zero);
        SimulateActiveUse(tracker, clock, TimeSpan.FromMinutes(70), TimeSpan.FromSeconds(30));
        Assert.Equal(TimeSpan.FromMinutes(70), tracker.UsedTime);

        // Simulate the machine sitting locked/idle overnight, crossing local midnight, with no
        // heartbeat and no password-verified unlock in between -- just wall-clock time passing.
        clock.Advance(TimeSpan.FromHours(5)); // now 1:10am the next day

        var rolledOver = tracker.RolloverIfNewDay();

        Assert.True(rolledOver);
        Assert.Equal(TimeSpan.Zero, tracker.UsedTime);
        Assert.False(tracker.IsOverLimit);
    }

    [Fact]
    public void RecordHeartbeat_AcrossMidnight_ResetsInsteadOfCountingTheGapAsUsage()
    {
        var clock = new FakeTimeProvider();
        var tracker = new SessionTracker(clock, limit: TimeSpan.FromMinutes(20), idleResetThreshold: TimeSpan.FromSeconds(60));

        tracker.RecordHeartbeat(idle: TimeSpan.Zero);
        SimulateActiveUse(tracker, clock, TimeSpan.FromMinutes(15), TimeSpan.FromSeconds(30));
        Assert.True(tracker.UsedTime >= TimeSpan.FromMinutes(15));

        // Heartbeats stopped arriving for a stretch that happens to cross local midnight; the
        // very next heartbeat must not fold that entire stale gap into the new day's usage.
        clock.Advance(TimeSpan.FromHours(24));
        tracker.RecordHeartbeat(idle: TimeSpan.Zero);

        Assert.False(tracker.IsOverLimit);
        Assert.Equal(TimeSpan.Zero, tracker.UsedTime);
    }

    [Fact]
    public void RecordHeartbeat_AfterLongGapSameDay_DoesNotCountTheGapAsUsage()
    {
        var clock = new FakeTimeProvider();
        clock.SetUtcNow(new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero)); // noon, nowhere near midnight
        var tracker = new SessionTracker(clock, limit: TimeSpan.FromMinutes(20), idleResetThreshold: TimeSpan.FromSeconds(60));

        tracker.RecordHeartbeat(idle: TimeSpan.Zero);
        clock.Advance(TimeSpan.FromSeconds(10));
        tracker.RecordHeartbeat(idle: TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromSeconds(10), tracker.UsedTime);

        // Heartbeats stop arriving for 2 hours mid-day (e.g. laptop lid closed for a nap) -- no
        // midnight crossed, so RolloverIfNewDay/Reset never fires. The child then touches the
        // machine to wake it, so the *current* idle reading is low again -- but that only proves
        // they're active now, not that they were active for the whole 2-hour gap.
        clock.Advance(TimeSpan.FromHours(2));
        tracker.RecordHeartbeat(idle: TimeSpan.Zero);

        Assert.Equal(TimeSpan.FromSeconds(10), tracker.UsedTime);
        Assert.False(tracker.IsOverLimit);
    }

    [Fact]
    public void RecordHeartbeat_AfterPollAlreadyRolledOverDay_DoesNotCountTheGapAsUsage()
    {
        var clock = new FakeTimeProvider();
        var tracker = new SessionTracker(clock, limit: TimeSpan.FromMinutes(20), idleResetThreshold: TimeSpan.FromSeconds(60));

        tracker.RecordHeartbeat(idle: TimeSpan.Zero);
        SimulateActiveUse(tracker, clock, TimeSpan.FromMinutes(15), TimeSpan.FromSeconds(30));
        Assert.True(tracker.UsedTime >= TimeSpan.FromMinutes(15));

        // Simulates LockEnforcerService's independent 1s poll crossing midnight (e.g. overnight
        // sleep) and rolling the day over on its own, *before* the next heartbeat arrives -- this
        // is the common case in production, since the poll runs every second regardless of
        // whether Tray is even connected.
        clock.Advance(TimeSpan.FromHours(24));
        var rolledOverByPoll = tracker.RolloverIfNewDay();
        Assert.True(rolledOverByPoll);

        // The heartbeat's own RolloverIfNewDay() call is now a same-day no-op, so it must not
        // fall back to folding the stale pre-sleep baseline into the fresh day's usage.
        tracker.RecordHeartbeat(idle: TimeSpan.Zero);

        Assert.False(tracker.IsOverLimit);
        Assert.Equal(TimeSpan.Zero, tracker.UsedTime);
    }

    [Fact]
    public void IsBreakDue_FalseWhenBreakIntervalDisabled()
    {
        var clock = new FakeTimeProvider();
        var tracker = new SessionTracker(clock, limit: TimeSpan.FromMinutes(120), idleResetThreshold: TimeSpan.FromSeconds(60), breakInterval: TimeSpan.Zero);

        tracker.RecordHeartbeat(idle: TimeSpan.Zero);
        clock.Advance(TimeSpan.FromMinutes(60));
        tracker.RecordHeartbeat(idle: TimeSpan.Zero);

        Assert.False(tracker.IsBreakDue);
    }

    [Fact]
    public void IsBreakDue_TrueOnceUsedSinceBreakReachesInterval()
    {
        var clock = new FakeTimeProvider();
        var tracker = new SessionTracker(clock, limit: TimeSpan.FromMinutes(120), idleResetThreshold: TimeSpan.FromSeconds(60), breakInterval: TimeSpan.FromMinutes(30));

        tracker.RecordHeartbeat(idle: TimeSpan.Zero);
        SimulateActiveUse(tracker, clock, TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(30));

        Assert.True(tracker.IsBreakDue);
    }

    [Fact]
    public void IsBreakDue_FalseWhenAlreadyOverDailyLimit()
    {
        var clock = new FakeTimeProvider();
        var tracker = new SessionTracker(clock, limit: TimeSpan.FromMinutes(20), idleResetThreshold: TimeSpan.FromSeconds(60), breakInterval: TimeSpan.FromMinutes(10));

        tracker.RecordHeartbeat(idle: TimeSpan.Zero);
        SimulateActiveUse(tracker, clock, TimeSpan.FromMinutes(20), TimeSpan.FromSeconds(30));

        Assert.True(tracker.IsOverLimit);
        Assert.False(tracker.IsBreakDue);
    }

    [Fact]
    public void ResetBreak_ClearsUsedSinceBreakButNotUsedTime()
    {
        var clock = new FakeTimeProvider();
        var tracker = new SessionTracker(clock, limit: TimeSpan.FromMinutes(120), idleResetThreshold: TimeSpan.FromSeconds(60), breakInterval: TimeSpan.FromMinutes(30));

        tracker.RecordHeartbeat(idle: TimeSpan.Zero);
        SimulateActiveUse(tracker, clock, TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(30));
        Assert.True(tracker.IsBreakDue);

        tracker.ResetBreak();

        Assert.False(tracker.IsBreakDue);
        Assert.Equal(TimeSpan.FromMinutes(30), tracker.UsedTime);
    }
}
