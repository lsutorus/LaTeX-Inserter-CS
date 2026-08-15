using LaTeXInserter.Abstractions;
using LaTeXInserter.Models;
using LaTeXInserter.Platform.Windows;
using LaTeXInserter.Services;
using Microsoft.Extensions.DependencyInjection;

namespace LaTeXInserter.Platform;

public static class PlatformServiceRegistration
{
    public static void AddPlatformServices(this IServiceCollection services, PlatformKind platform)
    {
        switch (platform)
        {
            case PlatformKind.Windows:
                services.AddSingleton<IWindowActivator, WindowsWindowActivator>();
                services.AddSingleton<IOverlayPositioner, WindowsOverlayPositioner>();
                services.AddSingleton<IStartupRegistrar, WindowsStartupRegistrar>();
                services.AddSingleton<IPermissionService, NoOpPermissionService>();
                break;

            case PlatformKind.MacOS:
                // Replaced with real registrations in Task 9.
                throw new PlatformNotSupportedException(
                    "macOS platform services land in Task 9.");

            default:
                throw new PlatformNotSupportedException(
                    $"Unsupported platform: {platform}");
        }
    }
}
