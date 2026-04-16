# Easy Eyes

## Overview

Easy Eyes is a WPF app that reminds you to rest your eyes by displaying a
gradient overlay after a period of screen use (timer T = 20 minutes). When the
screen is locked, a shorter timer (L = 20 seconds) determines whether a toast
notification is shown on return.

## State Machine

The core logic is implemented as a state machine (`EasyEyesStateMachine`) using
the [Stateless](https://github.com/dotnet-state-machine/stateless) library.
All side effects (timers, overlay, toast) are injected via the
`IEasyEyesActions` interface, making the state machine fully testable.

### States

- **ScreenUnlocked** (superstate)
  - **T_TimerRunning** — initial state; the 20-minute eye-rest timer is running
  - **OverlayDisplayed** — T expired; the overlay is visible on screen
- **ScreenLocked** (superstate)
  - **L_TimerRunning** — initial substate; the 20-second lock timer is running
  - **ToastDisplayed** — L expired while overlay was showing; toast was shown
  - **Idle** — L expired while overlay was not showing; no toast
- **PausedUntilUnlock** — paused until next screen unlock (toggleable via tray checkmark)

### Triggers

| Trigger            | Description                                                                                                 |
| ------------------ | ----------------------------------------------------------------------------------------------------------- |
| `ScreenLock`       | OS session lock detected                                                                                    |
| `ScreenUnlock`     | OS session unlock detected                                                                                  |
| `TTimerExpired`    | The 20-minute eye-rest timer expired                                                                        |
| `LTimerExpired`    | The 20-second lock timer expired                                                                            |
| `Resume`           | User requests resume (from PausedUntilUnlock)                                                               |
| `PauseUntilUnlock` | User requests pause until next screen unlock                                                                |
| `PauseForDuration` | User requests timed extension (parameterized: TimeSpan); extends T.remaining to at least the given duration |
| `BusyCleared`      | All busy indicators cleared; transitions Busy → OverlayDisplayed                                            |
| `EnterBusy`        | User enables meeting indicator while overlay is displayed; transitions OverlayDisplayed → Busy              |

### Transition Actions

- **ScreenLock**: suspends T timer, restarts L timer
- **ScreenUnlock**: resumes T timer, stops L timer
- **T expires**: shows overlay
- **L expires**: resets T timer, hides overlay; shows toast only if overlay was displayed
- **PauseUntilUnlock**: suspends T timer, hides overlay, clears overlay flag
- **PauseForDuration**: suspends T timer, hides overlay, clears overlay flag, sets `T.remaining = max(duration, T.remaining)`, resumes T timer (returns to T_TimerRunning)
- **Resume** (from PausedUntilUnlock): resets T (via OnExit), then resumes T timer
- **ScreenUnlock from PausedUntilUnlock**: resets T (via OnExit), then resumes T
- **EnterBusy** (from OverlayDisplayed): hides overlay but preserves the overlay-displayed flag and does not reset the timer

### Tray Menu

The system tray context menu provides:

- **T timer display** — shows remaining time (`T: mm:ss remaining`) or `T: paused`
- **Pause until unlock** — checkmark toggle; checked when in PausedUntilUnlock
- **Pause for...** — opens a dialog to enter minutes; extends T.remaining to at least that duration
- **In a meeting** — 3-way toggle (click cycles): Off → Until end → Always → Off
  - **Off** — indicator disabled
  - **Until end** — indicator enabled; auto-disables when mic/camera stops (after grace period)
  - **Always** — indicator stays enabled permanently; must be toggled off manually
- **Exit** — shuts down the application

Menu items are shown/hidden dynamically via the `ContextMenuStrip.Opening` event.

## Original Design Notes

When the app starts, it will start timer T.

When timer T expires, the gradient overlay will appear.
Starting fully transparent, and slowly growing more opaque.

When the screen is locked, timer L will start.
When the screen is unlocked, timer L resets and stops.
When timer L expires, timer T is reset and stopped.
When timer L expires, a notification toast is shown.

When the screen is unlocked, if timer T is reset and stopped, it will start.

Timer T (running/)
Timer L
Screen Overlay
Notification
Lock

T = new Timer(20 minutes)
L = new Timer(20 seconds)
is_displayed = False

// Timer.start -> makes the timer tick, does not reset the current time
// Timer.stop -> makes the timer stop ticking, does not change current time
// Timer.reset -> resets the timer time, does not make it tick

on_display_overlay {
is_displayed = True
}

on_hide_overlay {
is_displayed = False
}

T.on_expiry {
display_overlay()
}

L.on_expiry {
if is_displayed {
display_toast()
}
T.reset()
hide_overlay()
}

on_screen_lock {
T.stop()
L.reset()
L.start()
}

on_screen_unlock {
T.start()
L.stop()
}

T.start()

# TODO:

Ensure the overlay window _never_ gets focus.
