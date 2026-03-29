using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Toolkit.Uwp.Notifications;
using Windows.Media.Control;
using Forms = System.Windows.Forms;

namespace EasyEyes;

public partial class MainWindow : Window
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

    private SessionNotificationListener? _sessionListener;
    private Forms.NotifyIcon _trayIcon = null!;
    private Forms.ToolStripLabel _tRemainingLabel = null!;
    private Forms.ToolStripLabel _snoozeRemainingLabel = null!;
    private Forms.ToolStripMenuItem _pauseItem = null!;
    private Forms.ToolStripMenuItem _resumeItem = null!;
    private Forms.ToolStripMenuItem _pauseUntilUnlockItem = null!;
    private Forms.ToolStripMenuItem _pauseForItem = null!;
    private Forms.ToolStripMenuItem _cancelSnoozeItem = null!;
    private bool _pauseMediaOnLock = true;
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
            TimeProvider.System,
            tDuration: TimeSpan.FromMinutes(20),
            lDuration: TimeSpan.FromSeconds(20),
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
        var iconStream = System.Reflection.Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("tray.ico")!;
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = new System.Drawing.Icon(iconStream),
            Text = "Easy Eyes",
            Visible = true
        };

        var menu = new Forms.ContextMenuStrip();

        _tRemainingLabel = new Forms.ToolStripLabel { Enabled = false };
        menu.Items.Add(_tRemainingLabel);

        _snoozeRemainingLabel = new Forms.ToolStripLabel { Enabled = false, Visible = false };
        menu.Items.Add(_snoozeRemainingLabel);

        menu.Items.Add(new Forms.ToolStripSeparator());

        _pauseItem = new Forms.ToolStripMenuItem("Pause");
        _pauseItem.Click += (_, _) => _stateMachine.Fire(Trigger.Pause);
        menu.Items.Add(_pauseItem);

        _resumeItem = new Forms.ToolStripMenuItem("Resume");
        _resumeItem.Click += (_, _) => _stateMachine.Fire(Trigger.Resume);
        _resumeItem.Visible = false;
        menu.Items.Add(_resumeItem);

        _pauseUntilUnlockItem = new Forms.ToolStripMenuItem("Pause until unlock");
        _pauseUntilUnlockItem.Click += (_, _) =>
        {
            if (_stateMachine.CurrentState == State.PausedUntilUnlock)
                _stateMachine.Fire(Trigger.Resume);
            else
                _stateMachine.Fire(Trigger.PauseUntilUnlock);
        };
        menu.Items.Add(_pauseUntilUnlockItem);

        _pauseForItem = new Forms.ToolStripMenuItem("Pause for...");
        _pauseForItem.Click += (_, _) =>
        {
            var dialog = new PauseForDialog();
            if (dialog.ShowDialog() == true)
            {
                _stateMachine.FirePauseForDuration(TimeSpan.FromMinutes(dialog.Minutes));
            }
        };
        menu.Items.Add(_pauseForItem);

        _cancelSnoozeItem = new Forms.ToolStripMenuItem("Cancel snooze");
        _cancelSnoozeItem.Click += (_, _) => _stateMachine.Fire(Trigger.Resume);
        _cancelSnoozeItem.Visible = false;
        menu.Items.Add(_cancelSnoozeItem);

        menu.Items.Add(new Forms.ToolStripSeparator());

        var pauseMediaOnLockItem = new Forms.ToolStripMenuItem("Pause media on lock")
        {
            CheckOnClick = true,
            Checked = _pauseMediaOnLock
        };
        pauseMediaOnLockItem.CheckedChanged += (_, _) => _pauseMediaOnLock = pauseMediaOnLockItem.Checked;
        menu.Items.Add(pauseMediaOnLockItem);

        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) =>
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            Application.Current.Shutdown();
        });

        menu.Opening += (_, _) => UpdateTrayMenu();

        _trayIcon.ContextMenuStrip = menu;
    }

    private void UpdateTrayMenu()
    {
        var state = _stateMachine.CurrentState;
        var isPaused = state is State.Paused or State.PausedUntilUnlock or State.PausedTimed;
        var isActive = state is State.T_TimerRunning or State.OverlayDisplayed;
        var isSnoozed = state == State.PausedTimed;

        // T timer display
        if (isActive)
        {
            var remaining = _actions.GetTRemaining();
            _tRemainingLabel.Text = $"T: {remaining:mm\\:ss} remaining";
            _tRemainingLabel.Visible = true;
        }
        else if (isPaused)
        {
            _tRemainingLabel.Text = "T: paused";
            _tRemainingLabel.Visible = true;
        }
        else
        {
            _tRemainingLabel.Visible = false;
        }

        // Snooze display
        if (isSnoozed)
        {
            var snoozeRemaining = _actions.GetSnoozeRemaining();
            _snoozeRemainingLabel.Text = $"Snoozed: {snoozeRemaining:mm\\:ss} remaining";
            _snoozeRemainingLabel.Visible = true;
        }
        else
        {
            _snoozeRemainingLabel.Visible = false;
        }

        // Pause / Resume swap
        _pauseItem.Visible = isActive;
        _resumeItem.Visible = state == State.Paused;

        // Pause until unlock toggle
        _pauseUntilUnlockItem.Checked = state == State.PausedUntilUnlock;
        _pauseUntilUnlockItem.Visible = isActive || state == State.PausedUntilUnlock;

        // Pause for... / Cancel snooze
        _pauseForItem.Visible = isActive;
        _cancelSnoozeItem.Visible = isSnoozed;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;

        _sessionListener = new SessionNotificationListener(this);
        _sessionListener.SessionLocked += (_, _) =>
        {
            App.Log($"SessionLocked, State={_stateMachine.CurrentState}");
            _stateMachine.Fire(Trigger.ScreenLock);
            PauseMediaIfEnabled();
        };
        _sessionListener.SessionUnlocked += (_, _) =>
        {
            App.Log($"SessionUnlocked, State={_stateMachine.CurrentState}");
            _stateMachine.Fire(Trigger.ScreenUnlock);
        };
        _sessionListener.DisplayOff += (_, _) =>
        {
            App.Log($"DisplayOff, State={_stateMachine.CurrentState}");
            _stateMachine.Fire(Trigger.ScreenSleep);
            PauseMediaIfEnabled();
        };
        _sessionListener.DisplayOn += (_, _) =>
        {
            App.Log($"DisplayOn, State={_stateMachine.CurrentState}");
            _stateMachine.Fire(Trigger.ScreenWake);
        };

        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        _ = SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
    }

    private async void PauseMediaIfEnabled()
    {
        if (!_pauseMediaOnLock) return;
        try
        {
            var sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var session = sessionManager.GetCurrentSession();
            if (session != null)
            {
                await session.TryPauseAsync();
            }
        }
        catch
        {
            // No media session available or pause not supported
        }
    }

    private static void ShowUrgentNotification(string message)
    {
        try
        {
            var escaped = System.Security.SecurityElement.Escape(message);
            var xml = $"""
                <toast scenario="urgent">
                  <visual>
                    <binding template="ToastGeneric">
                      <text>Easy Eyes</text>
                      <text>{escaped}</text>
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
        _sessionListener?.Dispose();
        CompositionTarget.Rendering -= OnRendering;
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        base.OnClosed(e);
    }
}
