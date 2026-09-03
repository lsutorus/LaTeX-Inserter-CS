using LaTeXInserter.Models;
using SharpHook.Data;
using Xunit;

namespace LaTeXInserter.Tests;

public class HotkeyBlocklistTests
{
    // Every assertion below names its PlatformKind explicitly. The 1-arg
    // HotkeyBlocklist.IsBlocked(chord) overload resolves PlatformKinds.Current,
    // so an ambient assertion silently changes meaning with the host OS —
    // Windows chords stop matching when the suite runs on a Mac.

    // ------------------------------------------------------------------
    // Reserved on Windows, free on macOS.
    // Each chord is absent from the macOS blocklist, so these rows assert
    // the platform split in both directions rather than merely pinning.
    // ------------------------------------------------------------------
    [Theory]
    // System-critical
    [InlineData(ModifierMask.Control | ModifierMask.Alt, KeyCode.VcDelete)]  // Secure Attention Sequence
    [InlineData(ModifierMask.Control, KeyCode.VcEscape)]                     // Start menu
    [InlineData(ModifierMask.Alt, KeyCode.VcEscape)]                         // cycle windows
    [InlineData(ModifierMask.Alt, KeyCode.VcSpace)]                          // window system menu
    // Shell launchers
    [InlineData(ModifierMask.Windows, KeyCode.VcD)]                          // show desktop
    [InlineData(ModifierMask.Windows, KeyCode.VcE)]                          // File Explorer
    [InlineData(ModifierMask.Windows, KeyCode.VcR)]                          // Run dialog
    [InlineData(ModifierMask.Windows, KeyCode.VcI)]                          // OS Settings — deliberately NOT paired with macOS
                                                                             // Cmd+Comma, which is per-app Preferences
    [InlineData(ModifierMask.Windows, KeyCode.VcS)]                          // Search
    [InlineData(ModifierMask.Windows, KeyCode.VcP)]                          // display projection
    [InlineData(ModifierMask.Windows, KeyCode.VcG)]                          // Game Bar
    // Virtual desktops (macOS Spaces have no default create/close chord)
    [InlineData(ModifierMask.Windows | ModifierMask.Control, KeyCode.VcD)]   // new desktop
    [InlineData(ModifierMask.Windows | ModifierMask.Control, KeyCode.VcF4)]  // close desktop
    // Aero Snap (macOS 13 predates keyboard window tiling)
    [InlineData(ModifierMask.Windows, KeyCode.VcUp)]
    [InlineData(ModifierMask.Windows, KeyCode.VcDown)]
    [InlineData(ModifierMask.Windows, KeyCode.VcLeft)]
    [InlineData(ModifierMask.Windows, KeyCode.VcRight)]
    public void WindowsOnly_BlockedOnWindows_FreeOnMacOs(ModifierMask modifiers, KeyCode key)
    {
        var chord = new HotkeyChord(modifiers, key);
        Assert.True(HotkeyBlocklist.IsBlocked(chord, PlatformKind.Windows));
        Assert.False(HotkeyBlocklist.IsBlocked(chord, PlatformKind.MacOS));
    }

    // ------------------------------------------------------------------
    // Reserved on Windows for a Windows-specific reason, while the same
    // Meta+key chord is independently reserved on macOS for an unrelated
    // one (Cmd+A select-all, Cmd+V paste, Cmd+X cut). Their macOS side is
    // covered by the universal-edit-command rows below, so these pin
    // Windows only — asserting "free on macOS" here would be false.
    // ------------------------------------------------------------------
    [Theory]
    [InlineData(ModifierMask.Windows, KeyCode.VcA)]  // Action Center
    [InlineData(ModifierMask.Windows, KeyCode.VcV)]  // clipboard history
    [InlineData(ModifierMask.Windows, KeyCode.VcX)]  // Quick Link menu
    public void WindowsOnly_ChordAlsoReservedOnMacOs_BlockedOnWindows(ModifierMask modifiers, KeyCode key)
        => Assert.True(HotkeyBlocklist.IsBlocked(new HotkeyChord(modifiers, key), PlatformKind.Windows));

