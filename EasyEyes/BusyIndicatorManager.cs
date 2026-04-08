namespace EasyEyes;

/// <summary>
/// Tracks enabled busy indicators and provides an aggregate busy state.
/// When any enabled indicator is active, <see cref="IsBusy"/> is true.
/// When the last active indicator clears, <see cref="BusyCleared"/> fires.
/// </summary>
/// <remarks>
/// The mic/camera indicator is exposed as a single user-facing toggle
/// but internally uses two <see cref="BusyIndicator"/> instances (one for
/// camera, one for mic). Either device being in use keeps the indicator
/// active.
/// </remarks>
public class BusyIndicatorManager
{
    private readonly BusyIndicator _cameraIndicator;
    private readonly BusyIndicator _microphoneIndicator;

    /// <summary>
    /// True if any enabled indicator is currently active.
    /// </summary>
    public bool IsBusy => _cameraIndicator.IsActive || _microphoneIndicator.IsActive;

    /// <summary>
    /// Whether the mic/camera indicator is currently enabled.
    /// </summary>
    public bool IsMicCameraEnabled => _cameraIndicator.IsEnabled || _microphoneIndicator.IsEnabled;

    /// <summary>
    /// Fires when <see cref="IsBusy"/> transitions from true to false.
    /// </summary>
    public event EventHandler? BusyCleared;

    /// <summary>
    /// Creates a manager wired to a <see cref="MediaDeviceMonitor"/> for
    /// camera and microphone monitoring.
    /// </summary>
    public BusyIndicatorManager(
        MediaDeviceMonitor monitor,
        ITimerScheduler cameraGraceScheduler,
        ITimerScheduler microphoneGraceScheduler,
        TimeSpan gracePeriod)
        : this(
            new BusyIndicator(
                isStateActive: () => MediaDeviceMonitor.IsCameraInUse,
                subscribeActivated: h => monitor.CameraActivated += h,
                unsubscribeActivated: h => monitor.CameraActivated -= h,
                subscribeDeactivated: h => monitor.CameraDeactivated += h,
                unsubscribeDeactivated: h => monitor.CameraDeactivated -= h,
                graceScheduler: cameraGraceScheduler,
                gracePeriod: gracePeriod),
            new BusyIndicator(
                isStateActive: () => MediaDeviceMonitor.IsMicrophoneInUse,
                subscribeActivated: h => monitor.MicrophoneActivated += h,
                unsubscribeActivated: h => monitor.MicrophoneActivated -= h,
                subscribeDeactivated: h => monitor.MicrophoneDeactivated += h,
                unsubscribeDeactivated: h => monitor.MicrophoneDeactivated -= h,
                graceScheduler: microphoneGraceScheduler,
                gracePeriod: gracePeriod))
    {
    }

    /// <summary>
    /// Creates a manager with pre-built indicators (for testing).
    /// </summary>
    public BusyIndicatorManager(
        BusyIndicator cameraIndicator,
        BusyIndicator microphoneIndicator)
    {
        _cameraIndicator = cameraIndicator;
        _microphoneIndicator = microphoneIndicator;

        _cameraIndicator.Cleared += OnIndicatorCleared;
        _microphoneIndicator.Cleared += OnIndicatorCleared;
    }

    /// <summary>
    /// Enables the mic/camera indicator. Both camera and microphone are
    /// monitored; either being in use makes the indicator active.
    /// </summary>
    public void EnableMicCamera()
    {
        _cameraIndicator.Enable();
        _microphoneIndicator.Enable();
    }

    /// <summary>
    /// Disables the mic/camera indicator.
    /// </summary>
    public void DisableMicCamera()
    {
        var wasBusy = IsBusy;
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
}
