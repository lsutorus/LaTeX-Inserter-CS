using SharpHook.Data;

namespace LaTeXInserter.Models;

public sealed record AppSettings(
    HotkeyChord Hotkey = default,
    bool StartOnStartup = false
)
{
    public static AppSettings Default => new()
    {
        Hotkey = new HotkeyChord(ModifierMask.Control | ModifierMask.Alt, KeyCode.VcM)
    };
}
