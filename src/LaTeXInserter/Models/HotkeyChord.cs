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
    public override string ToString() =>
        HotkeyChordFormatter.Format(this, PlatformKinds.Current);
}
