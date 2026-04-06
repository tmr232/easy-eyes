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

    private const int WM_POWERBROADCAST = 0x0218;
    private const int PBT_POWERSETTINGCHANGE = 0x8013;

    private static readonly Guid GUID_CONSOLE_DISPLAY_STATE = new("6fe69556-704a-47a0-8f24-c28d936fda47");

    [DllImport("wtsapi32.dll")]
    private static extern bool WTSRegisterSessionNotification(IntPtr hWnd, int dwFlags);

    [DllImport("wtsapi32.dll")]
    private static extern bool WTSUnRegisterSessionNotification(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr RegisterPowerSettingNotification(IntPtr hRecipient, ref Guid PowerSettingGuid, int Flags);

    [DllImport("user32.dll")]
    private static extern bool UnregisterPowerSettingNotification(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct POWERBROADCAST_SETTING
    {
        public Guid PowerSetting;
        public uint DataLength;
        public byte Data;
    }

    private readonly IntPtr _hwnd;
    private readonly IntPtr _powerNotification;

    public event EventHandler? SessionLocked;
    public event EventHandler? SessionUnlocked;
    public event EventHandler? DisplayOff;
    public event EventHandler? DisplayOn;

    public SessionNotificationListener(Window window)
    {
        _hwnd = new WindowInteropHelper(window).Handle;
        var source = HwndSource.FromHwnd(_hwnd);
        source?.AddHook(WndProc);
        WTSRegisterSessionNotification(_hwnd, NOTIFY_FOR_THIS_SESSION);

        var guid = GUID_CONSOLE_DISPLAY_STATE;
        _powerNotification = RegisterPowerSettingNotification(_hwnd, ref guid, 0);
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
        else if (msg == WM_POWERBROADCAST && wParam.ToInt32() == PBT_POWERSETTINGCHANGE)
        {
            var setting = Marshal.PtrToStructure<POWERBROADCAST_SETTING>(lParam);
            if (setting.PowerSetting == GUID_CONSOLE_DISPLAY_STATE)
            {
                if (setting.Data == 0)
                    DisplayOff?.Invoke(this, EventArgs.Empty);
                else if (setting.Data == 1)
                    DisplayOn?.Invoke(this, EventArgs.Empty);
            }
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        WTSUnRegisterSessionNotification(_hwnd);
        if (_powerNotification != IntPtr.Zero)
            UnregisterPowerSettingNotification(_powerNotification);
    }
}
