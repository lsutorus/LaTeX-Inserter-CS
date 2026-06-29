using System.Diagnostics;
using System.Reflection;
using Avalonia.Threading;
using LaTeXInserter.Abstractions;
using LaTeXInserter.ViewModels;
using LaTeXInserter.Views;

namespace LaTeXInserter.ViewModels;

public sealed class UpdateCoordinator : IUpdateCoordinator
{
    private readonly IUpdateService _updateService;
    private readonly UpToDateViewModel _upToDateViewModel;
    private readonly UpdateViewModel _updateViewModel;

    private UpToDateDialog? _activeUpToDateDialog;
    private UpdateDialog? _activeUpdateDialog;

    public UpdateCoordinator(
        IUpdateService updateService,
        UpToDateViewModel upToDateViewModel,
        UpdateViewModel updateViewModel)
    {
        _updateService = updateService;
        _upToDateViewModel = upToDateViewModel;
        _updateViewModel = updateViewModel;

        _updateViewModel.InstallRequested += OnInstallRequested;
    }

    private async void OnInstallRequested(object? sender, EventArgs _)
    {
        await InstallUpdateAsync();
    }

    public async Task CheckForUpdatesAsync()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";

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

    public async Task InstallUpdateAsync()
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

    private void ShowUpToDateDialog()
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

    private void ShowUpdateDialog()
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

    public void CloseDialogs()
    {
        _activeUpToDateDialog?.Close();
        _activeUpdateDialog?.Close();
    }
}
