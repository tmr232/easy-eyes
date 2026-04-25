namespace EasyEyes;

/// <summary>
/// Abstraction for capturing and monitoring the foreground window process.
/// Production code uses <see cref="ForegroundWindowStateSource"/>; tests
/// provide a fake.
/// </summary>
public interface IForegroundCapture : IStateSource
{
    /// <summary>
    /// Records the current foreground window's process and starts monitoring.
    /// </summary>
    void Capture();

    /// <summary>
    /// Clears the captured process and stops monitoring.
    /// </summary>
    void Release();
}
