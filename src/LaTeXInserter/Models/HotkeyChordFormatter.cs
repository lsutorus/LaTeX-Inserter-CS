using System.Text;
using SharpHook.Data;

namespace LaTeXInserter.Models;

/// <summary>
/// Renders a HotkeyChord for display. Purely presentational — serialization of
/// ModifierMask is unaffected, so settings.json stays compatible across platforms.
/// </summary>
public static class HotkeyChordFormatter
{
    public static string Format(HotkeyChord chord, PlatformKind platform) =>
        platform == PlatformKind.MacOS ? FormatMac(chord) : FormatWindows(chord);

    private static string FormatMac(HotkeyChord chord)
    {
        // Apple's canonical order: Control, Option, Shift, Command — no separators.
        var sb = new StringBuilder(8);
        if ((chord.Modifiers & ModifierMask.Control) != 0) sb.Append('⌃'); // ⌃
        if ((chord.Modifiers & ModifierMask.Alt) != 0) sb.Append('⌥');     // ⌥
        if ((chord.Modifiers & ModifierMask.Shift) != 0) sb.Append('⇧');   // ⇧
        if ((chord.Modifiers & ModifierMask.Windows) != 0) sb.Append('⌘'); // ⌘
        if (chord.TriggerKey != KeyCode.VcUndefined) sb.Append(FormatKeyCode(chord.TriggerKey));
        return sb.ToString();
    }

    private static string FormatWindows(HotkeyChord chord)
    {
        var parts = new List<string>(5);
        if ((chord.Modifiers & ModifierMask.Control) != 0) parts.Add("Ctrl");
        if ((chord.Modifiers & ModifierMask.Alt) != 0) parts.Add("Alt");
        if ((chord.Modifiers & ModifierMask.Shift) != 0) parts.Add("Shift");
        if ((chord.Modifiers & ModifierMask.Windows) != 0) parts.Add("Win");
        if (chord.TriggerKey != KeyCode.VcUndefined) parts.Add(FormatKeyCode(chord.TriggerKey));
        return string.Join("+", parts);
    }

    private static string FormatKeyCode(KeyCode key)
    {
        var name = key.ToString();
        return name.StartsWith("Vc") ? name[2..] : name;
    }
}
