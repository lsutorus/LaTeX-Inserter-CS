using LaTeXInserter.Abstractions;
using LaTeXInserter.Models;
using LaTeXInserter.Platform.MacOS;
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
#pragma warning disable CA1416 // Guarded by the PlatformKind.MacOS switch arm.
                services.AddSingleton<IWindowActivator, MacWindowActivator>();
                services.AddSingleton<IOverlayPositioner, MacOverlayPositioner>();
                services.AddSingleton<IStartupRegistrar, MacStartupRegistrar>();
                services.AddSingleton<IPermissionService, MacPermissionService>();
#pragma warning restore CA1416
                break;

            default:
                throw new PlatformNotSupportedException(
                    $"Unsupported platform: {platform}");
        }
    }
}
