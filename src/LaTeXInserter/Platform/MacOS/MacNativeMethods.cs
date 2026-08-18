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
