using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Forms = System.Windows.Forms;
using Point = System.Windows.Point;

namespace EasyEyes;

public partial class OverlayWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private const double SpotlightRadius = 200;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private readonly RadialGradientBrush _spotlightMask;

    public OverlayWindow(Forms.Screen screen)
    {
        _spotlightMask = new RadialGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            RadiusX = SpotlightRadius,
            RadiusY = SpotlightRadius,
            GradientStops =
            {
                new GradientStop(Colors.Transparent, 0.0),
                new GradientStop(Colors.White, 1.0)
            }
        };

        InitializeComponent();
        Overlay.OpacityMask = _spotlightMask;
        Overlay.Opacity = 0;

        PositionOnScreen(screen);

        SourceInitialized += OnSourceInitialized;
        CompositionTarget.Rendering += OnRendering;
    }

    public void PositionOnScreen(Forms.Screen screen)
    {
        var bounds = screen.Bounds;
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        // Leave 1px uncovered so Windows doesn't treat this as a
        // fullscreen app and hide the taskbar.
        Height = bounds.Height - 1;
    }

    public void ShowOverlay()
    {
        var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromSeconds(5));
        Overlay.BeginAnimation(OpacityProperty, fadeIn);
    }

    public void HideOverlay()
    {
        Overlay.BeginAnimation(OpacityProperty, null);
        Overlay.Opacity = 0;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (!GetCursorPos(out var pt)) return;

        var wpfPoint = PointFromScreen(new Point(pt.X, pt.Y));
        _spotlightMask.Center = wpfPoint;
        _spotlightMask.GradientOrigin = wpfPoint;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        _ = SetWindowLong(hwnd, GWL_EXSTYLE,
            exStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
    }

    protected override void OnClosed(EventArgs e)
    {
        CompositionTarget.Rendering -= OnRendering;
        base.OnClosed(e);
    }
}
