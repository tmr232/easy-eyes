using System.Windows.Threading;
using EasyEyes;

namespace EasyEyes.Tests;

/// <summary>
/// Unit tests for the wiring between <see cref="ForegroundWindowStateSource"/>
/// and an injected <see cref="IProcessLifetimeWatcher"/>.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="IForegroundCapture.TryCapture"/> path itself calls native
/// Win32 APIs (<c>GetForegroundWindow</c>, <c>GetWindowThreadProcessId</c>)
/// and is therefore not unit-testable here without an additional seam — the
/// same reason the rest of the polling-based behavior in this class is not
/// covered by unit tests. The wiring of <c>TryCapture → Watch</c> is verified
/// end-to-end by the <see cref="Win32ProcessLifetimeWatcher"/> integration
/// tests in a later step (which spawn a real child process). What we test
/// here is the parts that don't cross the OS boundary: the no-op watcher,
/// teardown, and constructor wiring.
/// </para>
/// </remarks>
public class ForegroundWindowStateSourceTests
{
    private static Dispatcher TestDispatcher => Dispatcher.CurrentDispatcher;

    [Fact]
    public void NullProcessLifetimeWatcher_Watch_DoesNotThrow()
    {
        var watcher = new NullProcessLifetimeWatcher();
        watcher.Watch(1234, () => { });
    }

    [Fact]
    public void NullProcessLifetimeWatcher_Cancel_DoesNotThrow()
    {
        var watcher = new NullProcessLifetimeWatcher();
        watcher.Cancel();
    }

    [Fact]
    public void NullProcessLifetimeWatcher_DoesNotInvokeCallback()
    {
        var watcher = new NullProcessLifetimeWatcher();
        var called = false;
        watcher.Watch(1234, () => called = true);

        Assert.False(called);
    }

    [Fact]
    public void Release_CancelsLifetimeWatcher()
    {
        var watcher = new FakeProcessLifetimeWatcher();
        var source = new ForegroundWindowStateSource(
            TimeSpan.FromSeconds(1), TestDispatcher, watcher);

        source.Release();

        Assert.Equal(1, watcher.CancelCount);
    }

    [Fact]
    public void Dispose_CancelsLifetimeWatcher()
    {
        var watcher = new FakeProcessLifetimeWatcher();
        var source = new ForegroundWindowStateSource(
            TimeSpan.FromSeconds(1), TestDispatcher, watcher);

        source.Dispose();

        Assert.Equal(1, watcher.CancelCount);
    }

    [Fact]
    public void Dispose_DisposesDisposableLifetimeWatcher()
    {
        var watcher = new FakeProcessLifetimeWatcher();
        var source = new ForegroundWindowStateSource(
            TimeSpan.FromSeconds(1), TestDispatcher, watcher);

        source.Dispose();

        Assert.True(watcher.IsDisposed);
    }
}

/// <summary>
/// Test implementation of <see cref="IProcessLifetimeWatcher"/> that records
/// calls and lets tests trigger the termination callback synchronously.
/// </summary>
public sealed class FakeProcessLifetimeWatcher : IProcessLifetimeWatcher, IDisposable
{
    private Action? _onTerminated;

    public uint? WatchedProcessId { get; private set; }
    public int WatchCount { get; private set; }
    public int CancelCount { get; private set; }
    public bool IsDisposed { get; private set; }

    public void Watch(uint processId, Action onTerminated)
    {
        WatchCount++;
        WatchedProcessId = processId;
        _onTerminated = onTerminated;
    }

    public void Cancel()
    {
        CancelCount++;
        _onTerminated = null;
        WatchedProcessId = null;
    }

    /// <summary>
    /// Helper for tests that want to register a callback without going
    /// through Watch's process-id parameter.
    /// </summary>
    public void SimulateWatch(Action callback)
    {
        Watch(processId: 0, callback);
    }

    /// <summary>
    /// Invokes the most recently registered termination callback, if any.
    /// </summary>
    public void TriggerTermination()
    {
        _onTerminated?.Invoke();
    }

    public void Dispose()
    {
        IsDisposed = true;
    }
}
