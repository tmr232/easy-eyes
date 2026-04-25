using EasyEyes;
using Color = System.Windows.Media.Color;

namespace EasyEyes.Tests;

public class DndManagerTests
{
    private readonly FakeForegroundCapture _fakeCapture = new();
    private readonly FakeTimerScheduler _settleScheduler = new();
    private readonly FakeTimerScheduler _graceScheduler = new();
    private readonly FakeDndFlashFeedback _flashFeedback = new();

    private static readonly TimeSpan SettleDuration = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(45);

    private DndManager CreateManager()
    {
        return new DndManager(
            _fakeCapture,
            _flashFeedback,
            _settleScheduler,
            _graceScheduler,
            SettleDuration,
            GracePeriod);
    }

    // --- Initial state ---

    [Fact]
    public void NewManager_IsOff()
    {
        var manager = CreateManager();
        Assert.Equal(DndState.Off, manager.CurrentState);
        Assert.False(manager.IsBusy);
    }

    // --- Activation ---

    [Fact]
    public void Activate_TransitionsToArming()
    {
        var manager = CreateManager();
        manager.Activate();

        Assert.Equal(DndState.Arming, manager.CurrentState);
        Assert.True(_settleScheduler.IsRunning);
    }

    [Fact]
    public void Activate_ShowsArmingBorder()
    {
        var manager = CreateManager();
        manager.Activate();

        Assert.Equal("persistent", _flashFeedback.LastShowType);
        Assert.Equal(BorderFlashManager.ArmingColor, _flashFeedback.LastColor);
    }

    [Fact]
    public void Activate_WhenAlreadyArming_DoesNothing()
    {
        var manager = CreateManager();
        manager.Activate();
        _flashFeedback.Reset();

        manager.Activate();

        Assert.Null(_flashFeedback.LastShowType);
    }

    // --- Settle expiry ---

    [Fact]
    public void SettleExpiry_TransitionsToActive()
    {
        _fakeCapture.IsActive = true;
        var manager = CreateManager();
        manager.Activate();

        _settleScheduler.Expire();

        Assert.Equal(DndState.Active, manager.CurrentState);
    }

    [Fact]
    public void SettleExpiry_ShowsLockedFlash()
    {
        _fakeCapture.IsActive = true;
        var manager = CreateManager();
        manager.Activate();

        _settleScheduler.Expire();

        Assert.Equal("flash", _flashFeedback.LastShowType);
        Assert.Equal(BorderFlashManager.LockedColor, _flashFeedback.LastColor);
    }

    [Fact]
    public void SettleExpiry_CallsCapture()
    {
        var manager = CreateManager();
        manager.Activate();

        _settleScheduler.Expire();

        Assert.True(_fakeCapture.WasCaptured);
    }

    [Fact]
    public void SettleExpiry_IsBusy_WhenSourceIsActive()
    {
        _fakeCapture.IsActive = true;
        var manager = CreateManager();
        manager.Activate();

        _settleScheduler.Expire();

        Assert.True(manager.IsBusy);
    }

    [Fact]
    public void Activate_IsBusyDuringArming()
    {
        // Issue #5: DND must report busy as soon as the user expresses
        // intent (Arming), not only after settle expires. Otherwise an
        // activity-timer expiry during Arming would surface the overlay.
        var manager = CreateManager();
        manager.Activate();

        Assert.Equal(DndState.Arming, manager.CurrentState);
        Assert.True(manager.IsBusy);
    }

    [Fact]
    public void Deactivate_FromArming_IsNotBusy()
    {
        var manager = CreateManager();
        manager.Activate();
        manager.Deactivate();

        Assert.False(manager.IsBusy);
    }

    // --- Grace period and clearing ---

    [Fact]
    public void Active_WhenSourceDeactivatesAndGraceExpires_TransitionsToOff()
    {
        _fakeCapture.IsActive = true;
        var manager = CreateManager();
        manager.Activate();
        _settleScheduler.Expire();

        _fakeCapture.SimulateDeactivated();
        _graceScheduler.Expire();

        Assert.Equal(DndState.Off, manager.CurrentState);
        Assert.False(manager.IsBusy);
    }

