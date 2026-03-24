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
}

public class EasyEyesStateMachine
{
    private readonly StateMachine<State, Trigger> _machine;
    private readonly IEasyEyesActions _actions;
    private bool _wasOverlayDisplayed;

    public State CurrentState => _machine.State;

    public bool IsInState(State state) => _machine.IsInState(state);

    public EasyEyesStateMachine(IEasyEyesActions actions)
    {
        _actions = actions;
        _machine = new StateMachine<State, Trigger>(State.T_TimerRunning);

        ConfigureStates();
    }

    private void ConfigureStates()
    {
        // Superstate: ScreenUnlocked
        _machine.Configure(State.ScreenUnlocked)
            .Permit(Trigger.ScreenLock, State.L_TimerRunning);

        // Superstate: ScreenLocked
        _machine.Configure(State.ScreenLocked)
            .Permit(Trigger.ScreenUnlock, State.T_TimerRunning);

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
            });
    }

    public void Fire(Trigger trigger) => _machine.Fire(trigger);
}
