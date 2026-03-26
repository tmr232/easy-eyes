using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace EasyEyes;

public sealed class SessionNotificationListener : IDisposable
{
    private const int WM_WTSSESSION_CHANGE = 0x02B1;
    private const int WTS_SESSION_LOCK = 0x7;
    private const int WTS_SESSION_UNLOCK = 0x8;
    private const int NOTIFY_FOR_THIS_SESSION = 0x0;

    [DllImport("wtsapi32.dll")]
    private static extern bool WTSRegisterSessionNotification(IntPtr hWnd, int dwFlags);

    [DllImport("wtsapi32.dll")]
    private static extern bool WTSUnRegisterSessionNotification(IntPtr hWnd);

    private readonly IntPtr _hwnd;

    public event EventHandler? SessionLocked;
    public event EventHandler? SessionUnlocked;

    public SessionNotificationListener(Window window)
    {
        _hwnd = new WindowInteropHelper(window).Handle;
        var source = HwndSource.FromHwnd(_hwnd);
        source?.AddHook(WndProc);
        WTSRegisterSessionNotification(_hwnd, NOTIFY_FOR_THIS_SESSION);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_WTSSESSION_CHANGE)
        {
            var sessionEvent = wParam.ToInt32();
            switch (sessionEvent)
            {
                case WTS_SESSION_LOCK:
                    SessionLocked?.Invoke(this, EventArgs.Empty);
                    break;
                case WTS_SESSION_UNLOCK:
                    SessionUnlocked?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        WTSUnRegisterSessionNotification(_hwnd);
    }
}
