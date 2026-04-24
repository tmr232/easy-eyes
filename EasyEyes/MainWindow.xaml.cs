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
    private readonly ForegroundWindowStateSource _foregroundSource;
    private readonly BorderFlashManager _borderFlashManager;
    private readonly DndManager _dndManager;
    private readonly TrayIconManager _trayIconManager;
    private bool _wasDndActiveOnLock;

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

        _foregroundSource = new ForegroundWindowStateSource(TimeSpan.FromSeconds(1), Dispatcher);
        _borderFlashManager = new BorderFlashManager();
        _dndManager = new DndManager(
            _foregroundSource,
            _borderFlashManager,
            settleScheduler: new DispatcherTimerScheduler(),
            graceScheduler: new DispatcherTimerScheduler(),
            settleDuration: TimeSpan.FromSeconds(10),
            gracePeriod: TimeSpan.FromSeconds(45));

        _stateMachine = new EasyEyesStateMachine(_actions, () => _busyIndicatorManager.IsBusy || _dndManager.IsBusy);

        triggerRelay.Connect(trigger =>
        {
            App.Log($"FireTrigger: {trigger}, CurrentState: {_stateMachine.CurrentState}");
            _stateMachine.Fire(trigger);
            App.Log($"  -> NewState: {_stateMachine.CurrentState}");
        });

        _trayIconManager = new TrayIconManager(_stateMachine, _actions, _busyIndicatorManager, _dndManager);

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

        _dndManager.BusyCleared += (_, _) =>
        {
            if (_stateMachine.CurrentState == State.Busy)
                _stateMachine.Fire(Trigger.BusyCleared);
        };
        _dndManager.BecameActive += (_, _) =>
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
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int exStyle = NativeMethods.GetWindowLongChecked(hwnd, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowLongChecked(hwnd, NativeMethods.GWL_EXSTYLE, exStyle | NativeMethods.WS_EX_TOOLWINDOW);

            _sessionListener = new SessionNotificationListener(this);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            App.FatalError("Failed to initialize main window", ex);
            return;
        }

        _sessionListener.SessionLocked += (_, _) =>
        {
            App.Log($"SessionLocked, State={_stateMachine.CurrentState}");
            DeactivateDndOnLock();
            _stateMachine.Fire(Trigger.ScreenLock);
            PauseMediaIfEnabled();
        };
        _sessionListener.SessionUnlocked += (_, _) =>
        {
            App.Log($"SessionUnlocked, State={_stateMachine.CurrentState}");
            _stateMachine.Fire(Trigger.ScreenUnlock);
            FlashDndClearedOnUnlock();
        };
        _sessionListener.DisplayOff += (_, _) =>
        {
            App.Log($"DisplayOff, State={_stateMachine.CurrentState}");
            DeactivateDndOnLock();
            _stateMachine.Fire(Trigger.ScreenSleep);
            PauseMediaIfEnabled();
        };
        _sessionListener.DisplayOn += (_, _) =>
        {
            App.Log($"DisplayOn, State={_stateMachine.CurrentState}");
            _stateMachine.Fire(Trigger.ScreenWake);
            FlashDndClearedOnUnlock();
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

    private void DeactivateDndOnLock()
    {
        if (_dndManager.CurrentState != DndState.Off)
        {
            _wasDndActiveOnLock = true;
            _dndManager.Deactivate();
        }
    }

    private void FlashDndClearedOnUnlock()
    {
        if (_wasDndActiveOnLock)
        {
            _wasDndActiveOnLock = false;
            _dndManager.FlashCleared();
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
        _dndManager.Dispose();
        _borderFlashManager.Dispose();
        _foregroundSource.Dispose();
        _overlayManager.Dispose();
        _trayIconManager.Dispose();
        base.OnClosed(e);
    }
}
