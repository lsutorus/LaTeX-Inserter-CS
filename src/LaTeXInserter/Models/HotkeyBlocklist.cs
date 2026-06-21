using System.Collections.Frozen;
using LaTeXInserter.Models;
using SharpHook.Data;

namespace LaTeXInserter.Models;

public static class HotkeyBlocklist
{
    private static readonly FrozenSet<HotkeyChord> Blocked = CreateBlocklist();

    public static bool IsBlocked(HotkeyChord chord)
    {
        return Blocked.Contains(Services.HotkeyNormalizer.Normalize(chord));
    }

    private static FrozenSet<HotkeyChord> CreateBlocklist()
    {
        var entries = new HashSet<HotkeyChord>
        {
            // System-critical
            new(ModifierMask.Control | ModifierMask.Alt, KeyCode.VcDelete),
            new(ModifierMask.Control | ModifierMask.Shift, KeyCode.VcEscape),

            // Alt combos
            new(ModifierMask.Alt, KeyCode.VcTab),
            new(ModifierMask.Alt | ModifierMask.Shift, KeyCode.VcTab),
            new(ModifierMask.Alt, KeyCode.VcF4),
            new(ModifierMask.Alt, KeyCode.VcSpace),
            new(ModifierMask.Alt, KeyCode.VcEscape),

            // Ctrl combos
            new(ModifierMask.Control, KeyCode.VcEscape),
            new(ModifierMask.Control, KeyCode.VcC),
            new(ModifierMask.Control, KeyCode.VcV),
            new(ModifierMask.Control, KeyCode.VcX),
            new(ModifierMask.Control, KeyCode.VcZ),
            new(ModifierMask.Control, KeyCode.VcA),

            // Win combos
            new(ModifierMask.Windows, KeyCode.VcTab),
            new(ModifierMask.Windows, KeyCode.VcL),
            new(ModifierMask.Windows, KeyCode.VcD),
            new(ModifierMask.Windows, KeyCode.VcE),
            new(ModifierMask.Windows, KeyCode.VcR),
            new(ModifierMask.Windows, KeyCode.VcI),
            new(ModifierMask.Windows, KeyCode.VcS),
            new(ModifierMask.Windows, KeyCode.VcA),
            new(ModifierMask.Windows, KeyCode.VcP),
            new(ModifierMask.Windows, KeyCode.VcV),
            new(ModifierMask.Windows, KeyCode.VcX),
            new(ModifierMask.Windows, KeyCode.VcG),
            new(ModifierMask.Windows, KeyCode.VcM),
            new(ModifierMask.Windows | ModifierMask.Shift, KeyCode.VcS),
            new(ModifierMask.Windows | ModifierMask.Control, KeyCode.VcD),
            new(ModifierMask.Windows | ModifierMask.Control, KeyCode.VcF4),
            new(ModifierMask.Windows | ModifierMask.Control, KeyCode.VcLeft),
            new(ModifierMask.Windows | ModifierMask.Control, KeyCode.VcRight),
            new(ModifierMask.Windows, KeyCode.VcUp),
            new(ModifierMask.Windows, KeyCode.VcDown),
            new(ModifierMask.Windows, KeyCode.VcLeft),
            new(ModifierMask.Windows, KeyCode.VcRight),
        };

        return entries.Select(Services.HotkeyNormalizer.Normalize).ToFrozenSet();
    }
}