    // ------------------------------------------------------------------
    // Behaviors reserved on both platforms under different chords.
    // Each row carries the Windows chord and its macOS twin, so the
    // counterpart relationship is stated in one place and both platforms
    // are asserted together.
    // ------------------------------------------------------------------
    [Theory]
    // Universal edit commands
    [InlineData(ModifierMask.Control, KeyCode.VcC, ModifierMask.Windows, KeyCode.VcC)]                        // copy
    [InlineData(ModifierMask.Control, KeyCode.VcV, ModifierMask.Windows, KeyCode.VcV)]                        // paste
    [InlineData(ModifierMask.Control, KeyCode.VcX, ModifierMask.Windows, KeyCode.VcX)]                        // cut
    [InlineData(ModifierMask.Control, KeyCode.VcZ, ModifierMask.Windows, KeyCode.VcZ)]                        // undo
    [InlineData(ModifierMask.Control, KeyCode.VcA, ModifierMask.Windows, KeyCode.VcA)]                        // select all
    // Window and application management
    [InlineData(ModifierMask.Alt, KeyCode.VcTab, ModifierMask.Windows, KeyCode.VcTab)]                        // app switcher
    [InlineData(ModifierMask.Alt | ModifierMask.Shift, KeyCode.VcTab,
                ModifierMask.Windows | ModifierMask.Shift, KeyCode.VcTab)]                                    // reverse app switcher
    [InlineData(ModifierMask.Alt, KeyCode.VcF4, ModifierMask.Windows, KeyCode.VcW)]                           // close window
    [InlineData(ModifierMask.Windows, KeyCode.VcM, ModifierMask.Windows, KeyCode.VcM)]                        // minimize
    [InlineData(ModifierMask.Windows, KeyCode.VcTab, ModifierMask.Control, KeyCode.VcUp)]                     // show all windows
                                                                                                              // (Task View / Mission Control)
    // System
    [InlineData(ModifierMask.Control | ModifierMask.Shift, KeyCode.VcEscape,
                ModifierMask.Windows | ModifierMask.Alt, KeyCode.VcEscape)]                                   // force-quit a hung app
    [InlineData(ModifierMask.Windows, KeyCode.VcL, ModifierMask.Windows | ModifierMask.Control, KeyCode.VcQ)]  // lock screen
    [InlineData(ModifierMask.Windows | ModifierMask.Shift, KeyCode.VcS,
                ModifierMask.Windows | ModifierMask.Shift, KeyCode.Vc4)]                                      // region screenshot
    [InlineData(ModifierMask.Windows | ModifierMask.Control, KeyCode.VcLeft,
                ModifierMask.Control, KeyCode.VcLeft)]                                                        // desktop/Space left
    [InlineData(ModifierMask.Windows | ModifierMask.Control, KeyCode.VcRight,
                ModifierMask.Control, KeyCode.VcRight)]                                                       // desktop/Space right
    public void CrossPlatform_ReservedBehavior_BlockedOnBothPlatforms(
        ModifierMask windowsModifiers, KeyCode windowsKey,
        ModifierMask macModifiers, KeyCode macKey)
    {
        Assert.True(HotkeyBlocklist.IsBlocked(
            new HotkeyChord(windowsModifiers, windowsKey), PlatformKind.Windows));
        Assert.True(HotkeyBlocklist.IsBlocked(
            new HotkeyChord(macModifiers, macKey), PlatformKind.MacOS));
    }

    // ------------------------------------------------------------------
    // Reserved on macOS with no Windows counterpart to pair against.
    // ModifierMask.Windows is the Meta key == Command on macOS.
    // ------------------------------------------------------------------
    [Theory]
    [InlineData(ModifierMask.Windows | ModifierMask.Alt, KeyCode.VcSpace)]    // Finder search / input source
    [InlineData(ModifierMask.Control, KeyCode.VcSpace)]                       // input source switch
    [InlineData(ModifierMask.Windows, KeyCode.VcBackQuote)]                   // cycle windows within app
    [InlineData(ModifierMask.Windows, KeyCode.VcH)]                           // hide app
    [InlineData(ModifierMask.Windows, KeyCode.VcComma)]                       // per-app Preferences
    [InlineData(ModifierMask.Control, KeyCode.VcDown)]                        // App Expose
    [InlineData(ModifierMask.Windows | ModifierMask.Shift, KeyCode.Vc3)]      // full-screen screenshot
    [InlineData(ModifierMask.Windows | ModifierMask.Shift, KeyCode.Vc5)]      // screenshot UI
    [InlineData(ModifierMask.Windows | ModifierMask.Shift, KeyCode.VcSlash)]  // Help search
    public void MacOsOnly_BlockedOnMacOs_FreeOnWindows(ModifierMask modifiers, KeyCode key)
    {
        var chord = new HotkeyChord(modifiers, key);
        Assert.True(HotkeyBlocklist.IsBlocked(chord, PlatformKind.MacOS));
        Assert.False(HotkeyBlocklist.IsBlocked(chord, PlatformKind.Windows));
    }

