using LaTeXInserter.Abstractions;
using LaTeXInserter.Models;
using LaTeXInserter.Platform;
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
}
