using Avalonia;
using Avalonia.Media;
using LaTeXInserter.Abstractions;

namespace LaTeXInserter.Services;

internal sealed class AccentColorModule : IAccentColorModule
{
    private readonly ISettingsService _settingsService;

    public event EventHandler<string>? AccentColorApplied;

    public AccentColorModule(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public void Apply(string hex)
    {
        var color = Color.Parse(hex);

        // Set Application resources so Fluent theme cascades the accent color
        Application.Current!.Resources["SystemAccentColor"] = color;
        Application.Current.Resources["AccentBgBrush"] = new SolidColorBrush(color, 0.25);

        // Persist to settings
        var settings = _settingsService.Load();
        if (settings.AccentColor != hex)
        {
            var updated = settings with { AccentColor = hex };
            _settingsService.Save(updated);
        }

        AccentColorApplied?.Invoke(this, hex);
    }
}
