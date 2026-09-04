using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Forms = System.Windows.Forms;
using Point = System.Windows.Point;

namespace EasyEyes;

/// <summary>
/// Covers the final physical row that the main overlay deliberately leaves
/// uncovered to avoid Windows' fullscreen taskbar heuristic. It is static:
/// no spotlight or cursor work is performed here.
/// </summary>
internal sealed class BottomEdgeWindow : Window
{
    private readonly Forms.Screen _screen;

    public BottomEdgeWindow(Forms.Screen screen)
    {
        _screen = screen;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4A, 0x90, 0xD9));
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Opacity = 0;
        SourceInitialized += OnSourceInitialized;
    }

    public void ShowAndFadeIn(TimeSpan duration)
    {
        Show();
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration));
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int exStyle = NativeMethods.GetWindowLongChecked(hwnd, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowLongChecked(hwnd, NativeMethods.GWL_EXSTYLE,
                exStyle | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED |
                    NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            App.FatalError("Failed to initialize overlay bottom edge", ex);
            return;
        }

        PositionOnScreen();
    }

    private void PositionOnScreen()
    {
        var bounds = _screen.Bounds;
        var source = PresentationSource.FromVisual(this);
        var transform = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var topLeft = transform.Transform(new Point(bounds.Left, bounds.Bottom - 1));
        var bottomRight = transform.Transform(new Point(bounds.Right, bounds.Bottom));

        Left = topLeft.X;
        Top = topLeft.Y;
        Width = bottomRight.X - topLeft.X;
        Height = bottomRight.Y - topLeft.Y;
    }
}
