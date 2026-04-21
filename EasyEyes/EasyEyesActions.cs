namespace EasyEyes;

public sealed class EasyEyesActions : IEasyEyesActions
{
    private readonly CountdownTimer _activityTimer;
    private readonly CountdownTimer _restTimer;
    private readonly Action _showOverlay;
    private readonly Action _hideOverlay;
    private readonly Action _notifyUser;

    public EasyEyesActions(
        TimeProvider timeProvider,
        ITimerScheduler activityScheduler,
        ITimerScheduler restScheduler,
        TimeSpan activityDuration,
        TimeSpan restDuration,
        Action showOverlay,
        Action hideOverlay,
        Action showToast,
        TriggerRelay triggerRelay)
    {
        _showOverlay = showOverlay;
        _hideOverlay = hideOverlay;
        _notifyUser = showToast;

        _activityTimer = new CountdownTimer(
            timeProvider, activityScheduler, activityDuration,
            () => triggerRelay.Fire(Trigger.ActivityTimerExpired));

        _restTimer = new CountdownTimer(
            timeProvider, restScheduler, restDuration,
            () => triggerRelay.Fire(Trigger.RestTimerExpired));
    }

    public void ShowOverlay() => _showOverlay();
    public void HideOverlay() => _hideOverlay();
    public void NotifyUser() => _notifyUser();

    public void SuspendActivityTimer() => _activityTimer.Suspend();
    public void ResumeActivityTimer() => _activityTimer.Resume();
    public void ResetActivityTimer() => _activityTimer.Reset();
    public void RestartRestTimer()
    {
        _restTimer.Stop();
        _restTimer.Start();
    }

    public void StopRestTimer() => _restTimer.Stop();

    public TimeSpan GetTRemaining() => _activityTimer.GetRemaining();

    public void ExtendActivityTimer(TimeSpan duration) => _activityTimer.Extend(duration);

    public void StartActivityTimer() => _activityTimer.Start();
}
