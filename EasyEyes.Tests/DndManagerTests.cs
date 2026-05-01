using EasyEyes;
using Color = System.Windows.Media.Color;

namespace EasyEyes.Tests;

public class DndManagerTests
{
    private readonly FakeForegroundCapture _fakeCapture = new();
    private readonly FakeTimerScheduler _settleScheduler = new();
    private readonly FakeTimerScheduler _graceScheduler = new();
    private readonly FakeTimerScheduler _armingProbeScheduler = new();
    private readonly FakeDndFlashFeedback _flashFeedback = new();

    private static readonly TimeSpan SettleDuration = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan ArmingProbeInterval = TimeSpan.FromSeconds(1);

    private DndManager CreateManager()
    {
        return new DndManager(
            _fakeCapture,
            _flashFeedback,
            _settleScheduler,
            _graceScheduler,
            _armingProbeScheduler,
            SettleDuration,
            GracePeriod,
            ArmingProbeInterval);
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

        Assert.Equal("bloom", _flashFeedback.LastShowType);
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

        Assert.Equal("bloom", _flashFeedback.LastShowType);
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

        Assert.Equal("bloom", _flashFeedback.LastShowType);
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

        Assert.Equal("bloom", _flashFeedback.LastShowType);
        Assert.Equal(BorderFlashManager.ClearedColor, _flashFeedback.LastColor);
        Assert.Equal(DndState.Off, manager.CurrentState);
    }

    // --- Grace-period hint (issue #2) ---

    [Fact]
    public void Active_WhenSourceDeactivates_ShowsGraceHint()
    {
        // Issue #2: while DND is Active, the moment the user switches away
        // from the captured app the grace hint border appears so the user
        // can see the grace timer running. Colors come from BorderFlashManager.
        _fakeCapture.IsActive = true;
        var manager = CreateManager();
        manager.Activate();
        _settleScheduler.Expire();
        _flashFeedback.Reset();

        _fakeCapture.SimulateDeactivated();

        Assert.Equal("graceHint", _flashFeedback.LastShowType);
        Assert.Equal(BorderFlashManager.GraceHintStartColor, _flashFeedback.LastGraceHintStartColor);
        Assert.Equal(BorderFlashManager.GraceHintEndColor, _flashFeedback.LastGraceHintEndColor);
        Assert.Equal(GracePeriod, _flashFeedback.LastGraceHintDuration);
    }

    [Fact]
    public void Active_WhenSourceReactivatesWithinGrace_CancelsGraceHintWithGreen()
    {
        _fakeCapture.IsActive = true;
        var manager = CreateManager();
        manager.Activate();
        _settleScheduler.Expire();
        _fakeCapture.SimulateDeactivated();
        _flashFeedback.Reset();

        _fakeCapture.SimulateActivated();

        Assert.Equal("cancelGraceHint", _flashFeedback.LastShowType);
        Assert.Equal(BorderFlashManager.LockedColor, _flashFeedback.LastColor);
    }

    [Fact]
    public void Off_WhenSourceFiresEvents_DoesNothing()
    {
        // Source events while not Active (e.g. after manual Deactivate or
        // after grace expiry) must not surface any visual change.
        var manager = CreateManager();

        _fakeCapture.SimulateDeactivated();
        _fakeCapture.SimulateActivated();

        Assert.Null(_flashFeedback.LastShowType);
    }

    [Fact]
    public void Arming_WhenSourceFiresEvents_DoesNothing()
    {
        var manager = CreateManager();
        manager.Activate();
        _flashFeedback.Reset();

        _fakeCapture.SimulateDeactivated();
        _fakeCapture.SimulateActivated();

        Assert.Null(_flashFeedback.LastShowType);
    }

    [Fact]
    public void Active_GraceHintRetriggers_OnRapidAwayBackAway()
    {
        // Away → back → away within grace: the second away should show a
        // fresh grace hint (the user got "another 45 seconds").
        _fakeCapture.IsActive = true;
        var manager = CreateManager();
        manager.Activate();
        _settleScheduler.Expire();

        _fakeCapture.SimulateDeactivated();
        _fakeCapture.SimulateActivated();
        _flashFeedback.Reset();
        _fakeCapture.SimulateDeactivated();

        Assert.Equal("graceHint", _flashFeedback.LastShowType);
    }

