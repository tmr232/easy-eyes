namespace EasyEyes;

/// <summary>
/// Abstraction for a one-shot "tell me when process X dies" watcher.
/// Production code uses <see cref="Win32ProcessLifetimeWatcher"/>;
/// tests provide a fake.
/// </summary>
/// <remarks>
/// <para>
/// One instance watches at most one process at a time. Calling
/// <see cref="Watch"/> while already watching cancels the previous
/// watch and starts a new one. <see cref="Cancel"/> stops the current
/// watch and is safe to call when nothing is being watched.
/// </para>
/// <para>
/// The implementation is responsible for delivering <c>onTerminated</c>
/// on whatever thread its consumers expect. The Win32 implementation
/// marshals to the application's WPF dispatcher so that subscribers of
/// <see cref="IForegroundCapture.Terminated"/> always run on the UI
/// thread (matching the existing polling-based <see cref="IStateSource"/>
/// events).
/// </para>
/// </remarks>
public interface IProcessLifetimeWatcher
{
    /// <summary>
    /// Begins watching the given process. <paramref name="onTerminated"/>
    /// is invoked exactly once when the process exits. If the process
    /// cannot be opened (already gone, access denied), the watcher
    /// silently does nothing — DND falls back to the existing grace
    /// period rather than producing a spurious immediate exit.
    /// </summary>
    void Watch(uint processId, Action onTerminated);

    /// <summary>
    /// Cancels any current watch. Idempotent. After return, the
    /// callback passed to the most recent <see cref="Watch"/> will
    /// not be invoked.
    /// </summary>
    void Cancel();
}

/// <summary>
/// Watcher that does nothing. Used as a default when the host has not
/// yet wired up the real Win32 implementation, and as a convenient
/// stand-in in tests that don't care about the termination path.
/// </summary>
public sealed class NullProcessLifetimeWatcher : IProcessLifetimeWatcher
{
    public void Watch(uint processId, Action onTerminated)
    {
    }

    public void Cancel()
    {
    }
}
