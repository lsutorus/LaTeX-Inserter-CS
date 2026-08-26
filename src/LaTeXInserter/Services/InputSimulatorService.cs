using LaTeXInserter.Abstractions;
using LaTeXInserter.Models;
using SharpHook;
using SharpHook.Data;

namespace LaTeXInserter.Services;

internal sealed class InputSimulatorService : IInputSimulatorService
{
    // macOS drops synthetic modifier chords that are held too briefly; 10ms is
    // reliable on Windows but marginal on macOS.
    private const int WindowsHoldMs = 10;
    private const int MacHoldMs = 40;

    private readonly IEventSimulator _simulator;
    private readonly IPermissionService _permissions;
    private readonly PlatformKind _platform;
    private readonly int _modifierHoldMs;

    /// <summary>Raised when the paste could not be simulated. Argument is user-facing.</summary>
    public event EventHandler<string>? PasteBlocked;

    public InputSimulatorService(IEventSimulator simulator, IPermissionService permissions)
        : this(simulator, permissions, PlatformKinds.Current,
               PlatformKinds.Current == PlatformKind.MacOS ? MacHoldMs : WindowsHoldMs)
    {
    }

    public InputSimulatorService(
        IEventSimulator simulator,
        IPermissionService permissions,
        PlatformKind platform,
        int modifierHoldMs)
    {
        _simulator = simulator;
        _permissions = permissions;
        _platform = platform;
        _modifierHoldMs = modifierHoldMs;
    }

    public async Task SimulatePasteAsync(string unicodeText)
    {
        // EnableSecureEventInput makes CGEventPost a no-op. Without this check the
        // app looks broken: the clipboard is set but nothing is ever pasted.
        var status = _permissions.Query();
        if (status.SecureInputActive)
        {
            PasteBlocked?.Invoke(this,
                "Paste was blocked because Secure Keyboard Entry is active. The text is "
              + "on the clipboard — press Cmd+V, or turn off Terminal ▸ Secure Keyboard Entry.");
            return;
        }

        var modifier = _platform == PlatformKind.MacOS
            ? KeyCode.VcLeftMeta
            : KeyCode.VcLeftControl;

        _simulator.SimulateKeyPress(modifier);
        _simulator.SimulateKeyPress(KeyCode.VcV);
        _simulator.SimulateKeyRelease(KeyCode.VcV);
        if (_modifierHoldMs > 0)
            await Task.Delay(_modifierHoldMs);
        _simulator.SimulateKeyRelease(modifier);
    }
}