    // --- Arming probe: fast-path lock when fullscreen is stable ---

    [Fact]
    public void Activate_StartsArmingProbe()
    {
        var manager = CreateManager();
        manager.Activate();

        Assert.True(_armingProbeScheduler.IsRunning);
        Assert.Equal(ArmingProbeInterval, _armingProbeScheduler.LastInterval);
    }

    [Fact]
    public void ArmingProbe_StableFullscreen_LocksEarly()
    {
        // Fullscreen window focused at activation, still focused at the
        // next probe tick → lock without waiting for full settle.
        _fakeCapture.IsActive = true;
        _fakeCapture.FullscreenForegroundWindow = new IntPtr(0x1234);
        var manager = CreateManager();
        manager.Activate();

        _armingProbeScheduler.Expire();

        Assert.Equal(DndState.Active, manager.CurrentState);
        Assert.True(_fakeCapture.WasCaptured);
        Assert.False(_settleScheduler.IsRunning);
        Assert.False(_armingProbeScheduler.IsRunning);
    }

    [Fact]
    public void ArmingProbe_NoFullscreen_KeepsArmingAndReschedules()
    {
        _fakeCapture.FullscreenForegroundWindow = null;
        var manager = CreateManager();
        manager.Activate();

        _armingProbeScheduler.Expire();

        Assert.Equal(DndState.Arming, manager.CurrentState);
        Assert.True(_settleScheduler.IsRunning);
        Assert.True(_armingProbeScheduler.IsRunning);
    }

    [Fact]
    public void ArmingProbe_FullscreenAppearsAfterActivation_RequiresSecondConfirmingTick()
    {
        // Activation: nothing fullscreen. Probe1: a fullscreen window is
        // now focused (recorded but not yet stable). Probe2: same window
        // → lock.
        _fakeCapture.IsActive = true;
        _fakeCapture.FullscreenForegroundWindow = null;
        var manager = CreateManager();
        manager.Activate();

        _fakeCapture.FullscreenForegroundWindow = new IntPtr(0x1234);
        _armingProbeScheduler.Expire();
        Assert.Equal(DndState.Arming, manager.CurrentState);

        _armingProbeScheduler.Expire();
        Assert.Equal(DndState.Active, manager.CurrentState);
    }

    [Fact]
    public void ArmingProbe_FullscreenWindowChanges_DoesNotLock()
    {
        // Different fullscreen hwnd between ticks → not stable, keep arming.
        _fakeCapture.IsActive = true;
        _fakeCapture.FullscreenForegroundWindow = new IntPtr(0x1111);
        var manager = CreateManager();
        manager.Activate();

        _fakeCapture.FullscreenForegroundWindow = new IntPtr(0x2222);
        _armingProbeScheduler.Expire();

        Assert.Equal(DndState.Arming, manager.CurrentState);
        Assert.True(_armingProbeScheduler.IsRunning);
    }

    [Fact]
    public void ArmingProbe_FullscreenLost_ResetsStability()
    {
        // Fullscreen at activation, then gone → recorded as null. Even
        // if the same hwnd reappears, we need another tick to confirm.
        _fakeCapture.IsActive = true;
        _fakeCapture.FullscreenForegroundWindow = new IntPtr(0x1234);
        var manager = CreateManager();
        manager.Activate();

        _fakeCapture.FullscreenForegroundWindow = null;
        _armingProbeScheduler.Expire();
        Assert.Equal(DndState.Arming, manager.CurrentState);

        _fakeCapture.FullscreenForegroundWindow = new IntPtr(0x1234);
        _armingProbeScheduler.Expire();
        Assert.Equal(DndState.Arming, manager.CurrentState);

        _armingProbeScheduler.Expire();
        Assert.Equal(DndState.Active, manager.CurrentState);
    }

