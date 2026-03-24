using EasyEyes;

namespace EasyEyes.Tests;

public class MockActions : IEasyEyesActions
{
    public List<string> Calls { get; } = [];

    public void ShowOverlay() => Calls.Add(nameof(ShowOverlay));
    public void HideOverlay() => Calls.Add(nameof(HideOverlay));
    public void ShowToast() => Calls.Add(nameof(ShowToast));
    public void ClearToast() => Calls.Add(nameof(ClearToast));
    public void SuspendTTimer() => Calls.Add(nameof(SuspendTTimer));
    public void ResumeTTimer() => Calls.Add(nameof(ResumeTTimer));
    public void ResetTTimer() => Calls.Add(nameof(ResetTTimer));
    public void RestartLTimer() => Calls.Add(nameof(RestartLTimer));
    public void StopLTimer() => Calls.Add(nameof(StopLTimer));
}

public class EasyEyesStateMachineTests
{
    private readonly MockActions _actions = new();

    private EasyEyesStateMachine CreateMachine() => new(_actions);

    [Fact]
    public void InitialState_Is_T_TimerRunning()
    {
        var sm = CreateMachine();
        Assert.Equal(State.T_TimerRunning, sm.CurrentState);
        Assert.True(sm.IsInState(State.ScreenUnlocked));
    }

    // --- T Timer Expiry ---

