# Easy Eyes

A Windows desktop app that reminds you to rest your eyes. After 20 minutes of screen use, a gradient overlay appears on all monitors to prompt a break. If you lock your screen briefly (under 20 seconds), a sound reminder plays when you return.

The app lives in the system tray with pause, resume, and snooze controls.

## Features

- **Multi-monitor overlay** — gradient overlay with a spotlight following the cursor, displayed across all monitors simultaneously
- **Pause until unlock** — suspends the timer until the next screen unlock
- **Pause for duration** — extends the timer by a custom number of minutes
- **In a meeting** — 3-way toggle (Off → Until end → Always) that defers the overlay while mic/camera is active, with a configurable grace period
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
- **Pause media on lock** — checkmark toggle (enabled by default)
- **Exit** — shuts down the application

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

## Publish

```
dotnet publish EasyEyes/EasyEyes.csproj --configuration Release --output ./publish
```

This produces a single-file, self-contained executable in the `publish/` directory.

## Release

Releases are automated via GitHub Actions. To publish a new version:

1. Update `CHANGELOG.md`: move items from `[Unreleased]` into a new `[x.y.z] - YYYY-MM-DD` section.
2. Commit and push.
3. Tag and push: `git tag vx.y.z && git push --tags`.

The workflow builds, tests, and creates a GitHub Release with `EasyEyes.exe` attached and release notes extracted from the changelog.
