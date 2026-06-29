using Avalonia.Media;
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
    private readonly IAccentColorModule _accentColorModule;

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

    public static List<AccentSwatchInfo> AccentPalette { get; } =
    [
        new("#404040", new SolidColorBrush(Color.Parse("#404040"))),
        new("#D1D5DB", new SolidColorBrush(Color.Parse("#D1D5DB"))),
        new("#3B82F6", new SolidColorBrush(Color.Parse("#3B82F6"))),
        new("#8B5CF6", new SolidColorBrush(Color.Parse("#8B5CF6"))),
        new("#EC4899", new SolidColorBrush(Color.Parse("#EC4899"))),
        new("#EF4444", new SolidColorBrush(Color.Parse("#EF4444"))),
        new("#F97316", new SolidColorBrush(Color.Parse("#F97316"))),
        new("#F59E0B", new SolidColorBrush(Color.Parse("#F59E0B"))),
        new("#10B981", new SolidColorBrush(Color.Parse("#10B981"))),
        new("#06B6D4", new SolidColorBrush(Color.Parse("#06B6D4")))
    ];

    public void SelectSwatch(AccentSwatchInfo swatch)
    {
        foreach (var s in AccentPalette)
            s.IsSelected = s == swatch;
        AccentColor = swatch.Hex;
        _accentColorModule.Apply(swatch.Hex);
    }

    public string CurrentHotkeyDisplay => _hotkeyService.CurrentHotkey.ToString();

    public event EventHandler<AppSettings>? SettingsSaved;
    public event EventHandler? CloseRequested;
    public event EventHandler? ChangeHotkeyRequested;

    public SettingsViewModel(
        ISettingsService settingsService,
        IHotkeyService hotkeyService,
        IStartupRegistrar startupRegistrar,
        IAccentColorModule accentColorModule)
    {
        _settingsService = settingsService;
        _hotkeyService = hotkeyService;
        _startupRegistrar = startupRegistrar;
        _accentColorModule = accentColorModule;
        var settings = _settingsService.Load();
        InputFontSize = settings.InputFontSize;
        PreviewFontSize = settings.PreviewFontSize;
        AccentColor = settings.AccentColor;
        AutocompleteEnabled = settings.AutocompleteEnabled;
        StartOnStartup = settings.StartOnStartup;

        // Mark initial swatch selection
        foreach (var s in AccentPalette)
            s.IsSelected = s.Hex == settings.AccentColor;

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
