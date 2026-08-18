using System.Diagnostics;
using System.Runtime.Versioning;
using LaTeXInserter.Abstractions;

namespace LaTeXInserter.Platform.MacOS;

/// <summary>
/// Captures and restores the frontmost application around the overlay.
///
/// The overlayHandle argument is ignored: Avalonia's macOS platform handle is an
/// NSView*, and macOS activation is per-application, not per-window. Activating
/// NSApp is both correct and sufficient because the app is an LSUIElement accessory
/// with exactly one visible window at a time.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacWindowActivator : IWindowActivator
{
    // NSApplicationActivationOptions
    private const ulong ActivateAllWindows = 1UL << 0;
    private const ulong ActivateIgnoringOtherApps = 1UL << 1;

    private int _previousPid;

    static MacWindowActivator()
    {
        ObjC.LoadFramework(ObjC.AppKitPath);
    }

    public void CapturePrevious()
    {
        try
        {
            var workspaceClass = ObjC.GetClass("NSWorkspace");
            var workspace = ObjC.Send(workspaceClass, ObjC.Sel("sharedWorkspace"));
            var frontmost = ObjC.Send(workspace, ObjC.Sel("frontmostApplication"));

            _previousPid = frontmost == IntPtr.Zero
                ? 0
                : ObjC.SendInt(frontmost, ObjC.Sel("processIdentifier"));
        }
        catch (Exception ex)
        {
            _previousPid = 0;
            Debug.WriteLine($"CapturePrevious failed: {ex}");
        }
    }

    public void Activate(IntPtr overlayHandle)
    {
        try
        {
            var appClass = ObjC.GetClass("NSApplication");
            var app = ObjC.Send(appClass, ObjC.Sel("sharedApplication"));
            // -[NSApplication activateIgnoringOtherApps:] takes a BOOL.
            ObjC.SendBoolByte(app, ObjC.Sel("activateIgnoringOtherApps:"), 1);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Activate failed: {ex}");
        }
    }

    public void Restore()
    {
        if (_previousPid == 0) return;

        try
        {
            // 1. Yield activation. Always permitted, unlike cross-app activation.
            var appClass = ObjC.GetClass("NSApplication");
            var app = ObjC.Send(appClass, ObjC.Sel("sharedApplication"));
            ObjC.Send(app, ObjC.Sel("deactivate"));

            // 2. Ask the previous app to come forward. macOS 14+ may refuse this;
            //    step 1 already put us behind it, so the outcome is still correct.
            var runningAppClass = ObjC.GetClass("NSRunningApplication");
            var target = ObjC.Send(
                runningAppClass,
                ObjC.Sel("runningApplicationWithProcessIdentifier:"),
                _previousPid);

            if (target != IntPtr.Zero)
            {
                ObjC.SendBoolUlong(
                    target,
                    ObjC.Sel("activateWithOptions:"),
                    ActivateAllWindows | ActivateIgnoringOtherApps);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Restore failed: {ex}");
        }
    }
}
