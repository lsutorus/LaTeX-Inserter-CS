using System.Text.Json.Serialization;
using SharpHook.Data;

namespace LaTeXInserter.Models;

[Flags]
public enum ModifierMask
{
    None = 0,
    Control = 1,
    Alt = 2,
    Shift = 4,
    Windows = 8
}

public readonly record struct HotkeyChord(
    [property: JsonConverter(typeof(JsonStringEnumConverter<ModifierMask>))]
    ModifierMask Modifiers,
    [property: JsonConverter(typeof(KeyCodeConverter))]
    KeyCode TriggerKey
)
{
    public override string ToString()
    {
        var parts = new List<string>(5);
        if ((Modifiers & ModifierMask.Control) != 0) parts.Add("Ctrl");
        if ((Modifiers & ModifierMask.Alt) != 0) parts.Add("Alt");
        if ((Modifiers & ModifierMask.Shift) != 0) parts.Add("Shift");
        if ((Modifiers & ModifierMask.Windows) != 0) parts.Add("Win");
        if (TriggerKey != KeyCode.VcUndefined) parts.Add(FormatKeyCode(TriggerKey));
        return string.Join("+", parts);
    }

    private static string FormatKeyCode(KeyCode key)
    {
        var name = key.ToString();
        if (name.StartsWith("Vc")) return name[2..];
        return name;
    }
}
