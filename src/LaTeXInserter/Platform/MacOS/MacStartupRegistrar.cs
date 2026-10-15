using System.Diagnostics;
using System.Runtime.Versioning;
using LaTeXInserter.Abstractions;

namespace LaTeXInserter.Platform.MacOS;

/// <summary>
/// Login-item registration via ServiceManagement's SMAppService (macOS 13+),
/// the supported replacement for SMLoginItemSetEnabled and LaunchAgent plists.
///
/// Registration targets the running .app bundle, so it works from any location —
/// which matters because a Velopack .app is portable and need not live in
/// /Applications. Registering requires a real bundle: running loose from
/// `dotnet run` will fail here, and that failure is expected in development.
///
/// status() distinguishes "never registered" from "user switched it off in
/// System Settings" (RequiresApproval), so the Settings checkbox can reflect the
/// user's own choice instead of silently re-enabling it.
/// </summary>
[SupportedOSPlatform("macos13.0")]
internal sealed class MacStartupRegistrar : IStartupRegistrar
{
    private const long StatusNotRegistered = 0;
    private const long StatusEnabled = 1;
    private const long StatusRequiresApproval = 2;
    private const long StatusNotFound = 3;

    static MacStartupRegistrar()
    {
        ObjC.LoadFramework(ObjC.ServiceManagementPath);
    }

    public Task<bool> GetIsRegisteredAsync()
    {
        try
        {
            return Task.FromResult(GetStatus() == StatusEnabled);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GetIsRegisteredAsync failed: {ex}");
            return Task.FromResult(false);
        }
    }

    public Task RegisterAsync()
    {
        var service = MainAppService();
        // -[SMAppService registerAndReturnError:] — pass NULL for the error out-param.
        bool ok = ObjC.SendBool(service, ObjC.Sel("registerAndReturnError:"), IntPtr.Zero);
        if (!ok)
            Debug.WriteLine("SMAppService registerAndReturnError: returned NO.");
        return Task.CompletedTask;
    }

    public Task UnregisterAsync()
    {
        var service = MainAppService();
        bool ok = ObjC.SendBool(service, ObjC.Sel("unregisterAndReturnError:"), IntPtr.Zero);
        if (!ok)
            Debug.WriteLine("SMAppService unregisterAndReturnError: returned NO.");
        return Task.CompletedTask;
    }

    public async Task SyncRegistrationAsync(bool desired)
    {
        long status = GetStatus();

        // The user disabling the login item in System Settings must stick. Only
        // re-register when the app has genuinely never been registered.
        if (desired && status == StatusNotRegistered)
        {
            await RegisterAsync();
        }
        else if (!desired && status is StatusEnabled or StatusRequiresApproval)
        {
            await UnregisterAsync();
        }
    }

    /// <summary>Raw SMAppServiceStatus, for the Settings UI to explain RequiresApproval.</summary>
    public long GetStatus()
    {
        var service = MainAppService();
        return service == IntPtr.Zero ? StatusNotFound : ObjC.SendLong(service, ObjC.Sel("status"));
    }

    private static IntPtr MainAppService()
    {
        var cls = ObjC.GetClass("SMAppService");
        // +[SMAppService mainAppService] backs the `SMAppService.mainApp` property.
        return cls == IntPtr.Zero ? IntPtr.Zero : ObjC.Send(cls, ObjC.Sel("mainAppService"));
    }
}
