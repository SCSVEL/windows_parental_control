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
}
