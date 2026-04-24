using System.Windows;
using System.Windows.Interop;
using Color = System.Windows.Media.Color;
using Forms = System.Windows.Forms;
using Matrix = System.Windows.Media.Matrix;
using Point = System.Windows.Point;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace EasyEyes;

/// <summary>
/// A lightweight window that displays a solid colored border on a single
/// monitor. Used by <see cref="BorderFlashManager"/> to provide visual
/// feedback for the Do Not Disturb feature (amber while arming, green
/// when locked, red when cleared).
/// </summary>
/// <remarks>
/// Like <see cref="OverlayWindow"/>, this window is click-through and
/// will not steal focus from games or video players.
/// </remarks>
public partial class BorderFlashWindow : Window
{
    private readonly Forms.Screen _screen;

    public BorderFlashWindow(Forms.Screen screen, Color borderColor)
    {
        _screen = screen;

        InitializeComponent();
        FlashBorder.BorderBrush = new SolidColorBrush(borderColor);

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
    }
}
