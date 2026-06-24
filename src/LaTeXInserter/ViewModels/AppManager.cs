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
    private readonly IClipboardProvider _clipboardProvider;
    private readonly IInputSimulatorService _inputSimulator;
    private readonly IUpdateService _updateService;
    private readonly TrayIconViewModel _trayIconViewModel;
    private readonly OverlayViewModel _overlayViewModel;
    private readonly UpToDateViewModel _upToDateViewModel;
    private readonly UpdateViewModel _updateViewModel;
    private readonly HotkeyDialogViewModel _hotkeyDialogViewModel;
    private readonly SettingsViewModel _settingsViewModel;

    private OverlayWindow? _overlayWindow;
    private UpToDateDialog? _activeUpToDateDialog;
    private UpdateDialog? _activeUpdateDialog;
    private HotkeyDialogWindow? _activeHotkeyDialog;
    private SettingsWindow? _activeSettingsWindow;
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
        IClipboardProvider clipboardProvider,
        IInputSimulatorService inputSimulator,
        IUpdateService updateService,
        TrayIconViewModel trayIconViewModel,
        OverlayViewModel overlayViewModel,
        UpToDateViewModel upToDateViewModel,
        UpdateViewModel updateViewModel,
        HotkeyDialogViewModel hotkeyDialogViewModel,
        SettingsViewModel settingsViewModel)
    {
        _settingsService = settingsService;
        _hotkeyService = hotkeyService;
        _startupRegistrar = startupRegistrar;
        _windowActivator = windowActivator;
        _clipboardProvider = clipboardProvider;
        _inputSimulator = inputSimulator;
        _updateService = updateService;
        _trayIconViewModel = trayIconViewModel;
        _overlayViewModel = overlayViewModel;
        _upToDateViewModel = upToDateViewModel;
        _updateViewModel = updateViewModel;
        _hotkeyDialogViewModel = hotkeyDialogViewModel;
        _settingsViewModel = settingsViewModel;
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

            // 5. Sync startup registration from OS truth
            try
            {
                var isRegistered = await _startupRegistrar.GetIsRegisteredAsync();
                if (settings.StartOnStartup != isRegistered)
                {
                    settings = settings with { StartOnStartup = isRegistered };
                    _settingsService.Save(settings);
                }
            }
            catch
            {
                // Non-fatal: startup check failure shouldn't block init
            }

            // 6. Wire events
            _hotkeyService.HotkeyPressed += OnHotkeyPressed;
            _trayIconViewModel.ShowOverlayRequested += OnShowOverlayRequested;
            _trayIconViewModel.SettingsRequested += OnSettingsRequested;
            _settingsViewModel.ChangeHotkeyRequested += OnChangeHotkeyRequested;
            _trayIconViewModel.CheckForUpdatesRequested += OnCheckForUpdatesRequested;
            _updateViewModel.InstallRequested += OnInstallRequested;
            _trayIconViewModel.QuitRequested += OnQuitRequested;
            _overlayViewModel.SubmitRequested += OnSubmitRequested;
            _overlayViewModel.HideRequested += OnHideRequested;
            _settingsViewModel.SettingsSaved += OnSettingsSaved;
            _settingsViewModel.CloseRequested += OnSettingsCloseRequested;

            // Set version text on dialog VM
            var version = typeof(AppManager).Assembly.GetName().Version?.ToString() ?? "unknown";
            _upToDateViewModel.SubtitleText = $"v{version}";
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
        var version = typeof(AppManager).Assembly.GetName().Version?.ToString() ?? "unknown";

        // Show dialog immediately in "checking" state
        _upToDateViewModel.VersionText = "Checking for Updates...";
        _upToDateViewModel.SubtitleText = string.Empty;
        _upToDateViewModel.IsChecking = true;
        ShowUpToDateDialog();

        var result = await _updateService.CheckForUpdatesAsync();
        _upToDateViewModel.IsChecking = false;

        if (result.IsError)
        {
            _upToDateViewModel.VersionText = "Unable to Check for Updates";
            _upToDateViewModel.SubtitleText = result.ErrorMessage ?? "Unknown error";
        }
        else if (result.IsUpdateAvailable)
        {
            // Close checking dialog, show update dialog instead
            _activeUpToDateDialog?.Close();

            _updateViewModel.HeadingText = $"Version {result.Version} is Available";
            _updateViewModel.SubtitleText = $"Current: v{version}";
            _updateViewModel.ChangelogText = result.ReleaseNotes ?? string.Empty;
            _updateViewModel.IsDownloading = false;
            _updateViewModel.DownloadProgress = 0;
            _updateViewModel.StatusText = string.Empty;
            _updateViewModel.HasError = false;
            ShowUpdateDialog();
        }
        else
        {
            _upToDateViewModel.VersionText = "You are running the latest version";
            _upToDateViewModel.SubtitleText = $"v{version}";
        }
    }

    private async void OnInstallRequested(object? sender, EventArgs _)
    {
        if (_updateViewModel.IsDownloading) return;

        _updateViewModel.IsDownloading = true;
        _updateViewModel.HasError = false;
        _updateViewModel.StatusText = "Downloading update...";

        try
        {
            var progress = new Progress<int>(p =>
            {
                _updateViewModel.DownloadProgress = p;
            });

            await _updateService.DownloadUpdatesAsync(progress);

            _updateViewModel.StatusText = "Installing and restarting...";
            _updateService.ApplyUpdatesAndRestart();
        }
        catch (Exception ex)
        {
            _updateViewModel.IsDownloading = false;
            _updateViewModel.HasError = true;
            _updateViewModel.StatusText = $"Download failed: {ex.Message}";
        }
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

    private async void OnSubmitRequested(object? sender, string convertedText)
    {
        try
        {
            // 1. Set clipboard while the overlay window is visible and active
            await _clipboardProvider.SetTextAsync(convertedText);

            // 2. Restore focus to the target editor while we still have Win32 foreground permission
            _windowActivator.Restore();

            // 3. Hide the overlay
            HideOverlay();

            // 4. Delay briefly to let the OS focus transition complete
            await Task.Delay(50);

            // 5. Simulate the paste keys
            await _inputSimulator.SimulatePasteAsync(convertedText);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SubmitAndPaste failed: {ex}");
        }
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
                WindowActivator = _windowActivator
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

    public void ShowUpToDateDialog()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_activeUpToDateDialog is not null)
            {
                _activeUpToDateDialog.Activate();
                return;
            }

            _activeUpToDateDialog = new UpToDateDialog { DataContext = _upToDateViewModel };
            _activeUpToDateDialog.Closed += (_, _) => _activeUpToDateDialog = null;
            _activeUpToDateDialog.Show();
        });
    }

    public void ShowUpdateDialog()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_activeUpdateDialog is not null)
            {
                _activeUpdateDialog.Activate();
                return;
            }

            _activeUpdateDialog = new UpdateDialog { DataContext = _updateViewModel };
            _activeUpdateDialog.Closed += (_, _) => _activeUpdateDialog = null;
            _activeUpdateDialog.Show();
        });
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
            _trayIconViewModel.ShowOverlayRequested -= OnShowOverlayRequested;
            _trayIconViewModel.SettingsRequested -= OnSettingsRequested;
            _settingsViewModel.ChangeHotkeyRequested -= OnChangeHotkeyRequested;
            _trayIconViewModel.CheckForUpdatesRequested -= OnCheckForUpdatesRequested;
            _trayIconViewModel.QuitRequested -= OnQuitRequested;
            _overlayViewModel.SubmitRequested -= OnSubmitRequested;
            _overlayViewModel.HideRequested -= OnHideRequested;
            _updateViewModel.InstallRequested -= OnInstallRequested;
            _settingsViewModel.SettingsSaved -= OnSettingsSaved;
            _settingsViewModel.CloseRequested -= OnSettingsCloseRequested;
            _hotkeyService.Dispose();
        }

        _overlayWindow?.Close();
        _overlayWindow = null;
        _activeHotkeyDialog?.Close();
        _activeSettingsWindow?.Close();
    }
}
