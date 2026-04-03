using EasyEyes;
using Microsoft.Extensions.Time.Testing;

namespace EasyEyes.Tests;

public class MockActions : IEasyEyesActions
{
    public List<string> Calls { get; } = [];

    public void ShowOverlay() => Calls.Add(nameof(ShowOverlay));
    public void HideOverlay() => Calls.Add(nameof(HideOverlay));
    public void NotifyUser() => Calls.Add(nameof(NotifyUser));
    public void SuspendActivityTimer() => Calls.Add(nameof(SuspendActivityTimer));
    public void ResumeActivityTimer() => Calls.Add(nameof(ResumeActivityTimer));
    public void ResetActivityTimer() => Calls.Add(nameof(ResetActivityTimer));
    public void RestartRestTimer() => Calls.Add(nameof(RestartRestTimer));
    public void StopRestTimer() => Calls.Add(nameof(StopRestTimer));
    public TimeSpan? LastExtendTTimerDuration { get; private set; }
    public void ExtendActivityTimer(TimeSpan duration)
    {
        LastExtendTTimerDuration = duration;
        Calls.Add(nameof(ExtendActivityTimer));
    }
}

public class EasyEyesStateMachineTests
{
    private readonly MockActions _actions = new();

    private EasyEyesStateMachine CreateMachine() => new(_actions);

    [Fact]
    public void InitialState_Is_T_TimerRunning()
    {
        var sm = CreateMachine();
        Assert.Equal(State.ActivityTimerRunning, sm.CurrentState);
        Assert.True(sm.IsInState(State.ScreenUnlocked));
    }

    // --- T Timer Expiry ---

