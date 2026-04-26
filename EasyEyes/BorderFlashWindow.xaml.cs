using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Color = System.Windows.Media.Color;
using Forms = System.Windows.Forms;
using Matrix = System.Windows.Media.Matrix;
using Point = System.Windows.Point;

namespace EasyEyes;

/// <summary>
/// A click-through window that paints a soft glowing border around a single
/// monitor using four trapezoidal polygons (one per side, mitred at the
/// corners) filled with a perpendicular linear gradient (opaque at the
/// outer edge, transparent at the inner edge). Used by
/// <see cref="BorderFlashManager"/> to provide visual feedback for the
/// Do Not Disturb feature.
/// </summary>
/// <remarks>
/// <para>
/// The glow look (issue #1 in <c>issues-with-dnd.md</c>) replaces the
/// previous flat 6 px border. Each polygon's gradient stops are kept as
/// fields so the color can be swapped cheaply (8 writes total for a
/// cross-fade). Fade in/out and bloom are driven by storyboards.
/// </para>
/// <para>
/// Like <see cref="OverlayWindow"/>, the window is click-through and will
/// not steal focus from games or video players.
/// </para>
/// </remarks>
public partial class BorderFlashWindow : Window
{
    /// <summary>Glow thickness in DIPs. Tuned to be visible at the screen edge without being intrusive.</summary>
    private const double GlowThickness = 24.0;

    private static readonly TimeSpan FadeInDuration = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan FadeOutDuration = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan BloomColorDuration = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan BloomHoldDuration = TimeSpan.FromMilliseconds(300);

    private readonly Forms.Screen _screen;
    private Color _currentColor;
    private GradientStop[] _outerStops = [];
    private GradientStop[] _innerStops = [];

    public BorderFlashWindow(Forms.Screen screen, Color borderColor)
    {
        _screen = screen;
        _currentColor = borderColor;

        InitializeComponent();

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
        Height = size.Y;
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
            App.FatalError("Failed to initialize border flash window", ex);
        }

        PositionOnScreen();
        BuildGlowPolygons();
    }

    /// <summary>
    /// Constructs the four trapezoidal polygons that form the glow frame.
    /// Built after positioning because the trapezoid points depend on the
    /// runtime monitor dimensions in DIPs.
    /// </summary>
    private void BuildGlowPolygons()
    {
        double w = Width;
        double h = Height;
        double t = GlowThickness;
        var transparent = Color.FromArgb(0, _currentColor.R, _currentColor.G, _currentColor.B);

        // Trapezoid points are in window coordinates; the four sides tile
        // with 45° miter seams (no overlap, no gap).
        // Top:    (0,0) (w,0) (w-t,t) (t,t)
        // Bottom: (0,h) (w,h) (w-t,h-t) (t,h-t)
        // Left:   (0,0) (t,t) (t,h-t) (0,h)
        // Right:  (w,0) (w,h) (w-t,h-t) (w-t,t)
        var top = MakePolygon(
            [new Point(0, 0), new Point(w, 0), new Point(w - t, t), new Point(t, t)],
            new Point(0.5, 0), new Point(0.5, 1), transparent);
        var bottom = MakePolygon(
            [new Point(0, h), new Point(w, h), new Point(w - t, h - t), new Point(t, h - t)],
            new Point(0.5, 1), new Point(0.5, 0), transparent);
        var left = MakePolygon(
            [new Point(0, 0), new Point(t, t), new Point(t, h - t), new Point(0, h)],
            new Point(0, 0.5), new Point(1, 0.5), transparent);
        var right = MakePolygon(
            [new Point(w, 0), new Point(w, h), new Point(w - t, h - t), new Point(w - t, t)],
            new Point(1, 0.5), new Point(0, 0.5), transparent);

        RootGrid.Children.Add(top);
        RootGrid.Children.Add(bottom);
        RootGrid.Children.Add(left);
        RootGrid.Children.Add(right);
    }

    /// <summary>
    /// Creates a polygon with a linear gradient brush going from the outer
    /// edge (opaque <see cref="_currentColor"/>) to the inner edge
    /// (transparent). The outer/inner GradientStops are recorded in
    /// <see cref="_outerStops"/> / <see cref="_innerStops"/> so colors can
    /// be cross-faded later.
    /// </summary>
    private Polygon MakePolygon(Point[] points, Point gradientStart, Point gradientEnd, Color transparent)
    {
        var outerStop = new GradientStop(_currentColor, 0.0);
        var innerStop = new GradientStop(transparent, 1.0);
        _outerStops = [.. _outerStops, outerStop];
        _innerStops = [.. _innerStops, innerStop];

        var brush = new LinearGradientBrush
        {
            StartPoint = gradientStart,
            EndPoint = gradientEnd,
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
        };
        brush.GradientStops.Add(outerStop);
        brush.GradientStops.Add(innerStop);

        var polygon = new Polygon
        {
            Fill = brush,
            IsHitTestVisible = false,
        };
        foreach (var p in points)
        {
            polygon.Points.Add(p);
        }

        return polygon;
    }

    /// <summary>
    /// Animates the window opacity from 0 to 1 over
    /// <see cref="FadeInDuration"/> with a quadratic ease-out.
    /// </summary>
    public void FadeIn()
    {
        var animation = new DoubleAnimation
        {
            From = 0.0,
            To = 1.0,
            Duration = FadeInDuration,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        BeginAnimation(OpacityProperty, animation);
    }

    /// <summary>
    /// Stops all animations and closes the window immediately, with no
    /// fade-out. Used by <see cref="BorderFlashManager"/> when a new
    /// border replaces an existing one and the existing one shouldn't
    /// linger.
    /// </summary>
    public void CloseImmediately()
    {
        BeginAnimation(OpacityProperty, null);
        Close();
    }

    /// <summary>
    /// Fades the window opacity from current to 0 over
    /// <see cref="FadeOutDuration"/>, then closes the window.
    /// </summary>
    public void FadeOutAndClose()
    {
        var animation = new DoubleAnimation
        {
            To = 0.0,
            Duration = FadeOutDuration,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
        };
        animation.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, animation);
    }

    /// <summary>
    /// Plays the bloom-and-fade finale: cross-fade the border color to
    /// <paramref name="targetColor"/>, hold briefly, then fade out and
    /// close. Used both for the "captured" green flash (success) and the
    /// "cleared" / "rejected" red flash (failure).
    /// </summary>
    public void BloomAndFade(Color targetColor)
    {
        // 1. Cross-fade the eight gradient-stop colors (opaque outer +
        //    transparent inner per side) to the new color.
        var transparent = Color.FromArgb(0, targetColor.R, targetColor.G, targetColor.B);
        foreach (var stop in _outerStops)
        {
            var anim = new ColorAnimation
            {
                To = targetColor,
                Duration = BloomColorDuration,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            };
            stop.BeginAnimation(GradientStop.ColorProperty, anim);
        }

        foreach (var stop in _innerStops)
        {
            var anim = new ColorAnimation
            {
                To = transparent,
                Duration = BloomColorDuration,
            };
            stop.BeginAnimation(GradientStop.ColorProperty, anim);
        }

        _currentColor = targetColor;

        // 2. After the color cross-fade settles and a brief hold, fade out
        //    the whole window and close.
        var fadeOut = new DoubleAnimation
        {
            To = 0.0,
            BeginTime = BloomColorDuration + BloomHoldDuration,
            Duration = FadeOutDuration,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
        };
        fadeOut.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fadeOut);
    }
}
