using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using Point = System.Windows.Point;

namespace EasyEyes;

public sealed class OverlayManager : IDisposable
{
    // A reminder overlay does not need display-refresh-rate cursor tracking.
    // Polling at 30 Hz is visually smooth while preventing every visible
    // overlay from invalidating its full screen-sized opacity mask each frame.
    private static readonly TimeSpan CursorPollInterval = TimeSpan.FromMilliseconds(33);
    private const double CursorMovementThreshold = 2;

    private readonly List<OverlayWindow> _windows = [];
    private readonly DispatcherTimer _cursorPollTimer;
    private bool _overlayVisible;
    private Point? _lastCursorPosition;

    public OverlayManager()
    {
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        _cursorPollTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = CursorPollInterval
        };
        _cursorPollTimer.Tick += OnCursorPollTimerTick;
    }

    public void ShowAll()
    {
        if (_windows.Count > 0)
            HideAll();

        _overlayVisible = true;
        foreach (var screen in Forms.Screen.AllScreens)
        {
            var window = new OverlayWindow(screen);
            window.Show();
            window.ShowOverlay();
            _windows.Add(window);
        }

        UpdateSpotlightForCursor();
        _cursorPollTimer.Start();
    }

    public void HideAll()
    {
        StopCursorTracking();
        _overlayVisible = false;
        foreach (var window in _windows)
            window.Close();
        _windows.Clear();
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        App.Log("DisplaySettingsChanged: refreshing overlay windows");
        RefreshWindows();
    }

    private void RefreshWindows()
    {
        StopCursorTracking();
        foreach (var window in _windows)
            window.Close();
        _windows.Clear();

        if (_overlayVisible)
            ShowAll();
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        StopCursorTracking();
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        foreach (var window in _windows)
            window.Close();
        _windows.Clear();
    }

    private void OnCursorPollTimerTick(object? sender, EventArgs e)
    {
        UpdateSpotlightForCursor();
    }

    private void UpdateSpotlightForCursor()
    {
        if (!NativeMethods.GetCursorPos(out var cursor))
            return;

        var cursorPosition = new Point(cursor.X, cursor.Y);
        if (_lastCursorPosition is { } lastPosition &&
            DistanceSquared(cursorPosition, lastPosition) < CursorMovementThreshold * CursorMovementThreshold)
        {
            return;
        }

        _lastCursorPosition = cursorPosition;
        foreach (var window in _windows)
            window.UpdateSpotlight(cursorPosition);
    }

    private void StopCursorTracking()
    {
        _cursorPollTimer.Stop();
        _lastCursorPosition = null;
    }

    private static double DistanceSquared(Point first, Point second)
    {
        var horizontal = first.X - second.X;
        var vertical = first.Y - second.Y;
        return horizontal * horizontal + vertical * vertical;
    }
}
