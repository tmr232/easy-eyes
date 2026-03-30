using System.Windows.Threading;

namespace EasyEyes;

public class EasyEyesActions : IEasyEyesActions
{
    private readonly DispatcherTimer _tTimer;
    private readonly DispatcherTimer _lTimer;
    private readonly TimeProvider _timeProvider;
    private readonly Action _showOverlay;
    private readonly Action _hideOverlay;
    private readonly Action _showToast;
    private readonly Action _clearToast;

    private TimeSpan _tRemaining;
    private readonly TimeSpan _tDuration;
    private readonly TimeSpan _lDuration;
    private DateTime _tStartedAt;
    private bool _tRunning;

    public EasyEyesActions(
        TimeProvider timeProvider,
        TimeSpan tDuration,
        TimeSpan lDuration,
        Action showOverlay,
        Action hideOverlay,
        Action showToast,
        Action clearToast,
        Action<Trigger> fireTrigger)
    {
        _timeProvider = timeProvider;
        _tDuration = tDuration;
        _lDuration = lDuration;
        _tRemaining = tDuration;
        _showOverlay = showOverlay;
        _hideOverlay = hideOverlay;
        _showToast = showToast;
        _clearToast = clearToast;

        _tTimer = new DispatcherTimer();
        _tTimer.Tick += (_, _) =>
        {
            _tTimer.Stop();
            _tRunning = false;
            fireTrigger(Trigger.TTimerExpired);
        };

        _lTimer = new DispatcherTimer { Interval = _lDuration };
        _lTimer.Tick += (_, _) =>
        {
            _lTimer.Stop();
            fireTrigger(Trigger.LTimerExpired);
        };
    }

    public void ShowOverlay() => _showOverlay();
    public void HideOverlay() => _hideOverlay();
    public void ShowToast() => _showToast();
    public void ClearToast() => _clearToast();

    public void SuspendTTimer()
    {
        if (_tTimer.IsEnabled)
        {
            _tRemaining -= _timeProvider.GetUtcNow().UtcDateTime - _tStartedAt;
            if (_tRemaining < TimeSpan.Zero)
                _tRemaining = TimeSpan.Zero;
            _tTimer.Stop();
            _tRunning = false;
        }
    }

    public void ResumeTTimer()
    {
        _tTimer.Interval = _tRemaining;
        _tStartedAt = _timeProvider.GetUtcNow().UtcDateTime;
        _tRunning = true;
        _tTimer.Start();
    }

    public void ResetTTimer()
    {
        _tTimer.Stop();
        _tRunning = false;
        _tRemaining = _tDuration;
    }

    public void RestartLTimer()
    {
        _lTimer.Stop();
        _lTimer.Interval = _lDuration;
        _lTimer.Start();
    }

    public void StopLTimer()
    {
        _lTimer.Stop();
    }

    /// <summary>
    /// Returns the time remaining on the T timer.
    /// </summary>
    public TimeSpan GetTRemaining()
    {
        if (_tRunning)
        {
            var elapsed = _timeProvider.GetUtcNow().UtcDateTime - _tStartedAt;
            var remaining = _tRemaining - elapsed;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }

        return _tRemaining;
    }

    public void ExtendTTimer(TimeSpan duration)
    {
        if (duration > _tRemaining)
            _tRemaining = duration;
    }

    /// <summary>
    /// Starts the T timer initially (called once at app startup).
    /// </summary>
    public void StartTTimer()
    {
        _tTimer.Interval = _tDuration;
        _tRemaining = _tDuration;
        _tStartedAt = _timeProvider.GetUtcNow().UtcDateTime;
        _tRunning = true;
        _tTimer.Start();
    }
}
