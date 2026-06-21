using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using LaTeXInserter.Abstractions;
using LaTeXInserter.Models;

namespace LaTeXInserter.ViewModels;

public sealed partial class TrayIconViewModel
{
    private readonly IHotkeyService _hotkeyService;
    private readonly ISettingsService _settingsService;
    private readonly ILatexConverterService _latexConverter;
    private readonly IStartupRegistrar _startupRegistrar;

    private readonly NativeMenuItem _showHideOverlayItem;
    private readonly NativeMenuItem _editMappingsItem;
    private readonly NativeMenuItem _reloadMappingsItem;
    private readonly NativeMenuItem _changeHotkeyItem;
    private readonly NativeMenuItem _startupToggleItem;
    private readonly NativeMenuItem _checkForUpdatesItem;
    private readonly NativeMenuItem _quitItem;

    private bool _isSyncing;

    public NativeMenu TrayMenu { get; }

    public event EventHandler? ShowOverlayRequested;
    public event EventHandler? CheckForUpdatesRequested;
    public event EventHandler? QuitRequested;

    public TrayIconViewModel(
        IHotkeyService hotkeyService,
        ISettingsService settingsService,
        ILatexConverterService latexConverter,
        IStartupRegistrar startupRegistrar)
    {
        _hotkeyService = hotkeyService;
        _settingsService = settingsService;
        _latexConverter = latexConverter;
        _startupRegistrar = startupRegistrar;

        _showHideOverlayItem = new NativeMenuItem($"Show/Hide Overlay ({hotkeyService.CurrentHotkey})");
        _editMappingsItem = new NativeMenuItem("Edit Custom Mappings");
        _reloadMappingsItem = new NativeMenuItem("Reload Custom Mappings");
        _changeHotkeyItem = new NativeMenuItem("Change Hotkey...");
        _startupToggleItem = new NativeMenuItem("Run on Startup")
        {
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = false
        };
        _checkForUpdatesItem = new NativeMenuItem("Check for Updates...");
        _quitItem = new NativeMenuItem("Quit");

        _showHideOverlayItem.Command = ShowHideOverlayCommand;
        _editMappingsItem.Command = EditMappingsCommand;
        _reloadMappingsItem.Command = ReloadMappingsCommand;
        _changeHotkeyItem.Command = ChangeHotkeyCommand;
        _startupToggleItem.Command = ToggleStartupCommand;
        _checkForUpdatesItem.Command = CheckForUpdatesCommand;
        _quitItem.Command = QuitCommand;

        TrayMenu = new NativeMenu
        {
            _showHideOverlayItem,
            new NativeMenuItemSeparator(),
            _editMappingsItem,
            _reloadMappingsItem,
            new NativeMenuItemSeparator(),
            _changeHotkeyItem,
            new NativeMenuItemSeparator(),
            _startupToggleItem,
            new NativeMenuItemSeparator(),
            _checkForUpdatesItem,
            new NativeMenuItemSeparator(),
            _quitItem
        };

        _hotkeyService.HotkeyChanged += OnHotkeyChanged;
    }

    private void OnHotkeyChanged(object? sender, HotkeyChord chord)
    {
        UpdateShowHideLabel(chord);
    }

    public void UpdateShowHideLabel(HotkeyChord chord)
    {
        Dispatcher.UIThread.Post(() =>
            _showHideOverlayItem.Header = $"Show/Hide Overlay ({chord})");
    }

    public async Task SyncStartupToggleAsync()
    {
        _isSyncing = true;
        try
        {
            var isRegistered = await _startupRegistrar.GetIsRegisteredAsync();
            Dispatcher.UIThread.Post(() =>
            {
                _startupToggleItem.IsChecked = isRegistered;
            });
            var settings = _settingsService.Load();
            if (settings.StartOnStartup != isRegistered)
            {
                _settingsService.Save(settings with { StartOnStartup = isRegistered });
            }
        }
        finally
        {
            _isSyncing = false;
        }
    }

    [RelayCommand]
    private void ShowHideOverlay() => ShowOverlayRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void EditMappings()
    {
        var path = _settingsService.GetCustomMappingsFilePath();
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    [RelayCommand]
    private void ReloadMappings() => _latexConverter.Reload();

    [RelayCommand]
    private void ChangeHotkey()
    {
        // Phase 5 placeholder — intentionally empty to avoid unhandled exception
    }

    [RelayCommand]
    private async Task ToggleStartupAsync()
    {
        if (_isSyncing) return;

        var isChecked = _startupToggleItem.IsChecked;
        try
        {
            if (isChecked)
                await _startupRegistrar.RegisterAsync();
            else
                await _startupRegistrar.UnregisterAsync();

            var settings = _settingsService.Load();
            _settingsService.Save(settings with { StartOnStartup = isChecked });
        }
        catch
        {
            // Revert toggle on failure
            Dispatcher.UIThread.Post(() => _startupToggleItem.IsChecked = !isChecked);
        }
    }

    [RelayCommand]
    private void CheckForUpdates() => CheckForUpdatesRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Quit() => QuitRequested?.Invoke(this, EventArgs.Empty);
}
