using EasyEyes;
using Microsoft.Extensions.Time.Testing;

namespace EasyEyes.Tests;

public class CountdownTimerTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly FakeTimerScheduler _scheduler = new();
    private readonly TimeSpan _duration = TimeSpan.FromMinutes(20);
    private int _expiredCount;

    private CountdownTimer CreateTimer() =>
        new(_time, _scheduler, _duration, () => _expiredCount++);

    // --- Start ---

    [Fact]
    public void Start_SetsRemainingToFullDuration()
    {
        var timer = CreateTimer();
        timer.Start();

        Assert.Equal(_duration, timer.GetRemaining());
    }

    [Fact]
    public void Start_StartsScheduler()
    {
        var timer = CreateTimer();
        timer.Start();

        Assert.True(_scheduler.IsRunning);
        Assert.Equal(_duration, _scheduler.LastInterval);
    }

    // --- GetRemaining while running ---

    [Fact]
    public void GetRemaining_WhileRunning_ReflectsElapsedTime()
    {
        var timer = CreateTimer();
        timer.Start();

        _time.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal(TimeSpan.FromMinutes(15), timer.GetRemaining());
    }

    [Fact]
    public void GetRemaining_WhileRunning_ClampsToZero()
    {
        var timer = CreateTimer();
        timer.Start();

        _time.Advance(_duration + TimeSpan.FromSeconds(1));

        Assert.Equal(TimeSpan.Zero, timer.GetRemaining());
    }

    // --- Expiry ---

    [Fact]
    public void Expire_SetsRemainingToZero()
    {
        var timer = CreateTimer();
        timer.Start();
        _time.Advance(_duration);

        _scheduler.Expire();

        Assert.Equal(TimeSpan.Zero, timer.GetRemaining());
    }

    [Fact]
    public void Expire_InvokesCallback()
    {
        var timer = CreateTimer();
        timer.Start();
        _time.Advance(_duration);

        _scheduler.Expire();

        Assert.Equal(1, _expiredCount);
    }

    [Fact]
    public void Expire_StopsTimer()
    {
        var timer = CreateTimer();
        timer.Start();
        _time.Advance(_duration);

        _scheduler.Expire();

        Assert.False(_scheduler.IsRunning);
    }

    // --- Suspend ---

    [Fact]
    public void Suspend_CapturesRemainingTime()
    {
        var timer = CreateTimer();
        timer.Start();
        _time.Advance(TimeSpan.FromMinutes(8));

        timer.Suspend();

        Assert.Equal(TimeSpan.FromMinutes(12), timer.GetRemaining());
    }

    [Fact]
    public void Suspend_StopsScheduler()
    {
        var timer = CreateTimer();
        timer.Start();
        _time.Advance(TimeSpan.FromMinutes(8));

        timer.Suspend();

        Assert.False(_scheduler.IsRunning);
    }

    [Fact]
    public void Suspend_WhenNotRunning_IsNoOp()
    {
        var timer = CreateTimer();
        timer.Start();
        _time.Advance(TimeSpan.FromMinutes(8));
        timer.Suspend();
        var remaining = timer.GetRemaining();

        timer.Suspend(); // second suspend should be a no-op

        Assert.Equal(remaining, timer.GetRemaining());
    }

    [Fact]
    public void Suspend_WhenPastDuration_ClampsToZero()
    {
        var timer = CreateTimer();
        timer.Start();
        _time.Advance(_duration + TimeSpan.FromSeconds(5));

        timer.Suspend();

        Assert.Equal(TimeSpan.Zero, timer.GetRemaining());
    }

    // --- Resume ---

    [Fact]
    public void Resume_StartsSchedulerWithRemainingTime()
    {
        var timer = CreateTimer();
        timer.Start();
        _time.Advance(TimeSpan.FromMinutes(8));
        timer.Suspend();

        timer.Resume();

        Assert.True(_scheduler.IsRunning);
        Assert.Equal(TimeSpan.FromMinutes(12), _scheduler.LastInterval);
    }

    [Fact]
    public void SuspendAndResume_GetRemainingReflectsNewElapsed()
    {
        var timer = CreateTimer();
        timer.Start();
        _time.Advance(TimeSpan.FromMinutes(8));
        timer.Suspend();
        _time.Advance(TimeSpan.FromMinutes(100)); // time while suspended doesn't count
        timer.Resume();
        _time.Advance(TimeSpan.FromMinutes(2));

        Assert.Equal(TimeSpan.FromMinutes(10), timer.GetRemaining());
    }

    // --- Reset ---

    [Fact]
    public void Reset_SetsRemainingToFullDuration()
    {
        var timer = CreateTimer();
        timer.Start();
        _time.Advance(TimeSpan.FromMinutes(10));
        timer.Suspend();

        timer.Reset();

        Assert.Equal(_duration, timer.GetRemaining());
    }

    [Fact]
    public void Reset_StopsScheduler()
    {
        var timer = CreateTimer();
        timer.Start();

        timer.Reset();

        Assert.False(_scheduler.IsRunning);
    }

    // --- Extend ---

    [Fact]
    public void Extend_WhenLargerThanRemaining_SetsRemaining()
    {
        var timer = CreateTimer();
        timer.Start();
        _time.Advance(TimeSpan.FromMinutes(15));
        timer.Suspend(); // remaining = 5 min

        timer.Extend(TimeSpan.FromMinutes(10));

        Assert.Equal(TimeSpan.FromMinutes(10), timer.GetRemaining());
    }

    [Fact]
    public void Extend_WhenSmallerThanRemaining_IsNoOp()
    {
        var timer = CreateTimer();
        timer.Start();
        _time.Advance(TimeSpan.FromMinutes(5));
        timer.Suspend(); // remaining = 15 min

        timer.Extend(TimeSpan.FromMinutes(10));

        Assert.Equal(TimeSpan.FromMinutes(15), timer.GetRemaining());
    }

    [Fact]
    public void Extend_AfterExpiry_SetsRemaining()
    {
        var timer = CreateTimer();
        timer.Start();
        _time.Advance(_duration);
        _scheduler.Expire(); // remaining = 0

        timer.Extend(TimeSpan.FromMinutes(15));

        Assert.Equal(TimeSpan.FromMinutes(15), timer.GetRemaining());
    }

    // --- Stop ---

    [Fact]
    public void Stop_StopsSchedulerAndPreservesRemaining()
    {
        var timer = CreateTimer();
        timer.Start();
        _time.Advance(TimeSpan.FromMinutes(5));

        timer.Stop();

        Assert.False(_scheduler.IsRunning);
    }

    // --- Full cycle: the bug scenario ---

    [Fact]
    public void AfterExpiry_ExtendThenResume_UsesExtendedDuration()
    {
        // This is the exact bug scenario: timer expires naturally,
        // then ExtendTTimer + Resume should use the pause duration, not the original.
        var timer = CreateTimer();
        timer.Start();
        _time.Advance(_duration);
        _scheduler.Expire();

        // Simulate PauseForDuration: suspend (no-op since expired), extend, resume
        timer.Suspend();
        var pauseDuration = TimeSpan.FromMinutes(15);
        timer.Extend(pauseDuration);
        timer.Resume();

        Assert.Equal(pauseDuration, _scheduler.LastInterval);
        Assert.Equal(pauseDuration, timer.GetRemaining());
    }

    [Fact]
    public void AfterExpiry_RemainingIsZero_NotOriginalDuration()
    {
        // The core invariant: after expiry, remaining must be zero.
        var timer = CreateTimer();
        timer.Start();
        _time.Advance(_duration);

        _scheduler.Expire();

        Assert.Equal(TimeSpan.Zero, timer.GetRemaining());
    }
}