    [Fact]
    public void TTimerExpired_TransitionsTo_OverlayDisplayed()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.ActivityTimerExpired);

        Assert.Equal(State.OverlayDisplayed, sm.CurrentState);
        Assert.True(sm.IsInState(State.ScreenUnlocked));
    }

    [Fact]
    public void TTimerExpired_Calls_ShowOverlay()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.ActivityTimerExpired);

        Assert.Contains(nameof(IEasyEyesActions.ShowOverlay), _actions.Calls);
    }

    // --- Screen Lock ---

    [Fact]
    public void ScreenLock_FromT_TimerRunning_TransitionsTo_L_TimerRunning()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.ScreenLock);

        Assert.Equal(State.RestTimerRunning, sm.CurrentState);
        Assert.True(sm.IsInState(State.ScreenLocked));
    }

    [Fact]
    public void ScreenLock_SuspendsT_And_RestartsL()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.ScreenLock);

        Assert.Equal(
            new[] { nameof(IEasyEyesActions.SuspendActivityTimer), nameof(IEasyEyesActions.RestartRestTimer) },
            _actions.Calls.ToArray()
        );
    }

    [Fact]
    public void ScreenLock_FromOverlayDisplayed_TransitionsTo_L_TimerRunning()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.ActivityTimerExpired); // → OverlayDisplayed
        _actions.Calls.Clear();

        sm.Fire(Trigger.ScreenLock);

        Assert.Equal(State.RestTimerRunning, sm.CurrentState);
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

        Assert.Equal(State.ActivityTimerRunning, sm.CurrentState);
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
            new[] { nameof(IEasyEyesActions.ResumeActivityTimer), nameof(IEasyEyesActions.StopRestTimer) },
            _actions.Calls.ToArray()
        );
    }

    // --- L Timer Expiry with overlay displayed ---

    [Fact]
    public void LTimerExpired_WhenOverlayWasDisplayed_TransitionsTo_ToastDisplayed()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.ActivityTimerExpired);  // → OverlayDisplayed (sets _wasOverlayDisplayed)
        sm.Fire(Trigger.ScreenLock);     // → L_TimerRunning

        sm.Fire(Trigger.RestTimerExpired);

        Assert.Equal(State.ToastDisplayed, sm.CurrentState);
        Assert.True(sm.IsInState(State.ScreenLocked));
    }

    [Fact]
    public void LTimerExpired_WhenOverlayWasDisplayed_ResetsT_HidesOverlay_ShowsToast()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.ActivityTimerExpired);
        sm.Fire(Trigger.ScreenLock);
        _actions.Calls.Clear();

        sm.Fire(Trigger.RestTimerExpired);

        Assert.Equal(
            new[]
            {
                nameof(IEasyEyesActions.ResetActivityTimer),
                nameof(IEasyEyesActions.HideOverlay),
                nameof(IEasyEyesActions.NotifyUser),
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

        sm.Fire(Trigger.RestTimerExpired);

        Assert.Equal(State.Idle, sm.CurrentState);
        Assert.True(sm.IsInState(State.ScreenLocked));
    }

    [Fact]
    public void LTimerExpired_WhenOverlayNotDisplayed_ResetsT_HidesOverlay_NoToast()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.ScreenLock);
        _actions.Calls.Clear();

        sm.Fire(Trigger.RestTimerExpired);

        Assert.Equal(
            new[]
            {
                nameof(IEasyEyesActions.ResetActivityTimer),
                nameof(IEasyEyesActions.HideOverlay),
            },
            _actions.Calls.ToArray()
        );
        Assert.DoesNotContain(nameof(IEasyEyesActions.NotifyUser), _actions.Calls);
    }

    // --- Unlock from ToastDisplayed / Idle ---

    [Fact]
    public void ScreenUnlock_FromToastDisplayed_TransitionsTo_T_TimerRunning()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.ActivityTimerExpired);
        sm.Fire(Trigger.ScreenLock);
        sm.Fire(Trigger.RestTimerExpired); // → ToastDisplayed

        sm.Fire(Trigger.ScreenUnlock);

        Assert.Equal(State.ActivityTimerRunning, sm.CurrentState);
    }

    [Fact]
    public void ScreenUnlock_FromIdle_TransitionsTo_T_TimerRunning()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.ScreenLock);
        sm.Fire(Trigger.RestTimerExpired); // → Idle

        sm.Fire(Trigger.ScreenUnlock);

        Assert.Equal(State.ActivityTimerRunning, sm.CurrentState);
    }

    // --- Full cycle: lock → L expires → unlock → T expires again ---

    [Fact]
    public void FullCycle_OverlayShown_Lock_LExpires_Unlock_TExpiresAgain()
    {
        var sm = CreateMachine();

        sm.Fire(Trigger.ActivityTimerExpired);  // T_TimerRunning → OverlayDisplayed
        Assert.Equal(State.OverlayDisplayed, sm.CurrentState);

        sm.Fire(Trigger.ScreenLock);     // → L_TimerRunning
        Assert.Equal(State.RestTimerRunning, sm.CurrentState);

        sm.Fire(Trigger.RestTimerExpired);  // → ToastDisplayed (overlay was shown)
        Assert.Equal(State.ToastDisplayed, sm.CurrentState);

        sm.Fire(Trigger.ScreenUnlock);   // → T_TimerRunning
        Assert.Equal(State.ActivityTimerRunning, sm.CurrentState);

        sm.Fire(Trigger.ActivityTimerExpired);  // → OverlayDisplayed again
        Assert.Equal(State.OverlayDisplayed, sm.CurrentState);
    }

    [Fact]
    public void FullCycle_NoOverlay_Lock_LExpires_Unlock()
    {
        var sm = CreateMachine();

        sm.Fire(Trigger.ScreenLock);     // → L_TimerRunning
        sm.Fire(Trigger.RestTimerExpired);  // → Idle (no overlay)
        Assert.Equal(State.Idle, sm.CurrentState);

        sm.Fire(Trigger.ScreenUnlock);   // → T_TimerRunning
        Assert.Equal(State.ActivityTimerRunning, sm.CurrentState);

        sm.Fire(Trigger.ActivityTimerExpired);  // → OverlayDisplayed
        Assert.Equal(State.OverlayDisplayed, sm.CurrentState);
    }

    // --- Quick lock/unlock without L expiry preserves overlay state ---

    [Fact]
    public void QuickLockUnlock_WithOverlay_OverlayStillTracked()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.ActivityTimerExpired);  // → OverlayDisplayed
        sm.Fire(Trigger.ScreenLock);     // → L_TimerRunning
        sm.Fire(Trigger.ScreenUnlock);   // → T_TimerRunning (T resumed, still expired)

        // Lock again — overlay was displayed flag should still be true
        sm.Fire(Trigger.ScreenLock);
        sm.Fire(Trigger.RestTimerExpired);

        Assert.Equal(State.ToastDisplayed, sm.CurrentState);
    }

    [Fact]
    public void DuplicateScreenLock_IsIgnored()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.ScreenLock);
        _actions.Calls.Clear();

        sm.Fire(Trigger.ScreenLock); // duplicate — should be ignored

        Assert.Equal(State.RestTimerRunning, sm.CurrentState);
        Assert.Empty(_actions.Calls);
    }

    [Fact]
    public void DuplicateScreenUnlock_IsIgnored()
    {
        var sm = CreateMachine();
        _actions.Calls.Clear();

        sm.Fire(Trigger.ScreenUnlock); // already unlocked — should be ignored

        Assert.Equal(State.ActivityTimerRunning, sm.CurrentState);
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
        sm.Fire(Trigger.RestTimerExpired);

        Assert.Equal(State.Idle, sm.CurrentState);
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
            new[] { nameof(IEasyEyesActions.SuspendActivityTimer), nameof(IEasyEyesActions.HideOverlay) },
            _actions.Calls.ToArray());
    }

    [Fact]
    public void PauseUntilUnlock_FromOverlayDisplayed()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.ActivityTimerExpired);
        _actions.Calls.Clear();

        sm.Fire(Trigger.PauseUntilUnlock);

        Assert.Equal(State.PausedUntilUnlock, sm.CurrentState);
        Assert.Equal(
            new[] { nameof(IEasyEyesActions.SuspendActivityTimer), nameof(IEasyEyesActions.HideOverlay) },
            _actions.Calls.ToArray());
    }

    [Fact]
    public void PausedUntilUnlock_ScreenUnlock_ResumesTo_T_TimerRunning()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.PauseUntilUnlock);
        _actions.Calls.Clear();

        sm.Fire(Trigger.ScreenUnlock);

        Assert.Equal(State.ActivityTimerRunning, sm.CurrentState);
        // OnExit resets T, then OnEntryFrom(ScreenUnlock) resumes T
        Assert.Equal(
            new[]
            {
                nameof(IEasyEyesActions.ResetActivityTimer),
                nameof(IEasyEyesActions.ResumeActivityTimer),
                nameof(IEasyEyesActions.StopRestTimer),
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

        Assert.Equal(State.ActivityTimerRunning, sm.CurrentState);
        // OnExit resets T, then OnEntryFrom(Resume) also resets + resumes
        Assert.Contains(nameof(IEasyEyesActions.ResetActivityTimer), _actions.Calls);
        Assert.Contains(nameof(IEasyEyesActions.ResumeActivityTimer), _actions.Calls);
    }

    [Fact]
    public void PausedUntilUnlock_IgnoresAllIrrelevantTriggers()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.PauseUntilUnlock);
        _actions.Calls.Clear();

        sm.Fire(Trigger.ScreenLock);
        sm.Fire(Trigger.ActivityTimerExpired);
        sm.Fire(Trigger.RestTimerExpired);

        Assert.Equal(State.PausedUntilUnlock, sm.CurrentState);
        Assert.Empty(_actions.Calls);
    }

    [Fact]
    public void PauseUntilUnlock_ResetsOverlayFlag()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.ActivityTimerExpired);      // overlay displayed, flag = true
        sm.Fire(Trigger.PauseUntilUnlock);   // should reset flag
        sm.Fire(Trigger.ScreenUnlock);       // → T_TimerRunning

        // Lock + L expires → should go to Idle (not Toast)
        sm.Fire(Trigger.ScreenLock);
        sm.Fire(Trigger.RestTimerExpired);

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
        Assert.Equal(State.ActivityTimerRunning, sm.CurrentState);

        sm.Fire(Trigger.ActivityTimerExpired);
        Assert.Equal(State.OverlayDisplayed, sm.CurrentState);
    }

    // --- PauseForDuration ---

    [Fact]
    public void PauseForDuration_FromT_TimerRunning_StaysInT_TimerRunning()
    {
        var sm = CreateMachine();
        sm.FirePauseForDuration(TimeSpan.FromMinutes(30));

        Assert.Equal(State.ActivityTimerRunning, sm.CurrentState);
    }

    [Fact]
    public void PauseForDuration_EntryActions()
    {
        var sm = CreateMachine();
        sm.FirePauseForDuration(TimeSpan.FromMinutes(30));

        Assert.Equal(
            new[]
            {
                nameof(IEasyEyesActions.SuspendActivityTimer),
                nameof(IEasyEyesActions.HideOverlay),
                nameof(IEasyEyesActions.ExtendActivityTimer),
                nameof(IEasyEyesActions.ResumeActivityTimer),
            },
            _actions.Calls.ToArray());
    }

    [Fact]
    public void PauseForDuration_FromOverlayDisplayed()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.ActivityTimerExpired);
        _actions.Calls.Clear();

        sm.FirePauseForDuration(TimeSpan.FromMinutes(10));

        Assert.Equal(State.ActivityTimerRunning, sm.CurrentState);
        Assert.Equal(
            new[]
            {
                nameof(IEasyEyesActions.SuspendActivityTimer),
                nameof(IEasyEyesActions.HideOverlay),
                nameof(IEasyEyesActions.ExtendActivityTimer),
                nameof(IEasyEyesActions.ResumeActivityTimer),
            },
            _actions.Calls.ToArray());
    }

    [Fact]
    public void PauseForDuration_ResetsOverlayFlag()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.ActivityTimerExpired);            // overlay displayed, flag = true
        sm.FirePauseForDuration(TimeSpan.FromMinutes(5)); // should reset flag, back to T_TimerRunning

        // Lock + L expires → should go to Idle (not Toast)
        sm.Fire(Trigger.ScreenLock);
        sm.Fire(Trigger.RestTimerExpired);

        Assert.Equal(State.Idle, sm.CurrentState);
    }

    [Fact]
    public void FullCycle_PauseForDuration_TExpiresAgain()
    {
        var sm = CreateMachine();

        sm.FirePauseForDuration(TimeSpan.FromMinutes(30));
        Assert.Equal(State.ActivityTimerRunning, sm.CurrentState);

        sm.Fire(Trigger.ActivityTimerExpired);
        Assert.Equal(State.OverlayDisplayed, sm.CurrentState);
    }

    // --- Screen Sleep ---

    [Fact]
    public void ScreenSleep_FromT_TimerRunning_TransitionsTo_L_TimerRunning()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.ScreenSleep);

        Assert.Equal(State.RestTimerRunning, sm.CurrentState);
        Assert.True(sm.IsInState(State.ScreenLocked));
    }

    [Fact]
    public void ScreenSleep_SuspendsT_RestartsL_HidesOverlay()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.ScreenSleep);

        Assert.Equal(
            new[] { nameof(IEasyEyesActions.SuspendActivityTimer), nameof(IEasyEyesActions.RestartRestTimer), nameof(IEasyEyesActions.HideOverlay) },
            _actions.Calls.ToArray()
        );
    }

    [Fact]
    public void ScreenSleep_FromOverlayDisplayed_TransitionsTo_L_TimerRunning()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.ActivityTimerExpired); // → OverlayDisplayed
        _actions.Calls.Clear();

        sm.Fire(Trigger.ScreenSleep);

        Assert.Equal(State.RestTimerRunning, sm.CurrentState);
        Assert.True(sm.IsInState(State.ScreenLocked));
    }

    [Fact]
    public void ScreenSleep_FromOverlayDisplayed_NeverShowsToast()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.ActivityTimerExpired); // overlay displayed, _wasOverlayDisplayed = true
        sm.Fire(Trigger.ScreenSleep);

        sm.Fire(Trigger.RestTimerExpired);

        Assert.Equal(State.Idle, sm.CurrentState);
        Assert.DoesNotContain(nameof(IEasyEyesActions.NotifyUser), _actions.Calls);
    }

    [Fact]
    public void ScreenSleep_IgnoredInScreenLocked()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.ScreenLock);
        sm.Fire(Trigger.ScreenSleep);

        Assert.Equal(State.RestTimerRunning, sm.CurrentState);
    }

    [Fact]
    public void ScreenSleep_IgnoredInPausedUntilUnlock()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.PauseUntilUnlock);
        sm.Fire(Trigger.ScreenSleep);
        Assert.Equal(State.PausedUntilUnlock, sm.CurrentState);
    }

    // --- Screen Wake ---

    [Fact]
    public void ScreenWake_FromL_TimerRunning_TransitionsTo_T_TimerRunning()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.ScreenSleep);
        sm.Fire(Trigger.ScreenWake);

        Assert.Equal(State.ActivityTimerRunning, sm.CurrentState);
        Assert.True(sm.IsInState(State.ScreenUnlocked));
    }

    [Fact]
    public void ScreenWake_ResumesT_StopsL()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.ScreenSleep);
        _actions.Calls.Clear();

        sm.Fire(Trigger.ScreenWake);

        Assert.Equal(
            new[] { nameof(IEasyEyesActions.ResumeActivityTimer), nameof(IEasyEyesActions.StopRestTimer) },
            _actions.Calls.ToArray()
        );
    }

    [Fact]
    public void ScreenWake_FromIdle_TransitionsTo_T_TimerRunning()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.ScreenSleep);
        sm.Fire(Trigger.RestTimerExpired); // → Idle

        sm.Fire(Trigger.ScreenWake);

        Assert.Equal(State.ActivityTimerRunning, sm.CurrentState);
    }

    [Fact]
    public void ScreenWake_IgnoredInScreenUnlocked()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.ScreenWake);
        Assert.Equal(State.ActivityTimerRunning, sm.CurrentState);
    }

    [Fact]
    public void ScreenWake_IgnoredInPausedUntilUnlock()
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.PauseUntilUnlock);
        sm.Fire(Trigger.ScreenWake);
        Assert.Equal(State.PausedUntilUnlock, sm.CurrentState);
    }

    // --- Full cycle with screen sleep ---

    [Fact]
    public void FullCycle_ScreenSleep_LExpires_ScreenWake_TExpiresAgain()
    {
        var sm = CreateMachine();

        sm.Fire(Trigger.ScreenSleep);
        Assert.Equal(State.RestTimerRunning, sm.CurrentState);

        sm.Fire(Trigger.RestTimerExpired);
        Assert.Equal(State.Idle, sm.CurrentState);

        sm.Fire(Trigger.ScreenWake);
        Assert.Equal(State.ActivityTimerRunning, sm.CurrentState);

        sm.Fire(Trigger.ActivityTimerExpired);
        Assert.Equal(State.OverlayDisplayed, sm.CurrentState);
    }

    // --- Invalid transitions (should throw) ---

    // From T_TimerRunning: unhandled triggers
    [Theory]
    [InlineData(Trigger.RestTimerExpired)]
    [InlineData(Trigger.Resume)]
    public void T_TimerRunning_InvalidTrigger_Throws(Trigger trigger)
    {
        var sm = CreateMachine();
        Assert.Equal(State.ActivityTimerRunning, sm.CurrentState);

        Assert.Throws<InvalidOperationException>(() => sm.Fire(trigger));
    }

    // From OverlayDisplayed: TTimerExpired again is unhandled
    [Theory]
    [InlineData(Trigger.ActivityTimerExpired)]
    [InlineData(Trigger.RestTimerExpired)]
    [InlineData(Trigger.Resume)]
    public void OverlayDisplayed_InvalidTrigger_Throws(Trigger trigger)
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.ActivityTimerExpired);
        Assert.Equal(State.OverlayDisplayed, sm.CurrentState);

        Assert.Throws<InvalidOperationException>(() => sm.Fire(trigger));
    }

    // From L_TimerRunning: unhandled triggers
    [Theory]
    [InlineData(Trigger.ActivityTimerExpired)]
    [InlineData(Trigger.PauseUntilUnlock)]
    [InlineData(Trigger.Resume)]
    public void L_TimerRunning_InvalidTrigger_Throws(Trigger trigger)
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.ScreenLock);
        Assert.Equal(State.RestTimerRunning, sm.CurrentState);

        Assert.Throws<InvalidOperationException>(() => sm.Fire(trigger));
    }

    // From ToastDisplayed: unhandled triggers
    [Theory]
    [InlineData(Trigger.ActivityTimerExpired)]
    [InlineData(Trigger.RestTimerExpired)]
    [InlineData(Trigger.PauseUntilUnlock)]
    [InlineData(Trigger.Resume)]
    public void ToastDisplayed_InvalidTrigger_Throws(Trigger trigger)
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.ActivityTimerExpired); // → OverlayDisplayed
        sm.Fire(Trigger.ScreenLock);    // → L_TimerRunning
        sm.Fire(Trigger.RestTimerExpired); // → ToastDisplayed
        Assert.Equal(State.ToastDisplayed, sm.CurrentState);

        Assert.Throws<InvalidOperationException>(() => sm.Fire(trigger));
    }

    // From Idle: unhandled triggers
    [Theory]
    [InlineData(Trigger.ActivityTimerExpired)]
    [InlineData(Trigger.RestTimerExpired)]
    [InlineData(Trigger.PauseUntilUnlock)]
    [InlineData(Trigger.Resume)]
    public void Idle_InvalidTrigger_Throws(Trigger trigger)
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.ScreenLock);    // → L_TimerRunning
        sm.Fire(Trigger.RestTimerExpired); // → Idle
        Assert.Equal(State.Idle, sm.CurrentState);

        Assert.Throws<InvalidOperationException>(() => sm.Fire(trigger));
    }

    // From PausedUntilUnlock: unhandled triggers
    [Theory]
    [InlineData(Trigger.PauseUntilUnlock)]
    public void PausedUntilUnlock_InvalidTrigger_Throws(Trigger trigger)
    {
        var sm = CreateMachine();
        sm.Fire(Trigger.PauseUntilUnlock);
        Assert.Equal(State.PausedUntilUnlock, sm.CurrentState);

        Assert.Throws<InvalidOperationException>(() => sm.Fire(trigger));
    }

}

