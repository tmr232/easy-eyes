using Color = System.Windows.Media.Color;

namespace EasyEyes;

/// <summary>
/// Abstraction for the visual feedback (colored border glows) used by
/// <see cref="DndManager"/>. Production code uses
/// <see cref="BorderFlashManager"/>; tests provide a fake.
/// </summary>
public interface IDndFlashFeedback
{
    /// <summary>
    /// Shows a persistent glowing border in the given color on all
    /// monitors. Used during the arming settle window. Replaces any
    /// existing border (instantly cancels in-progress fade-outs).
    /// </summary>
    void ShowPersistent(Color color);

    /// <summary>
    /// Plays the bloom-and-fade finale in the given target color on all
    /// monitors: if a persistent border is currently shown it cross-fades
    /// to the target color, otherwise a fresh border is created in the
    /// target color. After a brief hold the border fades out and closes.
    /// Used both for the green capture-success flash and the red
    /// capture-rejected / cleared flash.
    /// </summary>
    void BloomAndFade(Color color);

    /// <summary>
    /// Starts fading out any visible border and closes it on completion.
    /// </summary>
    void Hide();
}
