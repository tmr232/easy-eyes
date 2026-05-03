namespace EasyEyes;

/// <summary>
/// Abstraction for capturing and monitoring the foreground window process.
/// Production code uses <see cref="ForegroundWindowStateSource"/>; tests
/// provide a fake.
/// </summary>
public interface IForegroundCapture : IStateSource
{
    /// <summary>
    /// Records the current foreground window's process and starts monitoring.
    /// Returns <c>false</c> when the foreground window does not satisfy the
    /// capture preconditions (e.g. not a fullscreen window, see issue #4 in
    /// <c>issues-with-dnd.md</c>); in that case nothing is captured and no
    /// polling starts.
    /// </summary>
    bool TryCapture();

    /// <summary>
    /// Clears the captured process and stops monitoring.
    /// </summary>
    void Release();

    /// <summary>
    /// Returns the handle of the current foreground window if it is a
    /// fullscreen window, or <c>null</c> otherwise. Used during DND
    /// arming to detect a stable fullscreen focus and lock early
    /// without waiting for the full settle period.
    /// </summary>
    IntPtr? GetFullscreenForegroundWindow();

    /// <summary>
    /// Fires when the captured process has terminated. Allows DND to bail
    /// out immediately rather than waiting out the full grace period
    /// after the foreground window goes away — there is no possibility
    /// of the user "coming back" to a process that no longer exists.
    /// </summary>
    /// <remarks>
    /// Always raised on the dispatcher thread. Never fires when nothing
    /// is captured, and is silenced after <see cref="Release"/> so a
    /// late kernel signal cannot reach subscribers after they have torn
    /// down their state.
    /// </remarks>
    event EventHandler? Terminated;
}
