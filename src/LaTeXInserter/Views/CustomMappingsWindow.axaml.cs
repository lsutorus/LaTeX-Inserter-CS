using Avalonia.Controls;
using Avalonia.Input;
using LaTeXInserter.Models;
using LaTeXInserter.ViewModels;

namespace LaTeXInserter.Views;

public partial class CustomMappingsWindow : Window
{
    public CustomMappingsWindow()
    {
        InitializeComponent();
        // ResizeMode in AXAML trips compiled-binds (AVLN2000); set in code-behind instead.
        CanResize = false;
    }

    private void OnCustomItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is ListBox lb && lb.SelectedItem is MappingItem item)
            item.IsEditing = true;
    }

    private void OnDefaultItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is ListBox lb && lb.SelectedItem is MappingItem item)
            item.IsEditing = true;
    }

    private void OnEditKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        if (textBox.DataContext is not MappingItem item) return;

        if (e.Key == Key.Enter)
        {
            CommitEdit(item);
            e.Handled = true;
        }
        else if (e.Key == Key.Tab)
        {
            // Let default Tab navigation move Command↔Character within the row.
            // Edit stays active; commit happens on Enter or Save.
        }
        else if (e.Key == Key.Escape)
        {
            item.CancelEdit();
            e.Handled = true;
        }
    }

    private void CommitEdit(MappingItem item)
    {
        item.IsEditing = false;

        if (DataContext is CustomMappingsViewModel vm)
            vm.OnItemEditCommitted(item);
    }
}