/// <summary>
/// Given-When-Then style tests for the EasyEyes state machine.
/// These tests describe scenarios in terms of preconditions, events, and expected outcomes.
/// </summary>
public class GivenWhenThenTests
{
    private readonly MockActions _actions = new();
    private EasyEyesStateMachine CreateMachine() => new(_actions);

    // --- Screen lock / unlock scenarios ---

    [Fact]
    public void Given_ScreenLockedAndLExpired_When_ScreenUnlocked_Then_OverlayIsHidden()
    {
        // Given: screen is locked and L expired (overlay was displayed before lock)
        var sm = CreateMachine();
        sm.Fire(Trigger.ActivityTimerExpired);  // overlay displayed
        sm.Fire(Trigger.ScreenLock);     // screen locked
        sm.Fire(Trigger.RestTimerExpired);  // L expired → ToastDisplayed (hides overlay)
        _actions.Calls.Clear();

        // When: screen is unlocked
        sm.Fire(Trigger.ScreenUnlock);

        // Then: overlay is hidden (it was already hidden on L expiry, and not re-shown)
        Assert.Equal(State.ActivityTimerRunning, sm.CurrentState);
        Assert.DoesNotContain(nameof(IEasyEyesActions.ShowOverlay), _actions.Calls);
    }

    [Fact]
    public void Given_OverlayDisplayedAndScreenLocked_When_LExpires_Then_ToastIsDisplayed()
    {
        // Given: overlay is displayed and screen is locked
        var sm = CreateMachine();
        sm.Fire(Trigger.ActivityTimerExpired);  // overlay displayed
        sm.Fire(Trigger.ScreenLock);     // screen locked, L timer starts
        _actions.Calls.Clear();

        // When: L expires
        sm.Fire(Trigger.RestTimerExpired);

        // Then: toast is displayed
        Assert.Equal(State.ToastDisplayed, sm.CurrentState);
        Assert.Contains(nameof(IEasyEyesActions.NotifyUser), _actions.Calls);
    }

