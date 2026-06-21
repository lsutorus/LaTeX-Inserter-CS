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
    void RegisterHotkey(HotkeyChord chord);
    Task StartAsync(CancellationToken ct);
}
