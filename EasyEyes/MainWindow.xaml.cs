using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Toolkit.Uwp.Notifications;
using Forms = System.Windows.Forms;

namespace EasyEyes;

public partial class MainWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    private const int WM_WTSSESSION_CHANGE = 0x02B1;
    private const int WTS_SESSION_LOCK = 0x7;
    private const int WTS_SESSION_UNLOCK = 0x8;
    private const int NOTIFY_FOR_THIS_SESSION = 0x0;

    private const double SpotlightRadius = 200;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("wtsapi32.dll")]
    private static extern bool WTSRegisterSessionNotification(IntPtr hWnd, int dwFlags);

    [DllImport("wtsapi32.dll")]
    private static extern bool WTSUnRegisterSessionNotification(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private Forms.NotifyIcon _trayIcon = null!;
    private readonly RadialGradientBrush _spotlightMask;
    private readonly EasyEyesStateMachine _stateMachine;
    private readonly EasyEyesActions _actions;

    public MainWindow()
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

        EasyEyesStateMachine? stateMachine = null;
        _actions = new EasyEyesActions(
            tDuration: TimeSpan.FromSeconds(2),
            lDuration: TimeSpan.FromSeconds(2),
            showOverlay: DoShowOverlay,
            hideOverlay: DoHideOverlay,
            showToast: () => ShowUrgentNotification("Time to rest your eyes!"),
            clearToast: ClearNotifications,
            fireTrigger: trigger =>
            {
                App.Log($"FireTrigger: {trigger}, CurrentState: {stateMachine!.CurrentState}");
                stateMachine.Fire(trigger);
                App.Log($"  -> NewState: {stateMachine.CurrentState}");
            });

        stateMachine = new EasyEyesStateMachine(_actions);
        _stateMachine = stateMachine;

        InitializeComponent();
        Overlay.OpacityMask = _spotlightMask;
        Overlay.Opacity = 0;
        Loaded += OnLoaded;
        SourceInitialized += OnSourceInitialized;
        InitializeTrayIcon();
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Cover the virtual screen but leave 1px uncovered so Windows
        // doesn't treat this as a fullscreen app and hide the taskbar.
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight - 1;

        _actions.StartTTimer();
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (!GetCursorPos(out var pt)) return;

        var wpfPoint = PointFromScreen(new System.Windows.Point(pt.X, pt.Y));
        _spotlightMask.Center = wpfPoint;
        _spotlightMask.GradientOrigin = wpfPoint;
    }

    private void DoShowOverlay()
    {
        App.Log("DoShowOverlay");
        var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromSeconds(5));
        Overlay.BeginAnimation(OpacityProperty, fadeIn);
    }

    private void DoHideOverlay()
    {
        App.Log("DoHideOverlay");
        Overlay.BeginAnimation(OpacityProperty, null);
        Overlay.Opacity = 0;
    }

    private void InitializeTrayIcon()
    {
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Easy Eyes",
            Visible = true
        };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Exit", null, (_, _) =>
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            Application.Current.Shutdown();
        });

        _trayIcon.ContextMenuStrip = menu;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;

        var source = HwndSource.FromHwnd(hwnd);
        source?.AddHook(WndProc);
        WTSRegisterSessionNotification(hwnd, NOTIFY_FOR_THIS_SESSION);

        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_WTSSESSION_CHANGE)
        {
            var sessionEvent = wParam.ToInt32();
            App.Log($"WM_WTSSESSION_CHANGE wParam=0x{sessionEvent:X}, State={_stateMachine.CurrentState}");

            switch (sessionEvent)
            {
                case WTS_SESSION_LOCK:
                    _stateMachine.Fire(Trigger.ScreenLock);
                    break;
                case WTS_SESSION_UNLOCK:
                    _stateMachine.Fire(Trigger.ScreenUnlock);
                    break;
            }
        }

        return IntPtr.Zero;
    }

    private static void ShowUrgentNotification(string message)
    {
        try
        {
            var xml = $"""
                <toast scenario="urgent">
                  <visual>
                    <binding template="ToastGeneric">
                      <text>Easy Eyes</text>
                      <text>{message}</text>
                    </binding>
                  </visual>
                </toast>
                """;

            var doc = new Windows.Data.Xml.Dom.XmlDocument();
            doc.LoadXml(xml);

            ToastNotificationManagerCompat.CreateToastNotifier()
                .Show(new Windows.UI.Notifications.ToastNotification(doc));
        }
        catch
        {
            // Toast delivery can fail when the session is locked or
            // the notification platform is unavailable — swallow so
            // it doesn't crash the app.
        }
    }

    private static void ClearNotifications()
    {
        try
        {
            ToastNotificationManagerCompat.History.Clear();
        }
        catch
        {
            // Best-effort — notification platform may be unavailable.
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        App.Log($"MainWindow OnClosing, State={_stateMachine.CurrentState}");
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        App.Log($"MainWindow OnClosed, State={_stateMachine.CurrentState}");
        var hwnd = new WindowInteropHelper(this).Handle;
        WTSUnRegisterSessionNotification(hwnd);
        CompositionTarget.Rendering -= OnRendering;
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        base.OnClosed(e);
    }
}
