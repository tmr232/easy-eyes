using System.Diagnostics;
using EasyEyes;

namespace EasyEyes.Tests;

/// <summary>
/// Integration tests for <see cref="Win32ProcessLifetimeWatcher"/>. Each
/// test spawns a real child process (<c>cmd.exe</c>) so the kernel-wait
/// path is exercised end-to-end. Tests are configured with a synchronous
/// marshaler so the termination callback runs directly on the thread-pool
/// worker — no WPF dispatcher pumping required.
/// </summary>
public class Win32ProcessLifetimeWatcherTests
{
    /// <summary>
    /// Synchronous marshaler — passes the callback through to be invoked
    /// on whatever thread the kernel wait fires on.
    /// </summary>
    private static readonly Action<Action> SyncMarshal = cb => cb();

    private static readonly TimeSpan ShortTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SettlingDelay = TimeSpan.FromMilliseconds(200);

    private static Process StartLongRunningChild()
    {
        // 'timeout.exe' refuses to run with redirected I/O ("Input
        // redirection is not supported, exiting the process immediately"),
        // and we want to capture stdout to keep test output tidy. ping
        // tolerates redirection and lives for roughly N seconds with
        // -n N (a 1-second interval between pings against the loopback).
        var psi = new ProcessStartInfo("ping.exe", "-n 30 127.0.0.1")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        return Process.Start(psi)!;
    }

    [Fact]
    public void Watch_OnLiveProcess_DoesNotFireTerminated_WhileAlive()
    {
        using var child = StartLongRunningChild();
        try
        {
            using var watcher = new Win32ProcessLifetimeWatcher(SyncMarshal);
            using var fired = new ManualResetEventSlim();
            watcher.Watch((uint)child.Id, () => fired.Set());

            Assert.False(fired.Wait(SettlingDelay));
        }
        finally
        {
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
                child.WaitForExit();
            }
        }
    }

    [Fact]
    public void Watch_WhenProcessExits_FiresTerminatedCallback()
    {
        using var child = StartLongRunningChild();
        using var watcher = new Win32ProcessLifetimeWatcher(SyncMarshal);
        using var fired = new ManualResetEventSlim();
        watcher.Watch((uint)child.Id, () => fired.Set());

        child.Kill(entireProcessTree: true);
        child.WaitForExit();

        Assert.True(fired.Wait(ShortTimeout));
    }

    [Fact]
    public void Cancel_BeforeTermination_PreventsCallback()
    {
        using var child = StartLongRunningChild();
        try
        {
            using var watcher = new Win32ProcessLifetimeWatcher(SyncMarshal);
            using var fired = new ManualResetEventSlim();
            watcher.Watch((uint)child.Id, () => fired.Set());

            watcher.Cancel();

            child.Kill(entireProcessTree: true);
            child.WaitForExit();

            Assert.False(fired.Wait(SettlingDelay));
        }
        finally
        {
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
                child.WaitForExit();
            }
        }
    }

    [Fact]
    public void Cancel_IsIdempotent()
    {
        using var watcher = new Win32ProcessLifetimeWatcher(SyncMarshal);

        watcher.Cancel();
        watcher.Cancel();
        watcher.Cancel();

        // No assertion beyond "did not throw".
    }

    [Fact]
    public void Watch_WhenInvalidPid_DoesNotFireTerminated_AndDoesNotThrow()
    {
        // PID 0 is the "system idle" pseudo-process; OpenProcess refuses
        // it. Confirm the watcher copes cleanly: no exception, no
        // synthesized termination signal. (Watching an already-dead PID
        // is not exercised here because the kernel object can persist
        // while a parent holds a handle, in which case OpenProcess
        // succeeds and the wait correctly fires immediately. The DND
        // code only ever watches a process that is alive at capture
        // time, so that scenario is not a real-world concern.)
        using var watcher = new Win32ProcessLifetimeWatcher(SyncMarshal);
        using var fired = new ManualResetEventSlim();

        watcher.Watch(processId: 0, () => fired.Set());

        Assert.False(fired.Wait(SettlingDelay));
    }

    [Fact]
    public void Watch_AfterCancel_StartsFreshWatch()
    {
        using var child1 = StartLongRunningChild();
        using var child2 = StartLongRunningChild();
        try
        {
            using var watcher = new Win32ProcessLifetimeWatcher(SyncMarshal);
            using var firedFor1 = new ManualResetEventSlim();
            using var firedFor2 = new ManualResetEventSlim();

            watcher.Watch((uint)child1.Id, () => firedFor1.Set());
            // Re-watch (implicit cancel of previous watch).
            watcher.Watch((uint)child2.Id, () => firedFor2.Set());

            child1.Kill(entireProcessTree: true);
            child1.WaitForExit();

            // First watch was implicitly cancelled — its callback must
            // not fire even though child1 has died.
            Assert.False(firedFor1.Wait(SettlingDelay));

            child2.Kill(entireProcessTree: true);
            child2.WaitForExit();

            Assert.True(firedFor2.Wait(ShortTimeout));
        }
        finally
        {
            foreach (var c in new[] { child1, child2 })
            {
                if (!c.HasExited)
                {
                    c.Kill(entireProcessTree: true);
                    c.WaitForExit();
                }
            }
        }
    }

    [Fact]
    public void Dispose_CancelsWatch()
    {
        using var child = StartLongRunningChild();
        try
        {
            using var fired = new ManualResetEventSlim();
            var watcher = new Win32ProcessLifetimeWatcher(SyncMarshal);
            watcher.Watch((uint)child.Id, () => fired.Set());

            watcher.Dispose();

            child.Kill(entireProcessTree: true);
            child.WaitForExit();

            Assert.False(fired.Wait(SettlingDelay));
        }
        finally
        {
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
                child.WaitForExit();
            }
        }
    }
}
