using EasyEyes;

namespace EasyEyes.Tests;

public class BusyIndicatorManagerTests
{
    private static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ActivationWindow = TimeSpan.FromSeconds(30);

    private bool _deviceActive;
    private event EventHandler? DeviceActivated;
    private event EventHandler? DeviceDeactivated;

    private readonly FakeTimerScheduler _graceScheduler = new();
    private readonly FakeTimerScheduler _activationScheduler = new();

    private BusyIndicatorManager CreateManager()
    {
        var indicator = new ActivationWindowIndicator(
            new BusyIndicator(
                isStateActive: () => _deviceActive,
                subscribeActivated: h => DeviceActivated += h,
                unsubscribeActivated: h => DeviceActivated -= h,
                subscribeDeactivated: h => DeviceDeactivated += h,
                unsubscribeDeactivated: h => DeviceDeactivated -= h,
                graceScheduler: _graceScheduler,
                gracePeriod: GracePeriod),
            _activationScheduler,
            ActivationWindow);

        return new BusyIndicatorManager(indicator);
    }

    private void SimulateDeviceActivated() => DeviceActivated?.Invoke(this, EventArgs.Empty);
    private void SimulateDeviceDeactivated() => DeviceDeactivated?.Invoke(this, EventArgs.Empty);

    // --- Initial state ---

    [Fact]
    public void NewManager_IsNotBusy()
    {
        var manager = CreateManager();
        Assert.False(manager.IsBusy);
        Assert.False(manager.IsEnabled);
    }

    // --- Enable with active device ---

    [Fact]
    public void Given_DeviceActive_When_Enabled_Then_IsBusy()
    {
        _deviceActive = true;
        var manager = CreateManager();

        manager.EnableMeeting();

        Assert.True(manager.IsBusy);
        Assert.True(manager.IsEnabled);
    }

    [Fact]
    public void Given_DeviceInactive_When_Enabled_Then_NotBusy()
    {
        var manager = CreateManager();

        manager.EnableMeeting();

        Assert.True(manager.IsEnabled);
        Assert.False(manager.IsBusy);
    }

    // --- BusyCleared event ---

    [Fact]
    public void Given_DeviceActive_When_DeviceClears_Then_BusyCleared()
    {
        _deviceActive = true;
        var manager = CreateManager();
        manager.EnableMeeting();
        var cleared = false;
        manager.BusyCleared += (_, _) => cleared = true;

        SimulateDeviceDeactivated();
        _graceScheduler.Expire();

        Assert.True(cleared);
        Assert.False(manager.IsBusy);
    }

    // --- Disable ---

    [Fact]
    public void Given_Busy_When_Disabled_Then_BusyCleared()
    {
        _deviceActive = true;
        var manager = CreateManager();
        manager.EnableMeeting();
        var cleared = false;
        manager.BusyCleared += (_, _) => cleared = true;

        manager.DisableMeeting();

        Assert.True(cleared);
        Assert.False(manager.IsBusy);
        Assert.False(manager.IsEnabled);
    }

    [Fact]
    public void Given_NotBusy_When_Disabled_Then_NoBusyCleared()
    {
        var manager = CreateManager();
        manager.EnableMeeting();
        var cleared = false;
        manager.BusyCleared += (_, _) => cleared = true;

        manager.DisableMeeting();

        Assert.False(cleared);
    }

    // --- Grace period: device comes back within grace ---

    [Fact]
    public void Given_DeviceDeactivated_When_DeviceReactivatesWithinGrace_Then_StillBusy()
    {
        _deviceActive = true;
        var manager = CreateManager();
        manager.EnableMeeting();

        SimulateDeviceDeactivated();
        Assert.True(manager.IsBusy);

        SimulateDeviceActivated();
        Assert.True(manager.IsBusy);
        Assert.False(_graceScheduler.IsRunning);
    }

    // --- Device activates after enable ---

    [Fact]
    public void Given_EnabledWithNothingActive_When_DeviceActivates_Then_IsBusy()
    {
        var manager = CreateManager();
        manager.EnableMeeting();
        Assert.False(manager.IsBusy);

        SimulateDeviceActivated();

        Assert.True(manager.IsBusy);
    }

    // --- Activation window ---

