using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using LaTeXInserter.ViewModels;

namespace LaTeXInserter.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        AccentSelector.ItemsView.CollectionChanged += (_, _) => InitializeSwatchColors();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is SettingsViewModel vm)
        {
            vm.AccentColorChanged += OnAccentColorChanged;
            UpdateSwatchSelection(vm.AccentColor);
        }
    }

    private void InitializeSwatchColors()
    {
        foreach (var item in AccentSelector.Items)
        {
            if (item is not string hex) continue;
            var container = AccentSelector.ContainerFromItem(item);
            if (container is ContentControl { Content: Button btn })
            {
                btn.Background = new SolidColorBrush(Color.Parse(hex));
                btn.DataContext = hex;
            }
        }

        if (DataContext is SettingsViewModel vm)
            UpdateSwatchSelection(vm.AccentColor);
    }

    private void UpdateSwatchSelection(string selectedHex)
    {
        foreach (var item in AccentSelector.Items)
        {
            if (item is not string hex) continue;
            var container = AccentSelector.ContainerFromItem(item);
            if (container is ContentControl { Content: Button btn })
            {
                if (hex == selectedHex)
                    btn.Classes.Add("accent-selected");
                else
                    btn.Classes.Remove("accent-selected");
            }
        }
    }

    private void OnSwatchClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is string hex)
        {
            var vm = DataContext as SettingsViewModel;
            if (vm is not null && vm.AccentColor != hex)
            {
                vm.AccentColor = hex;
                UpdateSwatchSelection(hex);
            }
        }
    }

    private void OnAccentColorChanged(object? sender, string hex)
    {
        UpdateSwatchSelection(hex);
    }
}
