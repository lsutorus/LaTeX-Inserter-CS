using System.Diagnostics;
using System.Runtime.Versioning;
using Avalonia.Controls;

namespace LaTeXInserter.Platform.MacOS;

/// <summary>
/// macOS-specific NSWindow tweaks Avalonia does not expose.
///
/// Topmost alone does not float a window over a full-screen Space — that needs
/// CanJoinAllSpaces + FullScreenAuxiliary collection behavior. Without this the
/// overlay is invisible whenever the user is in a full-screen app, which is most
/// of the time for editors and browsers.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class MacWindowBehavior
{
    // NSWindowCollectionBehavior
    private const ulong CanJoinAllSpaces = 1UL << 0;
    private const ulong FullScreenAuxiliary = 1UL << 8;

    // NSStatusWindowLevel — above normal and floating windows, below screen saver.
    private const long StatusWindowLevel = 25;

    private static bool _configured;

    public static void ConfigureOverlay(Window window)
    {
        if (_configured) return;

        try
        {
            var handle = window.TryGetPlatformHandle();
            if (handle is null || handle.Handle == IntPtr.Zero) return;

            MacTrace.Write(
                $"ConfigureOverlay: descriptor={handle.HandleDescriptor} " +
                $"class={ObjC.ClassName(handle.Handle)}");

            // Avalonia's macOS platform handle is an NSWindow* for a top-level window,
            // but the descriptor is not contractual and older backends handed back an
            // NSView*. Both are handled: an NSView needs the -[NSView window] hop, an
            // NSWindow is already the target and does not respond to -window at all.
            // Probing rather than assuming is not defensive style here — an
            // unrecognized selector raises an ObjC exception that no C# catch block
            // can intercept, and the process aborts.
            var nsWindow = ObjC.Responds(handle.Handle, "window")
                ? ObjC.Send(handle.Handle, ObjC.Sel("window"))
                : handle.Handle;

            if (nsWindow == IntPtr.Zero) return;

            if (!ObjC.Responds(nsWindow, "setCollectionBehavior:") ||
                !ObjC.Responds(nsWindow, "setLevel:"))
            {
                MacTrace.Write($"ConfigureOverlay: {ObjC.ClassName(nsWindow)} is not an NSWindow, skipped");
                return;
            }

            ObjC.SendVoidUlong(
                nsWindow,
                ObjC.Sel("setCollectionBehavior:"),
                CanJoinAllSpaces | FullScreenAuxiliary);

            ObjC.SendVoidUlong(
                nsWindow,
                ObjC.Sel("setLevel:"),
                unchecked((ulong)StatusWindowLevel));

            MacTrace.Write($"ConfigureOverlay: applied to {ObjC.ClassName(nsWindow)}");
            _configured = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ConfigureOverlay failed: {ex}");
        }
    }
}
