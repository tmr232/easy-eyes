using System;
using System.Drawing;
using System.IO;
using System.Windows;
using Forms = System.Windows.Forms;

namespace EasyEyes;

/// <summary>
/// Owns the system tray icon, its context menu, and all menu interaction logic.
/// </summary>
public sealed class TrayIconManager : IDisposable
{
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly Stream _iconStream;
    private readonly Icon _icon;
    private readonly Forms.ToolStripLabel _activityTimeRemainingLabel;
    private readonly Forms.ToolStripMenuItem _pauseUntilUnlockItem;
    private readonly Forms.ToolStripMenuItem _pauseForItem;
    private readonly Forms.ToolStripMenuItem _micCameraItem;
    private readonly Forms.ToolStripMenuItem _dndItem;
    private readonly Forms.ToolStripMenuItem _pauseMediaOnLockItem;
    private readonly EasyEyesStateMachine _stateMachine;
    private readonly IEasyEyesActions _actions;
    private readonly BusyIndicatorManager _busyIndicatorManager;
    private readonly DndManager _dndManager;
    private bool _pauseMediaOnLock = true;
    private bool _disposed;

    public bool PauseMediaOnLock => _pauseMediaOnLock;

    public TrayIconManager(
        EasyEyesStateMachine stateMachine,
        IEasyEyesActions actions,
        BusyIndicatorManager busyIndicatorManager,
        DndManager dndManager)
    {
        _stateMachine = stateMachine;
        _actions = actions;
        _busyIndicatorManager = busyIndicatorManager;
        _dndManager = dndManager;

        _iconStream = System.Reflection.Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("tray.ico")!;
        _icon = new Icon(_iconStream);
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = _icon,
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

        _micCameraItem = new Forms.ToolStripMenuItem("Detect meetings")
        {
            CheckOnClick = true,
            Checked = true
        };
        _micCameraItem.CheckedChanged += (_, _) =>
        {
            if (_micCameraItem.Checked)
            {
                _busyIndicatorManager.SetMeetingMode(MeetingMode.On);
                if (_stateMachine.CurrentState == State.OverlayDisplayed && _busyIndicatorManager.IsBusy)
                    _stateMachine.Fire(Trigger.EnterBusy);
            }
            else
            {
                _busyIndicatorManager.SetMeetingMode(MeetingMode.Off);
            }
        };
        menu.Items.Add(_micCameraItem);

        _dndItem = new Forms.ToolStripMenuItem("Do not disturb");
        _dndItem.Click += (_, _) =>
        {
            if (_dndManager.CurrentState == DndState.Off)
            {
                _dndManager.Activate();
            }
            else
            {
                _dndManager.Deactivate();
            }

            UpdateDndMenuLabel();
        };
        _dndManager.StateChanged += (_, _) => UpdateDndMenuLabel();
        menu.Items.Add(_dndItem);

        menu.Items.Add(new Forms.ToolStripSeparator());

        _pauseMediaOnLockItem = new Forms.ToolStripMenuItem("Pause media on lock")
        {
            CheckOnClick = true,
            Checked = _pauseMediaOnLock
        };
        _pauseMediaOnLockItem.CheckedChanged += (_, _) => _pauseMediaOnLock = _pauseMediaOnLockItem.Checked;
        menu.Items.Add(_pauseMediaOnLockItem);

        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) =>
        {
            Dispose();
            Application.Current.Shutdown();
        });

        menu.Opening += (_, _) => UpdateTrayMenu();

        _trayIcon.ContextMenuStrip = menu;
    }

    public void ShowBalloonTip(int timeout, string tipTitle, string tipText, Forms.ToolTipIcon tipIcon)
    {
        _trayIcon.ShowBalloonTip(timeout, tipTitle, tipText, tipIcon);
    }

    public void UpdateMeetingMenuLabel()
    {
        _micCameraItem.Checked = _busyIndicatorManager.CurrentMeetingMode != MeetingMode.Off;
    }

    private void UpdateDndMenuLabel()
    {
        _dndItem.Text = FormatDndLabel(_dndManager.CurrentState);
        _dndItem.Checked = _dndManager.CurrentState != DndState.Off;
    }

    /// <summary>
    /// Formats the tray menu label for the given DND state. The captured
    /// process name is intentionally not surfaced (issue #3): the user
    /// already knows what they armed DND for, and the only useful tray
    /// signal is "armed" vs "active". The "(arming...)" suffix
    /// distinguishes the settle window from the active state.
    /// </summary>
    public static string FormatDndLabel(DndState state) => state switch
    {
        DndState.Arming => "Do not disturb (arming...)",
        _ => "Do not disturb",
    };

    private void UpdateTrayMenu()
    {
        var state = _stateMachine.CurrentState;
        var isPaused = state is State.PausedUntilUnlock;
        var isActive = state is State.ActivityTimerRunning or State.OverlayDisplayed or State.Busy;

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

        _pauseUntilUnlockItem.Checked = state == State.PausedUntilUnlock;
        _pauseUntilUnlockItem.Visible = isActive || state == State.PausedUntilUnlock;

        _pauseForItem.Visible = isActive;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _trayIcon.Visible = false;
        _trayIcon.ContextMenuStrip?.Dispose();
        _trayIcon.Dispose();
        _icon.Dispose();
        _iconStream.Dispose();
        GC.SuppressFinalize(this);
    }
}
