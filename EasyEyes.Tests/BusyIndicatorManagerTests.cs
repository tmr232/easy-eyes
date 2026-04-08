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

    private readonly FakeTimerScheduler _cameraGrace = new();
    private readonly FakeTimerScheduler _micGrace = new();

    private BusyIndicatorManager CreateManager()
    {
        var camera = new BusyIndicator(
            isStateActive: () => _cameraActive,
            subscribeActivated: h => CameraActivated += h,
            unsubscribeActivated: h => CameraActivated -= h,
            subscribeDeactivated: h => CameraDeactivated += h,
            unsubscribeDeactivated: h => CameraDeactivated -= h,
            graceScheduler: _cameraGrace,
            gracePeriod: GracePeriod);

        var mic = new BusyIndicator(
            isStateActive: () => _micActive,
            subscribeActivated: h => MicActivated += h,
            unsubscribeActivated: h => MicActivated -= h,
            subscribeDeactivated: h => MicDeactivated += h,
            unsubscribeDeactivated: h => MicDeactivated -= h,
            graceScheduler: _micGrace,
            gracePeriod: GracePeriod);

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
}
