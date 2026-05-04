using System.Windows.Threading;
using Microsoft.Win32.SafeHandles;

namespace EasyEyes;

/// <summary>
/// Win32 implementation of <see cref="IProcessLifetimeWatcher"/> using a
/// <see cref="SafeProcessHandle"/> obtained from <c>OpenProcess</c> and a
/// kernel wait registered via <see cref="ThreadPool.RegisterWaitForSingleObject"/>.
/// </summary>
/// <remarks>
/// <para>
/// The handle is requested with <c>PROCESS_QUERY_LIMITED_INFORMATION |
/// SYNCHRONIZE</c>. <c>SYNCHRONIZE</c> is required for the wait;
/// <c>PROCESS_QUERY_LIMITED_INFORMATION</c> is the broadly-granted
/// access right that keeps this working against UWP / packaged apps.
/// If <c>OpenProcess</c> fails (process already gone, access denied),
/// the watcher silently does nothing and DND falls back to the existing
/// grace-period behavior — never producing a spurious "process died"
/// signal.
/// </para>
/// <para>
/// The kernel wait runs the callback on a thread-pool worker thread.
/// We hand it to the constructor-supplied <c>marshal</c> delegate so
/// subscribers run on whatever thread the host expects (the WPF
/// dispatcher in production; synchronously in tests).
/// </para>
/// </remarks>
public sealed class Win32ProcessLifetimeWatcher : IProcessLifetimeWatcher, IDisposable
{
    private readonly Action<Action> _marshal;
    private readonly Lock _gate = new();
    private SafeProcessHandle? _processHandle;
    private RegisteredWaitHandle? _registeredWait;
    private Action? _onTerminated;
    private bool _disposed;

    /// <summary>
    /// Production constructor: marshals the termination callback onto the
    /// supplied <see cref="Dispatcher"/> so subscribers always run on the
    /// UI thread.
    /// </summary>
    public Win32ProcessLifetimeWatcher(Dispatcher dispatcher)
        : this(callback => { _ = dispatcher.BeginInvoke(callback); })
    {
    }

    /// <summary>
    /// Test-friendly constructor: callers control how (or whether) the
    /// termination callback is marshaled. Pass <c>cb =&gt; cb()</c> for
    /// fully synchronous delivery.
    /// </summary>
    public Win32ProcessLifetimeWatcher(Action<Action> marshal)
    {
        _marshal = marshal;
    }

    /// <inheritdoc />
    public void Watch(uint processId, Action onTerminated)
    {
        ArgumentNullException.ThrowIfNull(onTerminated);

        Cancel();

        SafeProcessHandle handle;
        try
        {
            handle = NativeMethods.OpenProcess(
                NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION | NativeMethods.SYNCHRONIZE,
                bInheritHandle: false,
                processId);
        }
        catch (Exception ex)
        {
            App.Log($"Win32ProcessLifetimeWatcher: OpenProcess({processId}) threw: {ex.Message}");
            return;
        }

        if (handle.IsInvalid)
        {
            // Process already gone or access denied — silently degrade
            // to the existing grace-period behavior.
            App.Log($"Win32ProcessLifetimeWatcher: OpenProcess({processId}) failed.");
            handle.Dispose();
            return;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                handle.Dispose();
                return;
            }

            _processHandle = handle;
            _onTerminated = onTerminated;
            _registeredWait = ThreadPool.RegisterWaitForSingleObject(
                waitObject: new ProcessWaitHandle(handle),
                callBack: OnProcessSignaled,
                state: null,
                millisecondsTimeOutInterval: Timeout.Infinite,
                executeOnlyOnce: true);
        }
    }

    /// <inheritdoc />
    public void Cancel()
    {
        RegisteredWaitHandle? wait;
        SafeProcessHandle? handle;

        lock (_gate)
        {
            wait = _registeredWait;
            handle = _processHandle;
            _registeredWait = null;
            _processHandle = null;
            _onTerminated = null;
        }

        // Unregister(null) waits for in-flight callbacks to complete on
        // the calling thread only if asked, and is safe to call after
        // the wait already fired. Passing a non-null handle would let
        // us be notified when unregister is complete; we don't need that.
        wait?.Unregister(null);
        handle?.Dispose();
    }

    private void OnProcessSignaled(object? state, bool timedOut)
    {
        Action? callback;
        lock (_gate)
        {
            // executeOnlyOnce==true means we won't be called again, but
            // a Cancel() racing the kernel signal may have just cleared
            // _onTerminated. Honor the cancel.
            callback = _onTerminated;
            _onTerminated = null;
        }

        if (callback is null)
        {
            return;
        }

        // Hand the callback to the marshaler — production wraps it in
        // dispatcher.BeginInvoke so subscribers run on the UI thread,
        // matching the existing IStateSource event contract; tests can
        // run it synchronously.
        _marshal(callback);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
        }

        Cancel();
    }

    /// <summary>
    /// Adapter that exposes a <see cref="SafeProcessHandle"/> as a
    /// <see cref="WaitHandle"/> without taking ownership of the handle.
    /// </summary>
    /// <remarks>
    /// <see cref="ThreadPool.RegisterWaitForSingleObject(WaitHandle, WaitOrTimerCallback, object?, int, bool)"/>
    /// requires a <see cref="WaitHandle"/>. We construct a synthetic one
    /// over the process handle's native handle. <c>SafeWaitHandle</c> is
    /// constructed with <c>ownsHandle: false</c> so disposing the wait
    /// handle does not close the process handle out from under us — the
    /// <see cref="SafeProcessHandle"/> remains the sole owner.
    /// </remarks>
    private sealed class ProcessWaitHandle : WaitHandle
    {
        public ProcessWaitHandle(SafeProcessHandle processHandle)
        {
            SafeWaitHandle = new Microsoft.Win32.SafeHandles.SafeWaitHandle(
                processHandle.DangerousGetHandle(), ownsHandle: false);
        }
    }
}
