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

    private readonly NativeMenuItem _showHideOverlayItem;
    private readonly NativeMenuItem _settingsItem;
    private readonly NativeMenuItem _editMappingsItem;
    private readonly NativeMenuItem _reloadMappingsItem;
    private readonly NativeMenuItem _checkForUpdatesItem;
    private readonly NativeMenuItem _quitItem;

    public NativeMenu TrayMenu { get; }

    public event EventHandler? ShowOverlayRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? CheckForUpdatesRequested;
    public event EventHandler? QuitRequested;

    public TrayIconViewModel(
        IHotkeyService hotkeyService,
        ISettingsService settingsService,
        ILatexConverterService latexConverter)
    {
        _hotkeyService = hotkeyService;
        _settingsService = settingsService;
        _latexConverter = latexConverter;

        _showHideOverlayItem = new NativeMenuItem($"Show/Hide Overlay ({hotkeyService.CurrentHotkey})");
        _settingsItem = new NativeMenuItem("Settings...");
        _editMappingsItem = new NativeMenuItem("Edit Custom Mappings");
        _reloadMappingsItem = new NativeMenuItem("Reload Custom Mappings");
        _checkForUpdatesItem = new NativeMenuItem("Check for Updates...");
        _quitItem = new NativeMenuItem("Quit");

        _showHideOverlayItem.Command = ShowHideOverlayCommand;
        _settingsItem.Command = SettingsCommand;
        _editMappingsItem.Command = EditMappingsCommand;
        _reloadMappingsItem.Command = ReloadMappingsCommand;
        _checkForUpdatesItem.Command = CheckForUpdatesCommand;
        _quitItem.Command = QuitCommand;

        TrayMenu = new NativeMenu
        {
            _showHideOverlayItem,
            new NativeMenuItemSeparator(),
            _settingsItem,
            _editMappingsItem,
            _reloadMappingsItem,
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

    [RelayCommand]
    private void ShowHideOverlay() => ShowOverlayRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Settings() => SettingsRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void EditMappings()
    {
        var path = _settingsService.GetCustomMappingsFilePath();
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    [RelayCommand]
    private void ReloadMappings() => _latexConverter.Reload();

    [RelayCommand]
    private void CheckForUpdates() => CheckForUpdatesRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Quit() => QuitRequested?.Invoke(this, EventArgs.Empty);
}
