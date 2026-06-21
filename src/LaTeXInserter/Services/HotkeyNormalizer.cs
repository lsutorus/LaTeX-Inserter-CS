using LaTeXInserter.Models;
using SharpHook.Data;

namespace LaTeXInserter.Services;

public static class HotkeyNormalizer
{
    private const EventMask StripMask =
        EventMask.NumLock | EventMask.CapsLock | EventMask.ScrollLock
        | EventMask.SimulatedEvent | EventMask.SuppressEvent
        | EventMask.Button1 | EventMask.Button2 | EventMask.Button3
        | EventMask.Button4 | EventMask.Button5;

    public static ModifierMask CollapseModifiers(EventMask rawMask)
    {
        var mask = rawMask & ~StripMask;
        var result = ModifierMask.None;

        if ((mask & EventMask.Ctrl) != 0)
            result |= ModifierMask.Control;
        if ((mask & EventMask.Alt) != 0)
            result |= ModifierMask.Alt;
        if ((mask & EventMask.Shift) != 0)
            result |= ModifierMask.Shift;
        if ((mask & EventMask.Meta) != 0)
            result |= ModifierMask.Windows;

        return result;
    }

    public static HotkeyChord Normalize(HotkeyChord chord)
    {
        // Sort: Ctrl → Alt → Shift → Windows, then trigger
        var sorted = chord.Modifiers;
        return new HotkeyChord(sorted, chord.TriggerKey);
    }
}
