using CommunityToolkit.Mvvm.ComponentModel;

namespace LaTeXInserter.ViewModels;

public sealed partial class UpdateViewModel : ObservableObject
{
    [ObservableProperty]
    private string _headingText = string.Empty;

    [ObservableProperty]
    private string _subtitleText = string.Empty;

    [ObservableProperty]
    private string _changelogText = string.Empty;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private double _downloadProgress;

    [ObservableProperty]
    private string _statusText = string.Empty;

    public event EventHandler? InstallRequested;
    public event EventHandler? LaterRequested;

    public void RequestInstall() => InstallRequested?.Invoke(this, EventArgs.Empty);
    public void RequestLater() => LaterRequested?.Invoke(this, EventArgs.Empty);
}
