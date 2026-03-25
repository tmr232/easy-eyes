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
    Paused,
    PausedUntilUnlock,
    PausedTimed,

    // Superstates (not used directly as current state, but for substate grouping)
    ScreenUnlocked,
    ScreenLocked,
}

public enum Trigger
{
    ScreenLock,
    ScreenUnlock,
    TTimerExpired,
    LTimerExpired,
    Pause,
    Resume,
    PauseUntilUnlock,
    PauseForDuration,
    SnoozeExpired,
}

public interface IEasyEyesActions
{
    void ShowOverlay();
    void HideOverlay();
    void ShowToast();
    void ClearToast();
    void SuspendTTimer();
    void ResumeTTimer();
    void ResetTTimer();
    void RestartLTimer();
    void StopLTimer();
    void StartSnoozeTimer(TimeSpan duration);
    void StopSnoozeTimer();
}

public class EasyEyesStateMachine
{
    private readonly StateMachine<State, Trigger> _machine;
    private readonly StateMachine<State, Trigger>.TriggerWithParameters<TimeSpan> _pauseForDurationTrigger;
    private readonly IEasyEyesActions _actions;
    private bool _wasOverlayDisplayed;

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
            .Permit(Trigger.Pause, State.Paused)
            .Permit(Trigger.PauseUntilUnlock, State.PausedUntilUnlock)
            .Permit(Trigger.PauseForDuration, State.PausedTimed)
            .Ignore(Trigger.ScreenUnlock);

        // Superstate: ScreenLocked
        _machine.Configure(State.ScreenLocked)
            .Permit(Trigger.ScreenUnlock, State.T_TimerRunning)
            .Ignore(Trigger.ScreenLock);

        // ScreenUnlocked substates
        _machine.Configure(State.T_TimerRunning)
            .SubstateOf(State.ScreenUnlocked)
            .Permit(Trigger.TTimerExpired, State.OverlayDisplayed);

        _machine.Configure(State.OverlayDisplayed)
            .SubstateOf(State.ScreenUnlocked)
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
                _actions.SuspendTTimer();
                _actions.RestartLTimer();
            })
            .PermitIf(Trigger.LTimerExpired, State.ToastDisplayed, () => _wasOverlayDisplayed)
            .PermitIf(Trigger.LTimerExpired, State.Idle, () => !_wasOverlayDisplayed);

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

        // T_TimerRunning entry from unlock
        _machine.Configure(State.T_TimerRunning)
            .OnEntryFrom(Trigger.ScreenUnlock, () =>
            {
                _actions.ResumeTTimer();
                _actions.StopLTimer();
                _actions.ClearToast();
            })
            .OnEntryFrom(Trigger.Resume, () =>
            {
                _actions.ResetTTimer();
                _actions.ResumeTTimer();
            })
            .OnEntryFrom(Trigger.SnoozeExpired, () =>
            {
                _actions.ResetTTimer();
                _actions.ResumeTTimer();
            });

        // Pause states
        _machine.Configure(State.Paused)
            .OnEntry(() =>
            {
                _actions.SuspendTTimer();
                _actions.HideOverlay();
                _wasOverlayDisplayed = false;
            })
            .Permit(Trigger.Resume, State.T_TimerRunning)
            .Ignore(Trigger.ScreenLock)
            .Ignore(Trigger.ScreenUnlock)
            .Ignore(Trigger.TTimerExpired)
            .Ignore(Trigger.LTimerExpired);

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
            .Ignore(Trigger.TTimerExpired)
            .Ignore(Trigger.LTimerExpired);

        _machine.Configure(State.PausedTimed)
            .OnEntryFrom(_pauseForDurationTrigger, (duration) =>
            {
                _actions.SuspendTTimer();
                _actions.HideOverlay();
                _wasOverlayDisplayed = false;
                _actions.StartSnoozeTimer(duration);
            })
            .OnExit(() =>
            {
                _actions.StopSnoozeTimer();
            })
            .Permit(Trigger.SnoozeExpired, State.T_TimerRunning)
            .Permit(Trigger.Resume, State.T_TimerRunning)
            .Ignore(Trigger.ScreenLock)
            .Ignore(Trigger.ScreenUnlock)
            .Ignore(Trigger.TTimerExpired)
            .Ignore(Trigger.LTimerExpired);
    }

    public void Fire(Trigger trigger) => _machine.Fire(trigger);

    public void FirePauseForDuration(TimeSpan duration) =>
        _machine.Fire(_pauseForDurationTrigger, duration);
}
