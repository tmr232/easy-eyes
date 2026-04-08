using EasyEyes;

namespace EasyEyes.Tests;

public class BusyIndicatorTests
{
    private static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(5);

    private bool _stateActive;
    private event EventHandler? Activated;
    private event EventHandler? Deactivated;

    private readonly FakeTimerScheduler _graceScheduler = new();

    private BusyIndicator CreateIndicator()
    {
        return new BusyIndicator(
            isStateActive: () => _stateActive,
            subscribeActivated: h => Activated += h,
            unsubscribeActivated: h => Activated -= h,
            subscribeDeactivated: h => Deactivated += h,
            unsubscribeDeactivated: h => Deactivated -= h,
            graceScheduler: _graceScheduler,
            gracePeriod: GracePeriod);
    }

    private void SimulateActivated() => Activated?.Invoke(this, EventArgs.Empty);
    private void SimulateDeactivated() => Deactivated?.Invoke(this, EventArgs.Empty);

    // --- Initial state ---

    [Fact]
    public void NewIndicator_IsNotEnabled()
    {
        var indicator = CreateIndicator();
        Assert.False(indicator.IsEnabled);
    }

    [Fact]
    public void NewIndicator_IsNotActive()
    {
        var indicator = CreateIndicator();
        Assert.False(indicator.IsActive);
    }

    // --- Enable ---

    [Fact]
    public void Given_StateActive_When_Enabled_Then_IsActive()
    {
        _stateActive = true;
        var indicator = CreateIndicator();

        indicator.Enable();

        Assert.True(indicator.IsEnabled);
        Assert.True(indicator.IsActive);
    }

    [Fact]
    public void Given_StateInactive_When_Enabled_Then_IsNotActive()
    {
        _stateActive = false;
        var indicator = CreateIndicator();

        indicator.Enable();

        Assert.True(indicator.IsEnabled);
        Assert.False(indicator.IsActive);
    }

    [Fact]
    public void Given_EnabledAndStateInactive_When_StateActivates_Then_IsActive()
    {
        _stateActive = false;
        var indicator = CreateIndicator();
        indicator.Enable();

        SimulateActivated();

        Assert.True(indicator.IsActive);
    }

    // --- Disable ---

    [Fact]
    public void Given_EnabledAndActive_When_Disabled_Then_IsNotActive()
    {
        _stateActive = true;
        var indicator = CreateIndicator();
        indicator.Enable();

        indicator.Disable();

        Assert.False(indicator.IsEnabled);
        Assert.False(indicator.IsActive);
    }

    // --- Grace period ---

    [Fact]
    public void Given_Active_When_StateDeactivates_Then_GraceTimerStarts()
    {
        _stateActive = true;
        var indicator = CreateIndicator();
        indicator.Enable();

        SimulateDeactivated();

        Assert.True(_graceScheduler.IsRunning);
        Assert.Equal(GracePeriod, _graceScheduler.LastInterval);
        Assert.True(indicator.IsActive);
    }

    [Fact]
    public void Given_GraceTimerRunning_When_StateReactivates_Then_GraceTimerCancelled()
    {
        _stateActive = true;
        var indicator = CreateIndicator();
        indicator.Enable();
        SimulateDeactivated();

        SimulateActivated();

        Assert.False(_graceScheduler.IsRunning);
        Assert.True(indicator.IsActive);
    }

    [Fact]
    public void Given_GraceTimerRunning_When_GraceExpires_Then_AutoDisables()
    {
        _stateActive = true;
        var indicator = CreateIndicator();
        indicator.Enable();
        SimulateDeactivated();

        _graceScheduler.Expire();

        Assert.False(indicator.IsActive);
        Assert.False(indicator.IsEnabled);
    }

    [Fact]
    public void Given_GraceTimerRunning_When_GraceExpires_Then_ClearedEventFires()
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

    [Fact]
    public void Given_Disabled_When_Disabled_Then_NoError()
    {
        var indicator = CreateIndicator();
        indicator.Disable(); // should be a no-op
        Assert.False(indicator.IsEnabled);
    }

    [Fact]
    public void Given_Enabled_When_EnabledAgain_Then_NoChange()
    {
        _stateActive = true;
        var indicator = CreateIndicator();
        indicator.Enable();

        indicator.Enable(); // idempotent

        Assert.True(indicator.IsEnabled);
        Assert.True(indicator.IsActive);
    }

    // --- Events after disable ---

    [Fact]
    public void Given_Disabled_When_StateChanges_Then_NoEffect()
    {
        _stateActive = true;
        var indicator = CreateIndicator();
        indicator.Enable();
        indicator.Disable();

        SimulateActivated();

        Assert.False(indicator.IsActive);
    }

    [Fact]
    public void Given_DisabledDuringGrace_When_GraceWouldExpire_Then_NoEffect()
    {
        _stateActive = true;
        var indicator = CreateIndicator();
        indicator.Enable();
        SimulateDeactivated();

        indicator.Disable();

        Assert.False(_graceScheduler.IsRunning);
        Assert.False(indicator.IsActive);
    }

    // --- Re-enable after auto-disable ---

    [Fact]
    public void Given_AutoDisabled_When_ReEnabled_Then_ChecksCurrentState()
    {
        _stateActive = true;
        var indicator = CreateIndicator();
        indicator.Enable();
        SimulateDeactivated();
        _graceScheduler.Expire(); // auto-disables

        _stateActive = true;
        indicator.Enable();

        Assert.True(indicator.IsEnabled);
        Assert.True(indicator.IsActive);
    }

    [Fact]
    public void Given_AutoDisabled_When_ReEnabledWithInactiveState_Then_NotActive()
    {
        _stateActive = true;
        var indicator = CreateIndicator();
        indicator.Enable();
        SimulateDeactivated();
        _graceScheduler.Expire(); // auto-disables

        _stateActive = false;
        indicator.Enable();

        Assert.True(indicator.IsEnabled);
        Assert.False(indicator.IsActive);
    }

    // --- Grace period uses correct interval ---

    [Fact]
    public void GraceTimer_UsesConfiguredGracePeriod()
    {
        var customGrace = TimeSpan.FromSeconds(10);
        var scheduler = new FakeTimerScheduler();
        var indicator = new BusyIndicator(
            isStateActive: () => true,
            subscribeActivated: h => Activated += h,
            unsubscribeActivated: h => Activated -= h,
            subscribeDeactivated: h => Deactivated += h,
            unsubscribeDeactivated: h => Deactivated -= h,
            graceScheduler: scheduler,
            gracePeriod: customGrace);

        indicator.Enable();
        SimulateDeactivated();

        Assert.Equal(customGrace, scheduler.LastInterval);
    }

    // --- Unsubscription ---

    [Fact]
    public void Given_AutoDisabled_When_StateChanges_Then_NoEffect()
    {
        _stateActive = true;
        var indicator = CreateIndicator();
        indicator.Enable();
        SimulateDeactivated();
        _graceScheduler.Expire(); // auto-disables and unsubscribes

        var cleared = false;
        indicator.Cleared += (_, _) => cleared = true;

        SimulateActivated();
        SimulateDeactivated();

        Assert.False(indicator.IsActive);
        Assert.False(cleared);
    }
}
