using SharpHook.Data;

namespace LaTeXInserter.Platform.MacOS;

/// <summary>
/// Maps SharpHook <see cref="KeyCode"/> values to macOS virtual key codes
/// (<c>kVK_ANSI_*</c> from Carbon's <c>Events.h</c>).
/// </summary>
/// <remarks>
/// Pure lookup with no native calls, so it is unit-testable on any platform —
/// which matters because the layer that consumes it can only be exercised on a
/// Mac with Accessibility granted.
///
/// The layout is the ANSI/US physical layout. macOS virtual key codes are
/// positional, so this is a keyboard-layout-independent statement about which
/// physical key to press, not about which character it produces.
/// </remarks>
internal static class MacVirtualKeyMap
{
    /// <summary>
    /// Returns the macOS virtual key code for <paramref name="keyCode"/>, or
    /// <see langword="null"/> if this key has no mapping here.
    /// </summary>
    public static ushort? Map(KeyCode keyCode) => keyCode switch
    {
        KeyCode.VcA => 0x00,
        KeyCode.VcB => 0x0B,
        KeyCode.VcC => 0x08,
        KeyCode.VcD => 0x02,
        KeyCode.VcE => 0x0E,
        KeyCode.VcF => 0x03,
        KeyCode.VcG => 0x05,
        KeyCode.VcH => 0x04,
        KeyCode.VcI => 0x22,
        KeyCode.VcJ => 0x26,
        KeyCode.VcK => 0x28,
        KeyCode.VcL => 0x25,
        KeyCode.VcM => 0x2E,
        KeyCode.VcN => 0x2D,
        KeyCode.VcO => 0x1F,
        KeyCode.VcP => 0x23,
        KeyCode.VcQ => 0x0C,
        KeyCode.VcR => 0x0F,
        KeyCode.VcS => 0x01,
        KeyCode.VcT => 0x11,
        KeyCode.VcU => 0x20,
        KeyCode.VcV => 0x09,
        KeyCode.VcW => 0x0D,
        KeyCode.VcX => 0x07,
        KeyCode.VcY => 0x10,
        KeyCode.VcZ => 0x06,

        KeyCode.Vc0 => 0x1D,
        KeyCode.Vc1 => 0x12,
        KeyCode.Vc2 => 0x13,
        KeyCode.Vc3 => 0x14,
        KeyCode.Vc4 => 0x15,
        KeyCode.Vc5 => 0x17,
        KeyCode.Vc6 => 0x16,
        KeyCode.Vc7 => 0x1A,
        KeyCode.Vc8 => 0x1C,
        KeyCode.Vc9 => 0x19,

        KeyCode.VcMinus => 0x1B,
        KeyCode.VcEquals => 0x18,
        KeyCode.VcOpenBracket => 0x21,
        KeyCode.VcCloseBracket => 0x1E,
        KeyCode.VcBackslash => 0x2A,
        KeyCode.VcSemicolon => 0x29,
        KeyCode.VcQuote => 0x27,
        KeyCode.VcBackQuote => 0x32,
        KeyCode.VcComma => 0x2B,
        KeyCode.VcPeriod => 0x2F,
        KeyCode.VcSlash => 0x2C,

        KeyCode.VcEnter => 0x24,
        KeyCode.VcTab => 0x30,
        KeyCode.VcSpace => 0x31,
        KeyCode.VcBackspace => 0x33,
        KeyCode.VcEscape => 0x35,
        KeyCode.VcDelete => 0x75,

        KeyCode.VcLeftMeta => 0x37,
        KeyCode.VcRightMeta => 0x36,
        KeyCode.VcLeftShift => 0x38,
        KeyCode.VcRightShift => 0x3C,
        KeyCode.VcLeftAlt => 0x3A,
        KeyCode.VcRightAlt => 0x3D,
        KeyCode.VcLeftControl => 0x3B,
        KeyCode.VcRightControl => 0x3E,
        KeyCode.VcCapsLock => 0x39,

        _ => null,
    };

    /// <summary>
    /// The <c>kCGEventFlagMask*</c> bit a modifier key contributes, or 0 for
    /// non-modifier keys.
    /// </summary>
    /// <remarks>
    /// Posted events do not inherit modifier state from the keys already "down"
    /// in the synthetic stream — the flags must be set explicitly on every event
    /// in the chord, so the caller accumulates these.
    /// </remarks>
    public static ulong ModifierFlag(KeyCode keyCode) => keyCode switch
    {
        KeyCode.VcLeftMeta or KeyCode.VcRightMeta => 0x00100000,      // Command
        KeyCode.VcLeftShift or KeyCode.VcRightShift => 0x00020000,    // Shift
        KeyCode.VcLeftControl or KeyCode.VcRightControl => 0x00040000,// Control
        KeyCode.VcLeftAlt or KeyCode.VcRightAlt => 0x00080000,        // Option
        _ => 0,
    };
}
