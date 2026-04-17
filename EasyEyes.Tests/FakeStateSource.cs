using EasyEyes;

namespace EasyEyes.Tests;

/// <summary>
/// Test implementation of <see cref="IStateSource"/> that lets tests
/// control the active state and fire events manually.
/// </summary>
public class FakeStateSource : IStateSource
{
    public bool IsActive { get; set; }

    public event EventHandler? Activated;
    public event EventHandler? Deactivated;

    public void SimulateActivated()
    {
        Activated?.Invoke(this, EventArgs.Empty);
    }

    public void SimulateDeactivated()
    {
        Deactivated?.Invoke(this, EventArgs.Empty);
    }
}
