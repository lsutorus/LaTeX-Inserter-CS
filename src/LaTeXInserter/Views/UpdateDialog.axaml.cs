using Avalonia.Controls;
using Avalonia.Interactivity;
using LaTeXInserter.ViewModels;

namespace LaTeXInserter.Views;

public partial class UpdateDialog : Window
{
    public UpdateDialog()
    {
        InitializeComponent();
    }

    private void OnInstallClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is UpdateViewModel vm)
            vm.RequestInstall();
    }

    private void OnLaterClick(object? sender, RoutedEventArgs e) => Close();
}
