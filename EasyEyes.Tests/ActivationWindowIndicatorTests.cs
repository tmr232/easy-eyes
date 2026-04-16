using EasyEyes;

namespace EasyEyes.Tests;

public class ActivationWindowIndicatorTests
{
    private static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ActivationWindow = TimeSpan.FromSeconds(30);

    private bool _stateActive;
    private event EventHandler? Activated;
    private event EventHandler? Deactivated;

    private readonly FakeTimerScheduler _graceScheduler = new();
    private readonly FakeTimerScheduler _activationScheduler = new();

    private ActivationWindowIndicator CreateIndicator()
    {
        var inner = new BusyIndicator(
            isStateActive: () => _stateActive,
            subscribeActivated: h => Activated += h,
            unsubscribeActivated: h => Activated -= h,
            subscribeDeactivated: h => Deactivated += h,
            unsubscribeDeactivated: h => Deactivated -= h,
            graceScheduler: _graceScheduler,
            gracePeriod: GracePeriod);

        return new ActivationWindowIndicator(inner, _activationScheduler, ActivationWindow);
    }

    private void SimulateActivated() => Activated?.Invoke(this, EventArgs.Empty);
    private void SimulateDeactivated() => Deactivated?.Invoke(this, EventArgs.Empty);

    // --- Activation window starts when state is inactive on enable ---

    [Fact]
    public void Given_StateInactive_When_Enabled_Then_ActivationWindowStarts()
    {
        var indicator = CreateIndicator();

        indicator.Enable();

        Assert.True(_activationScheduler.IsRunning);
        Assert.Equal(ActivationWindow, _activationScheduler.LastInterval);
    }

    [Fact]
    public void Given_StateActive_When_Enabled_Then_NoActivationWindow()
    {
        _stateActive = true;
        var indicator = CreateIndicator();

        indicator.Enable();

        Assert.False(_activationScheduler.IsRunning);
    }

    // --- Activation window cancelled when state activates ---

    [Fact]
    public void Given_ActivationWindowRunning_When_StateActivates_Then_WindowCancelled()
    {
        var indicator = CreateIndicator();
        indicator.Enable();

        SimulateActivated();

        Assert.False(_activationScheduler.IsRunning);
        Assert.True(indicator.IsActive);
    }

    // --- Activation window expires ---

    [Fact]
    public void Given_ActivationWindowRunning_When_WindowExpires_Then_AutoDisables()
    {
        var indicator = CreateIndicator();
        indicator.Enable();

        _activationScheduler.Expire();

        Assert.False(indicator.IsEnabled);
        Assert.False(indicator.IsActive);
    }

    [Fact]
    public void Given_ActivationWindowRunning_When_WindowExpires_Then_ActivationExpiredFires()
    {
        var indicator = CreateIndicator();
        indicator.Enable();
        var expired = false;
        indicator.ActivationExpired += (_, _) => expired = true;

        _activationScheduler.Expire();

        Assert.True(expired);
    }

    [Fact]
    public void Given_ActivationWindowRunning_When_WindowExpires_Then_ClearedDoesNotFire()
    {
        var indicator = CreateIndicator();
        indicator.Enable();
        var cleared = false;
        indicator.Cleared += (_, _) => cleared = true;

        _activationScheduler.Expire();

        Assert.False(cleared);
    }

    // --- Manual disable cancels activation window ---

    [Fact]
    public void Given_ActivationWindowRunning_When_Disabled_Then_WindowCancelled()
    {
        var indicator = CreateIndicator();
        indicator.Enable();

        indicator.Disable();

        Assert.False(_activationScheduler.IsRunning);
        Assert.False(indicator.IsEnabled);
    }

    // --- Cleared event forwarded from inner ---

    [Fact]
    public void Given_Active_When_GraceExpires_Then_ClearedFires()
    {
        _stateActive = true;
        var indicator = CreateIndicator();
        indicator.Enable();
        var cleared = false;
        indicator.Cleared += (_, _) => cleared = true;

        SimulateDeactivated();
        _graceScheduler.Expire();

        Assert.True(cleared);
    }

    // --- Delegates to inner ---

    [Fact]
    public void IsActive_DelegatesToInner()
    {
        _stateActive = true;
        var indicator = CreateIndicator();

        indicator.Enable();

        Assert.True(indicator.IsActive);
    }

    [Fact]
    public void IsEnabled_DelegatesToInner()
    {
        var indicator = CreateIndicator();

        indicator.Enable();

        Assert.True(indicator.IsEnabled);
    }

    // --- Persistent mode ---

    [Fact]
    public void Given_Persistent_When_EnabledWithInactiveState_Then_NoActivationWindow()
    {
        var indicator = CreateIndicator();
        indicator.Persistent = true;

        indicator.Enable();

        Assert.False(_activationScheduler.IsRunning);
        Assert.True(indicator.IsEnabled);
    }

    [Fact]
    public void Given_Persistent_When_GraceExpires_Then_StaysEnabled()
    {
        _stateActive = true;
        var indicator = CreateIndicator();
        indicator.Persistent = true;
        indicator.Enable();
        SimulateDeactivated();

        _graceScheduler.Expire();

        Assert.False(indicator.IsActive);
        Assert.True(indicator.IsEnabled);
    }
}
