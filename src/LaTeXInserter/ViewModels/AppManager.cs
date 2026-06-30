using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using LaTeXInserter.Abstractions;
using LaTeXInserter.Models;
using LaTeXInserter.Views;
using LaTeXInserter.ViewModels;

namespace LaTeXInserter.ViewModels;

public sealed class AppManager : IDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly IHotkeyService _hotkeyService;
    private readonly IStartupRegistrar _startupRegistrar;
    private readonly IWindowActivator _windowActivator;
    private readonly IOverlayPositioner _overlayPositioner;
    private readonly ISubmitPasteService _submitPasteService;
    private readonly IUpdateCoordinator _updateCoordinator;
    private readonly TrayIconViewModel _trayIconViewModel;
    private readonly OverlayViewModel _overlayViewModel;
    private readonly HotkeyDialogViewModel _hotkeyDialogViewModel;
    private readonly SettingsViewModel _settingsViewModel;
    private readonly CustomMappingsViewModel _customMappingsViewModel;

    private OverlayWindow? _overlayWindow;
    private HotkeyDialogWindow? _activeHotkeyDialog;
    private SettingsWindow? _activeSettingsWindow;
    private CustomMappingsWindow? _activeCustomMappingsWindow;
    private bool _isToggling;
    private bool _isShutdown;
    private bool _isDisposed;

    public bool IsOverlayVisible { get; private set; }
    public event EventHandler? OverlayVisibilityChanged;

    public AppManager(
        ISettingsService settingsService,
        IHotkeyService hotkeyService,
        IStartupRegistrar startupRegistrar,
        IWindowActivator windowActivator,
        IOverlayPositioner overlayPositioner,
        ISubmitPasteService submitPasteService,
        IUpdateCoordinator updateCoordinator,
        TrayIconViewModel trayIconViewModel,
        OverlayViewModel overlayViewModel,
        HotkeyDialogViewModel hotkeyDialogViewModel,
        SettingsViewModel settingsViewModel,
        CustomMappingsViewModel customMappingsViewModel)
    {
        _settingsService = settingsService;
        _hotkeyService = hotkeyService;
        _startupRegistrar = startupRegistrar;
        _windowActivator = windowActivator;
        _overlayPositioner = overlayPositioner;
        _submitPasteService = submitPasteService;
        _updateCoordinator = updateCoordinator;
        _trayIconViewModel = trayIconViewModel;
        _overlayViewModel = overlayViewModel;
        _hotkeyDialogViewModel = hotkeyDialogViewModel;
        _settingsViewModel = settingsViewModel;
        _customMappingsViewModel = customMappingsViewModel;
    }

    public async Task InitializeAsync()
    {
        try
        {
            // 1. Load settings
            var settings = _settingsService.Load();

            // 2. Validate hotkey against blocklist
            if (HotkeyBlocklist.IsBlocked(settings.Hotkey))
            {
                settings = settings with { Hotkey = AppSettings.Default.Hotkey };
                _settingsService.Save(settings);
            }

            // 3. Register hotkey
            _hotkeyService.RegisterHotkey(settings.Hotkey);

            // 4. Start hook (fire-and-forget)
#pragma warning disable CS4014
            _hotkeyService.StartAsync(CancellationToken.None);
#pragma warning restore CS4014

            // 5. Sync OS startup registration with settings
            try
            {
                await _startupRegistrar.SyncRegistrationAsync(settings.StartOnStartup);
            }
            catch
            {
                // Non-fatal: startup sync failure shouldn't block init
            }

            // 6. Wire events
            _hotkeyService.HotkeyPressed += OnHotkeyPressed;
            _submitPasteService.OverlayHideRequested += OnHideRequested;
            _trayIconViewModel.ShowOverlayRequested += OnShowOverlayRequested;
            _trayIconViewModel.SettingsRequested += OnSettingsRequested;
            _trayIconViewModel.EditMappingsRequested += OnEditMappingsRequested;
            _settingsViewModel.ChangeHotkeyRequested += OnChangeHotkeyRequested;
            _trayIconViewModel.CheckForUpdatesRequested += OnCheckForUpdatesRequested;
            _trayIconViewModel.QuitRequested += OnQuitRequested;
            _overlayViewModel.SubmitRequested += OnSubmitRequested;
            _overlayViewModel.HideRequested += OnHideRequested;
            _settingsViewModel.SettingsSaved += OnSettingsSaved;
            _settingsViewModel.CloseRequested += OnSettingsCloseRequested;
            _customMappingsViewModel.CloseRequested += OnCustomMappingsCloseRequested;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"AppManager.InitializeAsync failed: {ex}");
        }
    }

    private void OnHotkeyPressed(object? sender, HotkeyChord _) => ToggleOverlay();
    private void OnShowOverlayRequested(object? sender, EventArgs _) => ToggleOverlay();
    private async void OnCheckForUpdatesRequested(object? sender, EventArgs _)
    {
        await _updateCoordinator.CheckForUpdatesAsync();
    }

    private void OnQuitRequested(object? sender, EventArgs _) => Shutdown();
    private void OnHideRequested(object? sender, EventArgs _) => HideOverlay();

    private void OnChangeHotkeyRequested(object? sender, EventArgs _)
    {
        if (_activeHotkeyDialog is not null)
        {
            _activeHotkeyDialog.Activate();
            return;
        }

        if (IsOverlayVisible) HideOverlay();

        Dispatcher.UIThread.Post(() =>
        {
            _hotkeyDialogViewModel.StartRecording();

            _activeHotkeyDialog = new HotkeyDialogWindow
            {
                DataContext = _hotkeyDialogViewModel
            };

            _activeHotkeyDialog.Closed += (_, _) =>
            {
                _hotkeyDialogViewModel.Cleanup();
                _hotkeyDialogViewModel.CloseRequested -= OnDialogCloseRequested;
                _activeHotkeyDialog = null;
            };

            _hotkeyDialogViewModel.CloseRequested += OnDialogCloseRequested;
            _activeHotkeyDialog.Show();
        });
    }

    private void OnDialogCloseRequested(object? sender, EventArgs _)
    {
        _activeHotkeyDialog?.Close();
    }

    private void OnSettingsRequested(object? sender, EventArgs _)
    {
        if (_activeSettingsWindow is not null)
        {
            _activeSettingsWindow.Activate();
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            _activeSettingsWindow = new SettingsWindow
            {
                DataContext = _settingsViewModel
            };

            _activeSettingsWindow.Closed += (_, _) => _activeSettingsWindow = null;
            _activeSettingsWindow.Show();
        });
    }

    private void OnSettingsSaved(object? sender, AppSettings settings)
    {
        _overlayViewModel.ApplySettings(settings);
    }

    private void OnSettingsCloseRequested(object? sender, EventArgs _)
    {
        _activeSettingsWindow?.Close();
    }

    private void OnEditMappingsRequested(object? sender, EventArgs _)
    {
        if (_activeCustomMappingsWindow is not null)
        {
            _activeCustomMappingsWindow.Activate();
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            // Re-initialize VM with fresh data each time window opens
            _customMappingsViewModel.Reload();

            _activeCustomMappingsWindow = new CustomMappingsWindow
            {
                DataContext = _customMappingsViewModel
            };

            _activeCustomMappingsWindow.Closed += (_, _) => _activeCustomMappingsWindow = null;
            _activeCustomMappingsWindow.Show();
        });
    }

    private void OnCustomMappingsCloseRequested(object? sender, EventArgs _)
    {
        _activeCustomMappingsWindow?.Close();
    }

    private async void OnSubmitRequested(object? sender, string convertedText)
    {
        await _submitPasteService.ExecuteAsync(convertedText);
    }

    public void ToggleOverlay()
    {
        if (_isToggling) return;
        _isToggling = true;
        try
        {
            if (IsOverlayVisible) HideOverlay();
            else ShowOverlay();
        }
        finally
        {
            _isToggling = false;
        }
    }

    public void ShowOverlay()
    {
        _windowActivator.CapturePrevious();
        _overlayViewModel.ResetState();

        if (_overlayWindow is null)
        {
            _overlayWindow = new OverlayWindow
            {
                DataContext = _overlayViewModel,
                OverlayPositioner = _overlayPositioner
            };
        }

        _overlayWindow.Show();
        IsOverlayVisible = true;
        OverlayVisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    public void HideOverlay()
    {
        _overlayWindow?.Hide();
        IsOverlayVisible = false;
        OverlayVisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Shutdown()
    {
        if (_isShutdown) return;
        _isShutdown = true;

        _hotkeyService.Dispose();
        _settingsService.Save(_settingsService.Load());

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
        {
            lifetime.Shutdown();
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (!_isShutdown)
        {
            _hotkeyService.HotkeyPressed -= OnHotkeyPressed;
            _submitPasteService.OverlayHideRequested -= OnHideRequested;
            _trayIconViewModel.ShowOverlayRequested -= OnShowOverlayRequested;
            _trayIconViewModel.SettingsRequested -= OnSettingsRequested;
            _trayIconViewModel.EditMappingsRequested -= OnEditMappingsRequested;
            _settingsViewModel.ChangeHotkeyRequested -= OnChangeHotkeyRequested;
            _trayIconViewModel.CheckForUpdatesRequested -= OnCheckForUpdatesRequested;
            _trayIconViewModel.QuitRequested -= OnQuitRequested;
            _overlayViewModel.SubmitRequested -= OnSubmitRequested;
            _overlayViewModel.HideRequested -= OnHideRequested;
            _settingsViewModel.SettingsSaved -= OnSettingsSaved;
            _settingsViewModel.CloseRequested -= OnSettingsCloseRequested;
            _customMappingsViewModel.CloseRequested -= OnCustomMappingsCloseRequested;
            _hotkeyService.Dispose();
        }

        _overlayWindow?.Close();
        _overlayWindow = null;
        _activeHotkeyDialog?.Close();
        _activeSettingsWindow?.Close();
        _activeCustomMappingsWindow?.Close();
    }
}