    [Fact]
    public void TTimerExpired_TransitionsTo_OverlayDisplayed()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.TTimerExpired);

        Assert.Equal(State.OverlayDisplayed, sm.CurrentState);
        Assert.True(sm.IsInState(State.ScreenUnlocked));
    }

    [Fact]
    public void TTimerExpired_Calls_ShowOverlay()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.TTimerExpired);

        Assert.Contains(nameof(IEasyEyesActions.ShowOverlay), _actions.Calls);
    }

    // --- Screen Lock ---

    [Fact]
    public void ScreenLock_FromT_TimerRunning_TransitionsTo_L_TimerRunning()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.ScreenLock);

        Assert.Equal(State.L_TimerRunning, sm.CurrentState);
        Assert.True(sm.IsInState(State.ScreenLocked));
    }

    [Fact]
    public void ScreenLock_SuspendsT_And_RestartsL()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.ScreenLock);

        Assert.Equal(
            new[] { nameof(IEasyEyesActions.SuspendTTimer), nameof(IEasyEyesActions.RestartLTimer) },
            _actions.Calls.ToArray()
        );
    }

    [Fact]
    public void ScreenLock_FromOverlayDisplayed_TransitionsTo_L_TimerRunning()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.TTimerExpired); // → OverlayDisplayed
        _actions.Calls.Clear();

        sm.Fire(Trigger.ScreenLock);

        Assert.Equal(State.L_TimerRunning, sm.CurrentState);
        Assert.True(sm.IsInState(State.ScreenLocked));
    }

    // --- Screen Unlock ---

    [Fact]
    public void ScreenUnlock_TransitionsTo_T_TimerRunning()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.ScreenLock);
        _actions.Calls.Clear();

        sm.Fire(Trigger.ScreenUnlock);

        Assert.Equal(State.T_TimerRunning, sm.CurrentState);
        Assert.True(sm.IsInState(State.ScreenUnlocked));
    }

    [Fact]
    public void ScreenUnlock_ResumesT_And_StopsL()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.ScreenLock);
        _actions.Calls.Clear();

        sm.Fire(Trigger.ScreenUnlock);

        Assert.Equal(
            new[] { nameof(IEasyEyesActions.ResumeTTimer), nameof(IEasyEyesActions.StopLTimer), nameof(IEasyEyesActions.ClearToast) },
            _actions.Calls.ToArray()
        );
    }

    // --- L Timer Expiry with overlay displayed ---

    [Fact]
    public void LTimerExpired_WhenOverlayWasDisplayed_TransitionsTo_ToastDisplayed()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.TTimerExpired);  // → OverlayDisplayed (sets _wasOverlayDisplayed)
        sm.Fire(Trigger.ScreenLock);     // → L_TimerRunning

        sm.Fire(Trigger.LTimerExpired);

        Assert.Equal(State.ToastDisplayed, sm.CurrentState);
        Assert.True(sm.IsInState(State.ScreenLocked));
    }

    [Fact]
    public void LTimerExpired_WhenOverlayWasDisplayed_ResetsT_HidesOverlay_ShowsToast()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.TTimerExpired);
        sm.Fire(Trigger.ScreenLock);
        _actions.Calls.Clear();

        sm.Fire(Trigger.LTimerExpired);

        Assert.Equal(
            new[]
            {
                nameof(IEasyEyesActions.ResetTTimer),
                nameof(IEasyEyesActions.HideOverlay),
                nameof(IEasyEyesActions.ShowToast),
            },
            _actions.Calls.ToArray()
        );
    }

    // --- L Timer Expiry without overlay ---

    [Fact]
    public void LTimerExpired_WhenOverlayNotDisplayed_TransitionsTo_Idle()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.ScreenLock); // → L_TimerRunning (overlay was never shown)

        sm.Fire(Trigger.LTimerExpired);

        Assert.Equal(State.Idle, sm.CurrentState);
        Assert.True(sm.IsInState(State.ScreenLocked));
    }

    [Fact]
    public void LTimerExpired_WhenOverlayNotDisplayed_ResetsT_HidesOverlay_NoToast()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.ScreenLock);
        _actions.Calls.Clear();

        sm.Fire(Trigger.LTimerExpired);

        Assert.Equal(
            new[]
            {
                nameof(IEasyEyesActions.ResetTTimer),
                nameof(IEasyEyesActions.HideOverlay),
            },
            _actions.Calls.ToArray()
        );
        Assert.DoesNotContain(nameof(IEasyEyesActions.ShowToast), _actions.Calls);
    }

    // --- Unlock from ToastDisplayed / Idle ---

    [Fact]
    public void ScreenUnlock_FromToastDisplayed_TransitionsTo_T_TimerRunning()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.TTimerExpired);
        sm.Fire(Trigger.ScreenLock);
        sm.Fire(Trigger.LTimerExpired); // → ToastDisplayed

        sm.Fire(Trigger.ScreenUnlock);

        Assert.Equal(State.T_TimerRunning, sm.CurrentState);
    }

    [Fact]
    public void ScreenUnlock_FromIdle_TransitionsTo_T_TimerRunning()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.ScreenLock);
        sm.Fire(Trigger.LTimerExpired); // → Idle

        sm.Fire(Trigger.ScreenUnlock);

        Assert.Equal(State.T_TimerRunning, sm.CurrentState);
    }

    // --- Full cycle: lock → L expires → unlock → T expires again ---

    [Fact]
    public void FullCycle_OverlayShown_Lock_LExpires_Unlock_TExpiresAgain()
    {
        var sm = CreateMachine();

        sm.Fire(Trigger.TTimerExpired);  // T_TimerRunning → OverlayDisplayed
        Assert.Equal(State.OverlayDisplayed, sm.CurrentState);

        sm.Fire(Trigger.ScreenLock);     // → L_TimerRunning
        Assert.Equal(State.L_TimerRunning, sm.CurrentState);

        sm.Fire(Trigger.LTimerExpired);  // → ToastDisplayed (overlay was shown)
        Assert.Equal(State.ToastDisplayed, sm.CurrentState);

        sm.Fire(Trigger.ScreenUnlock);   // → T_TimerRunning
        Assert.Equal(State.T_TimerRunning, sm.CurrentState);

        sm.Fire(Trigger.TTimerExpired);  // → OverlayDisplayed again
        Assert.Equal(State.OverlayDisplayed, sm.CurrentState);
    }

    [Fact]
    public void FullCycle_NoOverlay_Lock_LExpires_Unlock()
    {
        var sm = CreateMachine();

        sm.Fire(Trigger.ScreenLock);     // → L_TimerRunning
        sm.Fire(Trigger.LTimerExpired);  // → Idle (no overlay)
        Assert.Equal(State.Idle, sm.CurrentState);

        sm.Fire(Trigger.ScreenUnlock);   // → T_TimerRunning
        Assert.Equal(State.T_TimerRunning, sm.CurrentState);

        sm.Fire(Trigger.TTimerExpired);  // → OverlayDisplayed
        Assert.Equal(State.OverlayDisplayed, sm.CurrentState);
    }

    // --- Quick lock/unlock without L expiry preserves overlay state ---

    [Fact]
    public void QuickLockUnlock_WithOverlay_OverlayStillTracked()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.TTimerExpired);  // → OverlayDisplayed
        sm.Fire(Trigger.ScreenLock);     // → L_TimerRunning
        sm.Fire(Trigger.ScreenUnlock);   // → T_TimerRunning (T resumed, still expired)

        // Lock again — overlay was displayed flag should still be true
        sm.Fire(Trigger.ScreenLock);
        sm.Fire(Trigger.LTimerExpired);

        Assert.Equal(State.ToastDisplayed, sm.CurrentState);
    }

    [Fact]
    public void DuplicateScreenLock_IsIgnored()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.ScreenLock);
        _actions.Calls.Clear();

        sm.Fire(Trigger.ScreenLock); // duplicate — should be ignored

        Assert.Equal(State.L_TimerRunning, sm.CurrentState);
        Assert.Empty(_actions.Calls);
    }

    [Fact]
    public void DuplicateScreenUnlock_IsIgnored()
    {
        var sm = CreateMachine();
        _actions.Calls.Clear();

        sm.Fire(Trigger.ScreenUnlock); // already unlocked — should be ignored

        Assert.Equal(State.T_TimerRunning, sm.CurrentState);
        Assert.Empty(_actions.Calls);
    }

    [Fact]
    public void QuickLockUnlock_WithoutOverlay_StaysNotDisplayed()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.ScreenLock);
        sm.Fire(Trigger.ScreenUnlock);

        // Lock again — overlay was never displayed
        sm.Fire(Trigger.ScreenLock);
        sm.Fire(Trigger.LTimerExpired);

        Assert.Equal(State.Idle, sm.CurrentState);
    }
}
