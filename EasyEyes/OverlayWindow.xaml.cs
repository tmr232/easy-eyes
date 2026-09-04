using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;
using Point = System.Windows.Point;

namespace EasyEyes;

public partial class OverlayWindow : Window
{
    private const double SpotlightRadius = 200;

    private readonly RadialGradientBrush _spotlightMask;
    private readonly Forms.Screen _screen;
    private readonly BottomEdgeWindow _bottomEdgeWindow;
    private System.Windows.Media.Brush? _vignetteBrush;
    private bool _spotlightMaskApplied = true;
    private Point? _lastSpotlightPosition;

    public OverlayWindow(Forms.Screen screen)
    {
        _screen = screen;
        _bottomEdgeWindow = new BottomEdgeWindow(screen);

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
        _vignetteBrush = Overlay.Fill;
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
        var bottomRight = transform.Transform(new Point(bounds.Right, bounds.Bottom));
        var lastPhysicalRowTop = transform.Transform(new Point(bounds.Left, bounds.Bottom - 1));

        Left = topLeft.X;
        Top = topLeft.Y;
        Width = bottomRight.X - topLeft.X;
        // Keep the main overlay one physical row short so Windows does not
        // classify it as fullscreen. BottomEdgeWindow fills that row.
        Height = lastPhysicalRowTop.Y - topLeft.Y;
    }

    public void ShowOverlay()
    {
        CacheVignette();
        var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromSeconds(5));
        var borderFadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromSeconds(2.5));
        Overlay.BeginAnimation(OpacityProperty, fadeIn);
        ScreenBorder.BeginAnimation(OpacityProperty, borderFadeIn);
        _bottomEdgeWindow.ShowAndFadeIn(TimeSpan.FromSeconds(2.5));
    }

    public void HideOverlay()
    {
        Overlay.BeginAnimation(OpacityProperty, null);
        Overlay.Opacity = 0;
        ScreenBorder.BeginAnimation(OpacityProperty, null);
        ScreenBorder.Opacity = 0;
        _bottomEdgeWindow.Close();
    }

    /// <summary>
    /// Updates the spotlight only when it can affect this monitor. If the
    /// spotlight is more than its radius away, the opacity mask would be fully
    /// opaque everywhere on this window, so removing it is visually identical
    /// and avoids a full-screen mask composition pass.
    /// </summary>
    public void UpdateSpotlight(Point screenPoint)
    {
        if (PresentationSource.FromVisual(this) is null) return;

        if (!CanSpotlightAffectWindow(screenPoint))
        {
            if (_spotlightMaskApplied)
            {
                Overlay.OpacityMask = null;
                _spotlightMaskApplied = false;
                _lastSpotlightPosition = null;
            }
            return;
        }

        var wpfPoint = PointFromScreen(screenPoint);
        if (_lastSpotlightPosition == wpfPoint && _spotlightMaskApplied)
            return;

        if (!_spotlightMaskApplied)
        {
            Overlay.OpacityMask = _spotlightMask;
            _spotlightMaskApplied = true;
        }

        _spotlightMask.Center = wpfPoint;
        _spotlightMask.GradientOrigin = wpfPoint;
        _lastSpotlightPosition = wpfPoint;
    }

    private bool CanSpotlightAffectWindow(Point screenPoint)
    {
        var bounds = _screen.Bounds;
        var nearestX = Math.Clamp(screenPoint.X, bounds.Left, bounds.Right);
        var nearestY = Math.Clamp(screenPoint.Y, bounds.Top, bounds.Bottom);
        var horizontal = screenPoint.X - nearestX;
        var vertical = screenPoint.Y - nearestY;
        var radiusInPhysicalPixels = SpotlightRadius * GetDeviceScale();

        return horizontal * horizontal + vertical * vertical <= radiusInPhysicalPixels * radiusInPhysicalPixels;
    }

    private double GetDeviceScale()
    {
        var source = PresentationSource.FromVisual(this);
        return Math.Abs(source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0);
    }

    private void CacheVignette()
    {
        if (_vignetteBrush is null || ActualWidth <= 0 || ActualHeight <= 0)
            return;

        UpdateLayout();
        var source = PresentationSource.FromVisual(this);
        var transform = source?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        var dpiX = 96 * Math.Abs(transform.M11);
        var dpiY = 96 * Math.Abs(transform.M22);
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(ActualWidth * Math.Abs(transform.M11)));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(ActualHeight * Math.Abs(transform.M22)));

        var visual = new DrawingVisual();
        using (var drawingContext = visual.RenderOpen())
        {
            drawingContext.DrawRectangle(_vignetteBrush, null, new Rect(0, 0, ActualWidth, ActualHeight));
        }

        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, dpiX, dpiY, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();

        var cachedVignette = new ImageBrush(bitmap)
        {
            Stretch = Stretch.Fill,
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Top
        };
        cachedVignette.Freeze();
        Overlay.Fill = cachedVignette;
        _vignetteBrush = null;
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
        _bottomEdgeWindow.Close();
        base.OnClosed(e);
    }
}
