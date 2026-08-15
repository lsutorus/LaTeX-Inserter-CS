namespace LaTeXInserter.Models;

/// <summary>
/// Snapshot of the OS input permissions the app needs. On Windows every field is
/// always granted; on macOS these map to TCC Accessibility, TCC Input Monitoring,
/// and the transient EnableSecureEventInput state.
/// </summary>
public sealed record PermissionStatus(
    bool AccessibilityGranted,
    bool InputMonitoringGranted,
    bool SecureInputActive)
{
    public static PermissionStatus AllGranted { get; } = new(true, true, false);

    /// <summary>True when the global hook and paste simulation can work right now.</summary>
    public bool IsUsable => AccessibilityGranted && InputMonitoringGranted;
}
