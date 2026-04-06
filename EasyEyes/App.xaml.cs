using System;
using System.IO;
using System.Media;
using System.Threading;
using System.Windows;
using Windows.Media.Control;
using Forms = System.Windows.Forms;

namespace EasyEyes;

public partial class App : Application
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EasyEyes",
        "EasyEyes.log");

    private static Mutex? s_instanceMutex;
    private static StreamWriter? s_logWriter;

    private SessionNotificationListener? _sessionListener;
    private Forms.NotifyIcon? _trayIcon;
    private Forms.ToolStripLabel _activityTimeRemainingLabel = null!;
    private Forms.ToolStripMenuItem _pauseUntilUnlockItem = null!;
    private Forms.ToolStripMenuItem _pauseForItem = null!;
    private bool _pauseMediaOnLock = true;
    private EasyEyesStateMachine _stateMachine = null!;
    private EasyEyesActions _actions = null!;
    private OverlayManager? _overlayManager;

    public App()
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        s_instanceMutex = new Mutex(true, "EasyEyes-C6A214F5-8E3B-4B2A-9F1D-7A5E6C3D2B1A", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("Easy Eyes is already running.", "Easy Eyes",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);

        try
        {
            s_logWriter = new StreamWriter(LogPath, append: true) { AutoFlush = true };
        }
        catch
        {
            // Best-effort logging — continue without file logging
        }

        DispatcherUnhandledException += (_, args) =>
        {
            Log($"DispatcherUnhandledException: {args.Exception}");
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Log($"UnhandledException: {args.ExceptionObject}");
        };

        Log("App started");
        base.OnStartup(e);

        InitializeCore();
    }

    private void InitializeCore()
    {
        _overlayManager = new OverlayManager();

        EasyEyesStateMachine? stateMachine = null;
        _actions = new EasyEyesActions(
            TimeProvider.System,
            activityScheduler: new DispatcherTimerScheduler(),
            restScheduler: new DispatcherTimerScheduler(),
            activityDuration: TimeSpan.FromMinutes(20),
            restDuration: TimeSpan.FromSeconds(20),
            showOverlay: () =>
            {
                Log("DoShowOverlay");
                _overlayManager.ShowAll();
            },
            hideOverlay: () =>
            {
                Log("DoHideOverlay");
                _overlayManager.HideAll();
            },
            showToast: () => SystemSounds.Asterisk.Play(),
            fireTrigger: trigger =>
            {
                Log($"FireTrigger: {trigger}, CurrentState: {stateMachine!.CurrentState}");
                stateMachine.Fire(trigger);
                Log($"  -> NewState: {stateMachine.CurrentState}");
            });

        stateMachine = new EasyEyesStateMachine(_actions);
        _stateMachine = stateMachine;

        InitializeTrayIcon();
        InitializeSessionListener();

        _overlayManager.CreateWindows();
        _actions.StartActivityTimer();
    }

    private void InitializeSessionListener()
    {
        _sessionListener = new SessionNotificationListener();
        _sessionListener.SessionLocked += (_, _) =>
        {
            Log($"SessionLocked, State={_stateMachine.CurrentState}");
            _stateMachine.Fire(Trigger.ScreenLock);
            PauseMediaIfEnabled();
        };
        _sessionListener.SessionUnlocked += (_, _) =>
        {
            Log($"SessionUnlocked, State={_stateMachine.CurrentState}");
            _stateMachine.Fire(Trigger.ScreenUnlock);
        };
        _sessionListener.DisplayOff += (_, _) =>
        {
            Log($"DisplayOff, State={_stateMachine.CurrentState}");
            _stateMachine.Fire(Trigger.ScreenSleep);
            PauseMediaIfEnabled();
        };
        _sessionListener.DisplayOn += (_, _) =>
        {
            Log($"DisplayOn, State={_stateMachine.CurrentState}");
            _stateMachine.Fire(Trigger.ScreenWake);
        };
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
            _trayIcon!.Visible = false;
            _trayIcon.Dispose();
            Shutdown();
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

    protected override void OnExit(ExitEventArgs e)
    {
        Log("App exiting");
        _sessionListener?.Dispose();
        _overlayManager?.Dispose();
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }

        s_logWriter?.Dispose();
        s_instanceMutex?.ReleaseMutex();
        s_instanceMutex?.Dispose();
        base.OnExit(e);
    }

    public static void Log(string message)
    {
        try
        {
            s_logWriter?.WriteLine($"{DateTime.Now:u} {message}");
        }
        catch
        {
            // Best-effort logging
        }
    }
}
