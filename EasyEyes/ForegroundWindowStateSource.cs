using System.Windows.Threading;

namespace EasyEyes;

/// <summary>
/// Monitors whether the foreground window belongs to a previously captured
/// process AND is still in fullscreen mode. Used by the Do Not Disturb
/// feature to detect when the user switches away from a video player or
/// game (or leaves fullscreen, e.g. closes a YouTube fullscreen view to
/// browse other tabs).
/// </summary>
/// <remarks>
/// <para>
/// Polls <see cref="NativeMethods.GetForegroundWindow"/> on a ThreadPool
/// timer and compares the owning process ID against the captured value,
/// then additionally requires the foreground window to be fullscreen
/// (issue #4 in <c>issues-with-dnd.md</c>). Events are marshalled to the
/// provided <see cref="Dispatcher"/> so subscribers always run on the UI
/// thread (same pattern as <see cref="MediaDeviceMonitor"/>).
/// </para>
/// <para>
/// Uses process ID rather than window handle so that if the target app
/// creates new windows (e.g. a game switching from launcher to main
/// window), the match is preserved.
/// </para>
/// </remarks>
public sealed class ForegroundWindowStateSource : IForegroundCapture, IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly TimeSpan _pollInterval;
    private Timer? _pollTimer;
    private uint? _capturedProcessId;
    private volatile bool _lastActive;
    private bool _disposed;

    /// <inheritdoc />
    public bool IsActive => _capturedProcessId.HasValue && IsForegroundActive(_capturedProcessId.Value);

    /// <summary>
    /// Fires when the foreground window returns to the captured process and
    /// is fullscreen.
    /// </summary>
    public event EventHandler? Activated;

    /// <summary>
    /// Fires when the foreground window leaves the captured process or
    /// stops being fullscreen.
    /// </summary>
    public event EventHandler? Deactivated;

    /// <inheritdoc />
    /// <remarks>
    /// Declared here for the interface, but not yet raised by this
    /// implementation. The kernel-wait wiring lands together with
    /// <c>Win32ProcessLifetimeWatcher</c> in a later step.
    /// </remarks>
#pragma warning disable CS0067 // Event is never used (wired up in a later commit)
    public event EventHandler? Terminated;
#pragma warning restore CS0067

    public ForegroundWindowStateSource(TimeSpan pollInterval, Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _pollInterval = pollInterval;
    }

    /// <summary>
    /// Records the current foreground window's process and starts polling.
    /// Refuses (returns <c>false</c>) when the foreground window is not
    /// fullscreen — DND is meant for fullscreen media/games, not for
    /// arbitrary windowed apps (issue #4 in <c>issues-with-dnd.md</c>).
    /// </summary>
    public bool TryCapture()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        if (!WindowInspector.IsFullscreen(hwnd))
        {
            return false;
        }

        _ = NativeMethods.GetWindowThreadProcessId(hwnd, out uint processId);
        _capturedProcessId = processId;
        _lastActive = true;
        _pollTimer?.Dispose();
        _pollTimer = new Timer(Poll, null, _pollInterval, _pollInterval);
        return true;
    }

    /// <inheritdoc />
    public IntPtr? GetFullscreenForegroundWindow()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }

        return WindowInspector.IsFullscreen(hwnd) ? hwnd : null;
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

    private void Poll(object? state)
    {
        if (!_capturedProcessId.HasValue)
        {
            return;
        }

        bool active = IsForegroundActive(_capturedProcessId.Value);
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

    private static bool IsForegroundActive(uint capturedProcessId)
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        _ = NativeMethods.GetWindowThreadProcessId(hwnd, out uint processId);
        return processId == capturedProcessId && WindowInspector.IsFullscreen(hwnd);
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
