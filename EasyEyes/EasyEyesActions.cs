namespace EasyEyes;

public class EasyEyesActions : IEasyEyesActions
{
    private readonly CountdownTimer _tTimer;
    private readonly CountdownTimer _lTimer;
    private readonly Action _showOverlay;
    private readonly Action _hideOverlay;
    private readonly Action _showToast;

    public EasyEyesActions(
        TimeProvider timeProvider,
        ITimerScheduler tScheduler,
        ITimerScheduler lScheduler,
        TimeSpan tDuration,
        TimeSpan lDuration,
        Action showOverlay,
        Action hideOverlay,
        Action showToast,
        Action<Trigger> fireTrigger)
    {
        _showOverlay = showOverlay;
        _hideOverlay = hideOverlay;
        _showToast = showToast;

        _tTimer = new CountdownTimer(
            timeProvider, tScheduler, tDuration,
            () => fireTrigger(Trigger.TTimerExpired));

        _lTimer = new CountdownTimer(
            timeProvider, lScheduler, lDuration,
            () => fireTrigger(Trigger.LTimerExpired));
    }

    public void ShowOverlay() => _showOverlay();
    public void HideOverlay() => _hideOverlay();
    public void ShowToast() => _showToast();

    public void SuspendTTimer() => _tTimer.Suspend();
    public void ResumeTTimer() => _tTimer.Resume();
    public void ResetTTimer() => _tTimer.Reset();
    public void RestartLTimer()
    {
        _lTimer.Stop();
        _lTimer.Start();
    }

    public void StopLTimer() => _lTimer.Stop();

    public TimeSpan GetTRemaining() => _tTimer.GetRemaining();

    public void ExtendTTimer(TimeSpan duration) => _tTimer.Extend(duration);

    public void StartTTimer() => _tTimer.Start();
}
