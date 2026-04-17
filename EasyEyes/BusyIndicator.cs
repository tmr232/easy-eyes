namespace EasyEyes;

/// <summary>
/// A generic one-shot, opt-in busy indicator that monitors a boolean state
/// (active/inactive) and auto-disables after a grace period when the state
/// becomes inactive.
///
/// The indicator is enabled by the user (via tray menu). Once the monitored
/// activity ends, a grace timer starts. If the activity resumes within the
/// grace period, the timer is cancelled. If the grace timer expires, the
/// indicator auto-disables and fires the <see cref="Cleared"/> event.
/// </summary>
/// <remarks>
/// The caller is responsible for thread safety. State-change event handlers
/// (passed to the constructor) may fire on any thread; the indicator itself
/// does not dispatch to the UI thread.
/// </remarks>
public class BusyIndicator
{
    private readonly IStateSource _source;
    private readonly ITimerScheduler _graceScheduler;
    private readonly TimeSpan _gracePeriod;

    private bool _enabled;
    private bool _subscribed;
    private bool _persistent;

    /// <summary>
    /// True if the indicator is enabled AND the monitored state is currently
    /// active (or within the grace period after deactivation).
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// Whether the indicator is currently enabled (opted-in by the user).
    /// </summary>
    public bool IsEnabled => _enabled;

    /// <summary>
    /// When true, the indicator does not auto-disable on grace expiry.
    /// It stays enabled and will re-activate when the monitored state
    /// becomes active again.
    /// </summary>
    public bool Persistent
    {
        get => _persistent;
        set => _persistent = value;
    }

    /// <summary>
    /// Fires when the indicator transitions from inactive to active.
    /// </summary>
    public event EventHandler? BecameActive;

    /// <summary>
    /// Fires when the indicator transitions from active to inactive
    /// (i.e., when the grace period expires and the indicator auto-disables).
    /// </summary>
    public event EventHandler? Cleared;

    /// <summary>
    /// Creates a new busy indicator.
    /// </summary>
    /// <param name="source">The state source to monitor.</param>
    /// <param name="graceScheduler">Timer scheduler for the grace period (use <see cref="DispatcherTimerScheduler"/> in production).</param>
    /// <param name="gracePeriod">Duration of the grace period before auto-disabling.</param>
    public BusyIndicator(
        IStateSource source,
        ITimerScheduler graceScheduler,
        TimeSpan gracePeriod)
    {
        _source = source;
        _graceScheduler = graceScheduler;
        _gracePeriod = gracePeriod;
    }

    /// <summary>
    /// Enables the indicator. Subscribes to state-change events and sets
    /// <see cref="IsActive"/> based on the current state.
    /// </summary>
    public void Enable()
    {
        if (_enabled)
            return;

        _enabled = true;
        Subscribe();
        IsActive = _source.IsActive;
        if (IsActive)
            BecameActive?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Disables the indicator. Cancels any pending grace timer, unsubscribes
    /// from events, and clears the active state.
    /// </summary>
    public void Disable()
    {
        if (!_enabled)
            return;

        _enabled = false;
        _graceScheduler.Cancel();
        Unsubscribe();
        IsActive = false;
    }

    private void Subscribe()
    {
        if (_subscribed)
            return;

        _subscribed = true;
        _source.Activated += OnActivated;
        _source.Deactivated += OnDeactivated;
    }

    private void Unsubscribe()
    {
        if (!_subscribed)
            return;

        _subscribed = false;
        _source.Activated -= OnActivated;
        _source.Deactivated -= OnDeactivated;
    }

    private void OnActivated(object? sender, EventArgs e)
    {
        if (!_enabled)
            return;

        _graceScheduler.Cancel();
        var wasPreviouslyActive = IsActive;
        IsActive = true;
        if (!wasPreviouslyActive)
            BecameActive?.Invoke(this, EventArgs.Empty);
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (!_enabled)
            return;

        _graceScheduler.Start(_gracePeriod, OnGraceExpired);
    }

    private void OnGraceExpired()
    {
        IsActive = false;

        if (_persistent)
        {
            // Stay enabled — will re-activate when the state becomes active again.
            Cleared?.Invoke(this, EventArgs.Empty);
            return;
        }

        _enabled = false;
        Unsubscribe();
        Cleared?.Invoke(this, EventArgs.Empty);
    }
}
