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
    public void StartSnoozeTimer(TimeSpan duration) => Calls.Add(nameof(StartSnoozeTimer));
    public void StopSnoozeTimer() => Calls.Add(nameof(StopSnoozeTimer));
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

    // --- Pause / Resume ---

    [Fact]
    public void Pause_FromT_TimerRunning_TransitionsTo_Paused()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.Pause);

        Assert.Equal(State.Paused, sm.CurrentState);
    }

    [Fact]
    public void Pause_EntryActions()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.Pause);

        Assert.Equal(
            new[] { nameof(IEasyEyesActions.SuspendTTimer), nameof(IEasyEyesActions.HideOverlay) },
            _actions.Calls.ToArray());
    }

    [Fact]
    public void Pause_FromOverlayDisplayed_TransitionsTo_Paused()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.TTimerExpired);
        _actions.Calls.Clear();

        sm.Fire(Trigger.Pause);

        Assert.Equal(State.Paused, sm.CurrentState);
        Assert.Equal(
            new[] { nameof(IEasyEyesActions.SuspendTTimer), nameof(IEasyEyesActions.HideOverlay) },
            _actions.Calls.ToArray());
    }

    [Fact]
    public void Resume_FromPaused_TransitionsTo_T_TimerRunning()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.Pause);
        _actions.Calls.Clear();

        sm.Fire(Trigger.Resume);

        Assert.Equal(State.T_TimerRunning, sm.CurrentState);
        Assert.Equal(
            new[] { nameof(IEasyEyesActions.ResetTTimer), nameof(IEasyEyesActions.ResumeTTimer) },
            _actions.Calls.ToArray());
    }

    [Fact]
    public void Paused_IgnoresAllTriggers()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.Pause);
        _actions.Calls.Clear();

        sm.Fire(Trigger.ScreenLock);
        sm.Fire(Trigger.ScreenUnlock);
        sm.Fire(Trigger.TTimerExpired);
        sm.Fire(Trigger.LTimerExpired);

        Assert.Equal(State.Paused, sm.CurrentState);
        Assert.Empty(_actions.Calls);
    }

    [Fact]
    public void Pause_ResetsOverlayFlag()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.TTimerExpired);  // overlay displayed, flag = true
        sm.Fire(Trigger.Pause);          // should reset flag
        sm.Fire(Trigger.Resume);         // → T_TimerRunning

        // Lock + L expires → should go to Idle (not Toast) because flag was cleared
        sm.Fire(Trigger.ScreenLock);
        sm.Fire(Trigger.LTimerExpired);

        Assert.Equal(State.Idle, sm.CurrentState);
    }

    [Fact]
    public void FullCycle_Pause_Resume_TExpiresAgain()
    {
        var sm = CreateMachine();

        sm.Fire(Trigger.Pause);
        Assert.Equal(State.Paused, sm.CurrentState);

        sm.Fire(Trigger.Resume);
        Assert.Equal(State.T_TimerRunning, sm.CurrentState);

        sm.Fire(Trigger.TTimerExpired);
        Assert.Equal(State.OverlayDisplayed, sm.CurrentState);
    }

    // --- PauseUntilUnlock ---

    [Fact]
    public void PauseUntilUnlock_FromT_TimerRunning_TransitionsTo_PausedUntilUnlock()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.PauseUntilUnlock);

        Assert.Equal(State.PausedUntilUnlock, sm.CurrentState);
    }

    [Fact]
    public void PauseUntilUnlock_EntryActions()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.PauseUntilUnlock);

        Assert.Equal(
            new[] { nameof(IEasyEyesActions.SuspendTTimer), nameof(IEasyEyesActions.HideOverlay) },
            _actions.Calls.ToArray());
    }

    [Fact]
    public void PauseUntilUnlock_FromOverlayDisplayed()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.TTimerExpired);
        _actions.Calls.Clear();

        sm.Fire(Trigger.PauseUntilUnlock);

        Assert.Equal(State.PausedUntilUnlock, sm.CurrentState);
        Assert.Equal(
            new[] { nameof(IEasyEyesActions.SuspendTTimer), nameof(IEasyEyesActions.HideOverlay) },
            _actions.Calls.ToArray());
    }

    [Fact]
    public void PausedUntilUnlock_ScreenUnlock_ResumesTo_T_TimerRunning()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.PauseUntilUnlock);
        _actions.Calls.Clear();

        sm.Fire(Trigger.ScreenUnlock);

        Assert.Equal(State.T_TimerRunning, sm.CurrentState);
        // OnExit resets T, then OnEntryFrom(ScreenUnlock) resumes T
        Assert.Equal(
            new[]
            {
                nameof(IEasyEyesActions.ResetTTimer),
                nameof(IEasyEyesActions.ResumeTTimer),
                nameof(IEasyEyesActions.StopLTimer),
                nameof(IEasyEyesActions.ClearToast),
            },
            _actions.Calls.ToArray());
    }

    [Fact]
    public void PausedUntilUnlock_Resume_ManualToggle()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.PauseUntilUnlock);
        _actions.Calls.Clear();

        sm.Fire(Trigger.Resume);

        Assert.Equal(State.T_TimerRunning, sm.CurrentState);
        // OnExit resets T, then OnEntryFrom(Resume) also resets + resumes
        Assert.Contains(nameof(IEasyEyesActions.ResetTTimer), _actions.Calls);
        Assert.Contains(nameof(IEasyEyesActions.ResumeTTimer), _actions.Calls);
    }

    [Fact]
    public void PausedUntilUnlock_IgnoresAllIrrelevantTriggers()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.PauseUntilUnlock);
        _actions.Calls.Clear();

        sm.Fire(Trigger.ScreenLock);
        sm.Fire(Trigger.TTimerExpired);
        sm.Fire(Trigger.LTimerExpired);

        Assert.Equal(State.PausedUntilUnlock, sm.CurrentState);
        Assert.Empty(_actions.Calls);
    }

    [Fact]
    public void PauseUntilUnlock_ResetsOverlayFlag()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.TTimerExpired);      // overlay displayed, flag = true
        sm.Fire(Trigger.PauseUntilUnlock);   // should reset flag
        sm.Fire(Trigger.ScreenUnlock);       // → T_TimerRunning

        // Lock + L expires → should go to Idle (not Toast)
        sm.Fire(Trigger.ScreenLock);
        sm.Fire(Trigger.LTimerExpired);

        Assert.Equal(State.Idle, sm.CurrentState);
    }

    [Fact]
    public void FullCycle_PauseUntilUnlock_Lock_Unlock_TExpiresAgain()
    {
        var sm = CreateMachine();

        sm.Fire(Trigger.PauseUntilUnlock);
        Assert.Equal(State.PausedUntilUnlock, sm.CurrentState);

        // Lock is ignored while in PausedUntilUnlock
        sm.Fire(Trigger.ScreenLock);
        Assert.Equal(State.PausedUntilUnlock, sm.CurrentState);

        // Unlock auto-resumes
        sm.Fire(Trigger.ScreenUnlock);
        Assert.Equal(State.T_TimerRunning, sm.CurrentState);

        sm.Fire(Trigger.TTimerExpired);
        Assert.Equal(State.OverlayDisplayed, sm.CurrentState);
    }

    // --- PauseForDuration (PausedTimed) ---

    [Fact]
    public void PauseForDuration_FromT_TimerRunning_TransitionsTo_PausedTimed()
    {
        var sm = CreateMachine();
        sm.FirePauseForDuration(TimeSpan.FromMinutes(30));

        Assert.Equal(State.PausedTimed, sm.CurrentState);
    }

    [Fact]
    public void PauseForDuration_EntryActions()
    {
        var sm = CreateMachine();
        sm.FirePauseForDuration(TimeSpan.FromMinutes(30));

        Assert.Equal(
            new[]
            {
                nameof(IEasyEyesActions.SuspendTTimer),
                nameof(IEasyEyesActions.HideOverlay),
                nameof(IEasyEyesActions.StartSnoozeTimer),
            },
            _actions.Calls.ToArray());
    }

    [Fact]
    public void PauseForDuration_FromOverlayDisplayed()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.TTimerExpired);
        _actions.Calls.Clear();

        sm.FirePauseForDuration(TimeSpan.FromMinutes(10));

        Assert.Equal(State.PausedTimed, sm.CurrentState);
        Assert.Equal(
            new[]
            {
                nameof(IEasyEyesActions.SuspendTTimer),
                nameof(IEasyEyesActions.HideOverlay),
                nameof(IEasyEyesActions.StartSnoozeTimer),
            },
            _actions.Calls.ToArray());
    }

    [Fact]
    public void SnoozeExpired_Actions()
    {
        var sm = CreateMachine();
        sm.FirePauseForDuration(TimeSpan.FromMinutes(30));
        _actions.Calls.Clear();

        sm.Fire(Trigger.SnoozeExpired);

        Assert.Equal(State.T_TimerRunning, sm.CurrentState);
        // OnExit stops snooze, then OnEntryFrom(SnoozeExpired) resets + resumes T
        Assert.Equal(
            new[]
            {
                nameof(IEasyEyesActions.StopSnoozeTimer),
                nameof(IEasyEyesActions.ResetTTimer),
                nameof(IEasyEyesActions.ResumeTTimer),
            },
            _actions.Calls.ToArray());
    }

    [Fact]
    public void PausedTimed_Resume_Actions()
    {
        var sm = CreateMachine();
        sm.FirePauseForDuration(TimeSpan.FromMinutes(30));
        _actions.Calls.Clear();

        sm.Fire(Trigger.Resume);

        Assert.Equal(State.T_TimerRunning, sm.CurrentState);
        // OnExit stops snooze, then OnEntryFrom(Resume) resets + resumes T
        Assert.Equal(
            new[]
            {
                nameof(IEasyEyesActions.StopSnoozeTimer),
                nameof(IEasyEyesActions.ResetTTimer),
                nameof(IEasyEyesActions.ResumeTTimer),
            },
            _actions.Calls.ToArray());
    }

    [Fact]
    public void PausedTimed_IgnoresAllIrrelevantTriggers()
    {
        var sm = CreateMachine();
        sm.FirePauseForDuration(TimeSpan.FromMinutes(30));
        _actions.Calls.Clear();

        sm.Fire(Trigger.ScreenLock);
        sm.Fire(Trigger.ScreenUnlock);
        sm.Fire(Trigger.TTimerExpired);
        sm.Fire(Trigger.LTimerExpired);

        Assert.Equal(State.PausedTimed, sm.CurrentState);
        Assert.Empty(_actions.Calls);
    }

    [Fact]
    public void PauseForDuration_ResetsOverlayFlag()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.TTimerExpired);            // overlay displayed, flag = true
        sm.FirePauseForDuration(TimeSpan.FromMinutes(5)); // should reset flag
        sm.Fire(Trigger.SnoozeExpired);            // → T_TimerRunning

        // Lock + L expires → should go to Idle (not Toast)
        sm.Fire(Trigger.ScreenLock);
        sm.Fire(Trigger.LTimerExpired);

        Assert.Equal(State.Idle, sm.CurrentState);
    }

    [Fact]
    public void FullCycle_PauseForDuration_SnoozeExpires_TExpiresAgain()
    {
        var sm = CreateMachine();

        sm.FirePauseForDuration(TimeSpan.FromMinutes(30));
        Assert.Equal(State.PausedTimed, sm.CurrentState);

        sm.Fire(Trigger.SnoozeExpired);
        Assert.Equal(State.T_TimerRunning, sm.CurrentState);

        sm.Fire(Trigger.TTimerExpired);
        Assert.Equal(State.OverlayDisplayed, sm.CurrentState);
    }

    [Fact]
    public void FullCycle_PauseForDuration_ManualResume_TExpiresAgain()
    {
        var sm = CreateMachine();

        sm.FirePauseForDuration(TimeSpan.FromMinutes(30));
        Assert.Equal(State.PausedTimed, sm.CurrentState);

        sm.Fire(Trigger.Resume);
        Assert.Equal(State.T_TimerRunning, sm.CurrentState);

        sm.Fire(Trigger.TTimerExpired);
        Assert.Equal(State.OverlayDisplayed, sm.CurrentState);
    }

    // --- Invalid transitions (should throw) ---

    // From T_TimerRunning: unhandled triggers
    [Theory]
    [InlineData(Trigger.LTimerExpired)]
    [InlineData(Trigger.Resume)]
    [InlineData(Trigger.SnoozeExpired)]
    public void T_TimerRunning_InvalidTrigger_Throws(Trigger trigger)
    {
        var sm = CreateMachine();
        Assert.Equal(State.T_TimerRunning, sm.CurrentState);

        Assert.Throws<InvalidOperationException>(() => sm.Fire(trigger));
    }

    // From OverlayDisplayed: TTimerExpired again is unhandled
    [Theory]
    [InlineData(Trigger.TTimerExpired)]
    [InlineData(Trigger.LTimerExpired)]
    [InlineData(Trigger.Resume)]
    [InlineData(Trigger.SnoozeExpired)]
    public void OverlayDisplayed_InvalidTrigger_Throws(Trigger trigger)
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.TTimerExpired);
        Assert.Equal(State.OverlayDisplayed, sm.CurrentState);

        Assert.Throws<InvalidOperationException>(() => sm.Fire(trigger));
    }

    // From L_TimerRunning: unhandled triggers
    [Theory]
    [InlineData(Trigger.TTimerExpired)]
    [InlineData(Trigger.Pause)]
    [InlineData(Trigger.PauseUntilUnlock)]
    [InlineData(Trigger.Resume)]
    [InlineData(Trigger.SnoozeExpired)]
    public void L_TimerRunning_InvalidTrigger_Throws(Trigger trigger)
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.ScreenLock);
        Assert.Equal(State.L_TimerRunning, sm.CurrentState);

        Assert.Throws<InvalidOperationException>(() => sm.Fire(trigger));
    }

    // From ToastDisplayed: unhandled triggers
    [Theory]
    [InlineData(Trigger.TTimerExpired)]
    [InlineData(Trigger.LTimerExpired)]
    [InlineData(Trigger.Pause)]
    [InlineData(Trigger.PauseUntilUnlock)]
    [InlineData(Trigger.Resume)]
    [InlineData(Trigger.SnoozeExpired)]
    public void ToastDisplayed_InvalidTrigger_Throws(Trigger trigger)
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.TTimerExpired); // → OverlayDisplayed
        sm.Fire(Trigger.ScreenLock);    // → L_TimerRunning
        sm.Fire(Trigger.LTimerExpired); // → ToastDisplayed
        Assert.Equal(State.ToastDisplayed, sm.CurrentState);

        Assert.Throws<InvalidOperationException>(() => sm.Fire(trigger));
    }

    // From Idle: unhandled triggers
    [Theory]
    [InlineData(Trigger.TTimerExpired)]
    [InlineData(Trigger.LTimerExpired)]
    [InlineData(Trigger.Pause)]
    [InlineData(Trigger.PauseUntilUnlock)]
    [InlineData(Trigger.Resume)]
    [InlineData(Trigger.SnoozeExpired)]
    public void Idle_InvalidTrigger_Throws(Trigger trigger)
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.ScreenLock);    // → L_TimerRunning
        sm.Fire(Trigger.LTimerExpired); // → Idle
        Assert.Equal(State.Idle, sm.CurrentState);

        Assert.Throws<InvalidOperationException>(() => sm.Fire(trigger));
    }

    // From Paused: unhandled triggers
    [Theory]
    [InlineData(Trigger.Pause)]
    [InlineData(Trigger.PauseUntilUnlock)]
    [InlineData(Trigger.SnoozeExpired)]
    public void Paused_InvalidTrigger_Throws(Trigger trigger)
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.Pause);
        Assert.Equal(State.Paused, sm.CurrentState);

        Assert.Throws<InvalidOperationException>(() => sm.Fire(trigger));
    }

    // From PausedUntilUnlock: unhandled triggers
    [Theory]
    [InlineData(Trigger.Pause)]
    [InlineData(Trigger.PauseUntilUnlock)]
    [InlineData(Trigger.SnoozeExpired)]
    public void PausedUntilUnlock_InvalidTrigger_Throws(Trigger trigger)
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.PauseUntilUnlock);
        Assert.Equal(State.PausedUntilUnlock, sm.CurrentState);

        Assert.Throws<InvalidOperationException>(() => sm.Fire(trigger));
    }

    // From PausedTimed: unhandled triggers
    [Theory]
    [InlineData(Trigger.Pause)]
    [InlineData(Trigger.PauseUntilUnlock)]
    public void PausedTimed_InvalidTrigger_Throws(Trigger trigger)
    {
        var sm = CreateMachine();
        sm.FirePauseForDuration(TimeSpan.FromMinutes(30));
        Assert.Equal(State.PausedTimed, sm.CurrentState);

        Assert.Throws<InvalidOperationException>(() => sm.Fire(trigger));
    }
}