    [Fact]
    public void Given_TTimerRunning_When_TExpires_Then_OverlayIsDisplayed()
    {
        // Given: T timer is running (initial state)
        var sm = CreateMachine();
        Assert.Equal(State.ActivityTimerRunning, sm.CurrentState);

        // When: T expires
        sm.Fire(Trigger.ActivityTimerExpired);

        // Then: overlay is displayed
        Assert.Equal(State.OverlayDisplayed, sm.CurrentState);
        Assert.Contains(nameof(IEasyEyesActions.ShowOverlay), _actions.Calls);
    }

    [Fact]
    public void Given_TTimerRunning_When_ScreenLocked_Then_TIsSuspendedAndLStarts()
    {
        // Given: T timer is running
        var sm = CreateMachine();
        _actions.Calls.Clear();

        // When: screen is locked
        sm.Fire(Trigger.ScreenLock);

        // Then: T is suspended and L starts
        Assert.Equal(State.RestTimerRunning, sm.CurrentState);
        Assert.Equal(
            new[] { nameof(IEasyEyesActions.SuspendActivityTimer), nameof(IEasyEyesActions.RestartRestTimer) },
            _actions.Calls.ToArray());
    }

    [Fact]
    public void Given_ScreenLockedAndLRunning_When_ScreenUnlocked_Then_TResumesAndLStops()
    {
        // Given: screen is locked and L timer is running
        var sm = CreateMachine();
        sm.Fire(Trigger.ScreenLock);
        _actions.Calls.Clear();

        // When: screen is unlocked
        sm.Fire(Trigger.ScreenUnlock);

        // Then: T resumes and L stops
        Assert.Equal(State.ActivityTimerRunning, sm.CurrentState);
        Assert.Contains(nameof(IEasyEyesActions.ResumeActivityTimer), _actions.Calls);
        Assert.Contains(nameof(IEasyEyesActions.StopRestTimer), _actions.Calls);
    }

