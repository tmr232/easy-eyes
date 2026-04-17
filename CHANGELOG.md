# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/).
This project uses [Calendar Versioning](https://calver.org/) (`YYYY.MM.DD`).

## [Unreleased]

### Changed

- Extract `TrayIconManager` from `MainWindow` to own the system tray icon, context menu, and all menu interaction logic (meeting mode cycling, pause controls, timer display)
- Implement `IDisposable` on `BusyIndicatorManager` to properly clean up owned indicator resources (timers, event subscriptions) on shutdown
- Add `GetTRemaining()` and `StartActivityTimer()` to `IEasyEyesActions` interface; update `TrayIconManager` to depend on the interface instead of the concrete `EasyEyesActions`

### Added

- Add `MediaPlaybackMonitor` for detecting system media playback via Windows SMTC API
- Add `EnterBusy` trigger: enabling "In a meeting" while the overlay is displayed now hides the overlay and enters the Busy state without resetting the timer
- Add 3-way "In a meeting" toggle: Off → Until end (auto-disables when meeting ends) → Always (stays active until manually toggled off)
- Add `Persistent` mode to `BusyIndicator`: when persistent, the indicator stays enabled after grace expiry and re-activates when mic/camera becomes active again
- Add `MeetingMode` enum and `SetMeetingMode()` API on `BusyIndicatorManager`

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
