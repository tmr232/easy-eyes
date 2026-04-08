using Microsoft.Win32;

namespace EasyEyes;

/// <summary>
/// Monitors whether the camera or microphone are currently in use by any application.
/// Reads the Windows CapabilityAccessManager registry keys (both HKLM and HKCU) to
/// detect active sessions (where LastUsedTimeStart > LastUsedTimeStop).
/// Provides static properties for one-shot queries and events for change notifications
/// via polling.
/// </summary>
/// <remarks>
/// Events fire on a ThreadPool thread; callers must dispatch to the UI thread if needed.
/// </remarks>
public sealed class MediaDeviceMonitor : IDisposable
{
    private static readonly RegistryHive[] Hives = [RegistryHive.LocalMachine, RegistryHive.CurrentUser];

    private readonly Timer _pollTimer;
    private bool _lastCameraInUse;
    private bool _lastMicrophoneInUse;
    private bool _disposed;

    public static bool IsCameraInUse => CheckDeviceInUse("webcam");
    public static bool IsMicrophoneInUse => CheckDeviceInUse("microphone");

    public event EventHandler? CameraActivated;
    public event EventHandler? CameraDeactivated;
    public event EventHandler? MicrophoneActivated;
    public event EventHandler? MicrophoneDeactivated;

    public MediaDeviceMonitor(TimeSpan pollInterval)
    {
        _lastCameraInUse = IsCameraInUse;
        _lastMicrophoneInUse = IsMicrophoneInUse;
        _pollTimer = new Timer(Poll, null, pollInterval, pollInterval);
    }

    private void Poll(object? state)
    {
        var cameraInUse = IsCameraInUse;
        var microphoneInUse = IsMicrophoneInUse;

        if (cameraInUse != _lastCameraInUse)
        {
            _lastCameraInUse = cameraInUse;
            if (cameraInUse)
                CameraActivated?.Invoke(this, EventArgs.Empty);
            else
                CameraDeactivated?.Invoke(this, EventArgs.Empty);
        }

        if (microphoneInUse != _lastMicrophoneInUse)
        {
            _lastMicrophoneInUse = microphoneInUse;
            if (microphoneInUse)
                MicrophoneActivated?.Invoke(this, EventArgs.Empty);
            else
                MicrophoneDeactivated?.Invoke(this, EventArgs.Empty);
        }
    }

    private static bool CheckDeviceInUse(string capability)
    {
        var path = $@"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\{capability}";

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

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _pollTimer.Dispose();
    }
}