    [Fact]
    public void Given_ScreenLockedWithoutOverlay_When_LExpires_Then_NoToastIsDisplayed()
    {
        // Given: screen is locked without overlay having been displayed
        var sm = CreateMachine();
        sm.Fire(Trigger.ScreenLock);
        _actions.Calls.Clear();

        // When: L expires
        sm.Fire(Trigger.RestTimerExpired);

        // Then: no toast is displayed, goes to Idle
        Assert.Equal(State.Idle, sm.CurrentState);
        Assert.DoesNotContain(nameof(IEasyEyesActions.NotifyUser), _actions.Calls);
    }

    // --- Quick lock/unlock scenarios ---

    [Fact]
    public void Given_OverlayDisplayedAndQuickLockUnlock_When_LockedAgainAndLExpires_Then_ToastIsDisplayed()
    {
        // Given: overlay was displayed, then a quick lock/unlock happened (L didn't expire)
        var sm = CreateMachine();
        sm.Fire(Trigger.ActivityTimerExpired);  // overlay displayed
        sm.Fire(Trigger.ScreenLock);
        sm.Fire(Trigger.ScreenUnlock);   // quick unlock before L expires
        _actions.Calls.Clear();

        // When: locked again and L expires
        sm.Fire(Trigger.ScreenLock);
        sm.Fire(Trigger.RestTimerExpired);

        // Then: toast is displayed (overlay flag was preserved)
        Assert.Equal(State.ToastDisplayed, sm.CurrentState);
        Assert.Contains(nameof(IEasyEyesActions.NotifyUser), _actions.Calls);
    }

