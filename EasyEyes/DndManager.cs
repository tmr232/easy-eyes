using Color = System.Windows.Media.Color;

namespace EasyEyes;

/// <summary>
/// Current state of the Do Not Disturb feature.
/// </summary>
public enum DndState
{
    /// <summary>DND is not active.</summary>
    Off,

    /// <summary>Settle timer is running; waiting for the user to switch to the target app.</summary>
    Arming,

    /// <summary>Foreground app is captured; overlay is deferred while the app is focused.</summary>
    Active,
}

/// <summary>
/// Orchestrates the Do Not Disturb lifecycle: settle period, foreground
/// window capture, busy-indicator management, and visual feedback via
/// colored border flashes.
/// </summary>
/// <remarks>
/// <para>
/// Activation flow (from tray menu):
/// 1. User clicks "Do not disturb" → <see cref="Activate"/> is called.
/// 2. An amber border appears on all monitors and a 10-second settle
///    timer starts (<see cref="DndState.Arming"/>).
/// 3. User alt-tabs to their video player or game.
/// 4. Settle timer expires → foreground window's process is captured,
///    green border flash confirms, busy indicator is enabled
///    (<see cref="DndState.Active"/>).
/// 5. While the captured app is in the foreground, <see cref="IsBusy"/>
///    is true and the overlay is deferred.
/// 6. When the user switches away long enough for the grace period to
///    expire, a red border flash appears and DND returns to
///    <see cref="DndState.Off"/>.
/// </para>
/// </remarks>
public sealed class DndManager : IDisposable
{
    private readonly IForegroundCapture _foregroundSource;
    private readonly IDndFlashFeedback _borderFlashManager;
    private readonly BusyIndicator _indicator;
    private readonly ITimerScheduler _settleScheduler;
    private readonly ITimerScheduler _armingProbeScheduler;
    private readonly TimeSpan _settleDuration;
    private readonly TimeSpan _armingProbeInterval;
    private readonly TimeSpan _gracePeriod;
    private IntPtr? _lastProbedFullscreenHwnd;
    private bool _disposed;

    /// <summary>
    /// Whether DND should defer the overlay. True whenever DND is not
    /// <see cref="DndState.Off"/> — that is, during <see cref="DndState.Arming"/>
    /// (the user has expressed intent and the amber border is up) and
    /// during <see cref="DndState.Active"/> (the captured app is in the
    /// foreground, or within the grace period after leaving).
    /// </summary>
    /// <remarks>
    /// Including <see cref="DndState.Arming"/> ensures that if the activity
    /// timer expires while the user is still arming DND, the overlay is
    /// suppressed (issue #5 in <c>issues-with-dnd.md</c>). The inner
    /// <see cref="BusyIndicator"/> still governs when DND eventually
    /// clears via the grace period after the user leaves the captured app.
    /// </remarks>
    public bool IsBusy => CurrentState != DndState.Off;

    /// <summary>Current DND state.</summary>
    public DndState CurrentState { get; private set; }

    /// <summary>
    /// Fires when the busy indicator clears (grace period expired after
    /// the user left the captured app).
    /// </summary>
    public event EventHandler? BusyCleared;

    /// <summary>
    /// Fires when the busy indicator becomes active (the captured app
    /// regained focus, possibly after a brief alt-tab).
    /// </summary>
    public event EventHandler? BecameActive;

    /// <summary>
    /// Fires when <see cref="CurrentState"/> changes.
    /// </summary>
    public event EventHandler? StateChanged;

    public DndManager(
        IForegroundCapture foregroundSource,
        IDndFlashFeedback borderFlashManager,
        ITimerScheduler settleScheduler,
        ITimerScheduler graceScheduler,
        ITimerScheduler armingProbeScheduler,
        TimeSpan settleDuration,
        TimeSpan gracePeriod,
        TimeSpan armingProbeInterval)
    {
        _foregroundSource = foregroundSource;
        _borderFlashManager = borderFlashManager;
        _settleScheduler = settleScheduler;
        _armingProbeScheduler = armingProbeScheduler;
        _settleDuration = settleDuration;
        _armingProbeInterval = armingProbeInterval;
        _gracePeriod = gracePeriod;

        _indicator = new BusyIndicator(foregroundSource, graceScheduler, gracePeriod);
        _indicator.Cleared += OnIndicatorCleared;
        _indicator.BecameActive += OnIndicatorBecameActive;

        // Drive the grace-hint visuals (issue #2 in issues-with-dnd.md)
        // directly from the foreground source. BusyIndicator still owns
        // the grace-period bookkeeping; we only listen here for the
        // visual handoff so the user can see the grace timer running.
        _foregroundSource.Deactivated += OnForegroundDeactivated;
        _foregroundSource.Activated += OnForegroundActivated;
    }

