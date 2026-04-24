using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace EasyEyes;

/// <summary>
/// Centralized Win32 P/Invoke declarations with proper error handling.
/// </summary>
internal static class NativeMethods
{
    internal const int GWL_EXSTYLE = -20;
    internal const int WS_EX_TRANSPARENT = 0x00000020;
    internal const int WS_EX_TOOLWINDOW = 0x00000080;
    internal const int WS_EX_LAYERED = 0x00080000;
    internal const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr RegisterPowerSettingNotification(IntPtr hRecipient, ref Guid PowerSettingGuid, int Flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterPowerSettingNotification(IntPtr handle);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WTSRegisterSessionNotification(IntPtr hWnd, int dwFlags);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WTSUnRegisterSessionNotification(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct POWERBROADCAST_SETTING
    {
        public Guid PowerSetting;
        public uint DataLength;
        public byte Data;
    }

    /// <summary>
    /// Calls <see cref="GetWindowLong"/> with the documented error-check pattern:
    /// clears last error, calls the function, then checks for failure (return value
    /// of zero with a nonzero last error).
    /// </summary>
    /// <exception cref="Win32Exception">Thrown when GetWindowLong fails.</exception>
    internal static int GetWindowLongChecked(IntPtr hwnd, int index)
    {
        Marshal.SetLastPInvokeError(0);
        int result = GetWindowLong(hwnd, index);
        if (result == 0)
        {
            int error = Marshal.GetLastPInvokeError();
            if (error != 0)
                throw new Win32Exception(error);
        }

        return result;
    }

    /// <summary>
    /// Calls <see cref="SetWindowLong"/> with the documented error-check pattern:
    /// clears last error, calls the function, then checks for failure (return value
    /// of zero with a nonzero last error).
    /// </summary>
    /// <exception cref="Win32Exception">Thrown when SetWindowLong fails.</exception>
    internal static void SetWindowLongChecked(IntPtr hwnd, int index, int newStyle)
    {
        Marshal.SetLastPInvokeError(0);
        int result = SetWindowLong(hwnd, index, newStyle);
        if (result == 0)
        {
            int error = Marshal.GetLastPInvokeError();
            if (error != 0)
                throw new Win32Exception(error);
        }
    }
}
