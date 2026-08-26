using LaTeXInserter.Abstractions;
using LaTeXInserter.Models;
using LaTeXInserter.Services;
using NSubstitute;
using SharpHook;
using SharpHook.Data;
using Xunit;

namespace LaTeXInserter.Tests;

public class InputSimulatorServiceTests
{
    [Fact]
    public async Task Windows_PressesControlV()
    {
        var sim = Substitute.For<IEventSimulator>();
        var permissions = Substitute.For<IPermissionService>();
        permissions.Query().Returns(PermissionStatus.AllGranted);

        var sut = new InputSimulatorService(sim, permissions, PlatformKind.Windows, modifierHoldMs: 0);
        await sut.SimulatePasteAsync("x");

        Received.InOrder(() =>
        {
            sim.SimulateKeyPress(KeyCode.VcLeftControl);
            sim.SimulateKeyPress(KeyCode.VcV);
            sim.SimulateKeyRelease(KeyCode.VcV);
            sim.SimulateKeyRelease(KeyCode.VcLeftControl);
        });
    }

    [Fact]
    public async Task MacOs_PressesCommandV()
    {
        var sim = Substitute.For<IEventSimulator>();
        var permissions = Substitute.For<IPermissionService>();
        permissions.Query().Returns(PermissionStatus.AllGranted);

        var sut = new InputSimulatorService(sim, permissions, PlatformKind.MacOS, modifierHoldMs: 0);
        await sut.SimulatePasteAsync("x");

        Received.InOrder(() =>
        {
            sim.SimulateKeyPress(KeyCode.VcLeftMeta);
            sim.SimulateKeyPress(KeyCode.VcV);
            sim.SimulateKeyRelease(KeyCode.VcV);
            sim.SimulateKeyRelease(KeyCode.VcLeftMeta);
        });
    }

    [Fact]
    public async Task SecureInputActive_SkipsSimulationAndReportsBlocked()
    {
        var sim = Substitute.For<IEventSimulator>();
        var permissions = Substitute.For<IPermissionService>();
        permissions.Query().Returns(new PermissionStatus(true, true, SecureInputActive: true));

        var sut = new InputSimulatorService(sim, permissions, PlatformKind.MacOS, modifierHoldMs: 0);

        string? blocked = null;
        sut.PasteBlocked += (_, reason) => blocked = reason;

        await sut.SimulatePasteAsync("x");

        sim.DidNotReceiveWithAnyArgs().SimulateKeyPress(default);
        Assert.NotNull(blocked);
        Assert.Contains("Secure Keyboard Entry", blocked);
    }
}
