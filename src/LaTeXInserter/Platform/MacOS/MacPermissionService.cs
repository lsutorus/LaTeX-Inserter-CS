using System.Diagnostics;
using System.Runtime.Versioning;
using LaTeXInserter.Abstractions;
using LaTeXInserter.Models;

namespace LaTeXInserter.Platform.MacOS;

/// <summary>
/// Reads macOS TCC state for the two permissions libuiohook needs.
///
/// Query() never prompts — it is safe to poll from the UI. The system prompt is
/// raised by libuiohook itself the first time it tries to create an event tap
/// without Accessibility access; this service only reports status and deep-links
/// the user to the right settings pane.
///
/// TCC grants are keyed to the app's code signature. Unsigned local builds are
/// re-prompted on every rebuild, and moving from an unsigned dev build to a signed
/// release invalidates the existing grant. That is expected, not a bug.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacPermissionService : IPermissionService
{
    private const string AccessibilityPane =
        "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility";
    private const string InputMonitoringPane =
        "x-apple.systempreferences:com.apple.preference.security?Privacy_ListenEvent";

    public bool RequiresUserAction => true;

    public PermissionStatus Query()
    {
        bool accessibility = MacNativeMethods.AXIsProcessTrusted();

        uint hid = MacNativeMethods.IOHIDCheckAccess(
            MacNativeMethods.IOHIDRequestTypeListenEvent);
        // Unknown means "not yet asked" — treat as not granted so the UI prompts.
        bool inputMonitoring = hid == MacNativeMethods.IOHIDAccessTypeGranted;

        bool secureInput = MacNativeMethods.IsSecureEventInputEnabled();

        return new PermissionStatus(accessibility, inputMonitoring, secureInput);
    }

    public void OpenAccessibilitySettings() => OpenUrl(AccessibilityPane);
    public void OpenInputMonitoringSettings() => OpenUrl(InputMonitoringPane);

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo("open", url) { UseShellExecute = false });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open settings pane {url}: {ex}");
        }
    }
}
