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
- **Paused** — user manually paused; resumes only via explicit Resume
- **PausedUntilUnlock** — paused until next screen unlock (toggleable via tray checkmark)
- **PausedTimed** — paused for a user-specified duration (snooze timer S running)

### Triggers

| Trigger            | Description                                         |
| ------------------ | --------------------------------------------------- |
| `ScreenLock`       | OS session lock detected                            |
| `ScreenUnlock`     | OS session unlock detected                          |
| `TTimerExpired`    | The 20-minute eye-rest timer expired                |
| `LTimerExpired`    | The 20-second lock timer expired                    |
| `Pause`            | User requests manual pause                          |
| `Resume`           | User requests resume (from any paused state)        |
| `PauseUntilUnlock` | User requests pause until next screen unlock        |
| `PauseForDuration` | User requests timed pause (parameterized: TimeSpan) |
| `SnoozeExpired`    | The timed-pause snooze timer expired                |

### Transition Actions

- **ScreenLock**: suspends T timer, restarts L timer
- **ScreenUnlock**: resumes T timer, stops L timer
- **T expires**: shows overlay
- **L expires**: resets T timer, hides overlay; shows toast only if overlay was displayed
- **Pause / PauseUntilUnlock**: suspends T timer, hides overlay, clears overlay flag
- **PauseForDuration**: hides overlay, starts snooze timer S; T timer keeps running
- **Resume** (from Paused/PausedUntilUnlock): resets and resumes T timer
- **Resume / SnoozeExpired** (from PausedTimed): stops snooze timer; if T expired during snooze → shows overlay (transitions to OverlayDisplayed); otherwise → T_TimerRunning (T is still running)
- **ScreenUnlock from PausedUntilUnlock**: resets T (via OnExit), then resumes T

### Tray Menu

The system tray context menu provides:

- **T timer display** — shows remaining time (`T: mm:ss remaining`) or `T: paused`
- **Snooze display** — shows remaining snooze time when in PausedTimed state
- **Pause / Resume** — swaps between the two based on state
- **Pause until unlock** — checkmark toggle; checked when in PausedUntilUnlock
- **Pause for...** — opens a dialog to enter minutes; starts timed pause
- **Cancel snooze** — visible only during PausedTimed; fires Resume
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
