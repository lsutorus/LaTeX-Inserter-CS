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

    // Settings take effect on Save only. On any unsaved close (Cancel / X),
    // live changes revert to this snapshot so the app state matches disk.
    private AppSettings _loadedSettings = AppSettings.Default;
    private bool _savePending;

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
        // No live Apply: accent takes effect on Save, matching every other setting.
        // Checkmark fill therefore does not recolor until Save.
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

        // Initial refresh from disk (also acts as first-open snapshot).
        Open();

        _hotkeyService.HotkeyChanged += OnHotkeyChanged;
    }

    private void OnHotkeyChanged(object? sender, HotkeyChord _)
        => OnPropertyChanged(nameof(CurrentHotkeyDisplay));

    // Called each time the Settings window opens. Refreshes the singleton VM from
    // disk and captures a revert snapshot, so Cancel / X never leaves stale or
    // unsaved state behind.
    public void Open()
    {
        var settings = _settingsService.Load();
        _loadedSettings = settings;
        _savePending = false;

        InputFontSize = settings.InputFontSize;
        PreviewFontSize = settings.PreviewFontSize;
        AccentColor = settings.AccentColor;
        AutocompleteEnabled = settings.AutocompleteEnabled;
        StartOnStartup = settings.StartOnStartup;

        foreach (var s in AccentPalette)
            s.IsSelected = s.Hex == settings.AccentColor;

        // Restore live accent resources to the persisted value in case a prior
        // unsaved close left a preview applied.
        _accentColorModule.Apply(settings.AccentColor);
    }

    // Called when the window closes for any reason (Save / Cancel / X). On an
    // unsaved close, revert the live accent preview back to the persisted value
    // (the overlay follows via AccentColorApplied).
    public void OnClosed()
    {
        if (!_savePending)
        {
            _accentColorModule.Apply(_loadedSettings.AccentColor);
            ResetFieldsToLoaded();
        }
        _savePending = false;
    }

    private void ResetFieldsToLoaded()
    {
        InputFontSize = _loadedSettings.InputFontSize;
        PreviewFontSize = _loadedSettings.PreviewFontSize;
        AccentColor = _loadedSettings.AccentColor;
        AutocompleteEnabled = _loadedSettings.AutocompleteEnabled;
        StartOnStartup = _loadedSettings.StartOnStartup;
        foreach (var s in AccentPalette)
            s.IsSelected = s.Hex == _loadedSettings.AccentColor;
    }

    [RelayCommand]
    private void ChangeHotkey() => ChangeHotkeyRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private async Task SaveAsync()
    {
        var updated = _loadedSettings with
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
            await _startupRegistrar.SyncRegistrationAsync(StartOnStartup);
        }
        catch
        {
            // Non-fatal: startup reg failure shouldn't block save
        }

        // Apply accent to Fluent theme resources + notify overlay (live effect on Save).
        _accentColorModule.Apply(AccentColor);

        _savePending = true;
        _loadedSettings = updated;
        SettingsSaved?.Invoke(this, updated);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, EventArgs.Empty);
}
