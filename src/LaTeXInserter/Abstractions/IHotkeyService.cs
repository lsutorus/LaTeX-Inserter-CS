using LaTeXInserter.Models;
using SharpHook.Data;

namespace LaTeXInserter.Abstractions;

public interface IHotkeyService : IDisposable
{
    HotkeyChord CurrentHotkey { get; }
    bool IsRecording { get; set; }
    event EventHandler<HotkeyChord>? HotkeyPressed;
    event EventHandler<HotkeyChord>? HotkeyRecorded;
    event EventHandler<HotkeyChord>? HotkeyChanged;

    /// <summary>Raised when the global hook fails to start or dies. Argument is a user-facing message.</summary>
    event EventHandler<string>? HookFailed;

    /// <summary>True once the hook is running and delivering events.</summary>
    bool IsRunning { get; }

    void RegisterHotkey(HotkeyChord chord);
    Task StartAsync(CancellationToken ct);
}
