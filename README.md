# Easy Eyes

A Windows desktop app that reminds you to rest your eyes. After 20 minutes of screen use, a gradient overlay appears on all monitors to prompt a break. If you lock your screen briefly (under 20 seconds), a sound reminder plays when you return.

The app lives in the system tray with pause, resume, and snooze controls.

## Features

- **Multi-monitor overlay** — gradient overlay with a spotlight following the cursor and a blue border, displayed across all monitors simultaneously
- **Pause until unlock** — suspends the timer until the next screen unlock
- **Pause for duration** — extends the timer by a custom number of minutes
- **In a meeting** — 3-way toggle (Off → Until end → Always) that defers the overlay while mic/camera is active, with a configurable grace period
- **Do not disturb** — defers the overlay while a video player or game is in the foreground; automatically resumes when you switch away (see [Do Not Disturb](#do-not-disturb) below)
- **Pause media on lock** — automatically pauses playing media when the screen locks or display turns off (enabled by default)

## Architecture

Core logic is a [Stateless](https://github.com/dotnet-state-machine/stateless) state machine (`EasyEyesStateMachine`). All side effects (timers, overlay, sounds) are injected via the `IEasyEyesActions` interface, making the state machine fully testable.

### Key Components

| Component                     | Responsibility                                                                                    |
| ----------------------------- | ------------------------------------------------------------------------------------------------- |
| `EasyEyesStateMachine`        | State machine with states, triggers, and transition actions                                       |
| `EasyEyesActions`             | Implements `IEasyEyesActions`; delegates to `CountdownTimer` instances                            |
| `CountdownTimer`              | Encapsulates timer state and enforces invariants (e.g. remaining time zeroed on expiry)           |
| `TrayIconManager`             | Owns the system tray icon, context menu, and all menu interaction logic                           |
| `OverlayManager`              | Creates/destroys `OverlayWindow` instances to match current monitors                              |
| `OverlayWindow`               | Lightweight WPF window with gradient overlay, spotlight mask, and cursor tracking for one monitor |
| `BusyIndicatorManager`        | Tracks the "In a meeting" indicator; fires `BusyCleared` when indicator clears                    |
| `BusyIndicator`               | Core indicator logic with grace-period handling backed by `IStateSource`                          |
| `ActivationWindowIndicator`   | Wraps `BusyIndicator` with a timed activation window                                              |
| `MediaDeviceMonitor`          | Polls mic/camera usage; implements `IStateSource`                                                 |
| `DndManager`                  | Orchestrates the Do Not Disturb lifecycle (settle timer, foreground capture, busy indicator)      |
| `ForegroundWindowStateSource` | Polls the foreground window process; implements `IForegroundCapture`                              |
| `BorderFlashManager`          | Shows colored border windows on all monitors (persistent or timed flash)                          |
| `BorderFlashWindow`           | Lightweight click-through WPF window displaying a single colored border on one monitor            |
| `SessionNotificationListener` | Listens for session lock/unlock and display on/off events                                         |
| `TriggerRelay`                | Decouples timer expiry callbacks from the state machine (avoids re-entrant fires)                 |
| `MainWindow`                  | Non-visual host wiring the state machine, tray icon, session listener, and overlay manager        |

### States

- **ScreenUnlocked** (superstate)
  - **ActivityTimerRunning** — initial state; the 20-minute eye-rest timer is running
  - **OverlayDisplayed** — timer expired; the overlay is visible
  - **Busy** — overlay deferred while a busy indicator is active
- **ScreenLocked** (superstate)
  - **RestTimerRunning** — the 20-second rest timer is running
  - **ToastDisplayed** — rest timer expired while overlay was showing; user notified
  - **Idle** — rest timer expired while overlay was not showing
- **PausedUntilUnlock** — paused until next screen unlock

### Tray Menu

- **T timer display** — shows remaining time (`T: mm:ss remaining`) or `T: paused`
- **Pause until unlock** — checkmark toggle
- **Pause for...** — opens a dialog to enter minutes
- **In a meeting** — 3-way click-cycle: Off → Until end (auto-disables when meeting ends) → Always (stays active until manually toggled off)
- **Do not disturb** — checkmark toggle; label shows "(arming...)" during settle, then "(ProcessName)" when locked
- **Pause media on lock** — checkmark toggle (enabled by default)
- **Exit** — shuts down the application

### Do Not Disturb

The Do Not Disturb (DND) feature lets you defer the overlay while watching a video or playing a game. It works by tracking which app is in the foreground.

**How it works:**

1. Click **Do not disturb** in the tray menu.
2. An **amber border** appears on all monitors — this is the 10-second settle period.
3. Switch to your video player or game within 10 seconds.
4. A **green border flash** confirms the app is captured. The overlay is now deferred.
5. When you switch away from the captured app for longer than the grace period (45 seconds), a **red border flash** appears and normal overlay behavior resumes.

**Automatic clearing:**

- Locking the screen or the display turning off automatically clears DND. On unlock, a red border flash reminds you that DND is no longer active.
- Briefly alt-tabbing (e.g. to check a message) and returning within the grace period keeps DND active.
- Clicking the tray menu item again immediately deactivates DND.

**Design rationale:** Rather than trying to auto-detect video playback or game activity (which is unreliable — controllers may not register as input, silent movie scenes have no audio, fullscreen detection has false positives), DND uses an intentional opt-in model. The user knows what they're doing, and the foreground-window check reliably detects when they stop.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/) (see `global.json` for the exact version)
- Windows (WPF)

## Build

```
dotnet build
```

## Run Tests

```
dotnet test
```

## Overlay Tester

A standalone app that shows the overlay immediately (with the fade-in animation) for visual testing:

```
dotnet run --project EasyEyes.OverlayTester
```

Press **Esc** to close.

## Publish

```
dotnet publish EasyEyes/EasyEyes.csproj -p:PublishProfile=Release
```

This produces a single-file, self-contained executable in the `publish/` directory.

## Release

Releases are automated via GitHub Actions. To publish a new version:

1. Update `CHANGELOG.md`: move items from `[Unreleased]` into a new `[x.y.z] - YYYY-MM-DD` section.
2. Commit and push.
3. Tag and push: `git tag vx.y.z && git push --tags`.

The workflow builds, tests, and creates a GitHub Release with `EasyEyes.exe` attached and release notes extracted from the changelog.
