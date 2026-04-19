using EasyEyes;

namespace EasyEyes.Tests;

public class BusyIndicatorTests
{
    private static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(5);

    private readonly FakeStateSource _source = new();
    private readonly FakeTimerScheduler _graceScheduler = new();

    private BusyIndicator CreateIndicator()
    {
        return new BusyIndicator(
            _source,
            graceScheduler: _graceScheduler,
            gracePeriod: GracePeriod);
    }

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
        _source.IsActive = true;
        var indicator = CreateIndicator();

        indicator.Enable();

        Assert.True(indicator.IsEnabled);
        Assert.True(indicator.IsActive);
    }

    [Fact]
    public void Given_StateInactive_When_Enabled_Then_IsNotActive()
    {
        _source.IsActive = false;
        var indicator = CreateIndicator();

        indicator.Enable();

        Assert.True(indicator.IsEnabled);
        Assert.False(indicator.IsActive);
    }

    [Fact]
    public void Given_EnabledAndStateInactive_When_StateActivates_Then_IsActive()
    {
        _source.IsActive = false;
        var indicator = CreateIndicator();
        indicator.Enable();

        _source.SimulateActivated();

        Assert.True(indicator.IsActive);
    }

    // --- Disable ---

    [Fact]
    public void Given_EnabledAndActive_When_Disabled_Then_IsNotActive()
    {
        _source.IsActive = true;
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
        _source.IsActive = true;
        var indicator = CreateIndicator();
        indicator.Enable();

        _source.SimulateDeactivated();

        Assert.True(_graceScheduler.IsRunning);
        Assert.Equal(GracePeriod, _graceScheduler.LastInterval);
        Assert.True(indicator.IsActive);
    }

    [Fact]
    public void Given_GraceTimerRunning_When_StateReactivates_Then_GraceTimerCancelled()
    {
        _source.IsActive = true;
        var indicator = CreateIndicator();
        indicator.Enable();
        _source.SimulateDeactivated();

        _source.SimulateActivated();

        Assert.False(_graceScheduler.IsRunning);
        Assert.True(indicator.IsActive);
    }

    [Fact]
    public void Given_GraceTimerRunning_When_GraceExpires_Then_ClearsButStaysEnabled()
    {
        _source.IsActive = true;
        var indicator = CreateIndicator();
        indicator.Enable();
        _source.SimulateDeactivated();

        _graceScheduler.Expire();

        Assert.False(indicator.IsActive);
        Assert.True(indicator.IsEnabled);
    }

    [Fact]
    public void Given_GraceTimerRunning_When_GraceExpires_Then_ClearedEventFires()
    {
        _source.IsActive = true;
        var indicator = CreateIndicator();
        indicator.Enable();
        var cleared = false;
        indicator.Cleared += (_, _) => cleared = true;
        _source.SimulateDeactivated();

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
        _source.IsActive = true;
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
        _source.IsActive = true;
        var indicator = CreateIndicator();
        indicator.Enable();
        indicator.Disable();

        _source.SimulateActivated();

        Assert.False(indicator.IsActive);
    }

    [Fact]
    public void Given_DisabledDuringGrace_When_GraceWouldExpire_Then_NoEffect()
    {
        _source.IsActive = true;
        var indicator = CreateIndicator();
        indicator.Enable();
        _source.SimulateDeactivated();

        indicator.Disable();

        Assert.False(_graceScheduler.IsRunning);
        Assert.False(indicator.IsActive);
    }

    // --- State after grace expiry ---

    [Fact]
    public void Given_GraceExpired_When_StateReactivates_Then_IsActive()
    {
        _source.IsActive = true;
        var indicator = CreateIndicator();
        indicator.Enable();
        _source.SimulateDeactivated();
        _graceScheduler.Expire();

        _source.IsActive = true;
        _source.SimulateActivated();

        Assert.True(indicator.IsActive);
    }

    [Fact]
    public void Given_GraceExpired_Then_StaysEnabledAndInactive()
    {
        _source.IsActive = true;
        var indicator = CreateIndicator();
        indicator.Enable();
        _source.SimulateDeactivated();
        _graceScheduler.Expire();

        Assert.True(indicator.IsEnabled);
        Assert.False(indicator.IsActive);
    }

    // --- Grace period uses correct interval ---

    [Fact]
    public void GraceTimer_UsesConfiguredGracePeriod()
    {
        var customGrace = TimeSpan.FromSeconds(10);
        var scheduler = new FakeTimerScheduler();
        var source = new FakeStateSource { IsActive = true };
        var indicator = new BusyIndicator(
            source,
            graceScheduler: scheduler,
            gracePeriod: customGrace);

        indicator.Enable();
        source.SimulateDeactivated();

        Assert.Equal(customGrace, scheduler.LastInterval);
    }

    // --- Reactivation after grace expiry ---

    [Fact]
    public void Given_GraceExpired_When_StateReactivates_Then_BecomesActive()
    {
        _source.IsActive = true;
        var indicator = CreateIndicator();
        indicator.Enable();
        _source.SimulateDeactivated();
        _graceScheduler.Expire();

        _source.SimulateActivated();

        Assert.True(indicator.IsActive);
    }

}
