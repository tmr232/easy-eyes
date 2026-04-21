using EasyEyes;

namespace EasyEyes.Tests;

public class BusyIndicatorManagerTests
{
    private static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(5);

    private readonly FakeStateSource _source = new();
    private readonly FakeTimerScheduler _graceScheduler = new();

    private BusyIndicatorManager CreateManager()
    {
        var indicator = new BusyIndicator(
            _source,
            graceScheduler: _graceScheduler,
            gracePeriod: GracePeriod);

        return new BusyIndicatorManager(indicator);
    }

    private void SimulateDeviceActivated() => _source.SimulateActivated();
    private void SimulateDeviceDeactivated() => _source.SimulateDeactivated();

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
        _source.IsActive = true;
        var manager = CreateManager();

        manager.SetMeetingMode(MeetingMode.On);

        Assert.True(manager.IsBusy);
        Assert.True(manager.IsEnabled);
    }

    [Fact]
    public void Given_DeviceInactive_When_Enabled_Then_NotBusy()
    {
        var manager = CreateManager();

        manager.SetMeetingMode(MeetingMode.On);

        Assert.True(manager.IsEnabled);
        Assert.False(manager.IsBusy);
    }

    // --- BusyCleared event ---

    [Fact]
    public void Given_DeviceActive_When_DeviceClears_Then_BusyCleared()
    {
        _source.IsActive = true;
        var manager = CreateManager();
        manager.SetMeetingMode(MeetingMode.On);
        var cleared = false;
        manager.BusyCleared += (_, _) => cleared = true;

        SimulateDeviceDeactivated();
        _graceScheduler.Expire();

        Assert.True(cleared);
        Assert.False(manager.IsBusy);
    }

    // --- BecameActive event ---

    [Fact]
    public void Given_Enabled_When_DeviceActivates_Then_BecameActiveFires()
    {
        var manager = CreateManager();
        manager.SetMeetingMode(MeetingMode.On);
        var becameActive = false;
        manager.BecameActive += (_, _) => becameActive = true;

        SimulateDeviceActivated();

        Assert.True(becameActive);
        Assert.True(manager.IsBusy);
    }

    // --- Disable ---

    [Fact]
    public void Given_Busy_When_Disabled_Then_BusyCleared()
    {
        _source.IsActive = true;
        var manager = CreateManager();
        manager.SetMeetingMode(MeetingMode.On);
        var cleared = false;
        manager.BusyCleared += (_, _) => cleared = true;

        manager.DisableMeeting();

        Assert.True(cleared);
        Assert.False(manager.IsBusy);
        Assert.False(manager.IsEnabled);
    }

    [Fact]
    public void Given_Busy_When_Disabled_Then_BusyCleared_FiresExactlyOnce()
    {
        _source.IsActive = true;
        var manager = CreateManager();
        manager.SetMeetingMode(MeetingMode.On);
        var clearedCount = 0;
        manager.BusyCleared += (_, _) => clearedCount++;

        manager.DisableMeeting();

        Assert.Equal(1, clearedCount);
    }

    [Fact]
    public void Given_BusyDuringGrace_When_Disabled_Then_BusyCleared_FiresExactlyOnce()
    {
        // Device was active, then deactivated (grace timer running).
        // DisableMeeting should fire BusyCleared exactly once, not double-fire
        // from both BusyIndicator.Cleared forwarding and the manual invoke.
        _source.IsActive = true;
        var manager = CreateManager();
        manager.SetMeetingMode(MeetingMode.On);
        SimulateDeviceDeactivated(); // grace timer starts, still IsBusy
        Assert.True(manager.IsBusy);

        var clearedCount = 0;
        manager.BusyCleared += (_, _) => clearedCount++;

        manager.DisableMeeting();

        Assert.Equal(1, clearedCount);
    }

    [Fact]
    public void Given_NotBusy_When_Disabled_Then_NoBusyCleared()
    {
        var manager = CreateManager();
        manager.SetMeetingMode(MeetingMode.On);
        var cleared = false;
        manager.BusyCleared += (_, _) => cleared = true;

        manager.DisableMeeting();

        Assert.False(cleared);
    }

    // --- Grace period: device comes back within grace ---

    [Fact]
    public void Given_DeviceDeactivated_When_DeviceReactivatesWithinGrace_Then_StillBusy()
    {
        _source.IsActive = true;
        var manager = CreateManager();
        manager.SetMeetingMode(MeetingMode.On);

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
        manager.SetMeetingMode(MeetingMode.On);
        Assert.False(manager.IsBusy);

        SimulateDeviceActivated();

        Assert.True(manager.IsBusy);
    }

    // --- Stays enabled after grace expiry ---

    [Fact]
    public void Given_GraceExpired_When_DeviceReactivates_Then_IsBusy()
    {
        _source.IsActive = true;
        var manager = CreateManager();
        manager.SetMeetingMode(MeetingMode.On);

        SimulateDeviceDeactivated();
        _graceScheduler.Expire();
        Assert.False(manager.IsBusy);
        Assert.True(manager.IsEnabled);

        SimulateDeviceActivated();
        Assert.True(manager.IsBusy);
    }

    // --- Meeting mode ---

    [Fact]
    public void SetMeetingMode_Off_DisablesIndicator()
    {
        _source.IsActive = true;
        var manager = CreateManager();
        manager.SetMeetingMode(MeetingMode.On);
        Assert.True(manager.IsBusy);

        manager.SetMeetingMode(MeetingMode.Off);

        Assert.Equal(MeetingMode.Off, manager.CurrentMeetingMode);
        Assert.False(manager.IsBusy);
        Assert.False(manager.IsEnabled);
    }

    [Fact]
    public void SetMeetingMode_On_Then_Off_Then_On()
    {
        var manager = CreateManager();

        manager.SetMeetingMode(MeetingMode.On);
        Assert.Equal(MeetingMode.On, manager.CurrentMeetingMode);
        Assert.True(manager.IsEnabled);

        manager.SetMeetingMode(MeetingMode.Off);
        Assert.Equal(MeetingMode.Off, manager.CurrentMeetingMode);
        Assert.False(manager.IsEnabled);

        manager.SetMeetingMode(MeetingMode.On);
        Assert.Equal(MeetingMode.On, manager.CurrentMeetingMode);
        Assert.True(manager.IsEnabled);
    }
}
