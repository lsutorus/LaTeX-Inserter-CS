using Avalonia.Controls;
using Avalonia.Interactivity;
using LaTeXInserter.ViewModels;

namespace LaTeXInserter.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    private void OnSwatchClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Background is Avalonia.Media.IBrush brush)
        {
            var vm = DataContext as SettingsViewModel;
            if (vm is not null && brush is Avalonia.Media.SolidColorBrush scb)
            {
                vm.AccentColor = $"#{scb.Color.R:X2}{scb.Color.G:X2}{scb.Color.B:X2}";
            }
        }
    }
}
