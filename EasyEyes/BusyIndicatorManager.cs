namespace EasyEyes;

/// <summary>
/// Controls how the "In a meeting" indicator behaves.
/// </summary>
public enum MeetingMode
{
    /// <summary>Indicator disabled.</summary>
    Off,

    /// <summary>
    /// Indicator enabled. Auto-disables when the meeting ends
    /// (mic/camera grace period expires).
    /// </summary>
    UntilEnd,

    /// <summary>
    /// Indicator enabled and persistent. When the meeting ends the
    /// indicator stays enabled and will re-activate when mic/camera
    /// becomes active again. Must be toggled off manually.
    /// </summary>
    Always,
}

/// <summary>
/// Tracks an "In a meeting" busy indicator backed by a
/// <see cref="MediaDeviceMonitor"/>. When the indicator is active,
/// <see cref="IsBusy"/> is true. When it clears, <see cref="BusyCleared"/>
/// fires.
/// </summary>
public class BusyIndicatorManager
{
    private readonly ActivationWindowIndicator _indicator;

    /// <summary>
    /// True if the indicator is currently active.
    /// </summary>
    public bool IsBusy => _indicator.IsActive;

    /// <summary>
    /// Whether the indicator is currently enabled.
    /// </summary>
    public bool IsEnabled => _indicator.IsEnabled;

    /// <summary>
    /// The current meeting indicator mode.
    /// </summary>
    public MeetingMode CurrentMeetingMode { get; private set; }

    /// <summary>
    /// Fires when <see cref="IsBusy"/> transitions from true to false.
    /// </summary>
    public event EventHandler? BusyCleared;

    /// <summary>
    /// Fires when the activation window expires without any device
    /// becoming active. The indicator has been auto-disabled.
    /// </summary>
    public event EventHandler? ActivationExpired;

    /// <summary>
    /// Creates a manager wired to a <see cref="MediaDeviceMonitor"/> for
    /// camera and microphone monitoring.
    /// </summary>
    public BusyIndicatorManager(
        MediaDeviceMonitor monitor,
        ITimerScheduler graceScheduler,
        TimeSpan gracePeriod,
        ITimerScheduler activationScheduler,
        TimeSpan activationWindow)
        : this(
            new ActivationWindowIndicator(
                new BusyIndicator(
                    isStateActive: () => monitor.IsInUse,
                    subscribeActivated: h => monitor.Activated += h,
                    unsubscribeActivated: h => monitor.Activated -= h,
                    subscribeDeactivated: h => monitor.Deactivated += h,
                    unsubscribeDeactivated: h => monitor.Deactivated -= h,
                    graceScheduler: graceScheduler,
                    gracePeriod: gracePeriod),
                activationScheduler,
                activationWindow))
    {
    }

    /// <summary>
    /// Creates a manager with a pre-built indicator (for testing).
    /// </summary>
    public BusyIndicatorManager(ActivationWindowIndicator indicator)
    {
        _indicator = indicator;

        _indicator.Cleared += (_, e) => BusyCleared?.Invoke(this, e);
        _indicator.ActivationExpired += (_, e) => ActivationExpired?.Invoke(this, e);
    }

    /// <summary>
    /// Sets the meeting indicator mode. <see cref="MeetingMode.Off"/>
    /// disables the indicator; other values enable it with the
    /// corresponding persistence behaviour.
    /// </summary>
    public void SetMeetingMode(MeetingMode mode)
    {
        if (mode == MeetingMode.Off)
        {
            DisableMeeting();
            return;
        }

        CurrentMeetingMode = mode;
        _indicator.Persistent = mode == MeetingMode.Always;
        _indicator.Enable();
    }

    /// <summary>
    /// Enables the indicator in <see cref="MeetingMode.UntilEnd"/> mode.
    /// </summary>
    public void EnableMeeting()
    {
        SetMeetingMode(MeetingMode.UntilEnd);
    }

    /// <summary>
    /// Disables the indicator.
    /// </summary>
    public void DisableMeeting()
    {
        CurrentMeetingMode = MeetingMode.Off;
        var wasBusy = IsBusy;
        _indicator.Persistent = false;
        _indicator.Disable();
        if (wasBusy)
            BusyCleared?.Invoke(this, EventArgs.Empty);
    }
}
