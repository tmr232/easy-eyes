namespace EasyEyes;

/// <summary>
/// Provides a boolean active/inactive state with change notifications.
/// Used by <see cref="BusyIndicator"/> to monitor an external source
/// (e.g. media devices) without taking direct dependencies.
/// </summary>
public interface IStateSource
{
    /// <summary>
    /// Whether the monitored state is currently active.
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Fires when the state transitions from inactive to active.
    /// </summary>
    event EventHandler? Activated;

    /// <summary>
    /// Fires when the state transitions from active to inactive.
    /// </summary>
    event EventHandler? Deactivated;
}
