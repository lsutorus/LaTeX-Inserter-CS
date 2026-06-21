using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using LaTeXInserter.Abstractions;
using LaTeXInserter.Views;

namespace LaTeXInserter.Services;

internal sealed class AvaloniaClipboardProvider : IClipboardProvider
{
    public async Task SetTextAsync(string text)
    {
        var desktop = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var activeWindow = desktop?.Windows.FirstOrDefault(w => w is OverlayWindow && w.IsVisible)
                        ?? desktop?.Windows.FirstOrDefault(w => w.IsVisible)
                        ?? desktop?.MainWindow;

        var clipboard = TopLevel.GetTopLevel(activeWindow)?.Clipboard
            ?? throw new InvalidOperationException("No clipboard available.");
        await clipboard.SetTextAsync(text);
    }
}
