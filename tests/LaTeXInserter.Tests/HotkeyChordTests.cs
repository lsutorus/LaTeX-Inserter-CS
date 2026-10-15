using System.Text.Json;
using LaTeXInserter.Models;
using SharpHook.Data;
using Xunit;

namespace LaTeXInserter.Tests;

public class HotkeyChordTests
{
    [Fact]
    public void DefaultMatchesExpected()
    {
        var expected = new HotkeyChord(ModifierMask.Control | ModifierMask.Alt, KeyCode.VcM);
        Assert.Equal(expected, AppSettings.Default.Hotkey);
    }

    [Fact]
    public void ModifierMaskFlagsWork()
    {
        var mask = ModifierMask.Control | ModifierMask.Alt;
        Assert.True(mask.HasFlag(ModifierMask.Control));
        Assert.True(mask.HasFlag(ModifierMask.Alt));
        Assert.False(mask.HasFlag(ModifierMask.Shift));
    }

    [Fact]
    public void EqualityWorks()
    {
        var a = new HotkeyChord(ModifierMask.Control, KeyCode.VcM);
        var b = new HotkeyChord(ModifierMask.Control, KeyCode.VcM);
        Assert.Equal(a, b);
    }

    [Fact]
    public void JsonRoundTrip()
    {
        var original = new HotkeyChord(ModifierMask.Control | ModifierMask.Alt, KeyCode.VcM);
        var json = JsonSerializer.Serialize(original, JsonContext.Default.HotkeyChord);
        var deserialized = JsonSerializer.Deserialize(json, JsonContext.Default.HotkeyChord);
        Assert.Equal(original, deserialized);
    }

    [Fact]
    public void Format_MacOs_UsesAppleGlyphsInCanonicalOrder()
    {
        var chord = new HotkeyChord(
            ModifierMask.Control | ModifierMask.Alt, KeyCode.VcM);

        Assert.Equal("⌃⌥M", HotkeyChordFormatter.Format(chord, PlatformKind.MacOS));
    }

    [Fact]
    public void Format_MacOs_MetaRendersAsCommandGlyph()
    {
        var chord = new HotkeyChord(
            ModifierMask.Windows | ModifierMask.Shift, KeyCode.VcK);

        Assert.Equal("⇧⌘K", HotkeyChordFormatter.Format(chord, PlatformKind.MacOS));
    }

    [Fact]
    public void Format_Windows_IsUnchanged()
    {
        var chord = new HotkeyChord(
            ModifierMask.Control | ModifierMask.Alt, KeyCode.VcM);

        Assert.Equal("Ctrl+Alt+M", HotkeyChordFormatter.Format(chord, PlatformKind.Windows));
    }

    [Fact]
    public void Format_MacOs_UndefinedTriggerOmitsKey()
    {
        var chord = new HotkeyChord(ModifierMask.Control, KeyCode.VcUndefined);

        Assert.Equal("⌃", HotkeyChordFormatter.Format(chord, PlatformKind.MacOS));
    }
}
