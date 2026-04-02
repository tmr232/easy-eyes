# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/),
and this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

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
