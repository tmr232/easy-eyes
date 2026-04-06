# Multi-Monitor Overlay Plan

## Steps

- [x] **1. Extract `OverlayWindow` class** — a lightweight WPF window that owns its own `Rectangle`, `RadialGradientBrush` spotlight mask, fade animation, and cursor tracking. Sized/positioned to exactly one monitor.

- [ ] **2. Create `OverlayManager`** — responsible for:

  - Enumerating screens via `System.Windows.Forms.Screen.AllScreens`
  - Creating/destroying `OverlayWindow` instances to match the current set of screens
  - Exposing `ShowAll()` / `HideAll()` methods that the state machine's `showOverlay`/`hideOverlay` callbacks invoke
  - Listening to `Microsoft.Win32.SystemEvents.DisplaySettingsChanged` to refresh the set of windows (add new ones, remove stale ones, resize changed ones)

- [ ] **3. `MainWindow` becomes a non-visual host** — it keeps the tray icon, session listener, and state machine, but delegates overlay display entirely to `OverlayManager`.

- [ ] **4. Spotlight cursor tracking** — only the `OverlayWindow` under the cursor needs an active spotlight. Each window tracks the cursor independently via `GetCursorPos` and `PointFromScreen`, so this works naturally — the spotlight will appear on whichever screen the cursor is on, and the gradient on other screens will be fully opaque (since the brush center is off-screen for them).

- [ ] **5. Win32 styles** — `WS_EX_TRANSPARENT`, `WS_EX_NOACTIVATE`, etc. and the 1px height trick apply to each `OverlayWindow` individually.

- [ ] **6. Remove `MainWindow` entirely** — move the tray icon, session listener, state machine, and media-pause logic into `App` (or a dedicated non-visual host class), eliminating the need for a hidden window. `App.OnStartup` becomes the entry point for wiring everything together, and `OverlayManager` owns all the windows.
