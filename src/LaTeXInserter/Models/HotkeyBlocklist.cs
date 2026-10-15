using System.Collections.Frozen;
using SharpHook.Data;

namespace LaTeXInserter.Models;

public static class HotkeyBlocklist
{
    private static readonly FrozenSet<HotkeyChord> WindowsBlocked = CreateWindowsBlocklist();
    private static readonly FrozenSet<HotkeyChord> MacBlocked = CreateMacBlocklist();

    public static bool IsBlocked(HotkeyChord chord) => IsBlocked(chord, PlatformKinds.Current);

    public static bool IsBlocked(HotkeyChord chord, PlatformKind platform) => platform switch
    {
        PlatformKind.MacOS => MacBlocked.Contains(chord),
        _ => WindowsBlocked.Contains(chord)
    };

    private static FrozenSet<HotkeyChord> CreateWindowsBlocklist()
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

        return entries.ToFrozenSet();
    }

    // ModifierMask.Windows is the Meta key == Command on macOS.
    private static FrozenSet<HotkeyChord> CreateMacBlocklist()
    {
        var entries = new HashSet<HotkeyChord>
        {
            // Spotlight / input source
            new(ModifierMask.Windows, KeyCode.VcSpace),
            new(ModifierMask.Windows | ModifierMask.Alt, KeyCode.VcSpace),
            new(ModifierMask.Control, KeyCode.VcSpace),

            // App / window switching
            new(ModifierMask.Windows, KeyCode.VcTab),
            new(ModifierMask.Windows | ModifierMask.Shift, KeyCode.VcTab),
            new(ModifierMask.Windows, KeyCode.VcBackQuote),

            // Universal app commands
            new(ModifierMask.Windows, KeyCode.VcQ),
            new(ModifierMask.Windows, KeyCode.VcW),
            new(ModifierMask.Windows, KeyCode.VcH),
            new(ModifierMask.Windows, KeyCode.VcM),
            new(ModifierMask.Windows, KeyCode.VcComma),

            // Universal edit commands
            new(ModifierMask.Windows, KeyCode.VcC),
            new(ModifierMask.Windows, KeyCode.VcV),
            new(ModifierMask.Windows, KeyCode.VcX),
            new(ModifierMask.Windows, KeyCode.VcZ),
            new(ModifierMask.Windows, KeyCode.VcA),

            // Mission Control / Spaces
            new(ModifierMask.Control, KeyCode.VcUp),
            new(ModifierMask.Control, KeyCode.VcDown),
            new(ModifierMask.Control, KeyCode.VcLeft),
            new(ModifierMask.Control, KeyCode.VcRight),

            // Screenshots
            new(ModifierMask.Windows | ModifierMask.Shift, KeyCode.Vc3),
            new(ModifierMask.Windows | ModifierMask.Shift, KeyCode.Vc4),
            new(ModifierMask.Windows | ModifierMask.Shift, KeyCode.Vc5),

            // Lock screen, Force Quit, Help
            new(ModifierMask.Windows | ModifierMask.Control, KeyCode.VcQ),
            new(ModifierMask.Windows | ModifierMask.Alt, KeyCode.VcEscape),
            new(ModifierMask.Windows | ModifierMask.Shift, KeyCode.VcSlash),
        };

        return entries.ToFrozenSet();
    }
}
