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
    private readonly TimeSpan _settleDuration;
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
        TimeSpan settleDuration,
        TimeSpan gracePeriod)
    {
        _foregroundSource = foregroundSource;
        _borderFlashManager = borderFlashManager;
        _settleScheduler = settleScheduler;
        _settleDuration = settleDuration;

        _indicator = new BusyIndicator(foregroundSource, graceScheduler, gracePeriod);
        _indicator.Cleared += OnIndicatorCleared;
        _indicator.BecameActive += OnIndicatorBecameActive;
    }

    /// <summary>
    /// Starts the DND arming flow: shows a persistent amber border and
    /// begins the settle timer.
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
        _indicator.Disable();
        _foregroundSource.Release();
        _borderFlashManager.ShowFlash(BorderFlashManager.ClearedColor);
        CurrentState = DndState.Off;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Shows a red border flash without changing DND state. Used by
    /// <see cref="MainWindow"/> to indicate on screen unlock that DND
    /// was cleared by a prior screen lock.
    /// </summary>
    public void FlashCleared()
    {
        _borderFlashManager.ShowFlash(BorderFlashManager.ClearedColor);
    }

    private void OnSettleExpired()
    {
        _foregroundSource.Capture();
        _borderFlashManager.ShowFlash(BorderFlashManager.LockedColor);
        _indicator.Enable();
        CurrentState = DndState.Active;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnIndicatorCleared(object? sender, EventArgs e)
    {
        _foregroundSource.Release();
        _borderFlashManager.ShowFlash(BorderFlashManager.ClearedColor);
        CurrentState = DndState.Off;
        StateChanged?.Invoke(this, EventArgs.Empty);
        BusyCleared?.Invoke(this, e);
    }

    private void OnIndicatorBecameActive(object? sender, EventArgs e)
    {
        BecameActive?.Invoke(this, e);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _settleScheduler.Cancel();
        _indicator.Disable();
        _foregroundSource.Release();
        GC.SuppressFinalize(this);
    }
}
