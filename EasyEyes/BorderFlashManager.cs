using System.Windows.Threading;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Forms = System.Windows.Forms;

namespace EasyEyes;

/// <summary>
/// Manages colored border flash windows across all monitors. Provides
/// persistent borders (for the arming state) and timed flashes (for
/// locked/cleared feedback).
/// </summary>
public sealed class BorderFlashManager : IDndFlashFeedback, IDisposable
{
    /// <summary>Amber — shown persistently while the settle timer is running.</summary>
    public static readonly Color ArmingColor = (Color)ColorConverter.ConvertFromString("#FFA500");

    /// <summary>Green — flashed briefly when the foreground app is captured.</summary>
    public static readonly Color LockedColor = (Color)ColorConverter.ConvertFromString("#00CC00");

    /// <summary>Red — flashed briefly when DND clears.</summary>
    public static readonly Color ClearedColor = (Color)ColorConverter.ConvertFromString("#CC0000");

    private static readonly TimeSpan FlashDuration = TimeSpan.FromSeconds(1);

    private readonly List<BorderFlashWindow> _windows = [];
    private readonly DispatcherTimer _flashTimer;
    private bool _disposed;

    public BorderFlashManager()
    {
        _flashTimer = new DispatcherTimer();
        _flashTimer.Tick += (_, _) =>
        {
            _flashTimer.Stop();
            Hide();
        };
    }

    /// <summary>
    /// Shows a persistent border of the given color on all monitors.
    /// The border stays visible until <see cref="Hide"/> or another
    /// Show method is called.
    /// </summary>
    public void ShowPersistent(Color color)
    {
        Hide();
        CreateWindows(color);
    }

    /// <summary>
    /// Shows a border on all monitors for <see cref="FlashDuration"/>,
    /// then auto-hides.
    /// </summary>
    public void ShowFlash(Color color)
    {
        Hide();
        CreateWindows(color);
        _flashTimer.Interval = FlashDuration;
        _flashTimer.Start();
    }

    /// <summary>
    /// Closes all border windows immediately.
    /// </summary>
    public void Hide()
    {
        _flashTimer.Stop();
        foreach (var window in _windows)
        {
            window.Close();
        }

        _windows.Clear();
    }

    private void CreateWindows(Color color)
    {
        foreach (var screen in Forms.Screen.AllScreens)
        {
            var window = new BorderFlashWindow(screen, color);
            window.Show();
            _windows.Add(window);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Hide();
        GC.SuppressFinalize(this);
    }
}
