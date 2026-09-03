# Overlay Performance Optimization and Measurement Plan

## Purpose

Reduce the GPU cost of the timeout overlay, especially on high-resolution,
high-refresh-rate, and multi-monitor systems. Work through the
behavior-preserving options first, measuring each change independently before
considering visual trade-offs.

## Optimization Plan

### A. Preserve Visible Behavior

Goal: retain the current vignette, cursor-following spotlight, blue border,
and fade.

1. **Establish a baseline**

   - Record hidden, visible/stationary, visible/moving-cursor, and fade-in
     measurements.
   - Record results with one display and with all connected displays.

2. **Eliminate unnecessary render callbacks**

   - Replace per-frame spotlight updates with cursor-position-driven updates.
   - Do nothing when the cursor has not moved.
   - Confirm the spotlight remains visually synchronized with cursor movement.

3. **Bound the update rate**

   - Coalesce rapid cursor updates.
   - Apply a maximum update rate, initially 30 Hz; evaluate 15 Hz if it remains
     visually smooth.
   - Ignore insignificant pointer movement, initially below 1--2 physical
     pixels.

4. **Restrict dynamic work to the relevant monitor**

   - Update the mask only on the display currently containing the cursor.
   - Keep other monitor overlays visually unchanged and static.
   - Verify correct behavior when crossing monitor boundaries and with mixed
     DPI scaling.

5. **Cache static rendering inputs**

   - Pre-render or cache each monitor's static vignette at its physical size
     and DPI.
   - Recreate cached assets only after display or DPI configuration changes.
   - Keep brush/image resources alive while the overlay is shown.

6. **Validate retained behavior**

   - Check fade timing, spotlight size and falloff, cursor tracking, screen
     edges, taskbar gap, click-through behavior, and multi-monitor transitions.
   - Compare screenshots or recordings against the current implementation.

### B. Change Visible Behavior

Goal: offer substantially lower-cost visual modes if the
behavior-preserving work is insufficient.

1. **Define visual modes**

   - **Enhanced:** current design, using the optimizations from group A.
   - **Standard:** static vignette, no cursor-following spotlight.
   - **Minimal:** solid dimmer, short fade or no fade, border optional.

2. **Evaluate a static vignette**

   - Keep the dark radial appearance but remove cursor tracking.
   - Measure whether the steady-state cost is acceptable.

3. **Evaluate a solid dimmer**

   - Replace the vignette and spotlight with a uniform translucent black
     overlay.
   - Retain the border if it remains useful for noticeability.

4. **Evaluate native global-alpha composition**

   - Use an opaque black window with one global alpha instead of WPF
     per-pixel transparency.
   - Preserve topmost, click-through, no-activation, and per-monitor behavior.
   - Treat this as the preferred implementation for Minimal mode if it performs
     best.

5. **Reduce transition cost**

   - Compare the current five-second fade with 300 ms and no-fade variants.
   - Choose the shortest transition that still feels intentional.

6. **Select the default**

   - Prefer the least visually complex mode that materially reduces
     steady-state GPU use while still achieving the reminder's purpose.
   - Keep Enhanced as opt-in if it remains notably more expensive.

## Measurement Plan

### Controlled Test Conditions

- Use a Release build on the same hardware, display arrangement, refresh rate,
  DPI, and power plan.
- Close or stabilize GPU-heavy background apps.
- Keep the desktop behind the overlay static.
- Run every scenario 3--5 times for 30--60 seconds; use the median result.

### Scenarios

| ID  | Scenario                                      | What it isolates          |
| --- | --------------------------------------------- | ------------------------- |
| M0  | Overlay hidden                                | System baseline           |
| M1  | Overlay visible after fade, cursor stationary | Steady-state overlay cost |
| M2  | Overlay visible, cursor moved continuously    | Dynamic spotlight cost    |
| M3  | Overlay appearing                             | Fade and creation cost    |
| M4  | M1/M2 with one display                        | Per-display cost          |
| M5  | M1/M2 with all displays                       | Multi-monitor scaling     |

### Tools and Metrics

Use Task Manager for quick feedback:

- For `EasyEyes.exe`: GPU percentage, GPU engine, CPU percentage, and power
  usage.
- For `dwm.exe`: GPU percentage, GPU engine, and power usage.
- Compare the combined EasyEyes and DWM change against M0.

Use Windows Performance Recorder (WPR) and Windows Performance Analyzer (WPA)
for decisions:

- Capture an ETW trace for each implementation/scenario pair.
- In WPA, examine GPU Utilization by process for `EasyEyes.exe` and `dwm.exe`.
- Examine CPU Usage (Sampled) for EasyEyes UI and rendering activity.
- Examine GPU memory/commit and paging when available.
- Examine composition/presentation activity during M1 and M2.

### Decision Criteria

A behavior-preserving change is successful if:

- M1 drops materially toward M0 after fade completion.
- M2 no longer scales with display refresh rate across every monitor.
- Cursor tracking remains visually indistinguishable in normal use.

A visible-behavior change is successful if:

- Its M1 and M2 results are consistently lower than the optimized Enhanced mode.
- It still makes the timeout unmistakable without interfering with user input
  or taskbar behavior.

Keep a result log with the implementation name, hardware/display configuration,
scenario duration, median CPU/GPU/DWM values, and qualitative visual notes.
