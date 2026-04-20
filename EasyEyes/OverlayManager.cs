using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace EasyEyes;

public class OverlayManager : IDisposable
{
    private readonly List<OverlayWindow> _windows = [];
    private bool _overlayVisible;

    public OverlayManager()
    {
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    public void ShowAll()
    {
        _overlayVisible = true;
        foreach (var screen in Forms.Screen.AllScreens)
        {
            var window = new OverlayWindow(screen);
            window.Show();
            window.ShowOverlay();
            _windows.Add(window);
        }
    }

    public void HideAll()
    {
        _overlayVisible = false;
        foreach (var window in _windows)
            window.Close();
        _windows.Clear();
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        App.Log("DisplaySettingsChanged: refreshing overlay windows");
        RefreshWindows();
    }

    private void RefreshWindows()
    {
        foreach (var window in _windows)
            window.Close();
        _windows.Clear();

        if (_overlayVisible)
            ShowAll();
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        foreach (var window in _windows)
            window.Close();
        _windows.Clear();
    }
}
