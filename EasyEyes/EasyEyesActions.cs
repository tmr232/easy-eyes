using System.Windows.Threading;

namespace EasyEyes;

public class EasyEyesActions : IEasyEyesActions
{
    private readonly DispatcherTimer _tTimer;
    private readonly DispatcherTimer _lTimer;
    private readonly Action _showOverlay;
    private readonly Action _hideOverlay;
    private readonly Action _showToast;

    private TimeSpan _tRemaining;
    private readonly TimeSpan _tDuration;
    private readonly TimeSpan _lDuration;
    private DateTime _tStartedAt;

    public EasyEyesActions(
        TimeSpan tDuration,
        TimeSpan lDuration,
        Action showOverlay,
        Action hideOverlay,
        Action showToast,
        Action<Trigger> fireTrigger)
    {
        _tDuration = tDuration;
        _lDuration = lDuration;
        _tRemaining = tDuration;
        _showOverlay = showOverlay;
        _hideOverlay = hideOverlay;
        _showToast = showToast;

        _tTimer = new DispatcherTimer();
        _tTimer.Tick += (_, _) =>
        {
            _tTimer.Stop();
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

    public void SuspendTTimer()
    {
        if (_tTimer.IsEnabled)
        {
            _tRemaining -= DateTime.UtcNow - _tStartedAt;
            if (_tRemaining < TimeSpan.Zero)
                _tRemaining = TimeSpan.Zero;
            _tTimer.Stop();
        }
    }

    public void ResumeTTimer()
    {
        _tTimer.Interval = _tRemaining;
        _tStartedAt = DateTime.UtcNow;
        _tTimer.Start();
    }

    public void ResetTTimer()
    {
        _tTimer.Stop();
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
    /// Starts the T timer initially (called once at app startup).
    /// </summary>
    public void StartTTimer()
    {
        _tTimer.Interval = _tDuration;
        _tRemaining = _tDuration;
        _tStartedAt = DateTime.UtcNow;
        _tTimer.Start();
    }
}
