using System;
using Microsoft.Win32;

namespace EasyEyes;

/// <summary>
/// Manages the per-user "Run at logon" registry entry that controls whether
/// EasyEyes is launched automatically when the current Windows user signs in.
/// </summary>
/// <remarks>
/// We use <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c> rather
/// than the Startup folder, HKLM, or Task Scheduler because:
/// it requires no admin rights, is per-user, runs after interactive logon
/// (which is what a tray app needs), and is trivial to toggle from a
/// running process.
///
/// <see cref="IsEnabled"/> requires the registered path to exactly match the
/// currently running executable. If the user moves EasyEyes.exe, the menu
/// will honestly report "off" for the new location, leaving the stale entry
/// in place until the user re-toggles. This is preferred over silently
/// rewriting the registry value on every launch, which would re-enable
/// autostart even after the user disabled it via Task Manager / msconfig.
/// </remarks>
public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "EasyEyes";

    /// <summary>
    /// True when the Run entry exists and points at the currently executing
    /// process image. False if the entry is missing, points elsewhere, or
    /// cannot be read.
    /// </summary>
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            if (key?.GetValue(RunValueName) is not string value)
                return false;

            var registered = value.Trim('"');
            var current = Environment.ProcessPath;
            return current is not null
                && string.Equals(registered, current, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            App.Log($"StartupManager.IsEnabled failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Writes or removes the Run entry. When enabling, the value is set to
    /// the quoted path of the currently executing process so that paths
    /// containing spaces are handled correctly by the shell.
    /// </summary>
    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (key is null)
            {
                App.Log("StartupManager.SetEnabled: could not open or create Run key");
                return;
            }

            if (enabled)
            {
                var path = Environment.ProcessPath;
                if (string.IsNullOrEmpty(path))
                {
                    App.Log("StartupManager.SetEnabled: Environment.ProcessPath was null");
                    return;
                }

                key.SetValue(RunValueName, $"\"{path}\"");
            }
            else
            {
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            App.Log($"StartupManager.SetEnabled({enabled}) failed: {ex.Message}");
        }
    }
}
