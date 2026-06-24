using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaTeXInserter.Abstractions;
using LaTeXInserter.Models;

namespace LaTeXInserter.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IHotkeyService _hotkeyService;
    private readonly IStartupRegistrar _startupRegistrar;

    [ObservableProperty]
    private int _inputFontSize;

    [ObservableProperty]
    private int _previewFontSize;

    [ObservableProperty]
    private string _accentColor = "#404040";

    [ObservableProperty]
    private bool _autocompleteEnabled = true;

    [ObservableProperty]
    private bool _startOnStartup;

    partial void OnAccentColorChanged(string value)
        => AccentColorChanged?.Invoke(this, value);

    public static IReadOnlyList<string> AccentPalette { get; } =
    [
        "#404040", "#D1D5DB", "#3B82F6", "#8B5CF6", "#EC4899",
        "#EF4444", "#F97316", "#F59E0B", "#10B981", "#06B6D4"
    ];

    public string CurrentHotkeyDisplay => _hotkeyService.CurrentHotkey.ToString();

    public event EventHandler<AppSettings>? SettingsSaved;
    public event EventHandler? CloseRequested;
    public event EventHandler? ChangeHotkeyRequested;
    public event EventHandler<string>? AccentColorChanged;

    public SettingsViewModel(
        ISettingsService settingsService,
        IHotkeyService hotkeyService,
        IStartupRegistrar startupRegistrar)
    {
        _settingsService = settingsService;
        _hotkeyService = hotkeyService;
        _startupRegistrar = startupRegistrar;
        var settings = _settingsService.Load();
        InputFontSize = settings.InputFontSize;
        PreviewFontSize = settings.PreviewFontSize;
        AccentColor = settings.AccentColor;
        AutocompleteEnabled = settings.AutocompleteEnabled;
        StartOnStartup = settings.StartOnStartup;

        _hotkeyService.HotkeyChanged += OnHotkeyChanged;
    }

    private void OnHotkeyChanged(object? sender, HotkeyChord _)
        => OnPropertyChanged(nameof(CurrentHotkeyDisplay));

    [RelayCommand]
    private void ChangeHotkey() => ChangeHotkeyRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private async Task SaveAsync()
    {
        var settings = _settingsService.Load();
        var updated = settings with
        {
            InputFontSize = InputFontSize,
            PreviewFontSize = PreviewFontSize,
            AccentColor = AccentColor,
            AutocompleteEnabled = AutocompleteEnabled,
            StartOnStartup = StartOnStartup
        };
        _settingsService.Save(updated);

        // Sync startup registration with OS
        try
        {
            var isRegistered = await _startupRegistrar.GetIsRegisteredAsync();
            if (StartOnStartup && !isRegistered)
                await _startupRegistrar.RegisterAsync();
            else if (!StartOnStartup && isRegistered)
                await _startupRegistrar.UnregisterAsync();
        }
        catch
        {
            // Non-fatal: startup reg failure shouldn't block save
        }

        SettingsSaved?.Invoke(this, updated);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, EventArgs.Empty);
}
