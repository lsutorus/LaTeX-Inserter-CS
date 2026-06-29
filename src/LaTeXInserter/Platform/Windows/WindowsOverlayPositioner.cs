using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using LaTeXInserter.Abstractions;
using LaTeXInserter.Helpers;
using LaTeXInserter.Platform.Windows;

namespace LaTeXInserter.Platform.Windows;

internal sealed class WindowsOverlayPositioner : IOverlayPositioner
{
    private readonly IWindowActivator _windowActivator;

    public WindowsOverlayPositioner(IWindowActivator windowActivator)
    {
        _windowActivator = windowActivator;
    }

    public void PositionOverlay(Window window)
    {
        if (window.ClientSize.Height <= 0)
            return;

        PixelPoint cursorPos;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            NativeMethods.GetCursorPos(out var pt);
            cursorPos = new PixelPoint(pt.X, pt.Y);
        }
        else
        {
            var primary = window.Screens.Primary;
            cursorPos = primary is not null
                ? new PixelPoint(primary.WorkingArea.X + primary.WorkingArea.Width / 2,
                                 primary.WorkingArea.Y + primary.WorkingArea.Height / 2)
                : new PixelPoint(0, 0);
        }

        var screen = window.Screens.ScreenFromPoint(cursorPos) ?? window.Screens.Primary!;
        var scaling = screen.Scaling;
        var physicalSize = new PixelSize(
            (int)(window.ClientSize.Width * scaling),
            (int)(window.ClientSize.Height * scaling));

        window.Position = OverlayPositioner.GetPosition(cursorPos, physicalSize, screen.WorkingArea);
        window.Opacity = 1;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (handle != IntPtr.Zero)
                _windowActivator.Activate(handle);
        }
    }
}
