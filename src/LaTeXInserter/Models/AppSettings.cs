using SharpHook.Data;

namespace LaTeXInserter.Models;

public sealed record AppSettings(
    HotkeyChord Hotkey = default,
    bool StartOnStartup = false,
    int InputFontSize = 16,
    int PreviewFontSize = 14,
    string AccentColor = "#404040",
    bool AutocompleteEnabled = true
)
{
    public static AppSettings Default => new()
    {
        Hotkey = new HotkeyChord(ModifierMask.Control | ModifierMask.Alt, KeyCode.VcM)
    };
}
