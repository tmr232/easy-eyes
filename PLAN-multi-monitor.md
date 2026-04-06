# Multi-Monitor Overlay Plan

## Steps

- [x] **1. Extract `OverlayWindow` class** — a lightweight WPF window that owns its own `Rectangle`, `RadialGradientBrush` spotlight mask, fade animation, and cursor tracking. Sized/positioned to exactly one monitor.

- [x] **2. Create `OverlayManager`** — responsible for:

  - Enumerating screens via `System.Windows.Forms.Screen.AllScreens`
  - Creating/destroying `OverlayWindow` instances to match the current set of screens
  - Exposing `ShowAll()` / `HideAll()` methods that the state machine's `showOverlay`/`hideOverlay` callbacks invoke
  - Listening to `Microsoft.Win32.SystemEvents.DisplaySettingsChanged` to refresh the set of windows (add new ones, remove stale ones, resize changed ones)

- [x] **3. `MainWindow` becomes a non-visual host** — it keeps the tray icon, session listener, and state machine, but delegates overlay display entirely to `OverlayManager`.

- [x] **4. Spotlight cursor tracking** — only the `OverlayWindow` under the cursor needs an active spotlight. Each window tracks the cursor independently via `GetCursorPos` and `PointFromScreen`, so this works naturally — the spotlight will appear on whichever screen the cursor is on, and the gradient on other screens will be fully opaque (since the brush center is off-screen for them).

- [x] **5. Win32 styles** — `WS_EX_TRANSPARENT`, `WS_EX_NOACTIVATE`, etc. and the 1px height trick apply to each `OverlayWindow` individually.

- ~~**6. Remove `MainWindow` entirely**~~ — **Decided against.** Moving
  everything into `App` would turn it into a god class mixing lifecycle
  concerns (mutex, logging, error handling) with app logic (tray, state
  machine, session listener). Keeping `MainWindow` as a non-visual host
  preserves separation of concerns and is idiomatic WPF (hidden
  message-only windows are a well-understood pattern).
  `SessionNotificationListener` also already relies on a `Window` handle,
  so this avoids adding an extra `HwndSource`.
