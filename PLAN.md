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

### Triggers

| Trigger         | Description                          |
| --------------- | ------------------------------------ |
| `ScreenLock`    | OS session lock detected             |
| `ScreenUnlock`  | OS session unlock detected           |
| `TTimerExpired` | The 20-minute eye-rest timer expired |
| `LTimerExpired` | The 20-second lock timer expired     |

### Transition Actions

- **ScreenLock**: suspends T timer, restarts L timer
- **ScreenUnlock**: resumes T timer, stops L timer
- **T expires**: shows overlay
- **L expires**: resets T timer, hides overlay; shows toast only if overlay was displayed

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
