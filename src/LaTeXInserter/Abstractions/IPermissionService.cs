using LaTeXInserter.Models;

namespace LaTeXInserter.Abstractions;

public interface IPermissionService
{
    /// <summary>Cheap, synchronous, safe to poll. Never prompts.</summary>
    PermissionStatus Query();

    /// <summary>True when this platform can ever require the user to grant permissions.</summary>
    bool RequiresUserAction { get; }

    /// <summary>Opens the OS settings pane for Accessibility. No-op where unsupported.</summary>
    void OpenAccessibilitySettings();

    /// <summary>Opens the OS settings pane for Input Monitoring. No-op where unsupported.</summary>
    void OpenInputMonitoringSettings();
}