    [Fact]
    public void Given_EnabledWithNothingActive_When_ActivationWindowExpires_Then_ActivationExpiredFires()
    {
        var manager = CreateManager();
        var expired = false;
        manager.ActivationExpired += (_, _) => expired = true;
        manager.EnableMeeting();

        _activationScheduler.Expire();

        Assert.True(expired);
        Assert.False(manager.IsEnabled);
    }

    [Fact]
    public void Given_EnabledWithNothingActive_When_DeviceActivatesBeforeWindow_Then_NoActivationExpired()
    {
        var manager = CreateManager();
        var expired = false;
        manager.ActivationExpired += (_, _) => expired = true;
        manager.EnableMeeting();

        SimulateDeviceActivated();

        Assert.False(expired);
        Assert.True(manager.IsBusy);
    }

    // --- Meeting mode ---

    [Fact]
    public void SetMeetingMode_Off_DisablesIndicator()
    {
        _deviceActive = true;
        var manager = CreateManager();
        manager.SetMeetingMode(MeetingMode.UntilEnd);
        Assert.True(manager.IsBusy);

        manager.SetMeetingMode(MeetingMode.Off);

        Assert.Equal(MeetingMode.Off, manager.CurrentMeetingMode);
        Assert.False(manager.IsBusy);
        Assert.False(manager.IsEnabled);
    }

    [Fact]
    public void SetMeetingMode_UntilEnd_AutoDisablesOnGraceExpiry()
    {
        _deviceActive = true;
        var manager = CreateManager();
        manager.SetMeetingMode(MeetingMode.UntilEnd);
        Assert.Equal(MeetingMode.UntilEnd, manager.CurrentMeetingMode);

        SimulateDeviceDeactivated();
        _graceScheduler.Expire();

        Assert.False(manager.IsBusy);
        Assert.False(manager.IsEnabled);
    }

    [Fact]
    public void SetMeetingMode_Always_StaysEnabledAfterGraceExpiry()
    {
        _deviceActive = true;
        var manager = CreateManager();
        manager.SetMeetingMode(MeetingMode.Always);
        Assert.Equal(MeetingMode.Always, manager.CurrentMeetingMode);

        SimulateDeviceDeactivated();
        _graceScheduler.Expire();

        Assert.False(manager.IsBusy);
        Assert.True(manager.IsEnabled);
    }

    [Fact]
    public void SetMeetingMode_Always_ReactivatesWhenDeviceComesBack()
    {
        _deviceActive = true;
        var manager = CreateManager();
        manager.SetMeetingMode(MeetingMode.Always);

        SimulateDeviceDeactivated();
        _graceScheduler.Expire();
        Assert.False(manager.IsBusy);

        SimulateDeviceActivated();
        Assert.True(manager.IsBusy);
    }

    [Fact]
    public void SetMeetingMode_Always_NoActivationWindow()
    {
        var manager = CreateManager();
        manager.SetMeetingMode(MeetingMode.Always);

        Assert.False(_activationScheduler.IsRunning);
    }

    [Fact]
    public void SetMeetingMode_Always_BusyClearedFiresButStaysEnabled()
    {
        _deviceActive = true;
        var manager = CreateManager();
        manager.SetMeetingMode(MeetingMode.Always);
        var clearedCount = 0;
        manager.BusyCleared += (_, _) => clearedCount++;

        SimulateDeviceDeactivated();
        _graceScheduler.Expire();

        Assert.True(clearedCount > 0);
        Assert.True(manager.IsEnabled);
    }

    [Fact]
    public void SetMeetingMode_Cycling_Off_UntilEnd_Always_Off()
    {
        var manager = CreateManager();

        manager.SetMeetingMode(MeetingMode.UntilEnd);
        Assert.Equal(MeetingMode.UntilEnd, manager.CurrentMeetingMode);
        Assert.True(manager.IsEnabled);

        manager.SetMeetingMode(MeetingMode.Always);
        Assert.Equal(MeetingMode.Always, manager.CurrentMeetingMode);
        Assert.True(manager.IsEnabled);

        manager.SetMeetingMode(MeetingMode.Off);
        Assert.Equal(MeetingMode.Off, manager.CurrentMeetingMode);
        Assert.False(manager.IsEnabled);
    }
}
