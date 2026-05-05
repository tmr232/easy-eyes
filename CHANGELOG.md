# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/).
This project uses [Calendar Versioning](https://calver.org/) (`YYYY.MM.DD`).

## [Unreleased]

### Added

- Exit Do Not Disturb immediately when the captured process terminates instead of waiting out the full grace period. There is no possibility of the user "coming back" to a process that no longer exists, so DND now plays the standard red bloom-and-fade and clears at once. Built on a new `IProcessLifetimeWatcher` abstraction with a Win32 implementation that uses `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | SYNCHRONIZE)` plus `ThreadPool.RegisterWaitForSingleObject` for a kernel-signalled wait — no extra polling. If `OpenProcess` fails (process already gone, access denied, packaged-app restrictions) the watcher silently degrades and DND falls back to the existing grace-period behavior.

### Fixed

- Fix the Do Not Disturb red border being stuck on screen forever after the user left the captured app (grace hint showing) and then closed it. `OnForegroundTerminated` routed cleanup through `_indicator.Disable()`, which only fires `Cleared` when `BusyIndicator.IsActive == true` — but that flag is not strictly coupled to `DndManager.CurrentState` (e.g. the foreground may have drifted off the captured app between `TryCapture` and `Enable`, leaving the indicator inactive). When the invariant didn't hold, termination canceled the grace timer and unsubscribed the source while skipping `Release` / `BloomAndFade(red)` / `CurrentState = Off`, leaving the grace-hint border on screen with nothing to clear it. All teardown paths (manual `Deactivate`, natural grace expiry, process termination) now funnel through a single `ClearActiveDnd` helper guarded by an `_isClearing` re-entrancy flag, so the cleared bloom and `BusyCleared` always fire exactly once regardless of indicator state.
- Fix the EasyEyes state machine getting stuck in `Busy` after the user manually disables Do Not Disturb while the activity timer was already expired. `DndManager.Deactivate()` from the `Arming` state never raised `BusyCleared` because the inner `BusyIndicator` had not yet been enabled, leaving the app showing no overlay with both busy sources reporting clear. Deactivate now always raises `BusyCleared` exactly once when transitioning out of a busy state, while preserving the single-fire invariant for the `Active` path.

## [2026.05.02]

### Added

- Add **Do not disturb** mode — defers the overlay while a video player or game is in the foreground. Activated via the tray menu with a 10-second settle period. An amber border shows during arming, a green flash confirms capture, and a red flash indicates when DND clears (either by switching away, locking the screen, or manually toggling off). Uses a 45-second grace period so brief alt-tabs don't interrupt.
- Add a visible grace-period hint to Do Not Disturb (issue #2 in `issues-with-dnd.md`). When the user switches away from the captured app, a glowing border fades in over the 45-second grace duration, transitioning amber → red in lockstep with opacity 0 → 1. The border is barely visible at the start of grace and fully red at the end, so attention ramps up as the deadline approaches. If the user returns within grace, the border cross-fades to green and bloom-fades out as confirmation; if the grace timer expires, the cleared red bloom-and-fade flows continuously from the now-fully-red hint border.

### Fixed

- Fix Do Not Disturb failing to suppress the overlay when activated while the overlay was already showing or while still arming (issue #5 in `issues-with-dnd.md`). `DndManager.IsBusy` now reports busy from the moment DND is armed, the overlay state hides immediately when DND turns on, and the state machine defensively re-checks the busy predicate on entry to `OverlayDisplayed` so source-vs-trigger races no longer surface the overlay. Cross-source `BusyCleared` no longer pops the overlay while the other busy source (meeting indicator vs. DND) is still active.

### Changed

- Speed up Do Not Disturb arming when a fullscreen window is already focused. Alongside the 10-second settle timer, `DndManager` now polls the foreground window every second during arming; if the same fullscreen `hwnd` is focused across two consecutive polls (≈1 second of stability) it locks early instead of waiting out the full settle period. Switching to a windowed app, alt-tabbing rapidly, or having no fullscreen focus continues to wait for the regular 10-second timer. `IForegroundCapture` gained `GetFullscreenForegroundWindow()` for the probe and `DndManager` takes a new `armingProbeScheduler` / `armingProbeInterval` pair.
- Drop the captured process name from the Do Not Disturb tray menu label (issue #3 in `issues-with-dnd.md`). The label is now a plain "Do not disturb" with checkmark when active, plus an "(arming...)" suffix during the settle window. The user knows what they armed DND for, and exposing the underlying process name (e.g. `chrome` for a YouTube tab) was misleading. `IForegroundCapture.CapturedProcessName` and the `Process.GetProcessById` lookup in `ForegroundWindowStateSource` are gone.
- Require the foreground window to be fullscreen for Do Not Disturb to arm and remain active (issue #4 in `issues-with-dnd.md`). At settle expiry, if the foreground window is not fullscreen the capture is rejected and DND falls back to Off with a red flash. While active, DND treats the captured process as "in focus" only when its current foreground window is still fullscreen — exiting fullscreen (e.g. closing a YouTube fullscreen view to browse other tabs) now correctly kicks off the grace period. Fullscreen is detected by comparing `GetWindowRect` against `MonitorFromWindow` bounds and excluding desktop/shell windows (`Progman`, `WorkerW`, `GetShellWindow`); style bits are intentionally not checked so borderless fullscreen games still qualify. `IForegroundCapture.Capture()` is now `bool TryCapture()` so callers can react to rejection.
- Replace the flat 6 px Do Not Disturb borders with animated, soft-glow borders (issue #1 in `issues-with-dnd.md`). Each `BorderFlashWindow` now paints four rectangular edge polygons filled with a perpendicular linear gradient (opaque outer edge → transparent inner edge) joined by four square corner polygons filled with a radial gradient rooted at the inner corner — picture-frame look with rounded glow falloff and seamless edge↔corner transitions. The arming border fades in over 200 ms and holds; capture-success (green), capture-rejected (red), and cleared (red) all play a bloom-and-fade finale (cross-fade color, hold, fade out). `IDndFlashFeedback.ShowFlash` is replaced by `BloomAndFade(Color)`; `Hide` is now a fade-out rather than an instant close, and the manager kills any in-flight fade-out instantly when a new persistent border replaces it.

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
