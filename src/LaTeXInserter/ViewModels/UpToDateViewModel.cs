using CommunityToolkit.Mvvm.ComponentModel;

namespace LaTeXInserter.ViewModels;

public sealed partial class UpToDateViewModel : ObservableObject
{
    [ObservableProperty]
    private string _versionText = "You are running the latest version";

    [ObservableProperty]
    private string _subtitleText = string.Empty;

    [ObservableProperty]
    private bool _isChecking;
}