    // ------------------------------------------------------------------
    // Lookup mechanism — platform-agnostic, so exercised on both.
    // ------------------------------------------------------------------
    [Theory]
    // ModifierMask is [Flags], so the declaration order in the blocklist
    // carries no meaning; both rows spell the modifiers in the opposite
    // order to the blocklist entry they must still match.
    [InlineData(PlatformKind.Windows, ModifierMask.Control | ModifierMask.Windows, KeyCode.VcD)]
    [InlineData(PlatformKind.MacOS, ModifierMask.Alt | ModifierMask.Windows, KeyCode.VcEscape)]
    public void IsBlocked_UnnormalizedInput_StillBlocked(
        PlatformKind platform, ModifierMask modifiers, KeyCode key)
        => Assert.True(HotkeyBlocklist.IsBlocked(new HotkeyChord(modifiers, key), platform));

    [Theory]
    [InlineData(PlatformKind.Windows)]
    [InlineData(PlatformKind.MacOS)]
    public void IsBlocked_NonBlockedChord_ReturnsFalse(PlatformKind platform)
        => Assert.False(HotkeyBlocklist.IsBlocked(
            new HotkeyChord(ModifierMask.Control | ModifierMask.Alt, KeyCode.VcM), platform));

    [Theory]
    [InlineData(PlatformKind.Windows)]
    [InlineData(PlatformKind.MacOS)]
    public void IsBlocked_VcUndefined_NotBlocked(PlatformKind platform)
        => Assert.False(HotkeyBlocklist.IsBlocked(
            new HotkeyChord(ModifierMask.Control, KeyCode.VcUndefined), platform));

    [Theory]
    [InlineData(PlatformKind.Windows)]
    [InlineData(PlatformKind.MacOS)]
    public void IsBlocked_NoModifiersNotBlocked(PlatformKind platform)
        => Assert.False(HotkeyBlocklist.IsBlocked(
            new HotkeyChord(ModifierMask.None, KeyCode.VcM), platform));

    // ------------------------------------------------------------------
    // Added by Task 3 alongside the platform-aware blocklist.
    // ------------------------------------------------------------------
    [Fact]
    public void MacOs_BlocksCommandSpace()
    {
        var chord = new HotkeyChord(ModifierMask.Windows, KeyCode.VcSpace);
        Assert.True(HotkeyBlocklist.IsBlocked(chord, PlatformKind.MacOS));
    }

    [Fact]
    public void MacOs_BlocksCommandQ()
    {
        var chord = new HotkeyChord(ModifierMask.Windows, KeyCode.VcQ);
        Assert.True(HotkeyBlocklist.IsBlocked(chord, PlatformKind.MacOS));
    }

    [Fact]
    public void MacOs_DoesNotBlockWindowsOnlyCombos()
    {
        // Win+E is Explorer on Windows; Cmd+E is unremarkable on macOS.
        var chord = new HotkeyChord(ModifierMask.Windows, KeyCode.VcE);
        Assert.False(HotkeyBlocklist.IsBlocked(chord, PlatformKind.MacOS));
        Assert.True(HotkeyBlocklist.IsBlocked(chord, PlatformKind.Windows));
    }

    [Fact]
    public void MacOs_AllowsDefaultHotkey()
    {
        Assert.False(HotkeyBlocklist.IsBlocked(
            AppSettings.Default.Hotkey, PlatformKind.MacOS));
    }

    [Fact]
    public void Windows_AllowsDefaultHotkey()
    {
        Assert.False(HotkeyBlocklist.IsBlocked(
            AppSettings.Default.Hotkey, PlatformKind.Windows));
    }
}
