using Color = System.Windows.Media.Color;

namespace EasyEyes;

/// <summary>
/// Abstraction for the visual feedback (colored border flashes) used by
/// <see cref="DndManager"/>. Production code uses
/// <see cref="BorderFlashManager"/>; tests provide a fake.
/// </summary>
public interface IDndFlashFeedback
{
    void ShowPersistent(Color color);
    void ShowFlash(Color color);
    void Hide();
}
