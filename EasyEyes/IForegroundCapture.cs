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
}