    [Fact]
    public void Deactivate_CancelsArmingProbe()
    {
        var manager = CreateManager();
        manager.Activate();

        manager.Deactivate();

        Assert.False(_armingProbeScheduler.IsRunning);
    }

    [Fact]
    public void SettleExpiry_CancelsArmingProbe()
    {
        _fakeCapture.IsActive = true;
        var manager = CreateManager();
        manager.Activate();

        _settleScheduler.Expire();

        Assert.False(_armingProbeScheduler.IsRunning);
    }

    // --- Capture rejection (issue #4) ---

    [Fact]
    public void SettleExpiry_WhenCaptureRejected_TransitionsToOff()
    {
        // Issue #4: if the foreground window isn't fullscreen at settle time,
        // TryCapture returns false and DND falls back to Off rather than
        // proceeding to Active.
        _fakeCapture.CaptureSucceeds = false;
        var manager = CreateManager();
        manager.Activate();

        _settleScheduler.Expire();

        Assert.Equal(DndState.Off, manager.CurrentState);
        Assert.False(manager.IsBusy);
    }

    [Fact]
    public void SettleExpiry_WhenCaptureRejected_ShowsClearedFlash()
    {
        // Rejection is signalled visually with a red flash (the richer
        // bloom-and-fade rejection animation lands with issue #1).
        _fakeCapture.CaptureSucceeds = false;
        var manager = CreateManager();
        manager.Activate();

        _settleScheduler.Expire();

        Assert.Equal("bloom", _flashFeedback.LastShowType);
        Assert.Equal(BorderFlashManager.ClearedColor, _flashFeedback.LastColor);
    }

    [Fact]
    public void SettleExpiry_WhenCaptureRejected_FiresStateChanged()
    {
        _fakeCapture.CaptureSucceeds = false;
        var manager = CreateManager();
        var states = new List<DndState>();
        manager.StateChanged += (_, _) => states.Add(manager.CurrentState);

        manager.Activate();
        _settleScheduler.Expire();

        Assert.Equal(new[] { DndState.Arming, DndState.Off }, states.ToArray());
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

    /// <summary>
    /// Controls the return value of <see cref="TryCapture"/>. Defaults to
    /// <c>true</c> (capture succeeds) so existing tests do not need to be
    /// updated. Tests for the rejection path (issue #4) set this to
    /// <c>false</c>.
    /// </summary>
    public bool CaptureSucceeds { get; set; } = true;

    /// <summary>
    /// Controls the return value of <see cref="GetFullscreenForegroundWindow"/>.
    /// Defaults to <c>null</c> (no fullscreen window) so existing tests still
    /// see the full settle countdown.
    /// </summary>
    public IntPtr? FullscreenForegroundWindow { get; set; }

    public event EventHandler? Activated;
    public event EventHandler? Deactivated;

    public bool TryCapture()
    {
        WasCaptured = true;
        return CaptureSucceeds;
    }

    public void Release()
    {
        WasReleased = true;
    }

    public IntPtr? GetFullscreenForegroundWindow()
    {
        return FullscreenForegroundWindow;
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
    public Color? LastGraceHintStartColor { get; private set; }
    public Color? LastGraceHintEndColor { get; private set; }
    public TimeSpan? LastGraceHintDuration { get; private set; }

    public void Reset()
    {
        LastShowType = null;
        LastColor = null;
        LastGraceHintStartColor = null;
        LastGraceHintEndColor = null;
        LastGraceHintDuration = null;
    }

    public void ShowPersistent(Color color)
    {
        LastShowType = "persistent";
        LastColor = color;
    }

    public void BloomAndFade(Color color)
    {
        LastShowType = "bloom";
        LastColor = color;
    }

    public void ShowGraceHint(Color startColor, Color endColor, TimeSpan duration)
    {
        LastShowType = "graceHint";
        LastGraceHintStartColor = startColor;
        LastGraceHintEndColor = endColor;
        LastGraceHintDuration = duration;
    }

    public void CancelGraceHint(Color confirmationColor)
    {
        LastShowType = "cancelGraceHint";
        LastColor = confirmationColor;
    }

    public void Hide()
    {
    }
}
