using System.Linq;
using LaTeXInserter.Abstractions;
using LaTeXInserter.Models;
using LaTeXInserter.Platform;
using LaTeXInserter.Platform.MacOS;
using LaTeXInserter.Platform.Windows;
using LaTeXInserter.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LaTeXInserter.Tests;

public class PlatformServiceRegistrationTests
{
    [Fact]
    public void Windows_ResolvesWindowsImplementations()
    {
        var services = new ServiceCollection();
        services.AddPlatformServices(PlatformKind.Windows);
        using var sp = services.BuildServiceProvider();

        Assert.IsType<WindowsWindowActivator>(sp.GetRequiredService<IWindowActivator>());
        Assert.IsType<WindowsOverlayPositioner>(sp.GetRequiredService<IOverlayPositioner>());
        Assert.IsType<WindowsStartupRegistrar>(sp.GetRequiredService<IStartupRegistrar>());
        Assert.IsType<NoOpPermissionService>(sp.GetRequiredService<IPermissionService>());
    }

    [Fact]
    public void Windows_PermissionServiceReportsEverythingGranted()
    {
        var sut = new NoOpPermissionService();

        Assert.True(sut.Query().IsUsable);
        Assert.False(sut.RequiresUserAction);
    }

    [Fact]
    public void MacOs_ResolvesMacImplementations()
    {
        var services = new ServiceCollection();
        services.AddPlatformServices(PlatformKind.MacOS);

        // Resolution is not attempted off-macOS — the static ctors dlopen frameworks.
        // Asserting on the descriptors keeps this test runnable on Windows CI.
        Assert.Equal(typeof(MacWindowActivator),
            services.Single(d => d.ServiceType == typeof(IWindowActivator)).ImplementationType);
        Assert.Equal(typeof(MacOverlayPositioner),
            services.Single(d => d.ServiceType == typeof(IOverlayPositioner)).ImplementationType);
        Assert.Equal(typeof(MacStartupRegistrar),
            services.Single(d => d.ServiceType == typeof(IStartupRegistrar)).ImplementationType);
        Assert.Equal(typeof(MacPermissionService),
            services.Single(d => d.ServiceType == typeof(IPermissionService)).ImplementationType);
    }
}
