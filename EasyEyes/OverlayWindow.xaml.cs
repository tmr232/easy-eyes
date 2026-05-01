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
    private readonly Forms.Screen _screen;

    public OverlayWindow(Forms.Screen screen)
    {
        _screen = screen;

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

        SourceInitialized += OnSourceInitialized;
    }

    private void PositionOnScreen()
    {
        var bounds = _screen.Bounds;
        var source = PresentationSource.FromVisual(this);
        var transform = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var topLeft = transform.Transform(new Point(bounds.Left, bounds.Top));
        var size = transform.Transform(new Point(bounds.Width, bounds.Height));

        Left = topLeft.X;
        Top = topLeft.Y;
        Width = size.X;
        // Leave 1px (physical) uncovered so Windows doesn't treat this as a
        // fullscreen app and hide the taskbar.
        Height = size.Y - 1;
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
        // During window teardown (e.g. app shutdown while the overlay is
        // visible), the HwndSource can be disposed before this handler is
        // unsubscribed in OnClosed. PointFromScreen then throws because the
        // Visual is no longer connected to a PresentationSource. Skip the
        // frame in that case.
        if (PresentationSource.FromVisual(this) is null) return;

        if (!NativeMethods.GetCursorPos(out var pt)) return;

        var wpfPoint = PointFromScreen(new Point(pt.X, pt.Y));
        _spotlightMask.Center = wpfPoint;
        _spotlightMask.GradientOrigin = wpfPoint;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int exStyle = NativeMethods.GetWindowLongChecked(hwnd, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowLongChecked(hwnd, NativeMethods.GWL_EXSTYLE,
                exStyle | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            App.FatalError("Failed to initialize overlay window", ex);
        }

        PositionOnScreen();
    }

    protected override void OnClosed(EventArgs e)
    {
        CompositionTarget.Rendering -= OnRendering;
        base.OnClosed(e);
    }
}
