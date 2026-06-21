using LaTeXInserter.Models;
using SharpHook.Data;
using Xunit;

namespace LaTeXInserter.Tests;

public class HotkeyBlocklistTests
{
    [Fact]
    public void IsBlocked_CtrlAltDelete_Blocked()
    {
        Assert.True(HotkeyBlocklist.IsBlocked(new HotkeyChord(ModifierMask.Control | ModifierMask.Alt, KeyCode.VcDelete)));
    }

    [Fact]
    public void IsBlocked_CtrlShiftEscape_Blocked()
    {
        Assert.True(HotkeyBlocklist.IsBlocked(new HotkeyChord(ModifierMask.Control | ModifierMask.Shift, KeyCode.VcEscape)));
    }

    [Fact]
    public void IsBlocked_AltTab_Blocked()
    {
        Assert.True(HotkeyBlocklist.IsBlocked(new HotkeyChord(ModifierMask.Alt, KeyCode.VcTab)));
    }

    [Fact]
    public void IsBlocked_AltShiftTab_Blocked()
    {
        Assert.True(HotkeyBlocklist.IsBlocked(new HotkeyChord(ModifierMask.Alt | ModifierMask.Shift, KeyCode.VcTab)));
    }

    [Fact]
    public void IsBlocked_AltF4_Blocked()
    {
        Assert.True(HotkeyBlocklist.IsBlocked(new HotkeyChord(ModifierMask.Alt, KeyCode.VcF4)));
    }

    [Fact]
    public void IsBlocked_AltSpace_Blocked()
    {
        Assert.True(HotkeyBlocklist.IsBlocked(new HotkeyChord(ModifierMask.Alt, KeyCode.VcSpace)));
    }

    [Fact]
    public void IsBlocked_AltEscape_Blocked()
    {
        Assert.True(HotkeyBlocklist.IsBlocked(new HotkeyChord(ModifierMask.Alt, KeyCode.VcEscape)));
    }

    [Fact]
    public void IsBlocked_CtrlEscape_Blocked()
    {
        Assert.True(HotkeyBlocklist.IsBlocked(new HotkeyChord(ModifierMask.Control, KeyCode.VcEscape)));
    }

    [Fact]
    public void IsBlocked_CtrlC_Blocked()
    {
        Assert.True(HotkeyBlocklist.IsBlocked(new HotkeyChord(ModifierMask.Control, KeyCode.VcC)));
    }

    [Fact]
    public void IsBlocked_CtrlV_Blocked()
    {
        Assert.True(HotkeyBlocklist.IsBlocked(new HotkeyChord(ModifierMask.Control, KeyCode.VcV)));
    }

    [Fact]
    public void IsBlocked_CtrlX_Blocked()
    {
        Assert.True(HotkeyBlocklist.IsBlocked(new HotkeyChord(ModifierMask.Control, KeyCode.VcX)));
    }

    [Fact]
    public void IsBlocked_CtrlZ_Blocked()
    {
        Assert.True(HotkeyBlocklist.IsBlocked(new HotkeyChord(ModifierMask.Control, KeyCode.VcZ)));
    }

    [Fact]
    public void IsBlocked_CtrlA_Blocked()
    {
        Assert.True(HotkeyBlocklist.IsBlocked(new HotkeyChord(ModifierMask.Control, KeyCode.VcA)));
    }

    [Fact]
    public void IsBlocked_WinTab_Blocked()
    {
        Assert.True(HotkeyBlocklist.IsBlocked(new HotkeyChord(ModifierMask.Windows, KeyCode.VcTab)));
    }

    [Fact]
    public void IsBlocked_WinL_Blocked()
    {
        Assert.True(HotkeyBlocklist.IsBlocked(new HotkeyChord(ModifierMask.Windows, KeyCode.VcL)));
    }

    [Fact]
    public void IsBlocked_WinD_Blocked()
    {
        Assert.True(HotkeyBlocklist.IsBlocked(new HotkeyChord(ModifierMask.Windows, KeyCode.VcD)));
    }

    [Fact]
    public void IsBlocked_WinShiftS_Blocked()
    {
        Assert.True(HotkeyBlocklist.IsBlocked(new HotkeyChord(ModifierMask.Windows | ModifierMask.Shift, KeyCode.VcS)));
    }

    [Fact]
    public void IsBlocked_WinCtrlD_Blocked()
    {
        Assert.True(HotkeyBlocklist.IsBlocked(new HotkeyChord(ModifierMask.Windows | ModifierMask.Control, KeyCode.VcD)));
    }

    [Fact]
    public void IsBlocked_WinCtrlF4_Blocked()
    {
        Assert.True(HotkeyBlocklist.IsBlocked(new HotkeyChord(ModifierMask.Windows | ModifierMask.Control, KeyCode.VcF4)));
    }

