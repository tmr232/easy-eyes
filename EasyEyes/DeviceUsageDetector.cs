using Microsoft.Win32;

namespace EasyEyes;

/// <summary>
/// Detects whether a specific device capability (e.g., webcam or microphone)
/// is currently in use by reading Windows CapabilityAccessManager registry keys.
/// </summary>
public sealed class DeviceUsageDetector
{
    private static readonly RegistryHive[] Hives = [RegistryHive.LocalMachine, RegistryHive.CurrentUser];

    private readonly string _capability;

    public bool IsInUse => CheckInUse();

    public DeviceUsageDetector(string capability)
    {
        _capability = capability;
    }

    private bool CheckInUse()
    {
        var path = $@"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\{_capability}";

        foreach (var hive in Hives)
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
            using var root = baseKey.OpenSubKey(path);
            if (root != null && HasActiveSession(root))
                return true;
        }

        return false;
    }

    private static bool HasActiveSession(RegistryKey key)
    {
        if (IsSessionActive(key))
            return true;

        foreach (var subName in key.GetSubKeyNames())
        {
            using var sub = key.OpenSubKey(subName);
            if (sub != null && HasActiveSession(sub))
                return true;
        }

        return false;
    }

    private static bool IsSessionActive(RegistryKey key)
    {
        var startObj = key.GetValue("LastUsedTimeStart");
        var stopObj = key.GetValue("LastUsedTimeStop");

        if (startObj is long start && stopObj is long stop)
            return start > stop;

        return false;
    }
}
