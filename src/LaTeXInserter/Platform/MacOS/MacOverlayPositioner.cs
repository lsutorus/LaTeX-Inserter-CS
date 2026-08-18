using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using LaTeXInserter.Abstractions;
using LaTeXInserter.Helpers;

namespace LaTeXInserter.Platform.MacOS;

[SupportedOSPlatform("macos")]
internal sealed class MacOverlayPositioner : IOverlayPositioner
{
    private readonly IWindowActivator _windowActivator;

    public MacOverlayPositioner(IWindowActivator windowActivator)
    {
        _windowActivator = windowActivator;
    }

    public void PositionOverlay(Window window)
    {
        if (window.ClientSize.Height <= 0)
            return;

        MacWindowBehavior.ConfigureOverlay(window);

        var location = MacNativeMethods.GetCursorLocation();
        var cursorPos = new PixelPoint((int)location.X, (int)location.Y);

        var screen = window.Screens.ScreenFromPoint(cursorPos) ?? window.Screens.Primary!;
        var scaling = screen.Scaling;
        var physicalSize = new PixelSize(
            (int)(window.ClientSize.Width * scaling),
            (int)(window.ClientSize.Height * scaling));

        window.Position = OverlayPositioner.GetPosition(cursorPos, physicalSize, screen.WorkingArea);
        window.Opacity = 1;

        // Bring the app forward so the borderless overlay can become key window.
        // An LSUIElement app will not get key status otherwise.
        _windowActivator.Activate(IntPtr.Zero);
        window.Activate();
    }
}