    // --- PauseUntilUnlock scenarios ---

    [Fact]
    public void Given_PausedUntilUnlock_When_ScreenUnlocked_Then_TTimerResets()
    {
        // Given: paused until unlock
        var sm = CreateMachine();
        sm.Fire(Trigger.PauseUntilUnlock);
        _actions.Calls.Clear();

        // When: screen is unlocked
        sm.Fire(Trigger.ScreenUnlock);

        // Then: T timer resets and resumes
        Assert.Equal(State.ActivityTimerRunning, sm.CurrentState);
        Assert.Contains(nameof(IEasyEyesActions.ResetActivityTimer), _actions.Calls);
        Assert.Contains(nameof(IEasyEyesActions.ResumeActivityTimer), _actions.Calls);
    }

    [Fact]
    public void Given_PausedUntilUnlock_When_ScreenLocked_Then_NothingHappens()
    {
        // Given: paused until unlock
        var sm = CreateMachine();
        sm.Fire(Trigger.PauseUntilUnlock);
        _actions.Calls.Clear();

        // When: screen is locked
        sm.Fire(Trigger.ScreenLock);

        // Then: state unchanged, no actions
        Assert.Equal(State.PausedUntilUnlock, sm.CurrentState);
        Assert.Empty(_actions.Calls);
    }

