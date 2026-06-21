using Avalonia.Controls;
using Avalonia.Interactivity;

namespace LaTeXInserter.Views;

public partial class UpToDateDialog : Window
{
    public UpToDateDialog()
    {
        InitializeComponent();
    }

    private void OnOkClick(object? sender, RoutedEventArgs e) => Close();
}
