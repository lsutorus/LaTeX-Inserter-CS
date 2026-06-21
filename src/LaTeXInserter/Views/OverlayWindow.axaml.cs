using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using LaTeXInserter.Abstractions;
using LaTeXInserter.Helpers;
using LaTeXInserter.Platform.Windows;
using LaTeXInserter.ViewModels;

namespace LaTeXInserter.Views;

public partial class OverlayWindow : Window
{
    private OverlayViewModel? _vm;

    public IWindowActivator? WindowActivator { get; set; }

    public OverlayWindow()
    {
        InitializeComponent();
        AddHandler(InputElement.KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        Deactivated += OnDeactivated;
        Opened += OnOpened;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        PositionOverlay();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsVisibleProperty && IsVisible)
        {
            _vm = DataContext as OverlayViewModel;
            Opacity = 0;
            PositionOverlay();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                if (handle != IntPtr.Zero && WindowActivator is not null)
                    WindowActivator.Activate(handle);
            }

            Dispatcher.UIThread.Post(() => InputTextBox.Focus(), DispatcherPriority.Input);
        }
    }

    private void PositionOverlay()
    {
        if (ClientSize.Height <= 0)
            return;

        PixelPoint cursorPos;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            NativeMethods.GetCursorPos(out var pt);
            cursorPos = new PixelPoint(pt.X, pt.Y);
        }
        else
        {
            var primary = Screens.Primary;
            cursorPos = primary is not null
                ? new PixelPoint(primary.WorkingArea.X + primary.WorkingArea.Width / 2,
                                 primary.WorkingArea.Y + primary.WorkingArea.Height / 2)
                : new PixelPoint(0, 0);
        }

        var screen = Screens.ScreenFromPoint(cursorPos) ?? Screens.Primary!;
        var scaling = screen.Scaling;
        var physicalSize = new PixelSize(
            (int)(ClientSize.Width * scaling),
            (int)(ClientSize.Height * scaling));

        Position = OverlayPositioner.GetPosition(cursorPos, physicalSize, screen.WorkingArea);
        Opacity = 1;
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
                    var selected = _vm.GetSelectedAutocompleteItem();
                    if (selected is not null)
                        _vm.CommitAutocomplete(selected);
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