    // --- PauseForDuration scenarios ---

    [Fact]
    public void Given_T_TimerRunning_When_PauseForDuration_Then_ExtendsTTimerAndResumes()
    {
        // Given: T timer is running
        var sm = CreateMachine();
        _actions.Calls.Clear();

        // When: pause for duration
        sm.FirePauseForDuration(TimeSpan.FromMinutes(30));

        // Then: suspends T, hides overlay, extends T, resumes T
        Assert.Equal(State.ActivityTimerRunning, sm.CurrentState);
        Assert.Equal(
            new[]
            {
                nameof(IEasyEyesActions.SuspendActivityTimer),
                nameof(IEasyEyesActions.HideOverlay),
                nameof(IEasyEyesActions.ExtendActivityTimer),
                nameof(IEasyEyesActions.ResumeActivityTimer),
            },
            _actions.Calls.ToArray());
    }

    [Fact]
    public void Given_OverlayDisplayed_When_PauseForDuration_Then_HidesOverlayAndExtendsTTimer()
    {
        // Given: overlay is displayed
        var sm = CreateMachine();
        sm.Fire(Trigger.ActivityTimerExpired);
        _actions.Calls.Clear();

        // When: pause for duration
        sm.FirePauseForDuration(TimeSpan.FromMinutes(30));

        // Then: overlay is hidden, T is extended, state returns to T_TimerRunning
        Assert.Equal(State.ActivityTimerRunning, sm.CurrentState);
        Assert.Contains(nameof(IEasyEyesActions.HideOverlay), _actions.Calls);
        Assert.Contains(nameof(IEasyEyesActions.ExtendActivityTimer), _actions.Calls);
        Assert.Contains(nameof(IEasyEyesActions.ResumeActivityTimer), _actions.Calls);
    }

    [Fact]
    public void Given_OverlayDisplayedThenPauseForDuration_When_LockedAndLExpires_Then_NoToast()
    {
        // Given: overlay was displayed, then pause for duration (clears overlay flag)
        var sm = CreateMachine();
        sm.Fire(Trigger.ActivityTimerExpired);
        sm.FirePauseForDuration(TimeSpan.FromMinutes(30));

        // When: locked and L expires
        sm.Fire(Trigger.ScreenLock);
        sm.Fire(Trigger.RestTimerExpired);

        // Then: no toast (overlay flag was cleared by pause for duration)
        Assert.Equal(State.Idle, sm.CurrentState);
        Assert.DoesNotContain(nameof(IEasyEyesActions.NotifyUser), _actions.Calls);
    }

    // --- Screen sleep scenarios ---

    [Fact]
    public void Given_OverlayDisplayed_When_ScreenSleeps_Then_OverlayIsHiddenAndLStarts()
    {
        // Given: overlay is displayed
        var sm = CreateMachine();
        sm.Fire(Trigger.ActivityTimerExpired);
        _actions.Calls.Clear();

        // When: screen sleeps
        sm.Fire(Trigger.ScreenSleep);

        // Then: overlay is hidden and L timer starts
        Assert.Equal(State.RestTimerRunning, sm.CurrentState);
        Assert.Contains(nameof(IEasyEyesActions.HideOverlay), _actions.Calls);
        Assert.Contains(nameof(IEasyEyesActions.RestartRestTimer), _actions.Calls);
    }