    /// <summary>
    /// Starts the DND arming flow: shows a persistent amber border and
    /// begins the settle timer. In parallel, polls the foreground window
    /// every <c>armingProbeInterval</c>; if a fullscreen window stays
    /// focused across two consecutive polls, the manager locks early
    /// without waiting for the full settle period.
    /// </summary>
    public void Activate()
    {
        if (CurrentState != DndState.Off)
        {
            return;
        }

        CurrentState = DndState.Arming;
        StateChanged?.Invoke(this, EventArgs.Empty);
        _borderFlashManager.ShowPersistent(BorderFlashManager.ArmingColor);
        _settleScheduler.Start(_settleDuration, OnSettleExpired);
        _lastProbedFullscreenHwnd = _foregroundSource.GetFullscreenForegroundWindow();
        _armingProbeScheduler.Start(_armingProbeInterval, OnArmingProbeTick);
    }

    /// <summary>
    /// Immediately deactivates DND, cleaning up all state and showing
    /// a red border flash.
    /// </summary>
    public void Deactivate()
    {
        if (CurrentState == DndState.Off)
        {
            return;
        }

        _settleScheduler.Cancel();
        _armingProbeScheduler.Cancel();
        _lastProbedFullscreenHwnd = null;
        _indicator.Disable();
        _foregroundSource.Release();
        _borderFlashManager.BloomAndFade(BorderFlashManager.ClearedColor);
        CurrentState = DndState.Off;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Shows a red bloom-and-fade border without changing DND state. Used
    /// by <see cref="MainWindow"/> to indicate on screen unlock that DND
    /// was cleared by a prior screen lock.
    /// </summary>
    public void FlashCleared()
    {
        _borderFlashManager.BloomAndFade(BorderFlashManager.ClearedColor);
    }

    private void OnSettleExpired()
    {
        _armingProbeScheduler.Cancel();
        _lastProbedFullscreenHwnd = null;
        if (!_foregroundSource.TryCapture())
        {
            // Foreground window did not satisfy capture preconditions
            // (typically: not a fullscreen window, see issue #4 in
            // issues-with-dnd.md). Fall back to Off and signal the
            // rejection visually with a red bloom-and-fade.
            _borderFlashManager.BloomAndFade(BorderFlashManager.ClearedColor);
            CurrentState = DndState.Off;
            StateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        _borderFlashManager.BloomAndFade(BorderFlashManager.LockedColor);
        _indicator.Enable();
        CurrentState = DndState.Active;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Periodic check during arming: if the same fullscreen window has
    /// been focused for two consecutive ticks, lock early instead of
    /// waiting out the full settle period. Otherwise record the current
    /// fullscreen hwnd (or null) and reschedule the probe.
    /// </summary>
    private void OnArmingProbeTick()
    {
        if (CurrentState != DndState.Arming)
        {
            return;
        }

        var current = _foregroundSource.GetFullscreenForegroundWindow();
        if (current.HasValue && current == _lastProbedFullscreenHwnd)
        {
            _settleScheduler.Cancel();
            OnSettleExpired();
            return;
        }

        _lastProbedFullscreenHwnd = current;
        _armingProbeScheduler.Start(_armingProbeInterval, OnArmingProbeTick);
    }

    private void OnIndicatorCleared(object? sender, EventArgs e)
    {
        _foregroundSource.Release();
        _borderFlashManager.BloomAndFade(BorderFlashManager.ClearedColor);
        CurrentState = DndState.Off;
        StateChanged?.Invoke(this, EventArgs.Empty);
        BusyCleared?.Invoke(this, e);
    }

    private void OnIndicatorBecameActive(object? sender, EventArgs e)
    {
        BecameActive?.Invoke(this, e);
    }

    /// <summary>
    /// Foreground source signalled the user left the captured app.
    /// While DND is Active this kicks off the visible grace-period hint
    /// (amber → red, opacity 0 → 1, over the grace duration). Ignored in
    /// Off / Arming / during the synchronous Release in OnIndicatorCleared
    /// / Deactivate, where state has already been reset.
    /// </summary>
    private void OnForegroundDeactivated(object? sender, EventArgs e)
    {
        if (CurrentState != DndState.Active)
        {
            return;
        }

        _borderFlashManager.ShowGraceHint(
            BorderFlashManager.GraceHintStartColor,
            BorderFlashManager.GraceHintEndColor,
            _gracePeriod);
    }

    /// <summary>
    /// Foreground source signalled the user returned to the captured
    /// app. While DND is Active this cancels the grace-period hint with
    /// a green confirmation bloom-and-fade.
    /// </summary>
    private void OnForegroundActivated(object? sender, EventArgs e)
    {
        if (CurrentState != DndState.Active)
        {
            return;
        }

        _borderFlashManager.CancelGraceHint(BorderFlashManager.LockedColor);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _foregroundSource.Deactivated -= OnForegroundDeactivated;
        _foregroundSource.Activated -= OnForegroundActivated;
        _settleScheduler.Cancel();
        _armingProbeScheduler.Cancel();
        _indicator.Disable();
        _foregroundSource.Release();
        GC.SuppressFinalize(this);
    }
}
