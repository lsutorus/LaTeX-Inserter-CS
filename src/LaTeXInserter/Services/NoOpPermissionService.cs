using LaTeXInserter.Abstractions;
using LaTeXInserter.Models;

namespace LaTeXInserter.Services;

/// <summary>Windows: global hooks need no user-granted permission.</summary>
public sealed class NoOpPermissionService : IPermissionService
{
    public PermissionStatus Query() => PermissionStatus.AllGranted;
    public bool RequiresUserAction => false;
    public void OpenAccessibilitySettings() { }
    public void OpenInputMonitoringSettings() { }
}
