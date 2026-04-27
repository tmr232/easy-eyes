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
/// monitor using eight polygons: four straight edges filled with a
/// perpendicular linear gradient (opaque at the screen edge, transparent at
/// the inner side) and four corner squares filled with a radial gradient
/// whose origin sits at the inner corner so the falloff is rounded and
/// joins the edge gradients seamlessly. Used by
/// <see cref="BorderFlashManager"/> to provide visual feedback for the
/// Do Not Disturb feature.
/// </summary>
/// <remarks>
/// <para>
/// The glow look (issue #1 in <c>issues-with-dnd.md</c>) replaces the
/// previous flat 6 px border. Each polygon's gradient stops are kept as
/// fields so the color can be swapped cheaply (16 writes total for a
/// cross-fade — opaque + transparent stops on each of four edges and four
/// corners). Fade in/out and bloom are driven by storyboards.
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
    /// Constructs the eight polygons that form the glow frame: four
    /// rectangular edges with linear gradients and four square corners with
    /// radial gradients. Built after positioning because the points depend
    /// on the runtime monitor dimensions in DIPs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The polygons tile without overlap or gap. The seams between an edge
    /// and a corner are perfectly aligned: along a seam (e.g. <c>x=t</c>
    /// between the top edge and the top-left corner) the edge's linear
    /// gradient gives <c>alpha = 1 - y/t</c>, and the corner's radial
    /// gradient — origin at the inner corner <c>(1,1)</c> in relative
    /// coordinates with <c>RadiusX = RadiusY = 1</c> — gives
    /// <c>alpha = distance/radius = 1 - y/t</c> as well, since the relative
    /// distance from <c>(1,1)</c> along the seam is purely a function of
    /// <c>y</c>. Inside the corner the level sets are concentric arcs, so
    /// the visual falloff is rounded instead of producing the diagonal
    /// miter artifact that the previous trapezoidal layout had.
    /// </para>
    /// </remarks>
    private void BuildGlowPolygons()
    {
        double w = Width;
        double h = Height;
        double t = GlowThickness;
        var transparent = Color.FromArgb(0, _currentColor.R, _currentColor.G, _currentColor.B);

        // Edges: rectangles tiled between corner squares.
        // Top:    x∈[t, w-t], y∈[0, t]
        // Bottom: x∈[t, w-t], y∈[h-t, h]
        // Left:   x∈[0, t],   y∈[t, h-t]
        // Right:  x∈[w-t, w], y∈[t, h-t]
        var top = MakeEdgePolygon(
            [new Point(t, 0), new Point(w - t, 0), new Point(w - t, t), new Point(t, t)],
            new Point(0.5, 0), new Point(0.5, 1), transparent);
        var bottom = MakeEdgePolygon(
            [new Point(t, h - t), new Point(w - t, h - t), new Point(w - t, h), new Point(t, h)],
            new Point(0.5, 1), new Point(0.5, 0), transparent);
        var left = MakeEdgePolygon(
            [new Point(0, t), new Point(t, t), new Point(t, h - t), new Point(0, h - t)],
            new Point(0, 0.5), new Point(1, 0.5), transparent);
        var right = MakeEdgePolygon(
            [new Point(w - t, t), new Point(w, t), new Point(w, h - t), new Point(w - t, h - t)],
            new Point(1, 0.5), new Point(0, 0.5), transparent);

        // Corners: square polygons whose radial gradient origin is the
        // inner corner (relative coords). The origin and Center coincide,
        // RadiusX = RadiusY = 1, so the gradient is a circular falloff
        // rooted at the inside of the bend.
        var topLeft = MakeCornerPolygon(
            [new Point(0, 0), new Point(t, 0), new Point(t, t), new Point(0, t)],
            new Point(1, 1), transparent);
        var topRight = MakeCornerPolygon(
            [new Point(w - t, 0), new Point(w, 0), new Point(w, t), new Point(w - t, t)],
            new Point(0, 1), transparent);
        var bottomLeft = MakeCornerPolygon(
            [new Point(0, h - t), new Point(t, h - t), new Point(t, h), new Point(0, h)],
            new Point(1, 0), transparent);
        var bottomRight = MakeCornerPolygon(
            [new Point(w - t, h - t), new Point(w, h - t), new Point(w, h), new Point(w - t, h)],
            new Point(0, 0), transparent);

        RootGrid.Children.Add(top);
        RootGrid.Children.Add(bottom);
        RootGrid.Children.Add(left);
        RootGrid.Children.Add(right);
        RootGrid.Children.Add(topLeft);
        RootGrid.Children.Add(topRight);
        RootGrid.Children.Add(bottomLeft);
        RootGrid.Children.Add(bottomRight);
    }

    /// <summary>
    /// Creates an edge polygon with a linear gradient brush going from the
    /// outer edge (opaque <see cref="_currentColor"/>) to the inner edge
    /// (transparent). The outer/inner GradientStops are recorded in
    /// <see cref="_outerStops"/> / <see cref="_innerStops"/> so colors can
    /// be cross-faded later.
    /// </summary>
    private Polygon MakeEdgePolygon(Point[] points, Point gradientStart, Point gradientEnd, Color transparent)
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

        return MakePolygon(points, brush);
    }

    /// <summary>
    /// Creates a corner polygon (a square in window coordinates) with a
    /// radial gradient brush. The gradient is rooted at the *inner* corner
    /// (<paramref name="innerCorner"/> in relative-to-bounding-box coords)
    /// with offset 0 transparent and offset 1 opaque at radius 1, so that
    /// along the two seams shared with adjacent edges the alpha varies
    /// linearly from 0 (inner end) to 1 (outer end) — matching the
    /// neighbouring edge's linear gradient exactly. Outside the unit
    /// circle (toward the screen-edge corner) the brush stays at the
    /// opaque stop, so the screen edge itself reads as solid color all the
    /// way around.
    /// </summary>
    private Polygon MakeCornerPolygon(Point[] points, Point innerCorner, Color transparent)
    {
        var innerStop = new GradientStop(transparent, 0.0);
        var outerStop = new GradientStop(_currentColor, 1.0);
        _outerStops = [.. _outerStops, outerStop];
        _innerStops = [.. _innerStops, innerStop];

        var brush = new RadialGradientBrush
        {
            Center = innerCorner,
            GradientOrigin = innerCorner,
            RadiusX = 1.0,
            RadiusY = 1.0,
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
        };
        brush.GradientStops.Add(innerStop);
        brush.GradientStops.Add(outerStop);

        return MakePolygon(points, brush);
    }

    private static Polygon MakePolygon(Point[] points, System.Windows.Media.Brush fill)
    {
        var polygon = new Polygon
        {
            Fill = fill,
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
    /// <remarks>
    /// The opacity sequence is keyframed (boost-to-1 → hold → fade-to-0)
    /// so the bloom always presents a clean burst even when invoked
    /// while a grace-hint animation has only ramped opacity partway up
    /// (issue #2 in <c>issues-with-dnd.md</c>). When opacity is already
    /// at 1 (the common case) the boost segment is a no-op.
    /// </remarks>
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

        // 2. Boost opacity to 1 (no-op if already there), hold, fade out
        //    to 0, then close. KeyFrames give us a single animation that
        //    handles the whole sequence; the first segment uses the
        //    handoff snapshot so a partial grace-hint opacity is brought
        //    up cleanly into the bloom.
        var opacityAnim = new DoubleAnimationUsingKeyFrames();
        opacityAnim.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(BloomColorDuration)));
        opacityAnim.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(BloomColorDuration + BloomHoldDuration)));
        opacityAnim.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(BloomColorDuration + BloomHoldDuration + FadeOutDuration)));
        opacityAnim.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, opacityAnim);
    }

    /// <summary>
    /// Plays the grace-period hint: opacity ramps 0 → 1 linearly over
    /// <paramref name="duration"/> while the gradient color cross-fades
    /// linearly from <paramref name="startColor"/> to
    /// <paramref name="endColor"/> in lockstep. The intent (issue #2 in
    /// <c>issues-with-dnd.md</c>) is a warning that gets more
    /// attention-grabbing as the grace period runs out — at the end the
    /// border is fully <paramref name="endColor"/> so the cleared
    /// <see cref="BloomAndFade"/> (called by the manager when the grace
    /// timer expires) is a continuous handoff rather than a fresh burst.
    /// </summary>
    /// <remarks>
    /// Tuning: the timing here is intentionally simple (parallel linear
    /// animations). To change the curve, swap the easing on either
    /// animation; to split the timing (e.g. opacity ramps faster than
    /// the color shift), give them different durations and adjust
    /// callers.
    /// </remarks>
    public void ShowGraceHint(Color startColor, Color endColor, TimeSpan duration)
    {
        // Color cross-fade on every gradient stop (eight in total, two
        // per side — outer opaque, inner transparent).
        var startTransparent = Color.FromArgb(0, startColor.R, startColor.G, startColor.B);
        var endTransparent = Color.FromArgb(0, endColor.R, endColor.G, endColor.B);
        foreach (var stop in _outerStops)
        {
            stop.Color = startColor;
            var anim = new ColorAnimation
            {
                To = endColor,
                Duration = duration,
            };
            stop.BeginAnimation(GradientStop.ColorProperty, anim);
        }

        foreach (var stop in _innerStops)
        {
            stop.Color = startTransparent;
            var anim = new ColorAnimation
            {
                To = endTransparent,
                Duration = duration,
            };
            stop.BeginAnimation(GradientStop.ColorProperty, anim);
        }

        _currentColor = endColor;

        // Opacity ramps 0 → 1 in lockstep. HoldEnd ensures it sits at 1
        // when the duration elapses so the manager's BloomAndFade picks
        // up at full visibility for the cleared finale.
        var opacityAnim = new DoubleAnimation
        {
            From = 0.0,
            To = 1.0,
            Duration = duration,
        };
        BeginAnimation(OpacityProperty, opacityAnim);
    }
}
