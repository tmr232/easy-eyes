namespace EasyEyes;

/// <summary>
/// Monitors whether any media device (camera or microphone) is currently in
/// use. Composes two <see cref="DeviceUsageDetector"/> instances and tracks
/// the aggregate state via polling.
/// </summary>
/// <remarks>
/// Events fire on a ThreadPool thread; callers must dispatch to the UI thread if needed.
/// </remarks>
public sealed class MediaDeviceMonitor : IDisposable
{
    private readonly DeviceUsageDetector _camera = new("webcam");
    private readonly DeviceUsageDetector _microphone = new("microphone");
    private readonly Timer _pollTimer;
    private bool _lastInUse;
    private bool _disposed;

    /// <summary>
    /// Whether any monitored device (camera or microphone) is currently in use.
    /// </summary>
    public bool IsInUse => _camera.IsInUse || _microphone.IsInUse;

    /// <summary>
    /// Fires when transitioning from no devices in use to at least one device in use.
    /// </summary>
    public event EventHandler? Activated;

    /// <summary>
    /// Fires when transitioning from at least one device in use to no devices in use.
    /// </summary>
    public event EventHandler? Deactivated;

    public MediaDeviceMonitor(TimeSpan pollInterval)
    {
        _lastInUse = IsInUse;
        _pollTimer = new Timer(Poll, null, pollInterval, pollInterval);
    }

    private void Poll(object? state)
    {
        var inUse = IsInUse;
        if (inUse != _lastInUse)
        {
            _lastInUse = inUse;
            if (inUse)
                Activated?.Invoke(this, EventArgs.Empty);
            else
                Deactivated?.Invoke(this, EventArgs.Empty);
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
