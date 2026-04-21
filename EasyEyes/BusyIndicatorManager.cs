namespace EasyEyes;

public enum MeetingMode
{
    Off,
    On,
}

public class BusyIndicatorManager : IDisposable
{
    private readonly BusyIndicator _indicator;

    public bool IsBusy => _indicator.IsActive;
    public bool IsEnabled => _indicator.IsEnabled;
    public MeetingMode CurrentMeetingMode { get; private set; }

    public event EventHandler? BusyCleared;
    public event EventHandler? BecameActive;

    public BusyIndicatorManager(
        IStateSource source,
        ITimerScheduler graceScheduler,
        TimeSpan gracePeriod)
        : this(new BusyIndicator(source, graceScheduler, gracePeriod))
    {
    }

    public BusyIndicatorManager(BusyIndicator indicator)
    {
        _indicator = indicator;
        _indicator.Cleared += (_, e) => BusyCleared?.Invoke(this, e);
        _indicator.BecameActive += (_, e) => BecameActive?.Invoke(this, e);
    }

    public void SetMeetingMode(MeetingMode mode)
    {
        if (mode == MeetingMode.Off)
        {
            DisableMeeting();
            return;
        }
        CurrentMeetingMode = mode;
        _indicator.Enable();
    }

    public void DisableMeeting()
    {
        CurrentMeetingMode = MeetingMode.Off;
        _indicator.Disable();
    }

    public void Dispose()
    {
        _indicator.Disable();
        GC.SuppressFinalize(this);
    }
}
