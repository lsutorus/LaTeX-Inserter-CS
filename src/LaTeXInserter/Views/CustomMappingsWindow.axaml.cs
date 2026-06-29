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
            // Let default Tab navigation handle Command↔Character movement.
            // On LostFocus, edit will commit.
        }
        else if (e.Key == Key.Escape)
        {
            item.IsEditing = false;
            e.Handled = true;
        }
    }

    private void OnEditLostFocus(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        if (textBox.DataContext is not MappingItem item) return;
        if (item.IsEditing)
            CommitEdit(item);
    }

    private void CommitEdit(MappingItem item)
    {
        item.IsEditing = false;

        if (DataContext is CustomMappingsViewModel vm)
            vm.OnItemEditCommitted(item);
    }
}
