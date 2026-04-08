using Windows.Media.Control;

namespace EasyEyes;

/// <summary>
/// Monitors whether media (audio/video) is currently playing on the system.
/// Uses the Windows SMTC (System Media Transport Controls) API to detect active
/// playback sessions. Provides an instance property for one-shot queries and events
/// for change notifications via polling.
/// </summary>
/// <remarks>
/// Events fire on a ThreadPool thread; callers must dispatch to the UI thread if needed.
/// </remarks>
public sealed class MediaPlaybackMonitor : IDisposable
{
    private readonly GlobalSystemMediaTransportControlsSessionManager _sessionManager;
    private readonly Timer _pollTimer;
    private bool _lastIsPlaying;
    private bool _disposed;

    public bool IsPlaying => CheckIsPlaying();

    public event EventHandler? PlaybackStarted;
    public event EventHandler? PlaybackStopped;

    private MediaPlaybackMonitor(
        GlobalSystemMediaTransportControlsSessionManager sessionManager,
        TimeSpan pollInterval
    )
    {
        _sessionManager = sessionManager;
        _lastIsPlaying = CheckIsPlaying();
        _pollTimer = new Timer(Poll, null, pollInterval, pollInterval);
    }

    public static async Task<MediaPlaybackMonitor> CreateAsync(TimeSpan pollInterval)
    {
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        return new MediaPlaybackMonitor(manager, pollInterval);
    }

    private void Poll(object? state)
    {
        var isPlaying = CheckIsPlaying();

        if (isPlaying != _lastIsPlaying)
        {
            _lastIsPlaying = isPlaying;
            if (isPlaying)
                PlaybackStarted?.Invoke(this, EventArgs.Empty);
            else
                PlaybackStopped?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool CheckIsPlaying()
    {
        var sessions = _sessionManager.GetSessions();
        foreach (var session in sessions)
        {
            var playback = session.GetPlaybackInfo();
            if (playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                return true;
        }

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