    [Fact]
    public void Active_WhenSourceDeactivatesAndGraceExpires_ShowsClearedFlash()
    {
        _fakeCapture.IsActive = true;
        var manager = CreateManager();
        manager.Activate();
        _settleScheduler.Expire();

        _fakeCapture.SimulateDeactivated();
        _graceScheduler.Expire();

        Assert.Equal("flash", _flashFeedback.LastShowType);
        Assert.Equal(BorderFlashManager.ClearedColor, _flashFeedback.LastColor);
    }

    [Fact]
    public void Active_WhenSourceDeactivatesAndGraceExpires_ReleasesCapture()
    {
        _fakeCapture.IsActive = true;
        var manager = CreateManager();
        manager.Activate();
        _settleScheduler.Expire();
        _fakeCapture.WasReleased = false;

        _fakeCapture.SimulateDeactivated();
        _graceScheduler.Expire();

        Assert.True(_fakeCapture.WasReleased);
    }

    [Fact]
    public void Active_WhenSourceDeactivatesAndGraceExpires_FiresBusyCleared()
    {
        _fakeCapture.IsActive = true;
        var manager = CreateManager();
        manager.Activate();
        _settleScheduler.Expire();
        var cleared = false;
        manager.BusyCleared += (_, _) => cleared = true;

        _fakeCapture.SimulateDeactivated();
        _graceScheduler.Expire();

        Assert.True(cleared);
    }

    [Fact]
    public void Active_WhenSourceReactivatesWithinGrace_StillBusy()
    {
        _fakeCapture.IsActive = true;
        var manager = CreateManager();
        manager.Activate();
        _settleScheduler.Expire();

        _fakeCapture.SimulateDeactivated();
        Assert.True(manager.IsBusy);

        _fakeCapture.SimulateActivated();
        Assert.True(manager.IsBusy);
        Assert.False(_graceScheduler.IsRunning);
    }

    // --- BecameActive event ---

    [Fact]
    public void SettleExpiry_WhenSourceActive_FiresBecameActive()
    {
        // When the indicator is enabled with an active source,
        // BusyIndicator fires BecameActive immediately.
        _fakeCapture.IsActive = true;
        var manager = CreateManager();
        var becameActive = false;
        manager.BecameActive += (_, _) => becameActive = true;

        manager.Activate();
        _settleScheduler.Expire();

        Assert.True(becameActive);
    }

    [Fact]
    public void Active_WhenSourceReactivatesWithinGrace_DoesNotFireBecameActive()
    {
        // BusyIndicator stays IsActive=true during grace, so
        // reactivation within grace is not a false→true transition.
        _fakeCapture.IsActive = true;
        var manager = CreateManager();
        manager.Activate();
        _settleScheduler.Expire();

        _fakeCapture.SimulateDeactivated();
        var becameActive = false;
        manager.BecameActive += (_, _) => becameActive = true;
        _fakeCapture.SimulateActivated();

        Assert.False(becameActive);
    }

    // --- Manual deactivation ---

    [Fact]
    public void Deactivate_FromArming_TransitionsToOff()
    {
        var manager = CreateManager();
        manager.Activate();

        manager.Deactivate();

        Assert.Equal(DndState.Off, manager.CurrentState);
        Assert.False(_settleScheduler.IsRunning);
    }

    [Fact]
    public void Deactivate_FromActive_TransitionsToOff()
    {
        _fakeCapture.IsActive = true;
        var manager = CreateManager();
        manager.Activate();
        _settleScheduler.Expire();

        manager.Deactivate();

        Assert.Equal(DndState.Off, manager.CurrentState);
        Assert.False(manager.IsBusy);
    }

