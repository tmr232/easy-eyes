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
/// Tracks enabled busy indicators and provides an aggregate busy state.
/// When any enabled indicator is active, <see cref="IsBusy"/> is true.
/// When the last active indicator clears, <see cref="BusyCleared"/> fires.
/// </summary>
/// <remarks>
/// The mic/camera indicator is exposed as a single user-facing toggle
/// but internally uses two <see cref="ActivationWindowIndicator"/> instances
/// (one for camera, one for mic). Either device being in use keeps the
/// indicator active.
/// </remarks>
public class BusyIndicatorManager
{
    private readonly ActivationWindowIndicator _cameraIndicator;
    private readonly ActivationWindowIndicator _microphoneIndicator;

    /// <summary>
    /// True if any enabled indicator is currently active.
    /// </summary>
    public bool IsBusy => _cameraIndicator.IsActive || _microphoneIndicator.IsActive;

    /// <summary>
    /// Whether the mic/camera indicator is currently enabled.
    /// </summary>
    public bool IsMicCameraEnabled => _cameraIndicator.IsEnabled || _microphoneIndicator.IsEnabled;

    /// <summary>
    /// The current meeting indicator mode.
    /// </summary>
    public MeetingMode CurrentMeetingMode { get; private set; }

    /// <summary>
    /// Fires when <see cref="IsBusy"/> transitions from true to false.
    /// </summary>
    public event EventHandler? BusyCleared;

    /// <summary>
    /// Fires when the activation window expires without mic or camera
    /// becoming active. The indicator has been auto-disabled.
    /// </summary>
    public event EventHandler? ActivationExpired;

    /// <summary>
    /// Creates a manager wired to a <see cref="MediaDeviceMonitor"/> for
    /// camera and microphone monitoring.
    /// </summary>
    public BusyIndicatorManager(
        MediaDeviceMonitor monitor,
        ITimerScheduler cameraGraceScheduler,
        ITimerScheduler microphoneGraceScheduler,
        TimeSpan gracePeriod,
        ITimerScheduler cameraActivationScheduler,
        ITimerScheduler microphoneActivationScheduler,
        TimeSpan activationWindow)
        : this(
            new ActivationWindowIndicator(
                new BusyIndicator(
                    isStateActive: () => MediaDeviceMonitor.IsCameraInUse,
                    subscribeActivated: h => monitor.CameraActivated += h,
                    unsubscribeActivated: h => monitor.CameraActivated -= h,
                    subscribeDeactivated: h => monitor.CameraDeactivated += h,
                    unsubscribeDeactivated: h => monitor.CameraDeactivated -= h,
                    graceScheduler: cameraGraceScheduler,
                    gracePeriod: gracePeriod),
                cameraActivationScheduler,
                activationWindow),
            new ActivationWindowIndicator(
                new BusyIndicator(
                    isStateActive: () => MediaDeviceMonitor.IsMicrophoneInUse,
                    subscribeActivated: h => monitor.MicrophoneActivated += h,
                    unsubscribeActivated: h => monitor.MicrophoneActivated -= h,
                    subscribeDeactivated: h => monitor.MicrophoneDeactivated += h,
                    unsubscribeDeactivated: h => monitor.MicrophoneDeactivated -= h,
                    graceScheduler: microphoneGraceScheduler,
                    gracePeriod: gracePeriod),
                microphoneActivationScheduler,
                activationWindow))
    {
    }

    /// <summary>
    /// Creates a manager with pre-built indicators (for testing).
    /// </summary>
    public BusyIndicatorManager(
        ActivationWindowIndicator cameraIndicator,
        ActivationWindowIndicator microphoneIndicator)
    {
        _cameraIndicator = cameraIndicator;
        _microphoneIndicator = microphoneIndicator;

        _cameraIndicator.Cleared += OnIndicatorCleared;
        _microphoneIndicator.Cleared += OnIndicatorCleared;
        _cameraIndicator.ActivationExpired += OnActivationExpired;
        _microphoneIndicator.ActivationExpired += OnActivationExpired;
    }

    /// <summary>
    /// Sets the meeting indicator mode.  <see cref="MeetingMode.Off"/>
    /// disables the indicator; other values enable it with the
    /// corresponding persistence behaviour.
    /// </summary>
    public void SetMeetingMode(MeetingMode mode)
    {
        if (mode == MeetingMode.Off)
        {
            DisableMicCamera();
            return;
        }

        CurrentMeetingMode = mode;
        var persistent = mode == MeetingMode.Always;
        _cameraIndicator.Persistent = persistent;
        _microphoneIndicator.Persistent = persistent;
        _cameraIndicator.Enable();
        _microphoneIndicator.Enable();
    }

    /// <summary>
    /// Enables the mic/camera indicator in <see cref="MeetingMode.UntilEnd"/>
    /// mode. Both camera and microphone are monitored; either being in use
    /// makes the indicator active.
    /// </summary>
    public void EnableMicCamera()
    {
        SetMeetingMode(MeetingMode.UntilEnd);
    }

    /// <summary>
    /// Disables the mic/camera indicator.
    /// </summary>
    public void DisableMicCamera()
    {
        CurrentMeetingMode = MeetingMode.Off;
        var wasBusy = IsBusy;
        _cameraIndicator.Persistent = false;
        _microphoneIndicator.Persistent = false;
        _cameraIndicator.Disable();
        _microphoneIndicator.Disable();
        if (wasBusy)
            BusyCleared?.Invoke(this, EventArgs.Empty);
    }

    private void OnIndicatorCleared(object? sender, EventArgs e)
    {
        if (!IsBusy)
            BusyCleared?.Invoke(this, EventArgs.Empty);
    }

    private void OnActivationExpired(object? sender, EventArgs e)
    {
        if (!IsMicCameraEnabled)
            ActivationExpired?.Invoke(this, EventArgs.Empty);
    }
}
