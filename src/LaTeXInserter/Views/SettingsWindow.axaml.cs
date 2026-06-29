using Avalonia.Controls;
using Avalonia.Interactivity;
using LaTeXInserter.Models;
using LaTeXInserter.ViewModels;

namespace LaTeXInserter.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();

        CanResize = false;

        // Set ItemsSource in code-behind — {x:Static} doesn't work reliably
        // with AvaloniaUseCompiledBindingsByDefault=true
        AccentSelector.ItemsSource = SettingsViewModel.AccentPalette;
    }

    private void OnSwatchClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is AccentSwatchInfo swatch)
        {
            var vm = DataContext as SettingsViewModel;
            if (vm is not null && vm.AccentColor != swatch.Hex)
                vm.SelectSwatch(swatch);
        }
    }
}
