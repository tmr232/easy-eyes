using EasyEyes;

namespace EasyEyes.Tests;

public class BusyIndicatorManagerTests
{
    private static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(5);

    private bool _cameraActive;
    private event EventHandler? CameraActivated;
    private event EventHandler? CameraDeactivated;

    private bool _micActive;
    private event EventHandler? MicActivated;
    private event EventHandler? MicDeactivated;

    private static readonly TimeSpan ActivationWindow = TimeSpan.FromSeconds(30);

    private readonly FakeTimerScheduler _cameraGrace = new();
    private readonly FakeTimerScheduler _micGrace = new();
    private readonly FakeTimerScheduler _cameraActivation = new();
    private readonly FakeTimerScheduler _micActivation = new();

    private BusyIndicatorManager CreateManager()
    {
        var camera = new ActivationWindowIndicator(
            new BusyIndicator(
                isStateActive: () => _cameraActive,
                subscribeActivated: h => CameraActivated += h,
                unsubscribeActivated: h => CameraActivated -= h,
                subscribeDeactivated: h => CameraDeactivated += h,
                unsubscribeDeactivated: h => CameraDeactivated -= h,
                graceScheduler: _cameraGrace,
                gracePeriod: GracePeriod),
            _cameraActivation,
            ActivationWindow);

        var mic = new ActivationWindowIndicator(
            new BusyIndicator(
                isStateActive: () => _micActive,
                subscribeActivated: h => MicActivated += h,
                unsubscribeActivated: h => MicActivated -= h,
                subscribeDeactivated: h => MicDeactivated += h,
                unsubscribeDeactivated: h => MicDeactivated -= h,
                graceScheduler: _micGrace,
                gracePeriod: GracePeriod),
            _micActivation,
            ActivationWindow);

        return new BusyIndicatorManager(camera, mic);
    }

    private void SimulateCameraActivated() => CameraActivated?.Invoke(this, EventArgs.Empty);
    private void SimulateCameraDeactivated() => CameraDeactivated?.Invoke(this, EventArgs.Empty);
    private void SimulateMicActivated() => MicActivated?.Invoke(this, EventArgs.Empty);
    private void SimulateMicDeactivated() => MicDeactivated?.Invoke(this, EventArgs.Empty);

    // --- Initial state ---

    [Fact]
    public void NewManager_IsNotBusy()
    {
        var manager = CreateManager();
        Assert.False(manager.IsBusy);
        Assert.False(manager.IsMicCameraEnabled);
    }

    // --- Enable with active devices ---

    [Fact]
    public void Given_CameraActive_When_Enabled_Then_IsBusy()
    {
        _cameraActive = true;
        var manager = CreateManager();

        manager.EnableMicCamera();

        Assert.True(manager.IsBusy);
        Assert.True(manager.IsMicCameraEnabled);
    }

    [Fact]
    public void Given_MicActive_When_Enabled_Then_IsBusy()
    {
        _micActive = true;
        var manager = CreateManager();

        manager.EnableMicCamera();

        Assert.True(manager.IsBusy);
    }

    [Fact]
    public void Given_BothActive_When_Enabled_Then_IsBusy()
    {
        _cameraActive = true;
        _micActive = true;
        var manager = CreateManager();

        manager.EnableMicCamera();

        Assert.True(manager.IsBusy);
    }

    [Fact]
    public void Given_NeitherActive_When_Enabled_Then_NotBusy()
    {
        var manager = CreateManager();

        manager.EnableMicCamera();

        Assert.True(manager.IsMicCameraEnabled);
        Assert.False(manager.IsBusy);
    }

    // --- One device clears, other stays active ---

    [Fact]
    public void Given_BothActive_When_CameraClears_Then_StillBusy()
    {
        _cameraActive = true;
        _micActive = true;
        var manager = CreateManager();
        manager.EnableMicCamera();

        SimulateCameraDeactivated();
        _cameraGrace.Expire();

        Assert.True(manager.IsBusy);
    }

