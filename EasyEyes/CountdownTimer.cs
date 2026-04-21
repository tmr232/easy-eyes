using System.Windows.Threading;

namespace EasyEyes;

/// <summary>
/// Abstraction over a one-shot timer scheduling mechanism, allowing injection
/// of fake schedulers for testing.
/// </summary>
public interface ITimerScheduler
{
    void Start(TimeSpan interval, Action callback);
    void Cancel();
}

/// <summary>
/// Production implementation using WPF's DispatcherTimer.
/// </summary>
public class DispatcherTimerScheduler : ITimerScheduler
{
    private readonly DispatcherTimer _timer = new();
    private Action? _callback;

    public void Start(TimeSpan interval, Action callback)
    {
        Cancel();
        _callback = callback;
        _timer.Interval = interval;
        _timer.Tick += OnTick;
        _timer.Start();
    }

    public void Cancel()
    {
        _timer.Stop();
        if (_callback != null)
        {
            _timer.Tick -= OnTick;
            _callback = null;
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var callback = _callback;
        Cancel();
        callback?.Invoke();
    }
}

/// <summary>
/// A countdown timer that supports suspend/resume/reset/extend operations.
/// Encapsulates all timer state and enforces invariants (e.g., remaining is
/// zeroed on expiry).
/// </summary>
public class CountdownTimer
{
    private readonly TimeProvider _timeProvider;
    private readonly ITimerScheduler _scheduler;
    private readonly TimeSpan _duration;
    private readonly Action _onExpired;

    private TimeSpan _remaining;
    private DateTime _startedAt;
    private bool _running;

    public CountdownTimer(
        TimeProvider timeProvider,
        ITimerScheduler scheduler,
        TimeSpan duration,
        Action onExpired)
    {
        _timeProvider = timeProvider;
        _scheduler = scheduler;
        _duration = duration;
        _onExpired = onExpired;
        _remaining = duration;
    }

    public void Start()
    {
        _remaining = _duration;
        StartScheduler(_duration);
    }

    public void Suspend()
    {
        if (!_running)
            return;

        _remaining -= _timeProvider.GetUtcNow().UtcDateTime - _startedAt;
        if (_remaining < TimeSpan.Zero)
            _remaining = TimeSpan.Zero;

        _scheduler.Cancel();
        _running = false;
    }

    public void Resume()
    {
        if (_running)
            return;

        StartScheduler(_remaining);
    }

    public void Reset()
    {
        _scheduler.Cancel();
        _running = false;
        _remaining = _duration;
    }

    public void Extend(TimeSpan duration)
    {
        if (duration > _remaining)
            _remaining = duration;
    }

    public void Stop()
    {
        _scheduler.Cancel();
        _running = false;
    }

    public TimeSpan GetRemaining()
    {
        if (_running)
        {
            var elapsed = _timeProvider.GetUtcNow().UtcDateTime - _startedAt;
            var remaining = _remaining - elapsed;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }

        return _remaining;
    }

    private void StartScheduler(TimeSpan interval)
    {
        _startedAt = _timeProvider.GetUtcNow().UtcDateTime;
        _running = true;
        _scheduler.Start(interval, OnExpired);
    }

    private void OnExpired()
    {
        _running = false;
        _remaining = TimeSpan.Zero;
        _onExpired();
    }
}
