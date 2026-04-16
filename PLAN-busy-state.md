# Busy State Plan

## Overview

A new **Busy** state defers the overlay when the user is actively engaged
(in a call, watching media, etc.). Busy indicators are one-shot and opt-in:
the user enables them via the tray menu when they start an activity, and
they auto-disable when the activity ends.

## Requirements

1. **Busy = overlay deferred.** If the activity timer expires while any
   enabled indicator is active, enter `Busy` instead of `OverlayDisplayed`.
   Show the overlay once all active indicators clear.
2. **Screen lock exits busy.** Transitioning to `ScreenLocked` from `Busy`
   proceeds normally (→ `RestTimerRunning`), as if busy never happened.
3. **Indicators are one-shot and opt-in.** Each indicator is off by default.
   The user enables it via a tray menu checkbox. Once the activity ends
   (mic off, media stops), the indicator auto-disables (unchecks itself).
   The user must re-enable it each time.
4. **Grace periods.** When an activity briefly stops, a short grace period
   prevents the indicator from auto-disabling immediately.

### Indicator: Mic/Camera

- When enabled, if mic or camera is active at timer expiry → busy.
- When mic/camera turns off, start a grace period (~5 seconds).
  If it comes back on within the grace period, the indicator stays active.
  If it stays off, the indicator auto-disables.

### Indicator: Media Playing

- When enabled, the user has a 30-second window to start playing media.
  If no media starts within 30 seconds, the indicator auto-disables.
- Once media is playing, the media identity (title/artist from SMTC) is
  recorded.
- When playback pauses/stops, a grace period starts.
  - If the **same** media resumes → indicator stays active.
  - If a **different** media starts → indicator auto-disables immediately.
  - If nothing resumes within the grace period → indicator auto-disables.

## Implementation Order

1. Mic/Camera indicator (Phase 1)
2. Media Playing indicator (Phase 2)

______________________________________________________________________

## Phase 1: Mic/Camera Indicator

### State Machine Changes

| Change                                            | Detail                                                                                                                   |
| ------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------ |
| New state `Busy`                                  | Substate of `ScreenUnlocked`                                                                                             |
| `ActivityTimerExpired`                            | Conditional: → `Busy` if busy, → `OverlayDisplayed` otherwise                                                            |
| New trigger `BusyCleared`                         | Transitions `Busy` → `OverlayDisplayed`                                                                                  |
| `ScreenLock`/`ScreenSleep` from `Busy`            | Handled by `ScreenUnlocked` superstate → `RestTimerRunning`                                                              |
| `PauseUntilUnlock`/`PauseForDuration` from `Busy` | Same as from `OverlayDisplayed`                                                                                          |
| New trigger `EnterBusy`                           | Transitions `OverlayDisplayed` → `Busy`; hides overlay but preserves overlay-displayed flag and does not reset the timer |

### New Component: `BusyIndicatorManager`

- Tracks which indicators are enabled and whether their conditions are met.
- `bool IsBusy` — true if any enabled indicator is currently active.
- `event BusyCleared` — fires when `IsBusy` transitions from true → false.
- For mic/camera: subscribes to `MediaDeviceMonitor` events, manages the
  grace period timer. When mic/camera turns off, starts grace timer. If it
  comes back on, cancels the timer. If the timer expires, auto-disables the
  indicator and fires `BusyCleared` if no other indicators are active.

### Tray Menu Changes

The "In a meeting" tray menu item is a 3-way click-cycle toggle:

- **Off** — `In a meeting` (no checkmark). Indicator disabled.
- **Until end** — `In a meeting (until end)` ✓. Indicator enabled;
  auto-disables when mic/camera stops after grace period.
- **Always** — `In a meeting (always)` ✓. Indicator stays enabled
  permanently; must be toggled off manually. No activation window.

Clicking cycles Off → Until end → Always → Off.

(Media playing indicator added in Phase 2.)

### Integration in `MainWindow`

- Create `BusyIndicatorManager`, wire to existing `MediaDeviceMonitor`.
- Wire `BusyCleared` event → fire `Trigger.BusyCleared` on the state
  machine (dispatched to UI thread).
- Pass `busyIndicatorManager.IsBusy` as the guard for the conditional
  `ActivityTimerExpired` transition (injected via `IEasyEyesActions` or
  passed as a `Func<bool>` to the state machine).

### File Changes

| File                               | Change                                                                 |
| ---------------------------------- | ---------------------------------------------------------------------- |
| `EasyEyesStateMachine.cs`          | Add `State.Busy`, `Trigger.BusyCleared`, conditional transition, guard |
| New `BusyIndicatorManager.cs`      | Indicator tracking, `IsBusy`, `BusyCleared` event, grace period        |
| `MainWindow.xaml.cs`               | Create manager, wire events, add tray menu items                       |
| `EasyEyesStateMachineTests.cs`     | Tests for busy transitions                                             |
| New `BusyIndicatorManagerTests.cs` | Tests for indicator logic and grace period                             |

______________________________________________________________________

## Phase 2: Media Playing Indicator

### `BusyIndicatorManager` Changes

- Add media-playing indicator with 30-second activation window.
- Track media identity (title/artist) from `MediaPlaybackMonitor` when
  indicator is enabled and media starts.
- On playback stop: start grace period. On resume: compare media identity.
  Same media → stay busy. Different media → auto-disable immediately.

### `MediaPlaybackMonitor` Changes

- Expose current media identity (title, artist) in addition to
  playing/stopped state.
- Include identity info in `PlaybackStarted`/`PlaybackStopped` events
  (or provide a method to query it).

### Tray Menu Changes

Add to the busy indicators section:

- ☐ Media playing

### File Changes

| File                           | Change                                                       |
| ------------------------------ | ------------------------------------------------------------ |
| `MediaPlaybackMonitor.cs`      | Expose media identity                                        |
| `BusyIndicatorManager.cs`      | Media indicator logic, identity tracking, activation window  |
| `MainWindow.xaml.cs`           | Wire media indicator, add tray menu item                     |
| `BusyIndicatorManagerTests.cs` | Tests for media indicator, identity comparison, grace period |

______________________________________________________________________

## Test Plan

### State Machine Tests

- `ActivityTimerExpired` → `Busy` when guard returns true
- `ActivityTimerExpired` → `OverlayDisplayed` when guard returns false
- `BusyCleared` from `Busy` → `OverlayDisplayed`
- `ScreenLock` from `Busy` → `RestTimerRunning` (normal behavior)
- `PauseUntilUnlock` from `Busy` → `PausedUntilUnlock`
- `PauseForDuration` from `Busy` → `ActivityTimerRunning`

### BusyIndicatorManager Tests

- Enable mic indicator while mic active → `IsBusy` true
- Mic turns off → grace period → auto-disable → `BusyCleared` fires
- Mic turns off and back on within grace period → stays active
- Enable mic indicator while mic off → `IsBusy` false (but indicator stays
  enabled, waiting for mic to activate)
- (Phase 2) Enable media indicator → 30s window → no media → auto-disable
- (Phase 2) Media plays, then different media starts → auto-disable
- (Phase 2) Media pauses, same media resumes → stays active
