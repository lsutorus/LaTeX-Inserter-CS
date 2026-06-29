using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using LaTeXInserter.Abstractions;
using LaTeXInserter.ViewModels;

namespace LaTeXInserter.Views;

public partial class OverlayWindow : Window
{
    private OverlayViewModel? _vm;

    public IOverlayPositioner? OverlayPositioner { get; set; }

    public OverlayWindow()
    {
        InitializeComponent();
        AddHandler(InputElement.KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        Deactivated += OnDeactivated;
        Opened += OnOpened;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        OverlayPositioner?.PositionOverlay(this);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsVisibleProperty && IsVisible)
        {
            _vm = DataContext as OverlayViewModel;
            Opacity = 0;
            OverlayPositioner?.PositionOverlay(this);

            Dispatcher.UIThread.Post(() => InputTextBox.Focus(), DispatcherPriority.Input);
        }
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm is null) return;

        switch (e.Key)
        {
            case Key.Escape:
                _vm.Cancel();
                e.Handled = true;
                break;

            case Key.Tab:
                if (_vm.IsAutocompleteOpen)
                {
                    _vm.CommitAutocomplete(_vm.SelectedAutocompleteItem);
                    e.Handled = true;
                    Dispatcher.UIThread.Post(() =>
                        InputTextBox.CaretIndex = InputTextBox.Text?.Length ?? 0);
                }
                break;

            case Key.Enter:
                _vm.Submit();
                e.Handled = true;
                break;

            case Key.Up:
                if (_vm.IsAutocompleteOpen)
                {
                    _vm.NavigateAutocomplete(-1);
                    e.Handled = true;
                }
                break;

            case Key.Down:
                if (_vm.IsAutocompleteOpen)
                {
                    _vm.NavigateAutocomplete(1);
                    e.Handled = true;
                }
                break;
        }
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        _vm?.Cancel();
    }
}
