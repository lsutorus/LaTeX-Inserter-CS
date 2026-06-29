using System.Diagnostics;
using LaTeXInserter.Abstractions;

namespace LaTeXInserter.Services;

internal sealed class SubmitPasteService : ISubmitPasteService
{
    private readonly IClipboardProvider _clipboardProvider;
    private readonly IWindowActivator _windowActivator;
    private readonly IInputSimulatorService _inputSimulator;
    private readonly int _pasteDelayMs;

    public event EventHandler? OverlayHideRequested;

    public SubmitPasteService(
        IClipboardProvider clipboardProvider,
        IWindowActivator windowActivator,
        IInputSimulatorService inputSimulator,
        int pasteDelayMs = 50)
    {
        _clipboardProvider = clipboardProvider;
        _windowActivator = windowActivator;
        _inputSimulator = inputSimulator;
        _pasteDelayMs = pasteDelayMs;
    }

    public async Task ExecuteAsync(string convertedText)
    {
        try
        {
            // 1. Set clipboard while the overlay window is visible and active
            await _clipboardProvider.SetTextAsync(convertedText);

            // 2. Restore focus to the target editor while we still have Win32 foreground permission
            _windowActivator.Restore();

            // 3. Hide the overlay
            OverlayHideRequested?.Invoke(this, EventArgs.Empty);

            // 4. Delay briefly to let the OS focus transition complete
            await Task.Delay(_pasteDelayMs);

            // 5. Simulate the paste keys
            await _inputSimulator.SimulatePasteAsync(convertedText);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SubmitAndPaste failed: {ex}");
        }
    }
}
