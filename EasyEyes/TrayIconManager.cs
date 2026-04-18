using System;
using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace EasyEyes;

/// <summary>
/// Owns the system tray icon, its context menu, and all menu interaction logic.
/// </summary>
public class TrayIconManager : IDisposable
{
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly Forms.ToolStripLabel _activityTimeRemainingLabel;
    private readonly Forms.ToolStripMenuItem _pauseUntilUnlockItem;
    private readonly Forms.ToolStripMenuItem _pauseForItem;
    private readonly Forms.ToolStripMenuItem _micCameraItem;
    private readonly Forms.ToolStripMenuItem _pauseMediaOnLockItem;
    private readonly EasyEyesStateMachine _stateMachine;
    private readonly IEasyEyesActions _actions;
    private readonly BusyIndicatorManager _busyIndicatorManager;
    private bool _pauseMediaOnLock = true;

    public bool PauseMediaOnLock => _pauseMediaOnLock;

    public TrayIconManager(
        EasyEyesStateMachine stateMachine,
        IEasyEyesActions actions,
        BusyIndicatorManager busyIndicatorManager)
    {
        _stateMachine = stateMachine;
        _actions = actions;
        _busyIndicatorManager = busyIndicatorManager;

        var iconStream = System.Reflection.Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("tray.ico")!;
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = new Icon(iconStream),
            Text = "Easy Eyes",
            Visible = true
        };

        var menu = new Forms.ContextMenuStrip();
        menu.Closing += (_, e) =>
        {
            if (e.CloseReason == Forms.ToolStripDropDownCloseReason.ItemClicked)
                e.Cancel = true;
        };

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
            UpdateTrayMenu();
        };
        menu.Items.Add(_pauseUntilUnlockItem);

        _pauseForItem = new Forms.ToolStripMenuItem("Pause for...");
        _pauseForItem.Click += (_, _) =>
        {
            menu.Close();
            var dialog = new PauseForDialog();
            if (dialog.ShowDialog() == true)
            {
                _stateMachine.FirePauseForDuration(TimeSpan.FromMinutes(dialog.Minutes));
            }
        };
        menu.Items.Add(_pauseForItem);

        menu.Items.Add(new Forms.ToolStripSeparator());

        _micCameraItem = new Forms.ToolStripMenuItem("In a meeting")
        {
            CheckOnClick = false,
            Checked = false
        };
        _micCameraItem.Click += (_, _) => CycleMeetingMode();
        menu.Items.Add(_micCameraItem);

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
            menu.Close();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
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
        var (text, check) = _busyIndicatorManager.CurrentMeetingMode switch
        {
            MeetingMode.Off => ("In a meeting", false),
            MeetingMode.UntilEnd => ("In a meeting (until end)", true),
            MeetingMode.Always => ("In a meeting (always)", true),
            _ => ("In a meeting", false),
        };
        _micCameraItem.Text = text;
        _micCameraItem.Checked = check;
    }

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

    private void CycleMeetingMode()
    {
        var next = _busyIndicatorManager.CurrentMeetingMode switch
        {
            MeetingMode.Off => MeetingMode.UntilEnd,
            MeetingMode.UntilEnd => MeetingMode.Always,
            MeetingMode.Always => MeetingMode.Off,
            _ => MeetingMode.Off,
        };

        _busyIndicatorManager.SetMeetingMode(next);
        UpdateMeetingMenuLabel();

        if (next != MeetingMode.Off && _stateMachine.CurrentState == State.OverlayDisplayed)
            _stateMachine.Fire(Trigger.EnterBusy);
    }

    public void Dispose()
    {
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        GC.SuppressFinalize(this);
    }
}
