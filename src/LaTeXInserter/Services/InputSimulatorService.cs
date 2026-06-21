using System.Runtime.InteropServices;
using LaTeXInserter.Abstractions;
using SharpHook;
using SharpHook.Data;

namespace LaTeXInserter.Services;

internal sealed class InputSimulatorService : IInputSimulatorService
{
    private readonly IEventSimulator _simulator;

    public InputSimulatorService(IEventSimulator simulator)
    {
        _simulator = simulator;
    }

    public async Task SimulatePasteAsync(string unicodeText)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            _simulator.SimulateKeyPress(KeyCode.VcLeftMeta);
            _simulator.SimulateKeyPress(KeyCode.VcV);
            _simulator.SimulateKeyRelease(KeyCode.VcV);
            await Task.Delay(10);
            _simulator.SimulateKeyRelease(KeyCode.VcLeftMeta);
        }
        else
        {
            _simulator.SimulateKeyPress(KeyCode.VcLeftControl);
            _simulator.SimulateKeyPress(KeyCode.VcV);
            _simulator.SimulateKeyRelease(KeyCode.VcV);
            await Task.Delay(10);
            _simulator.SimulateKeyRelease(KeyCode.VcLeftControl);
        }
    }
}
