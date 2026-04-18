using System.Windows;
using System.Windows.Input;
using EasyEyes;
using Forms = System.Windows.Forms;

namespace EasyEyes.OverlayTester;

public partial class App : Application
{
    private OverlayManager? _overlayManager;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _overlayManager = new OverlayManager();
        _overlayManager.CreateWindows();
        _overlayManager.ShowAll();

        // Invisible helper window to capture Esc key
        var helperWindow = new Window
        {
            Width = 0,
            Height = 0,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = true,
            AllowsTransparency = true,
            Background = null,
            Topmost = true,
        };
        helperWindow.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape)
            {
                _overlayManager.Dispose();
                Shutdown();
            }
        };
        helperWindow.Show();
    }
}
