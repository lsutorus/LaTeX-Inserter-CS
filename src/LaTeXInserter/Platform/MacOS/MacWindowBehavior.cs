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

            // Avalonia's macOS handle is an NSView*; -[NSView window] gives the NSWindow*.
            var nsWindow = ObjC.Send(handle.Handle, ObjC.Sel("window"));
            if (nsWindow == IntPtr.Zero) return;

            ObjC.SendVoidUlong(
                nsWindow,
                ObjC.Sel("setCollectionBehavior:"),
                CanJoinAllSpaces | FullScreenAuxiliary);

            ObjC.SendVoidUlong(
                nsWindow,
                ObjC.Sel("setLevel:"),
                unchecked((ulong)StatusWindowLevel));

            _configured = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ConfigureOverlay failed: {ex}");
        }
    }
}
