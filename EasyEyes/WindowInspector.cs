using System.Runtime.InteropServices;
using System.Text;

namespace EasyEyes;

/// <summary>
/// Pure helpers for inspecting top-level windows.
/// </summary>
internal static class WindowInspector
{
    /// <summary>
    /// Returns true when <paramref name="hwnd"/> is a "fullscreen" window: its
    /// outer rectangle exactly equals the bounds of the monitor it lives on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Used by the Do Not Disturb feature (issue #4 in
    /// <c>issues-with-dnd.md</c>) to avoid arming on a windowed app: e.g. the
    /// user goes fullscreen on a YouTube video, alt-tabs back to a windowed
    /// browser tab, and the browser process is no longer "the video player".
    /// </para>
    /// <para>
    /// Style bits (<c>WS_CAPTION</c>, etc.) are intentionally not checked
    /// because borderless fullscreen games legitimately lack them. The desktop
    /// shell windows (<c>Progman</c>, <c>WorkerW</c>, the shell window
    /// returned by <c>GetShellWindow</c>) are explicitly excluded since they
    /// always cover the primary monitor.
    /// </para>
    /// </remarks>
    public static bool IsFullscreen(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        if (hwnd == NativeMethods.GetShellWindow())
        {
            return false;
        }

        var className = GetWindowClassName(hwnd);
        if (className is "Progman" or "WorkerW")
        {
            return false;
        }

        if (!NativeMethods.GetWindowRect(hwnd, out var windowRect))
        {
            return false;
        }

        var monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        var monitorInfo = new NativeMethods.MONITORINFO
        {
            cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>(),
        };
        if (!NativeMethods.GetMonitorInfo(monitor, ref monitorInfo))
        {
            return false;
        }

        return windowRect.Left == monitorInfo.rcMonitor.Left
            && windowRect.Top == monitorInfo.rcMonitor.Top
            && windowRect.Right == monitorInfo.rcMonitor.Right
            && windowRect.Bottom == monitorInfo.rcMonitor.Bottom;
    }

    private static string GetWindowClassName(IntPtr hwnd)
    {
        var buffer = new StringBuilder(256);
        int length = NativeMethods.GetClassName(hwnd, buffer, buffer.Capacity);
        return length > 0 ? buffer.ToString(0, length) : string.Empty;
    }
}
