using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaTeXInserter.Abstractions;
using LaTeXInserter.Models;

namespace LaTeXInserter.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;

    [ObservableProperty]
    private int _inputFontSize;

    [ObservableProperty]
    private int _previewFontSize;

    [ObservableProperty]
    private string _accentColor = "#404040";

    [ObservableProperty]
    private bool _autocompleteEnabled = true;

    public static IReadOnlyList<string> AccentPalette { get; } =
    [
        "#404040", "#D1D5DB", "#3B82F6", "#8B5CF6", "#EC4899",
        "#EF4444", "#F97316", "#F59E0B", "#10B981", "#06B6D4"
    ];

    public event EventHandler<AppSettings>? SettingsSaved;
    public event EventHandler? CloseRequested;

    public SettingsViewModel(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        var settings = _settingsService.Load();
        InputFontSize = settings.InputFontSize;
        PreviewFontSize = settings.PreviewFontSize;
        AccentColor = settings.AccentColor;
        AutocompleteEnabled = settings.AutocompleteEnabled;
    }

    [RelayCommand]
    private void Save()
    {
        var settings = _settingsService.Load();
        var updated = settings with
        {
            InputFontSize = InputFontSize,
            PreviewFontSize = PreviewFontSize,
            AccentColor = AccentColor,
            AutocompleteEnabled = AutocompleteEnabled
        };
        _settingsService.Save(updated);
        SettingsSaved?.Invoke(this, updated);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, EventArgs.Empty);
}
