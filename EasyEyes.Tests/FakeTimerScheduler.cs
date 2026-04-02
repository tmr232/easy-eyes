namespace EasyEyes.Tests;

/// <summary>
/// Test implementation of ITimerScheduler that captures the callback
/// and lets tests trigger expiry manually.
/// </summary>
public class FakeTimerScheduler : ITimerScheduler
{
    private Action? _callback;

    public TimeSpan? LastInterval { get; private set; }
    public bool IsRunning => _callback != null;

    public void Start(TimeSpan interval, Action callback)
    {
        _callback = callback;
        LastInterval = interval;
    }

    public void Cancel()
    {
        _callback = null;
    }

    /// <summary>
    /// Simulates the timer expiring by invoking the captured callback.
    /// </summary>
    public void Expire()
    {
        var callback = _callback ?? throw new InvalidOperationException("Timer is not running.");
        _callback = null;
        callback();
    }
}
