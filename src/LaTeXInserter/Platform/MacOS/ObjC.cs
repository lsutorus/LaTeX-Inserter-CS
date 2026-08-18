using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LaTeXInserter.Platform.MacOS;

/// <summary>
/// Minimal Objective-C runtime bridge. Native-AOT safe: no reflection, no dynamic
/// dispatch. objc_msgSend is not variadic-safe on arm64, so every call shape gets
/// its own [LibraryImport] declaration with exact parameter types.
/// </summary>
[SupportedOSPlatform("macos")]
internal static partial class ObjC
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";

    [LibraryImport(LibObjC, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr objc_getClass(string name);

    [LibraryImport(LibObjC, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr sel_registerName(string name);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial IntPtr msgSend_ptr(IntPtr receiver, IntPtr selector);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial IntPtr msgSend_ptr_ptr(IntPtr receiver, IntPtr selector, IntPtr arg1);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial IntPtr msgSend_ptr_int(IntPtr receiver, IntPtr selector, int arg1);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool msgSend_bool_ptr(IntPtr receiver, IntPtr selector, IntPtr arg1);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool msgSend_bool_ulong(IntPtr receiver, IntPtr selector, ulong arg1);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool msgSend_bool_byte(IntPtr receiver, IntPtr selector, byte arg1);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial long msgSend_long(IntPtr receiver, IntPtr selector);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial int msgSend_int(IntPtr receiver, IntPtr selector);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial void msgSend_void_ptr(IntPtr receiver, IntPtr selector, IntPtr arg1);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial void msgSend_void_ulong(IntPtr receiver, IntPtr selector, ulong arg1);

    public static IntPtr GetClass(string name) => objc_getClass(name);
    public static IntPtr Sel(string name) => sel_registerName(name);

    public static IntPtr Send(IntPtr recv, IntPtr sel) => msgSend_ptr(recv, sel);
    public static IntPtr Send(IntPtr recv, IntPtr sel, IntPtr arg) => msgSend_ptr_ptr(recv, sel, arg);
    public static IntPtr Send(IntPtr recv, IntPtr sel, int arg) => msgSend_ptr_int(recv, sel, arg);
    public static bool SendBool(IntPtr recv, IntPtr sel, IntPtr arg) => msgSend_bool_ptr(recv, sel, arg);
    public static bool SendBoolUlong(IntPtr recv, IntPtr sel, ulong arg) => msgSend_bool_ulong(recv, sel, arg);
    public static bool SendBoolByte(IntPtr recv, IntPtr sel, byte arg) => msgSend_bool_byte(recv, sel, arg);
    public static long SendLong(IntPtr recv, IntPtr sel) => msgSend_long(recv, sel);
    public static int SendInt(IntPtr recv, IntPtr sel) => msgSend_int(recv, sel);
    public static void SendVoid(IntPtr recv, IntPtr sel, IntPtr arg) => msgSend_void_ptr(recv, sel, arg);
    public static void SendVoidUlong(IntPtr recv, IntPtr sel, ulong arg) => msgSend_void_ulong(recv, sel, arg);

    /// <summary>
    /// Frameworks whose classes are not already loaded (ServiceManagement) must be
    /// dlopen'd before objc_getClass can find them. AppKit is already resident
    /// because Avalonia links it, but loading twice is harmless and cheap.
    /// </summary>
    public static void LoadFramework(string absolutePath)
    {
        if (!NativeLibrary.TryLoad(absolutePath, out _))
            throw new DllNotFoundException($"Failed to load framework: {absolutePath}");
    }

    public const string AppKitPath =
        "/System/Library/Frameworks/AppKit.framework/AppKit";
    public const string ServiceManagementPath =
        "/System/Library/Frameworks/ServiceManagement.framework/ServiceManagement";
}
