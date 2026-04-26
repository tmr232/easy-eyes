using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Forms = System.Windows.Forms;

namespace EasyEyes;

/// <summary>
/// Manages animated glowing border windows across all monitors. Provides
/// a persistent border (for the arming state) and a bloom-and-fade
/// finale (for capture-success and capture-cleared/rejected feedback).
/// </summary>
/// <remarks>
/// <para>
/// Borders are rendered by <see cref="BorderFlashWindow"/> as four
/// trapezoidal polygons filled with linear gradients (issue #1 in
/// <c>issues-with-dnd.md</c>). The manager owns one window per monitor
/// for the active border and tracks any windows that are currently
/// fading out so that a new <see cref="ShowPersistent"/> call can kill
/// them immediately rather than letting two animations stack.
/// </para>
/// </remarks>
public sealed class BorderFlashManager : IDndFlashFeedback, IDisposable
{
    /// <summary>Amber — shown persistently while the settle timer is running.</summary>
    public static readonly Color ArmingColor = (Color)ColorConverter.ConvertFromString("#FFA500");

    /// <summary>Green — bloom-and-fade when the foreground app is captured.</summary>
    public static readonly Color LockedColor = (Color)ColorConverter.ConvertFromString("#00CC00");

    /// <summary>Red — bloom-and-fade when DND clears or capture is rejected.</summary>
    public static readonly Color ClearedColor = (Color)ColorConverter.ConvertFromString("#CC0000");

    /// <summary>
    /// Currently visible borders (one per monitor) — either persistent
    /// (post-arming fade-in) or in-progress with a bloom finale running.
    /// </summary>
    private readonly List<BorderFlashWindow> _activeWindows = [];

    /// <summary>
    /// Borders that are no longer the "current" border but are still
    /// finishing their fade-out / bloom animation. Tracked so a new
    /// <see cref="ShowPersistent"/> can close them immediately.
    /// </summary>
    private readonly List<BorderFlashWindow> _finishingWindows = [];

    private bool _disposed;

    /// <inheritdoc />
    public void ShowPersistent(Color color)
    {
        // Persistent borders should never compete with finishing
        // animations from a previous border. Kill anything still on
        // screen.
        KillAll();

        foreach (var screen in Forms.Screen.AllScreens)
        {
            var window = new BorderFlashWindow(screen, color);
            window.Show();
            window.FadeIn();
            _activeWindows.Add(window);
        }
    }

    /// <inheritdoc />
    public void BloomAndFade(Color color)
    {
        // A previous bloom that is still finishing should be discarded
        // immediately so we don't end up with stacked animations.
        KillFinishing();

        if (_activeWindows.Count == 0)
        {
            // Nothing currently on screen — create fresh windows in the
            // target color, fade them in, and immediately start the
            // bloom finale on top.
            foreach (var screen in Forms.Screen.AllScreens)
            {
                var window = new BorderFlashWindow(screen, color);
                window.Show();
                window.FadeIn();
                window.BloomAndFade(color);
                _finishingWindows.Add(window);
            }
        }
        else
        {
            // Reuse the active windows: cross-fade their color and play
            // the bloom finale. Move them to the finishing list; the
            // window closes itself on completion and we remove on Closed.
            foreach (var window in _activeWindows)
            {
                window.BloomAndFade(color);
                window.Closed += OnFinishingWindowClosed;
                _finishingWindows.Add(window);
            }

            _activeWindows.Clear();
        }
    }

    /// <inheritdoc />
    public void Hide()
    {
        foreach (var window in _activeWindows)
        {
            window.FadeOutAndClose();
            window.Closed += OnFinishingWindowClosed;
            _finishingWindows.Add(window);
        }

        _activeWindows.Clear();
    }

    private void OnFinishingWindowClosed(object? sender, EventArgs e)
    {
        if (sender is BorderFlashWindow window)
        {
            _finishingWindows.Remove(window);
        }
    }

    /// <summary>
    /// Closes all active and finishing windows immediately (no fade).
    /// </summary>
    private void KillAll()
    {
        foreach (var window in _activeWindows)
        {
            window.CloseImmediately();
        }

        _activeWindows.Clear();

        KillFinishing();
    }

    private void KillFinishing()
    {
        foreach (var window in _finishingWindows)
        {
            window.CloseImmediately();
        }

        _finishingWindows.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        KillAll();
        GC.SuppressFinalize(this);
    }
}
