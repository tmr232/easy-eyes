namespace EasyEyes;

public class TriggerRelay
{
    private Action<Trigger>? _handler;

    public void Connect(Action<Trigger> handler) =>
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));

    public void Fire(Trigger trigger) =>
        (_handler ?? throw new InvalidOperationException("TriggerRelay not connected."))
            .Invoke(trigger);
}
