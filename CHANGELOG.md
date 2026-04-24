# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/).
This project uses [Calendar Versioning](https://calver.org/) (`YYYY.MM.DD`).

## [Unreleased]

### Added

- Add **Do not disturb** mode — defers the overlay while a video player or game is in the foreground. Activated via the tray menu with a 10-second settle period. An amber border shows during arming, a green flash confirms capture, and a red flash indicates when DND clears (either by switching away, locking the screen, or manually toggling off). Uses a 45-second grace period so brief alt-tabs don't interrupt.

## [2026.04.22]

### Fixed

- Hide EasyEyes windows from the Alt-Tab switcher: overlay windows are now created on show and destroyed on hide instead of persisting invisibly, and the main window applies `WS_EX_TOOLWINDOW`
- Fix `CountdownTimer.Resume()` restarting the timer from scratch when called while already running instead of being a no-op
- Fix `BusyIndicatorManager.DisableMeeting()` potentially double-firing `BusyCleared` by moving the `Cleared` event into `BusyIndicator.Disable()` for active→inactive transitions
- Fix `SessionNotificationListener` leaking a WTS session registration when `RegisterPowerSettingNotification` fails in the constructor
- Fix `OverlayManager.ShowAll()` leaking old overlay windows when called while windows are already visible
- Fix overlay window positioning on displays with >100% DPI scaling by converting `Screen.Bounds` physical pixels to WPF device-independent units
- Fix `TrayIconManager` leaking icon stream, `Icon`, and `ContextMenuStrip` on dispose
- Fix `TrayIconManager` double-disposing `NotifyIcon` when exiting via the tray menu
- Fix data race on `MediaDeviceMonitor._lastInUse` accessed from ThreadPool timer callbacks without synchronization
- Fix `CountdownTimer.Extend()` having no effect while the timer is running; it now reschedules the callback with the extended duration
- Route unhandled dispatcher exceptions through `FatalError` (log + dialog + shutdown) instead of silently swallowing them

### Changed

- Simplify meeting detection to a 2-state on/off toggle ("Detect meetings"), replacing the 3-way Off/UntilEnd/Always cycle
- Meeting detection defaults to on; when enabled, device usage (mic/camera) immediately triggers the busy state with no activation window delay
- When the overlay is displayed and device usage is detected, automatically enter the busy state and hide the overlay
- Grace period preserved: brief gaps in device usage don't flicker the busy state
- Seal `OverlayManager`, `BusyIndicatorManager`, `TrayIconManager`, and `EasyEyesActions` (not designed for inheritance; fixes `GC.SuppressFinalize` correctness)

### Removed

- Remove unused `MediaPlaybackMonitor` class (dead code; recoverable from git history)

- Remove `ActivationWindowIndicator` and its activation window timer — no longer needed without the delayed-activation behavior

- Remove `Persistent` mode from `BusyIndicator` — the indicator now always stays enabled after grace expiry

- Remove `MeetingMode.UntilEnd` and `MeetingMode.Always` — replaced by simple `MeetingMode.On`

## [2026.04.18]

### Changed

- Keep tray menu open when clicking toggle items (pause until unlock, in a meeting, pause media on lock) so they can be toggled multiple times without reopening the menu
- Extract `TrayIconManager` from `MainWindow` to own the system tray icon, context menu, and all menu interaction logic (meeting mode cycling, pause controls, timer display)
- Implement `IDisposable` on `BusyIndicatorManager` to properly clean up owned indicator resources (timers, event subscriptions) on shutdown
- Add `GetTRemaining()` and `StartActivityTimer()` to `IEasyEyesActions` interface; update `TrayIconManager` to depend on the interface instead of the concrete `EasyEyesActions`

### Added

- Add solid blue border around each screen to the overlay
- Add `EasyEyes.OverlayTester` project — a standalone app that shows the overlay with fade-in animation; press Esc to close
- Add `MediaPlaybackMonitor` for detecting system media playback via Windows SMTC API
- Add `EnterBusy` trigger: enabling "In a meeting" while the overlay is displayed now hides the overlay and enters the Busy state without resetting the timer
- Add 3-way "In a meeting" toggle: Off → Until end (auto-disables when meeting ends) → Always (stays active until manually toggled off)
- Add `Persistent` mode to `BusyIndicator`: when persistent, the indicator stays enabled after grace expiry and re-activates when mic/camera becomes active again
- Add `MeetingMode` enum and `SetMeetingMode()` API on `BusyIndicatorManager`

### Removed

- Remove stale plan files (`PLAN.md`, `PLAN-busy-state.md`, `PLAN-multi-monitor.md`); architecture now documented in `README.md`

### Fixed

- Fix overlay showing while busy indicator ("in a meeting") is active by integrating `Busy` state into the state machine

## [2026.04.02]

### Fixed

- Fix pause-for-duration using original timer duration (e.g. 20 min) instead of the requested pause duration when T timer has already expired

### Changed

- Extract `CountdownTimer` class to encapsulate timer state and enforce invariants (e.g. remaining time zeroed on expiry)
- Introduce `ITimerScheduler` interface to decouple timer scheduling from WPF's `DispatcherTimer`, enabling unit testing
- Refactor `EasyEyesActions` to delegate to `CountdownTimer` instead of managing timer state manually
- Replace toast notifications with a system sound (Asterisk) for break reminders

### Removed

- Remove `Microsoft.Toolkit.Uwp.Notifications` dependency
- Remove `ClearToast` from state machine, actions interface, and implementation (no longer needed with sound-based reminders)
