using LaTeXInserter.Models;
using LaTeXInserter.Services;
using SharpHook.Data;
using Xunit;

namespace LaTeXInserter.Tests;

public class HotkeyNormalizerTests
{
    [Fact]
    public void CollapseModifiers_LeftCtrlOnly_MapsToControl()
    {
        var result = HotkeyNormalizer.CollapseModifiers(EventMask.LeftCtrl);
        Assert.Equal(ModifierMask.Control, result);
    }

    [Fact]
    public void CollapseModifiers_RightCtrlOnly_MapsToControl()
    {
        var result = HotkeyNormalizer.CollapseModifiers(EventMask.RightCtrl);
        Assert.Equal(ModifierMask.Control, result);
    }

    [Fact]
    public void CollapseModifiers_BothCtrl_MapsToControl()
    {
        var result = HotkeyNormalizer.CollapseModifiers(EventMask.Ctrl);
        Assert.Equal(ModifierMask.Control, result);
    }

    [Fact]
    public void CollapseModifiers_LeftAltOnly_MapsToAlt()
    {
        var result = HotkeyNormalizer.CollapseModifiers(EventMask.LeftAlt);
        Assert.Equal(ModifierMask.Alt, result);
    }

    [Fact]
    public void CollapseModifiers_RightAltOnly_MapsToAlt()
    {
        var result = HotkeyNormalizer.CollapseModifiers(EventMask.RightAlt);
        Assert.Equal(ModifierMask.Alt, result);
    }

    [Fact]
    public void CollapseModifiers_LeftShiftOnly_MapsToShift()
    {
        var result = HotkeyNormalizer.CollapseModifiers(EventMask.LeftShift);
        Assert.Equal(ModifierMask.Shift, result);
    }

    [Fact]
    public void CollapseModifiers_RightShiftOnly_MapsToShift()
    {
        var result = HotkeyNormalizer.CollapseModifiers(EventMask.RightShift);
        Assert.Equal(ModifierMask.Shift, result);
    }

    [Fact]
    public void CollapseModifiers_LeftMetaOnly_MapsToWindows()
    {
        var result = HotkeyNormalizer.CollapseModifiers(EventMask.LeftMeta);
        Assert.Equal(ModifierMask.Windows, result);
    }

    [Fact]
    public void CollapseModifiers_RightMetaOnly_MapsToWindows()
    {
        var result = HotkeyNormalizer.CollapseModifiers(EventMask.RightMeta);
        Assert.Equal(ModifierMask.Windows, result);
    }

    [Fact]
    public void CollapseModifiers_MixedModifiers_CollapsesAll()
    {
        var raw = EventMask.Ctrl | EventMask.Alt | EventMask.Shift | EventMask.Meta;
        var result = HotkeyNormalizer.CollapseModifiers(raw);
        Assert.Equal(ModifierMask.Control | ModifierMask.Alt | ModifierMask.Shift | ModifierMask.Windows, result);
    }

    [Fact]
    public void CollapseModifiers_NumLockStripped()
    {
        var raw = EventMask.Ctrl | EventMask.NumLock;
        var result = HotkeyNormalizer.CollapseModifiers(raw);
        Assert.Equal(ModifierMask.Control, result);
    }

    [Fact]
    public void CollapseModifiers_CapsLockStripped()
    {
        var raw = EventMask.Alt | EventMask.CapsLock;
        var result = HotkeyNormalizer.CollapseModifiers(raw);
        Assert.Equal(ModifierMask.Alt, result);
    }

    [Fact]
    public void CollapseModifiers_ScrollLockStripped()
    {
        var raw = EventMask.Shift | EventMask.ScrollLock;
        var result = HotkeyNormalizer.CollapseModifiers(raw);
        Assert.Equal(ModifierMask.Shift, result);
    }

    [Fact]
    public void CollapseModifiers_SimulatedEventStripped()
    {
        var raw = EventMask.Ctrl | EventMask.SimulatedEvent;
        var result = HotkeyNormalizer.CollapseModifiers(raw);
        Assert.Equal(ModifierMask.Control, result);
    }

    [Fact]
    public void CollapseModifiers_SuppressEventStripped()
    {
        var raw = EventMask.Alt | EventMask.SuppressEvent;
        var result = HotkeyNormalizer.CollapseModifiers(raw);
        Assert.Equal(ModifierMask.Alt, result);
    }

    [Fact]
    public void CollapseModifiers_MouseButtonsStripped()
    {
        var raw = EventMask.Ctrl | EventMask.Button1 | EventMask.Button2 | EventMask.Button3 | EventMask.Button4 | EventMask.Button5;
        var result = HotkeyNormalizer.CollapseModifiers(raw);
        Assert.Equal(ModifierMask.Control, result);
    }

    [Fact]
    public void CollapseModifiers_NoModifiers_ReturnsNone()
    {
        var result = HotkeyNormalizer.CollapseModifiers(EventMask.None);
        Assert.Equal(ModifierMask.None, result);
    }

    [Fact]
    public void Normalize_SortOrder_CtrlBeforeAltBeforeShiftBeforeWindows()
    {
        var chord = new HotkeyChord(ModifierMask.Windows | ModifierMask.Alt | ModifierMask.Shift | ModifierMask.Control, KeyCode.VcM);
        var result = HotkeyNormalizer.Normalize(chord);
        Assert.Equal(ModifierMask.Control | ModifierMask.Alt | ModifierMask.Shift | ModifierMask.Windows, result.Modifiers);
        Assert.Equal(KeyCode.VcM, result.TriggerKey);
    }

    [Fact]
    public void Normalize_NoModifierChord_PreservesTriggerKey()
    {
        var chord = new HotkeyChord(ModifierMask.None, KeyCode.VcA);
        var result = HotkeyNormalizer.Normalize(chord);
        Assert.Equal(ModifierMask.None, result.Modifiers);
        Assert.Equal(KeyCode.VcA, result.TriggerKey);
    }

    [Fact]
    public void Normalize_ZeroMaskChord_PreservesDefaults()
    {
        var chord = default(HotkeyChord);
        var result = HotkeyNormalizer.Normalize(chord);
        Assert.Equal(ModifierMask.None, result.Modifiers);
        Assert.Equal(KeyCode.VcUndefined, result.TriggerKey);
    }
}
