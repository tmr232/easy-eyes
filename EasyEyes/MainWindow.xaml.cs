using System;
using System.Windows;
using System.Windows.Interop;
using Windows.Media.Control;

namespace EasyEyes;

public partial class MainWindow : Window
{
    private SessionNotificationListener? _sessionListener;
    private readonly EasyEyesStateMachine _stateMachine;
    private readonly EasyEyesActions _actions;
    private readonly OverlayManager _overlayManager = new();
    private readonly MediaDeviceMonitor _mediaDeviceMonitor;
    private readonly BusyIndicatorManager _busyIndicatorManager;
    private readonly TrayIconManager _trayIconManager;

    public MainWindow()
    {
        _mediaDeviceMonitor = new MediaDeviceMonitor(TimeSpan.FromSeconds(1), Dispatcher);
        var triggerRelay = new TriggerRelay();
        _actions = new EasyEyesActions(
            TimeProvider.System,
            activityScheduler: new DispatcherTimerScheduler(),
            restScheduler: new DispatcherTimerScheduler(),
            activityDuration: TimeSpan.FromMinutes(20),
            restDuration: TimeSpan.FromSeconds(20),
            showOverlay: DoShowOverlay,
            hideOverlay: DoHideOverlay,
            showToast: () => System.Media.SystemSounds.Asterisk.Play(),
            triggerRelay: triggerRelay);

        _busyIndicatorManager = new BusyIndicatorManager(
            _mediaDeviceMonitor,
            graceScheduler: new DispatcherTimerScheduler(),
            gracePeriod: TimeSpan.FromSeconds(5));

        _stateMachine = new EasyEyesStateMachine(_actions, () => _busyIndicatorManager.IsBusy);

        triggerRelay.Connect(trigger =>
        {
            App.Log($"FireTrigger: {trigger}, CurrentState: {_stateMachine.CurrentState}");
            _stateMachine.Fire(trigger);
            App.Log($"  -> NewState: {_stateMachine.CurrentState}");
        });

        _trayIconManager = new TrayIconManager(_stateMachine, _actions, _busyIndicatorManager);

        _busyIndicatorManager.BusyCleared += (_, _) =>
        {
            if (_stateMachine.CurrentState == State.Busy)
                _stateMachine.Fire(Trigger.BusyCleared);
        };
        _busyIndicatorManager.BecameActive += (_, _) =>
        {
            if (_stateMachine.CurrentState == State.OverlayDisplayed)
                _stateMachine.Fire(Trigger.EnterBusy);
        };

        _busyIndicatorManager.SetMeetingMode(MeetingMode.On);

        InitializeComponent();
        Loaded += OnLoaded;
        SourceInitialized += OnSourceInitialized;
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
        if (!_trayIconManager.PauseMediaOnLock) return;
        try
        {
            var sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var session = sessionManager.GetCurrentSession();
            if (session != null)
            {
                await session.TryPauseAsync();
            }
        }
        catch (Exception ex)
        {
            App.Log($"PauseMediaIfEnabled failed: {ex.Message}");
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
        _busyIndicatorManager.Dispose();
        _overlayManager.Dispose();
        _trayIconManager.Dispose();
        base.OnClosed(e);
    }
}
