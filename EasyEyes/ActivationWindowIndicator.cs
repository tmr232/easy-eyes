namespace EasyEyes;

/// <summary>
/// Wraps a <see cref="BusyIndicator"/> with an activation window: if the
/// indicator is enabled but the monitored state does not become active
/// within the window, the indicator is automatically disabled.
/// </summary>
public class ActivationWindowIndicator
{
    private readonly BusyIndicator _inner;
    private readonly ITimerScheduler _scheduler;
    private readonly TimeSpan _window;

    public bool IsActive => _inner.IsActive;
    public bool IsEnabled => _inner.IsEnabled;

    /// <summary>
    /// Fires when the inner indicator clears (grace period expired).
    /// </summary>
    public event EventHandler? Cleared;

    /// <summary>
    /// Fires when the activation window expires without the monitored state
    /// becoming active. The indicator has been auto-disabled.
    /// </summary>
    public event EventHandler? ActivationExpired;

    public ActivationWindowIndicator(
        BusyIndicator inner,
        ITimerScheduler activationScheduler,
        TimeSpan activationWindow)
    {
        _inner = inner;
        _scheduler = activationScheduler;
        _window = activationWindow;

        _inner.Cleared += (s, e) => Cleared?.Invoke(this, e);
        _inner.BecameActive += (_, _) => _scheduler.Cancel();
    }

    public void Enable()
    {
        _inner.Enable();

        if (!_inner.IsActive)
            _scheduler.Start(_window, OnActivationWindowExpired);
    }

    public void Disable()
    {
        _scheduler.Cancel();
        _inner.Disable();
    }

    private void OnActivationWindowExpired()
    {
        _inner.Disable();
        ActivationExpired?.Invoke(this, EventArgs.Empty);
    }
}