    [Fact]
    public void IsBlocked_WinCtrlLeft_Blocked()
    {
        Assert.True(HotkeyBlocklist.IsBlocked(new HotkeyChord(ModifierMask.Windows | ModifierMask.Control, KeyCode.VcLeft)));
    }

    [Fact]
    public void IsBlocked_WinCtrlRight_Blocked()
    {
        Assert.True(HotkeyBlocklist.IsBlocked(new HotkeyChord(ModifierMask.Windows | ModifierMask.Control, KeyCode.VcRight)));
    }

    [Fact]
    public void IsBlocked_WinUp_Blocked()
    {
        Assert.True(HotkeyBlocklist.IsBlocked(new HotkeyChord(ModifierMask.Windows, KeyCode.VcUp)));
    }

    [Fact]
    public void IsBlocked_WinDown_Blocked()
    {
        Assert.True(HotkeyBlocklist.IsBlocked(new HotkeyChord(ModifierMask.Windows, KeyCode.VcDown)));
    }

    [Fact]
    public void IsBlocked_WinLeft_Blocked()
    {
        Assert.True(HotkeyBlocklist.IsBlocked(new HotkeyChord(ModifierMask.Windows, KeyCode.VcLeft)));
    }

    [Fact]
    public void IsBlocked_WinRight_Blocked()
    {
        Assert.True(HotkeyBlocklist.IsBlocked(new HotkeyChord(ModifierMask.Windows, KeyCode.VcRight)));
    }

    [Fact]
    public void IsBlocked_NonBlockedChord_ReturnsFalse()
    {
        Assert.False(HotkeyBlocklist.IsBlocked(new HotkeyChord(ModifierMask.Control | ModifierMask.Alt, KeyCode.VcM)));
    }

    [Fact]
    public void IsBlocked_UnnormalizedInput_StillBlocked()
    {
        // IsBlocked normalizes input, so unsorted modifiers still match
        var chord = new HotkeyChord(ModifierMask.Windows | ModifierMask.Control, KeyCode.VcD);
        Assert.True(HotkeyBlocklist.IsBlocked(chord));
    }

    [Fact]
    public void IsBlocked_VcUndefined_NotBlocked()
    {
        Assert.False(HotkeyBlocklist.IsBlocked(new HotkeyChord(ModifierMask.Control, KeyCode.VcUndefined)));
    }

    [Fact]
    public void IsBlocked_NoModifiersNotBlocked()
    {
        Assert.False(HotkeyBlocklist.IsBlocked(new HotkeyChord(ModifierMask.None, KeyCode.VcM)));
    }

    [Fact]
    public void IsBlocked_AllWinCombos_WinE()
    {
        Assert.True(HotkeyBlocklist.IsBlocked(new HotkeyChord(ModifierMask.Windows, KeyCode.VcE)));
    }

    [Fact]
    public void IsBlocked_AllWinCombos_WinR()
    {
        Assert.True(HotkeyBlocklist.IsBlocked(new HotkeyChord(ModifierMask.Windows, KeyCode.VcR)));
    }

    [Fact]
    public void IsBlocked_AllWinCombos_WinI()
    {
        Assert.True(HotkeyBlocklist.IsBlocked(new HotkeyChord(ModifierMask.Windows, KeyCode.VcI)));
    }

    [Fact]
    public void IsBlocked_AllWinCombos_WinS()
    {
        Assert.True(HotkeyBlocklist.IsBlocked(new HotkeyChord(ModifierMask.Windows, KeyCode.VcS)));
    }

    [Fact]
    public void IsBlocked_AllWinCombos_WinA()
    {
        Assert.True(HotkeyBlocklist.IsBlocked(new HotkeyChord(ModifierMask.Windows, KeyCode.VcA)));
    }

    [Fact]
    public void IsBlocked_AllWinCombos_WinP()
    {
        Assert.True(HotkeyBlocklist.IsBlocked(new HotkeyChord(ModifierMask.Windows, KeyCode.VcP)));
    }

    [Fact]
    public void IsBlocked_AllWinCombos_WinV()
    {
        Assert.True(HotkeyBlocklist.IsBlocked(new HotkeyChord(ModifierMask.Windows, KeyCode.VcV)));
    }

    [Fact]
    public void IsBlocked_AllWinCombos_WinX()
    {
        Assert.True(HotkeyBlocklist.IsBlocked(new HotkeyChord(ModifierMask.Windows, KeyCode.VcX)));
    }

    [Fact]
    public void IsBlocked_AllWinCombos_WinG()
    {
        Assert.True(HotkeyBlocklist.IsBlocked(new HotkeyChord(ModifierMask.Windows, KeyCode.VcG)));
    }

    [Fact]
    public void IsBlocked_AllWinCombos_WinM()
    {
        Assert.True(HotkeyBlocklist.IsBlocked(new HotkeyChord(ModifierMask.Windows, KeyCode.VcM)));
    }
}
