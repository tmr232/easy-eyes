using System.Windows.Threading;

namespace EasyEyes;

/// <summary>
/// Monitors whether any media device (camera or microphone) is currently in
/// use. Composes two <see cref="DeviceUsageDetector"/> instances and tracks
/// the aggregate state via polling.
/// </summary>
/// <remarks>
/// <para>
/// We use polling rather than registry change notifications because the
/// CapabilityAccessManager keys are spread across per-app subkeys in both
/// HKLM and HKCU, with new subkeys appearing when new apps access a device.
/// <c>RegNotifyChangeKeyValue</c> fires on any write under the watched
/// subtree — not just the values we care about — so we'd get noisy false
/// positives and still need to re-read the actual values on each
/// notification, making it effectively polling with extra complexity.
/// A 1-second timer is simpler, reliable, and the cost of a few registry
/// reads per second is negligible.
/// </para>
/// <para>
/// The <see cref="System.Threading.Timer"/> fires on a ThreadPool thread,
/// but events are marshalled to the provided <see cref="Dispatcher"/> so
/// that subscribers (BusyIndicator, state machine, UI controls) always
/// run on the UI thread.
/// </para>
/// </remarks>
public sealed class MediaDeviceMonitor : IStateSource, IDisposable
{
    private readonly DeviceUsageDetector _camera = new("webcam");
    private readonly DeviceUsageDetector _microphone = new("microphone");
    private readonly Dispatcher _dispatcher;
    private readonly Timer _pollTimer;
    private bool _lastInUse;
    private bool _disposed;

    /// <summary>
    /// Whether any monitored device (camera or microphone) is currently in use.
    /// </summary>
    public bool IsInUse => _camera.IsInUse || _microphone.IsInUse;

    /// <inheritdoc />
    bool IStateSource.IsActive => IsInUse;

    /// <summary>
    /// Fires when transitioning from no devices in use to at least one device in use.
    /// </summary>
    public event EventHandler? Activated;

    /// <summary>
    /// Fires when transitioning from at least one device in use to no devices in use.
    /// </summary>
    public event EventHandler? Deactivated;

    public MediaDeviceMonitor(TimeSpan pollInterval, Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _lastInUse = IsInUse;
        _pollTimer = new Timer(Poll, null, pollInterval, pollInterval);
    }

    private void Poll(object? state)
    {
        var inUse = IsInUse;
        if (inUse != _lastInUse)
        {
            _lastInUse = inUse;
            _ = _dispatcher.BeginInvoke(() =>
            {
                if (inUse)
                    Activated?.Invoke(this, EventArgs.Empty);
                else
                    Deactivated?.Invoke(this, EventArgs.Empty);
            });
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _pollTimer.Dispose();
    }
}