    [Fact]
    public void Deactivate_ShowsClearedFlash()
    {
        _fakeCapture.IsActive = true;
        var manager = CreateManager();
        manager.Activate();
        _settleScheduler.Expire();

        manager.Deactivate();

        Assert.Equal("flash", _flashFeedback.LastShowType);
        Assert.Equal(BorderFlashManager.ClearedColor, _flashFeedback.LastColor);
    }

    [Fact]
    public void Deactivate_WhenOff_DoesNothing()
    {
        var manager = CreateManager();
        manager.Deactivate();

        Assert.Equal(DndState.Off, manager.CurrentState);
        Assert.Null(_flashFeedback.LastShowType);
    }

    // --- StateChanged event ---

    [Fact]
    public void Activate_FiresStateChanged()
    {
        var manager = CreateManager();
        var stateChangedCount = 0;
        manager.StateChanged += (_, _) => stateChangedCount++;

        manager.Activate();

        Assert.Equal(1, stateChangedCount);
    }

    [Fact]
    public void FullCycle_FiresStateChangedForEachTransition()
    {
        _fakeCapture.IsActive = true;
        var manager = CreateManager();
        var states = new List<DndState>();
        manager.StateChanged += (_, _) => states.Add(manager.CurrentState);

        manager.Activate();                 // Off → Arming
        _settleScheduler.Expire();          // Arming → Active
        _fakeCapture.SimulateDeactivated();
        _graceScheduler.Expire();           // Active → Off

        Assert.Equal(
            new[] { DndState.Arming, DndState.Active, DndState.Off },
            states.ToArray());
    }

    // --- FlashCleared ---

    [Fact]
    public void FlashCleared_ShowsClearedFlash_WithoutChangingState()
    {
        var manager = CreateManager();
        manager.FlashCleared();

        Assert.Equal("flash", _flashFeedback.LastShowType);
        Assert.Equal(BorderFlashManager.ClearedColor, _flashFeedback.LastColor);
        Assert.Equal(DndState.Off, manager.CurrentState);
    }

    // --- Tray label formatting (issue #3) ---

    [Fact]
    public void TrayLabel_WhenActive_IsPlainDoNotDisturb()
    {
        // Issue #3: the captured process name should not be surfaced
        // in the tray menu label. After Activate -> settle -> Active,
        // the label is just "Do not disturb".
        Assert.Equal("Do not disturb", TrayIconManager.FormatDndLabel(DndState.Active));
    }

    [Fact]
    public void TrayLabel_WhenArming_IncludesArmingSuffix()
    {
        Assert.Equal("Do not disturb (arming...)", TrayIconManager.FormatDndLabel(DndState.Arming));
    }

    [Fact]
    public void TrayLabel_WhenOff_IsPlainDoNotDisturb()
    {
        Assert.Equal("Do not disturb", TrayIconManager.FormatDndLabel(DndState.Off));
    }
}

/// <summary>
/// Test implementation of <see cref="IForegroundCapture"/> that lets tests
/// control capture/release and active state manually.
/// </summary>
public class FakeForegroundCapture : IForegroundCapture
{
    public bool IsActive { get; set; }
    public bool WasCaptured { get; set; }
    public bool WasReleased { get; set; }

    public event EventHandler? Activated;
    public event EventHandler? Deactivated;

    public void Capture()
    {
        WasCaptured = true;
    }

    public void Release()
    {
        WasReleased = true;
    }

    public void SimulateActivated()
    {
        Activated?.Invoke(this, EventArgs.Empty);
    }

    public void SimulateDeactivated()
    {
        Deactivated?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// Test implementation of <see cref="IDndFlashFeedback"/> that records
/// calls instead of creating windows.
/// </summary>
public class FakeDndFlashFeedback : IDndFlashFeedback
{
    public string? LastShowType { get; private set; }
    public Color? LastColor { get; private set; }

    public void Reset()
    {
        LastShowType = null;
        LastColor = null;
    }

    public void ShowPersistent(Color color)
    {
        LastShowType = "persistent";
        LastColor = color;
    }

    public void ShowFlash(Color color)
    {
        LastShowType = "flash";
        LastColor = color;
    }

    public void Hide()
    {
    }
}
