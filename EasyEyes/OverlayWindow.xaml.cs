using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Forms = System.Windows.Forms;
using Point = System.Windows.Point;

namespace EasyEyes;

public partial class OverlayWindow : Window
{
    private const double SpotlightRadius = 200;

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
        CompositionTarget.Rendering += OnRendering;
        var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromSeconds(5));
        var borderFadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromSeconds(2.5));
        Overlay.BeginAnimation(OpacityProperty, fadeIn);
        ScreenBorder.BeginAnimation(OpacityProperty, borderFadeIn);
    }

    public void HideOverlay()
    {
        CompositionTarget.Rendering -= OnRendering;
        Overlay.BeginAnimation(OpacityProperty, null);
        Overlay.Opacity = 0;
        ScreenBorder.BeginAnimation(OpacityProperty, null);
        ScreenBorder.Opacity = 0;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (!NativeMethods.GetCursorPos(out var pt)) return;

        var wpfPoint = PointFromScreen(new Point(pt.X, pt.Y));
        _spotlightMask.Center = wpfPoint;
        _spotlightMask.GradientOrigin = wpfPoint;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int exStyle = NativeMethods.GetWindowLongChecked(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLongChecked(hwnd, NativeMethods.GWL_EXSTYLE,
            exStyle | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE);
    }

    protected override void OnClosed(EventArgs e)
    {
        CompositionTarget.Rendering -= OnRendering;
        base.OnClosed(e);
    }
}
