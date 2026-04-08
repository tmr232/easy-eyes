using System;
using System.Drawing;
using System.Media;
using System.Windows;
using System.Windows.Interop;
using Windows.Media.Control;
using Forms = System.Windows.Forms;

namespace EasyEyes;

public partial class MainWindow : Window
{
    private SessionNotificationListener? _sessionListener;
    private Forms.NotifyIcon _trayIcon = null!;
    private Forms.ToolStripLabel _activityTimeRemainingLabel = null!;
    private Forms.ToolStripMenuItem _pauseUntilUnlockItem = null!;
    private Forms.ToolStripMenuItem _pauseForItem = null!;
    private bool _pauseMediaOnLock = true;
    private readonly EasyEyesStateMachine _stateMachine;
    private readonly EasyEyesActions _actions;
    private readonly OverlayManager _overlayManager = new();
    private readonly MediaDeviceMonitor _mediaDeviceMonitor = new(TimeSpan.FromSeconds(1));
    private readonly BusyIndicatorManager _busyIndicatorManager;
    private Forms.ToolStripMenuItem _micCameraItem = null!;

    public MainWindow()
    {
        EasyEyesStateMachine? stateMachine = null;
        _actions = new EasyEyesActions(
            TimeProvider.System,
            activityScheduler: new DispatcherTimerScheduler(),
            restScheduler: new DispatcherTimerScheduler(),
            activityDuration: TimeSpan.FromMinutes(20),
            restDuration: TimeSpan.FromSeconds(20),
            showOverlay: DoShowOverlay,
            hideOverlay: DoHideOverlay,
            showToast: () => SystemSounds.Asterisk.Play(),
            fireTrigger: trigger =>
            {
                App.Log($"FireTrigger: {trigger}, CurrentState: {stateMachine!.CurrentState}");
                stateMachine.Fire(trigger);
                App.Log($"  -> NewState: {stateMachine.CurrentState}");
            });

        stateMachine = new EasyEyesStateMachine(_actions);
        _stateMachine = stateMachine;

        _busyIndicatorManager = new BusyIndicatorManager(
            _mediaDeviceMonitor,
            cameraGraceScheduler: new DispatcherTimerScheduler(),
            microphoneGraceScheduler: new DispatcherTimerScheduler(),
            gracePeriod: TimeSpan.FromSeconds(5));
        _busyIndicatorManager.BusyCleared += (_, _) =>
        {
            _micCameraItem.Checked = false;
        };

        InitializeComponent();
        Loaded += OnLoaded;
        SourceInitialized += OnSourceInitialized;
        InitializeTrayIcon();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _overlayManager.CreateWindows();
        _actions.StartActivityTimer();
    }

    private void DoShowOverlay()
    {
        App.Log("DoShowOverlay");
        _overlayManager.ShowAll();
    }

    private void DoHideOverlay()
    {
        App.Log("DoHideOverlay");
        _overlayManager.HideAll();
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

        _activityTimeRemainingLabel = new Forms.ToolStripLabel { Enabled = false };
        menu.Items.Add(_activityTimeRemainingLabel);

        menu.Items.Add(new Forms.ToolStripSeparator());

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

        menu.Items.Add(new Forms.ToolStripSeparator());

        _micCameraItem = new Forms.ToolStripMenuItem("Mic / Camera active")
        {
            CheckOnClick = true,
            Checked = false
        };
        _micCameraItem.CheckedChanged += (_, _) =>
        {
            if (_micCameraItem.Checked)
                _busyIndicatorManager.EnableMicCamera();
            else
                _busyIndicatorManager.DisableMicCamera();
        };
        menu.Items.Add(_micCameraItem);

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
        var isPaused = state is State.PausedUntilUnlock;
        var isActive = state is State.ActivityTimerRunning or State.OverlayDisplayed;

        // T timer display
        if (isActive)
        {
            var remaining = _actions.GetTRemaining();
            _activityTimeRemainingLabel.Text = $"T: {remaining:mm\\:ss} remaining";
            _activityTimeRemainingLabel.Visible = true;
        }
        else if (isPaused)
        {
            _activityTimeRemainingLabel.Text = "T: paused";
            _activityTimeRemainingLabel.Visible = true;
        }
        else
        {
            _activityTimeRemainingLabel.Visible = false;
        }

        // Pause until unlock toggle
        _pauseUntilUnlockItem.Checked = state == State.PausedUntilUnlock;
        _pauseUntilUnlockItem.Visible = isActive || state == State.PausedUntilUnlock;

        // Pause for...
        _pauseForItem.Visible = isActive;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
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

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        App.Log($"MainWindow OnClosing, State={_stateMachine.CurrentState}");
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        App.Log($"MainWindow OnClosed, State={_stateMachine.CurrentState}");
        _sessionListener?.Dispose();
        _mediaDeviceMonitor.Dispose();
        _overlayManager.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        base.OnClosed(e);
    }
}
