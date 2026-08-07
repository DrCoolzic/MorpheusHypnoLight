using MPHCore.Models;

namespace MPHEditor.Controls;

/**
 * @brief In-memory clipboard shared by all ModulatorEditor/OscillatorEditor instances.
 *
 * Holds at most one copied modulator and one copied oscillator at a time. The two
 * clipboards are independent so a modulator and an oscillator can be copied
 * simultaneously without conflict. Raises Changed whenever either clipboard is
 * updated, allowing open editors to refresh their "copied source" highlight.
 */
public static class EditorClipboard
{
    /// <summary>
    /// Frozen snapshot of the last copied modulator, ready to be pasted.
    /// </summary>
    public static Modulator? CopiedModulator { get; private set; }

    /// <summary>
    /// Role (Title) of the last copied modulator (e.g. "Freq", "Bright", "Duty").
    /// Paste is only allowed into a modulator editor with the same role.
    /// </summary>
    public static string? CopiedModulatorRole { get; private set; }

    /// <summary>
    /// Reference to the original modulator instance that was copied, used only to
    /// highlight the source editor. Not used for pasting (use <see cref="CopiedModulator"/>).
    /// </summary>
    public static Modulator? CopiedModulatorSource { get; private set; }

    /// <summary>
    /// Frozen snapshot of the last copied oscillator, ready to be pasted.
    /// </summary>
    public static Oscillator? CopiedOscillator { get; private set; }

    /// <summary>
    /// Reference to the original oscillator instance that was copied, used only to
    /// highlight the source editor. Not used for pasting (use <see cref="CopiedOscillator"/>).
    /// </summary>
    public static Oscillator? CopiedOscillatorSource { get; private set; }

    /// <summary>
    /// Raised whenever the modulator or oscillator clipboard content changes.
    /// </summary>
    public static event EventHandler? Changed;

    /// <summary>
    /// Copies a modulator's values into the clipboard, tagged with its role.
    /// </summary>
    /// <param name="modulator">The modulator to copy.</param>
    /// <param name="role">The role of the modulator (must match on paste).</param>
    public static void CopyModulator(Modulator modulator, string role)
    {
        ArgumentNullException.ThrowIfNull(modulator);

        CopiedModulator = modulator.Clone();
        CopiedModulatorRole = role;
        CopiedModulatorSource = modulator;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>
    /// Copies an oscillator's values (waveform, phase, and all modulators) into the clipboard.
    /// </summary>
    /// <param name="oscillator">The oscillator to copy.</param>
    public static void CopyOscillator(Oscillator oscillator)
    {
        ArgumentNullException.ThrowIfNull(oscillator);

        CopiedOscillator = oscillator.Clone();
        CopiedOscillatorSource = oscillator;
        Changed?.Invoke(null, EventArgs.Empty);
    }
}