    [Fact]
    public void Given_OverlayDisplayedAndScreenSlept_When_LExpires_Then_NoToast()
    {
        // Given: overlay was displayed but screen went to sleep
        var sm = CreateMachine();
        sm.Fire(Trigger.ActivityTimerExpired);  // overlay displayed
        sm.Fire(Trigger.ScreenSleep);    // screen sleeps

        // When: L expires
        sm.Fire(Trigger.RestTimerExpired);

        // Then: no toast (sleep doesn't count as a deliberate rest)
        Assert.Equal(State.Idle, sm.CurrentState);
        Assert.DoesNotContain(nameof(IEasyEyesActions.NotifyUser), _actions.Calls);
    }

    [Fact]
    public void Given_ScreenAsleep_When_ScreenWakes_Then_TResumesAndLStops()
    {
        // Given: screen is asleep (L running)
        var sm = CreateMachine();
        sm.Fire(Trigger.ScreenSleep);
        _actions.Calls.Clear();

        // When: screen wakes
        sm.Fire(Trigger.ScreenWake);

        // Then: T resumes and L stops
        Assert.Equal(State.ActivityTimerRunning, sm.CurrentState);
        Assert.Contains(nameof(IEasyEyesActions.ResumeActivityTimer), _actions.Calls);
        Assert.Contains(nameof(IEasyEyesActions.StopRestTimer), _actions.Calls);
    }

    // --- Full cycle scenarios ---

    [Fact]
    public void Given_FreshStart_When_FullRestCycleCompletes_Then_TRestartsFromZero()
    {
        // Given: fresh start
        var sm = CreateMachine();

        // When: T expires → overlay → lock → L expires (rest completed) → unlock
        sm.Fire(Trigger.ActivityTimerExpired);
        sm.Fire(Trigger.ScreenLock);
        sm.Fire(Trigger.RestTimerExpired);
        _actions.Calls.Clear();
        sm.Fire(Trigger.ScreenUnlock);

        // Then: T restarts (was reset during L expiry)
        Assert.Equal(State.ActivityTimerRunning, sm.CurrentState);
        Assert.Contains(nameof(IEasyEyesActions.ResumeActivityTimer), _actions.Calls);
    }

    [Fact]
    public void Given_OverlayDisplayed_When_PauseForXMinutes_Then_OverlayIsHiddenAndTIsSetToX()
    {
        // Given: T has expired and the overlay is displayed
        var sm = CreateMachine();
        sm.Fire(Trigger.ActivityTimerExpired);
        _actions.Calls.Clear();

        // When: user pauses for X minutes
        var pauseDuration = TimeSpan.FromMinutes(15);
        sm.FirePauseForDuration(pauseDuration);

        // Then: overlay is hidden and T is set to X
        Assert.Equal(State.ActivityTimerRunning, sm.CurrentState);
        Assert.Contains(nameof(IEasyEyesActions.HideOverlay), _actions.Calls);
        Assert.Equal(pauseDuration, _actions.LastExtendTTimerDuration);
    }

}

public class EasyEyesActionsTests
{
    [Fact]
    public void ExtendTTimer_AfterTExpired_SetsTRemainingToRequestedDuration()
    {
        // Given: T has expired (simulated by advancing time past tDuration and suspending)
        var tDuration = TimeSpan.FromMinutes(20);
        var fakeTime = new FakeTimeProvider();
        var tScheduler = new FakeTimerScheduler();
        var actions = new EasyEyesActions(
            fakeTime,
            activityScheduler: tScheduler,
            restScheduler: new FakeTimerScheduler(),
            activityDuration: tDuration,
            restDuration: TimeSpan.FromMinutes(5),
            showOverlay: () => { },
            hideOverlay: () => { },
            showToast: () => { },
            fireTrigger: _ => { });

        actions.StartActivityTimer();
        fakeTime.Advance(tDuration + TimeSpan.FromSeconds(1));
        actions.SuspendActivityTimer(); // _tRemaining clamps to Zero

        // When: ExtendTTimer is called with a duration shorter than the original tDuration
        var pauseDuration = TimeSpan.FromMinutes(15);
        actions.ExtendActivityTimer(pauseDuration);

        // Then: T remaining is set to the requested duration, not the original 20 minutes
        Assert.Equal(pauseDuration, actions.GetTRemaining());
    }
}
