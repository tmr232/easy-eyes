using Stateless;

namespace EasyEyes;

public enum State
{
    // ScreenUnlocked substates
    T_TimerRunning,
    OverlayDisplayed,

    // ScreenLocked substates
    L_TimerRunning,
    ToastDisplayed,
    Idle,

    // Pause states (top-level, not substates)
    PausedUntilUnlock,

    // Superstates (not used directly as current state, but for substate grouping)
    ScreenUnlocked,
    ScreenLocked,
}

public enum Trigger
{
    ScreenLock,
    ScreenUnlock,
    ScreenSleep,
    ScreenWake,
    TTimerExpired,
    LTimerExpired,
    Resume,
    PauseUntilUnlock,
    PauseForDuration,
}

public interface IEasyEyesActions
{
    void ShowOverlay();
    void HideOverlay();
    void ShowToast();

    void SuspendTTimer();
    void ResumeTTimer();
    void ResetTTimer();
    void RestartLTimer();
    void StopLTimer();
    void ExtendTTimer(TimeSpan duration);
}

public class EasyEyesStateMachine
{
    private readonly StateMachine<State, Trigger> _machine;
    private readonly StateMachine<State, Trigger>.TriggerWithParameters<TimeSpan> _pauseForDurationTrigger;
    private readonly IEasyEyesActions _actions;
    private bool _wasOverlayDisplayed;
    private bool _wasScreenSleep;

    public State CurrentState => _machine.State;

    public bool IsInState(State state) => _machine.IsInState(state);

    public EasyEyesStateMachine(IEasyEyesActions actions)
    {
        _actions = actions;
        _machine = new StateMachine<State, Trigger>(State.T_TimerRunning);
        _pauseForDurationTrigger = _machine.SetTriggerParameters<TimeSpan>(Trigger.PauseForDuration);

        ConfigureStates();
    }

    private void ConfigureStates()
    {
        // Superstate: ScreenUnlocked
        _machine.Configure(State.ScreenUnlocked)
            .Permit(Trigger.ScreenLock, State.L_TimerRunning)
            .Permit(Trigger.ScreenSleep, State.L_TimerRunning)
            .Permit(Trigger.PauseUntilUnlock, State.PausedUntilUnlock)
            .Ignore(Trigger.ScreenUnlock)
            .Ignore(Trigger.ScreenWake);

        // Superstate: ScreenLocked
        _machine.Configure(State.ScreenLocked)
            .Permit(Trigger.ScreenUnlock, State.T_TimerRunning)
            .Permit(Trigger.ScreenWake, State.T_TimerRunning)
            .Ignore(Trigger.ScreenLock)
            .Ignore(Trigger.ScreenSleep);

        // ScreenUnlocked substates
        _machine.Configure(State.T_TimerRunning)
            .SubstateOf(State.ScreenUnlocked)
            .Permit(Trigger.TTimerExpired, State.OverlayDisplayed)
            .PermitReentry(Trigger.PauseForDuration);

        _machine.Configure(State.OverlayDisplayed)
            .SubstateOf(State.ScreenUnlocked)
            .Permit(Trigger.PauseForDuration, State.T_TimerRunning)
            .OnEntry(() =>
            {
                _wasOverlayDisplayed = true;
                _actions.ShowOverlay();
            });

        // ScreenLocked substates
        _machine.Configure(State.L_TimerRunning)
            .SubstateOf(State.ScreenLocked)
            .OnEntryFrom(Trigger.ScreenLock, () =>
            {
                _wasScreenSleep = false;
                _actions.SuspendTTimer();
                _actions.RestartLTimer();
            })
            .OnEntryFrom(Trigger.ScreenSleep, () =>
            {
                _wasScreenSleep = true;
                _actions.SuspendTTimer();
                _actions.RestartLTimer();
                _actions.HideOverlay();
            })
            .PermitIf(Trigger.LTimerExpired, State.ToastDisplayed, () => _wasOverlayDisplayed && !_wasScreenSleep)
            .PermitIf(Trigger.LTimerExpired, State.Idle, () => !_wasOverlayDisplayed || _wasScreenSleep);

        _machine.Configure(State.ToastDisplayed)
            .SubstateOf(State.ScreenLocked)
            .OnEntry(() =>
            {
                _actions.ResetTTimer();
                _actions.HideOverlay();
                _wasOverlayDisplayed = false;
                _actions.ShowToast();
            });

        _machine.Configure(State.Idle)
            .SubstateOf(State.ScreenLocked)
            .OnEntry(() =>
            {
                _actions.ResetTTimer();
                _actions.HideOverlay();
                _wasOverlayDisplayed = false;
            });

        // T_TimerRunning entry from various triggers
        _machine.Configure(State.T_TimerRunning)
            .OnEntryFrom(Trigger.ScreenUnlock, () =>
            {
                _actions.ResumeTTimer();
                _actions.StopLTimer();
            })
            .OnEntryFrom(Trigger.ScreenWake, () =>
            {
                _actions.ResumeTTimer();
                _actions.StopLTimer();
            })
            .OnEntryFrom(Trigger.Resume, () =>
            {
                _actions.ResumeTTimer();
            })
            .OnEntryFrom(_pauseForDurationTrigger, (duration) =>
            {
                _actions.SuspendTTimer();
                _actions.HideOverlay();
                _wasOverlayDisplayed = false;
                _actions.ExtendTTimer(duration);
                _actions.ResumeTTimer();
            });

        // PausedUntilUnlock
        _machine.Configure(State.PausedUntilUnlock)
            .OnEntry(() =>
            {
                _actions.SuspendTTimer();
                _actions.HideOverlay();
                _wasOverlayDisplayed = false;
            })
            .OnExit(() =>
            {
                _actions.ResetTTimer();
            })
            .Permit(Trigger.ScreenUnlock, State.T_TimerRunning)
            .Permit(Trigger.Resume, State.T_TimerRunning)
            .Ignore(Trigger.ScreenLock)
            .Ignore(Trigger.ScreenSleep)
            .Ignore(Trigger.ScreenWake)
            .Ignore(Trigger.TTimerExpired)
            .Ignore(Trigger.LTimerExpired);
    }

    public void Fire(Trigger trigger) => _machine.Fire(trigger);

    public void FirePauseForDuration(TimeSpan duration) =>
        _machine.Fire(_pauseForDurationTrigger, duration);
}