    [Fact]
    public void Given_BothActive_When_BothClear_Then_BusyCleared()
    {
        _cameraActive = true;
        _micActive = true;
        var manager = CreateManager();
        manager.EnableMicCamera();
        var cleared = false;
        manager.BusyCleared += (_, _) => cleared = true;

        SimulateCameraDeactivated();
        _cameraGrace.Expire();
        Assert.False(cleared);

        SimulateMicDeactivated();
        _micGrace.Expire();
        Assert.True(cleared);
    }

    // --- BusyCleared event ---

    [Fact]
    public void Given_OnlyCameraActive_When_CameraClears_Then_BusyCleared()
    {
        _cameraActive = true;
        var manager = CreateManager();
        manager.EnableMicCamera();
        var cleared = false;
        manager.BusyCleared += (_, _) => cleared = true;

        SimulateCameraDeactivated();
        _cameraGrace.Expire();

        Assert.True(cleared);
        Assert.False(manager.IsBusy);
    }

    [Fact]
    public void Given_OnlyMicActive_When_MicClears_Then_BusyCleared()
    {
        _micActive = true;
        var manager = CreateManager();
        manager.EnableMicCamera();
        var cleared = false;
        manager.BusyCleared += (_, _) => cleared = true;

        SimulateMicDeactivated();
        _micGrace.Expire();

        Assert.True(cleared);
        Assert.False(manager.IsBusy);
    }

    // --- Disable ---

    [Fact]
    public void Given_Busy_When_Disabled_Then_BusyCleared()
    {
        _cameraActive = true;
        var manager = CreateManager();
        manager.EnableMicCamera();
        var cleared = false;
        manager.BusyCleared += (_, _) => cleared = true;

        manager.DisableMicCamera();

        Assert.True(cleared);
        Assert.False(manager.IsBusy);
        Assert.False(manager.IsMicCameraEnabled);
    }

    [Fact]
    public void Given_NotBusy_When_Disabled_Then_NoBusyCleared()
    {
        var manager = CreateManager();
        manager.EnableMicCamera();
        var cleared = false;
        manager.BusyCleared += (_, _) => cleared = true;

        manager.DisableMicCamera();

        Assert.False(cleared);
    }

    // --- Grace period: device comes back within grace ---

    [Fact]
    public void Given_CameraDeactivated_When_CameraReactivatesWithinGrace_Then_StillBusy()
    {
        _cameraActive = true;
        var manager = CreateManager();
        manager.EnableMicCamera();

        SimulateCameraDeactivated();
        Assert.True(manager.IsBusy);

        SimulateCameraActivated();
        Assert.True(manager.IsBusy);
        Assert.False(_cameraGrace.IsRunning);
    }

    // --- Mic activates after enable ---

    [Fact]
    public void Given_EnabledWithNothingActive_When_MicActivates_Then_IsBusy()
    {
        var manager = CreateManager();
        manager.EnableMicCamera();
        Assert.False(manager.IsBusy);

        SimulateMicActivated();

        Assert.True(manager.IsBusy);
    }

    // --- Activation window ---

    [Fact]
    public void Given_EnabledWithNothingActive_When_BothActivationWindowsExpire_Then_ActivationExpiredFires()
    {
        var manager = CreateManager();
        var expired = false;
        manager.ActivationExpired += (_, _) => expired = true;
        manager.EnableMicCamera();

        _cameraActivation.Expire();
        Assert.False(expired);

        _micActivation.Expire();
        Assert.True(expired);
        Assert.False(manager.IsMicCameraEnabled);
    }

    [Fact]
    public void Given_EnabledWithNothingActive_When_MicActivatesBeforeWindow_Then_NoActivationExpired()
    {
        var manager = CreateManager();
        var expired = false;
        manager.ActivationExpired += (_, _) => expired = true;
        manager.EnableMicCamera();

        SimulateMicActivated();
        _cameraActivation.Expire();

        Assert.False(expired);
        Assert.True(manager.IsBusy);
    }

