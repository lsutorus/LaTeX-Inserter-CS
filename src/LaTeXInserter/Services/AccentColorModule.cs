using Avalonia;
using Avalonia.Media;
using LaTeXInserter.Abstractions;

namespace LaTeXInserter.Services;

internal sealed class AccentColorModule : IAccentColorModule
{
    public event EventHandler<string>? AccentColorApplied;

    public void Apply(string hex)
    {
        var color = Color.Parse(hex);

        // Set Application resources so Fluent theme cascades the accent color
        Application.Current!.Resources["SystemAccentColor"] = color;
        Application.Current.Resources["AccentBgBrush"] = new SolidColorBrush(color, 0.25);

        AccentColorApplied?.Invoke(this, hex);
    }
}
