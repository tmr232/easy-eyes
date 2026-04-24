using System.Windows.Threading;

namespace EasyEyes;

/// <summary>
/// Monitors whether the foreground window belongs to a previously captured
/// process. Used by the Do Not Disturb feature to detect when the user
/// switches away from a video player or game.
/// </summary>
/// <remarks>
/// <para>
/// Polls <see cref="NativeMethods.GetForegroundWindow"/> on a ThreadPool
/// timer and compares the owning process ID against the captured value.
/// Events are marshalled to the provided <see cref="Dispatcher"/> so
/// subscribers always run on the UI thread (same pattern as
/// <see cref="MediaDeviceMonitor"/>).
/// </para>
/// <para>
/// Uses process ID rather than window handle so that if the target app
/// creates new windows (e.g. a game switching from launcher to main
/// window), the match is preserved.
/// </para>
/// </remarks>
public sealed class ForegroundWindowStateSource : IStateSource, IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly TimeSpan _pollInterval;
    private Timer? _pollTimer;
    private uint? _capturedProcessId;
    private volatile bool _lastActive;
    private bool _disposed;

    /// <inheritdoc />
    public bool IsActive => _capturedProcessId.HasValue && GetForegroundProcessId() == _capturedProcessId;

    /// <summary>
    /// Fires when the foreground window returns to the captured process.
    /// </summary>
    public event EventHandler? Activated;

    /// <summary>
    /// Fires when the foreground window leaves the captured process.
    /// </summary>
    public event EventHandler? Deactivated;

    public ForegroundWindowStateSource(TimeSpan pollInterval, Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _pollInterval = pollInterval;
    }

    /// <summary>
    /// Records the current foreground window's process and starts polling.
    /// </summary>
    public void Capture()
    {
        _capturedProcessId = GetForegroundProcessId();
        _lastActive = _capturedProcessId.HasValue;
        _pollTimer?.Dispose();
        _pollTimer = new Timer(Poll, null, _pollInterval, _pollInterval);
    }

    /// <summary>
    /// Clears the captured process and stops polling. Fires
    /// <see cref="Deactivated"/> if the source was active.
    /// </summary>
    public void Release()
    {
        _pollTimer?.Dispose();
        _pollTimer = null;
        _capturedProcessId = null;

        if (_lastActive)
        {
            _lastActive = false;
            _ = _dispatcher.BeginInvoke(() => Deactivated?.Invoke(this, EventArgs.Empty));
        }
    }

    /// <summary>
    /// The process name of the captured process, or <c>null</c> if nothing
    /// is captured.
    /// </summary>
    public string? CapturedProcessName
    {
        get
        {
            if (!_capturedProcessId.HasValue)
            {
                return null;
            }

            try
            {
                return System.Diagnostics.Process.GetProcessById((int)_capturedProcessId.Value).ProcessName;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }

    private void Poll(object? state)
    {
        if (!_capturedProcessId.HasValue)
        {
            return;
        }

        bool active = GetForegroundProcessId() == _capturedProcessId;
        if (active != _lastActive)
        {
            _lastActive = active;
            _ = _dispatcher.BeginInvoke(() =>
            {
                if (active)
                {
                    Activated?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    Deactivated?.Invoke(this, EventArgs.Empty);
                }
            });
        }
    }

    private static uint? GetForegroundProcessId()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }

        _ = NativeMethods.GetWindowThreadProcessId(hwnd, out uint processId);
        return processId;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pollTimer?.Dispose();
    }
}