    // --- Meeting mode ---

    [Fact]
    public void SetMeetingMode_Off_DisablesIndicator()
    {
        _micActive = true;
        var manager = CreateManager();
        manager.SetMeetingMode(MeetingMode.UntilEnd);
        Assert.True(manager.IsBusy);

        manager.SetMeetingMode(MeetingMode.Off);

        Assert.Equal(MeetingMode.Off, manager.CurrentMeetingMode);
        Assert.False(manager.IsBusy);
        Assert.False(manager.IsMicCameraEnabled);
    }

    [Fact]
    public void SetMeetingMode_UntilEnd_AutoDisablesOnGraceExpiry()
    {
        _micActive = true;
        var manager = CreateManager();
        manager.SetMeetingMode(MeetingMode.UntilEnd);
        Assert.Equal(MeetingMode.UntilEnd, manager.CurrentMeetingMode);

        SimulateMicDeactivated();
        _micGrace.Expire();
        SimulateCameraDeactivated();
        _cameraGrace.Expire();

        Assert.False(manager.IsBusy);
        Assert.False(manager.IsMicCameraEnabled);
    }

    [Fact]
    public void SetMeetingMode_Always_StaysEnabledAfterGraceExpiry()
    {
        _micActive = true;
        var manager = CreateManager();
        manager.SetMeetingMode(MeetingMode.Always);
        Assert.Equal(MeetingMode.Always, manager.CurrentMeetingMode);

        SimulateMicDeactivated();
        _micGrace.Expire();

        Assert.False(manager.IsBusy);
        Assert.True(manager.IsMicCameraEnabled);
    }

    [Fact]
    public void SetMeetingMode_Always_ReactivatesWhenDeviceComesBack()
    {
        _micActive = true;
        var manager = CreateManager();
        manager.SetMeetingMode(MeetingMode.Always);

        SimulateMicDeactivated();
        _micGrace.Expire();
        Assert.False(manager.IsBusy);

        SimulateMicActivated();
        Assert.True(manager.IsBusy);
    }

    [Fact]
    public void SetMeetingMode_Always_NoActivationWindow()
    {
        var manager = CreateManager();
        manager.SetMeetingMode(MeetingMode.Always);

        // Activation window timers should not be running
        Assert.False(_cameraActivation.IsRunning);
        Assert.False(_micActivation.IsRunning);
    }

    [Fact]
    public void SetMeetingMode_Always_BusyClearedFiresButStaysEnabled()
    {
        _micActive = true;
        var manager = CreateManager();
        manager.SetMeetingMode(MeetingMode.Always);
        var clearedCount = 0;
        manager.BusyCleared += (_, _) => clearedCount++;

        SimulateMicDeactivated();
        _micGrace.Expire();
        SimulateCameraDeactivated();
        _cameraGrace.Expire();

        Assert.True(clearedCount > 0);
        Assert.True(manager.IsMicCameraEnabled);
    }

    [Fact]
    public void SetMeetingMode_Cycling_Off_UntilEnd_Always_Off()
    {
        var manager = CreateManager();

        manager.SetMeetingMode(MeetingMode.UntilEnd);
        Assert.Equal(MeetingMode.UntilEnd, manager.CurrentMeetingMode);
        Assert.True(manager.IsMicCameraEnabled);

        manager.SetMeetingMode(MeetingMode.Always);
        Assert.Equal(MeetingMode.Always, manager.CurrentMeetingMode);
        Assert.True(manager.IsMicCameraEnabled);

        manager.SetMeetingMode(MeetingMode.Off);
        Assert.Equal(MeetingMode.Off, manager.CurrentMeetingMode);
        Assert.False(manager.IsMicCameraEnabled);
    }
}
