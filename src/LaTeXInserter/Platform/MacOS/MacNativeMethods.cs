using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LaTeXInserter.Platform.MacOS;

[SupportedOSPlatform("macos")]
internal static partial class MacNativeMethods
{
    private const string CoreGraphics =
        "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string ApplicationServices =
        "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
    private const string IOKit =
        "/System/Library/Frameworks/IOKit.framework/IOKit";
    private const string Carbon =
        "/System/Library/Frameworks/Carbon.framework/Carbon";

    /// <summary>Global display coordinates, top-left origin, in points (not pixels).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CGPoint
    {
        public double X;
        public double Y;
    }

    // kCGEventSourceStateHIDSystemState
    private const int EventSourceStateHidSystemState = 1;

    // kCGSessionEventTap. NOT kCGHIDEventTap (0): on at least one Intel Mac running
    // macOS 13.7.8, every event posted to the HID tap is silently discarded, while
    // the identical event on the session tap is delivered. That is the whole of
    // blocker 12a — libuiohook posts only to the HID tap, so SharpHook's simulation
    // can never deliver a keystroke there. Measured, not inferred: a probe posted
    // Cmd+V on both taps back to back, and only the session tap pasted.
    private const uint SessionEventTap = 1;

    // kCGEventFlagMaskCommand
    public const ulong EventFlagMaskCommand = 0x00100000;

    // kIOHIDRequestTypeListenEvent
    public const uint IOHIDRequestTypeListenEvent = 1;
    // IOHIDAccessType
    public const uint IOHIDAccessTypeGranted = 0;
    public const uint IOHIDAccessTypeDenied = 1;
    public const uint IOHIDAccessTypeUnknown = 2;

    [LibraryImport(CoreGraphics)]
    private static partial IntPtr CGEventCreate(IntPtr source);

    [LibraryImport(CoreGraphics)]
    private static partial CGPoint CGEventGetLocation(IntPtr @event);

    [LibraryImport(CoreFoundation)]
    private static partial void CFRelease(IntPtr cf);

    [LibraryImport(CoreGraphics)]
    private static partial IntPtr CGEventSourceCreate(int stateID);

    [LibraryImport(CoreGraphics)]
    private static partial IntPtr CGEventCreateKeyboardEvent(
        IntPtr source, ushort virtualKey, [MarshalAs(UnmanagedType.U1)] bool keyDown);

    [LibraryImport(CoreGraphics)]
    private static partial void CGEventSetFlags(IntPtr @event, ulong flags);

    [LibraryImport(CoreGraphics)]
    private static partial void CGEventPost(uint tap, IntPtr @event);

    [LibraryImport(ApplicationServices)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool AXIsProcessTrusted();

    [LibraryImport(IOKit)]
    public static partial uint IOHIDCheckAccess(uint requestType);

    [LibraryImport(IOKit)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool IOHIDRequestAccess(uint requestType);

    [LibraryImport(Carbon)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool IsSecureEventInputEnabled();

    /// <summary>
    /// Creates the shared event source used for synthetic key events, or
    /// <see cref="IntPtr.Zero"/> if one cannot be made.
    /// </summary>
    /// <remarks>
    /// A real source matters: the same probe that found the tap bug also showed
    /// that posting with a NULL source is dropped even on the working session tap.
    /// </remarks>
    public static IntPtr CreateKeyEventSource() =>
        CGEventSourceCreate(EventSourceStateHidSystemState);

    /// <summary>Releases a Core Foundation object obtained from this class.</summary>
    public static void Release(IntPtr cf)
    {
        if (cf != IntPtr.Zero)
            CFRelease(cf);
    }

    /// <summary>
    /// Posts one synthetic key event to the session event tap.
    /// </summary>
    /// <returns><c>false</c> if the event could not be created.</returns>
    public static bool PostKeyEvent(IntPtr source, ushort virtualKey, bool keyDown, ulong flags)
    {
        var evt = CGEventCreateKeyboardEvent(source, virtualKey, keyDown);
        if (evt == IntPtr.Zero)
            return false;

        try
        {
            CGEventSetFlags(evt, flags);
            CGEventPost(SessionEventTap, evt);
            return true;
        }
        finally
        {
            CFRelease(evt);
        }
    }

    /// <summary>Current mouse location in global display coordinates (top-left origin).</summary>
    public static CGPoint GetCursorLocation()
    {
        var evt = CGEventCreate(IntPtr.Zero);
        if (evt == IntPtr.Zero)
            return default;

        try
        {
            return CGEventGetLocation(evt);
        }
        finally
        {
            CFRelease(evt);
        }
    }
}
