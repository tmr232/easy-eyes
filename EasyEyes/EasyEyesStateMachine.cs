using Stateless;

namespace EasyEyes;

public enum State
{
    // ScreenUnlocked substates
    ActivityTimerRunning,
    OverlayDisplayed,
    Busy,

    // ScreenLocked substates
    RestTimerRunning,
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
    ActivityTimerExpired,
    RestTimerExpired,
    Resume,
    PauseUntilUnlock,
    PauseForDuration,
    BusyCleared,
    EnterBusy,
}

public interface IEasyEyesActions
{
    void ShowOverlay();
    void HideOverlay();
    void NotifyUser();

    void SuspendActivityTimer();
    void ResumeActivityTimer();
    void ResetActivityTimer();
    void RestartRestTimer();
    void StopRestTimer();
    void ExtendActivityTimer(TimeSpan duration);

    TimeSpan GetTRemaining();
    void StartActivityTimer();
}

public class EasyEyesStateMachine
{
    private readonly StateMachine<State, Trigger> _machine;
    private readonly StateMachine<State, Trigger>.TriggerWithParameters<TimeSpan> _pauseForDurationTrigger;
    private readonly IEasyEyesActions _actions;
    private readonly Func<bool> _isBusy;
    private bool _wasOverlayDisplayed;
    private bool _wasScreenSleep;

    public State CurrentState => _machine.State;

    public bool IsInState(State state) => _machine.IsInState(state);

    public EasyEyesStateMachine(IEasyEyesActions actions, Func<bool>? isBusy = null)
    {
        _actions = actions;
        _isBusy = isBusy ?? (() => false);
        _machine = new StateMachine<State, Trigger>(State.ActivityTimerRunning);
        _pauseForDurationTrigger = _machine.SetTriggerParameters<TimeSpan>(Trigger.PauseForDuration);

        ConfigureStates();
    }

    private void ConfigureStates()
    {
        // Superstate: ScreenUnlocked
        _machine.Configure(State.ScreenUnlocked)
            .Permit(Trigger.ScreenLock, State.RestTimerRunning)
            .Permit(Trigger.ScreenSleep, State.RestTimerRunning)
            .Permit(Trigger.PauseUntilUnlock, State.PausedUntilUnlock)
            .Ignore(Trigger.ScreenUnlock)
            .Ignore(Trigger.ScreenWake);

        // Superstate: ScreenLocked
        _machine.Configure(State.ScreenLocked)
            .Permit(Trigger.ScreenUnlock, State.ActivityTimerRunning)
            .Permit(Trigger.ScreenWake, State.ActivityTimerRunning)
            .Ignore(Trigger.ScreenLock)
            .Ignore(Trigger.ScreenSleep);

        // ScreenUnlocked substates
        _machine.Configure(State.ActivityTimerRunning)
            .SubstateOf(State.ScreenUnlocked)
            .PermitIf(Trigger.ActivityTimerExpired, State.Busy, () => _isBusy())
            .PermitIf(Trigger.ActivityTimerExpired, State.OverlayDisplayed, () => !_isBusy())
            .PermitReentry(Trigger.PauseForDuration);

        _machine.Configure(State.OverlayDisplayed)
            .SubstateOf(State.ScreenUnlocked)
            .Permit(Trigger.PauseForDuration, State.ActivityTimerRunning)
            .Permit(Trigger.EnterBusy, State.Busy)
            .OnEntry(() =>
            {
                _wasOverlayDisplayed = true;
                _actions.ShowOverlay();
            });

        _machine.Configure(State.Busy)
            .SubstateOf(State.ScreenUnlocked)
            .Permit(Trigger.BusyCleared, State.OverlayDisplayed)
            .Permit(Trigger.PauseForDuration, State.ActivityTimerRunning)
            .OnEntryFrom(Trigger.EnterBusy, () =>
            {
                _actions.HideOverlay();
            });

        // ScreenLocked substates
        _machine.Configure(State.RestTimerRunning)
            .SubstateOf(State.ScreenLocked)
            .OnEntryFrom(Trigger.ScreenLock, () =>
            {
                _wasScreenSleep = false;
                _actions.SuspendActivityTimer();
                _actions.RestartRestTimer();
            })
            .OnEntryFrom(Trigger.ScreenSleep, () =>
            {
                _wasScreenSleep = true;
                _actions.SuspendActivityTimer();
                _actions.RestartRestTimer();
                _actions.HideOverlay();
            })
            .PermitIf(Trigger.RestTimerExpired, State.ToastDisplayed, () => _wasOverlayDisplayed && !_wasScreenSleep)
            .PermitIf(Trigger.RestTimerExpired, State.Idle, () => !_wasOverlayDisplayed || _wasScreenSleep);

        _machine.Configure(State.ToastDisplayed)
            .SubstateOf(State.ScreenLocked)
            .OnEntry(() =>
            {
                _actions.ResetActivityTimer();
                _actions.HideOverlay();
                _wasOverlayDisplayed = false;
                _actions.NotifyUser();
            });

        _machine.Configure(State.Idle)
            .SubstateOf(State.ScreenLocked)
            .OnEntry(() =>
            {
                _actions.ResetActivityTimer();
                _actions.HideOverlay();
                _wasOverlayDisplayed = false;
            });

        // T_TimerRunning entry from various triggers
        _machine.Configure(State.ActivityTimerRunning)
            .OnEntryFrom(Trigger.ScreenUnlock, () =>
            {
                _actions.ResumeActivityTimer();
                _actions.StopRestTimer();
            })
            .OnEntryFrom(Trigger.ScreenWake, () =>
            {
                _actions.ResumeActivityTimer();
                _actions.StopRestTimer();
            })
            .OnEntryFrom(Trigger.Resume, () =>
            {
                _actions.ResumeActivityTimer();
            })
            .OnEntryFrom(_pauseForDurationTrigger, (duration) =>
            {
                _actions.SuspendActivityTimer();
                _actions.HideOverlay();
                _wasOverlayDisplayed = false;
                _actions.ExtendActivityTimer(duration);
                _actions.ResumeActivityTimer();
            });

        // PausedUntilUnlock
        _machine.Configure(State.PausedUntilUnlock)
            .OnEntry(() =>
            {
                _actions.SuspendActivityTimer();
                _actions.HideOverlay();
                _wasOverlayDisplayed = false;
            })
            .OnExit(() =>
            {
                _actions.ResetActivityTimer();
            })
            .Permit(Trigger.ScreenUnlock, State.ActivityTimerRunning)
            .Permit(Trigger.Resume, State.ActivityTimerRunning)
            .Ignore(Trigger.ScreenLock)
            .Ignore(Trigger.ScreenSleep)
            .Ignore(Trigger.ScreenWake)
            .Ignore(Trigger.ActivityTimerExpired)
            .Ignore(Trigger.RestTimerExpired);
    }

    public void Fire(Trigger trigger) => _machine.Fire(trigger);

    public void FirePauseForDuration(TimeSpan duration) =>
        _machine.Fire(_pauseForDurationTrigger, duration);
}
