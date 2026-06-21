using Avalonia;

namespace LaTeXInserter.Helpers;

public static class OverlayPositioner
{
    public static PixelPoint GetPosition(
        PixelPoint cursorPosition,
        PixelSize windowPhysicalSize,
        PixelRect screenWorkingArea)
    {
        var x = cursorPosition.X;
        var y = cursorPosition.Y;

        // Flip right if overflows right edge
        if (x + windowPhysicalSize.Width > screenWorkingArea.X + screenWorkingArea.Width)
            x = cursorPosition.X - windowPhysicalSize.Width;

        // Flip bottom if overflows bottom edge
        if (y + windowPhysicalSize.Height > screenWorkingArea.Y + screenWorkingArea.Height)
            y = cursorPosition.Y - windowPhysicalSize.Height;

        // Clamp left/top to screen bounds
        x = Math.Max(x, screenWorkingArea.X);
        y = Math.Max(y, screenWorkingArea.Y);

        // Clamp right/bottom to screen bounds
        x = Math.Min(x, screenWorkingArea.X + screenWorkingArea.Width - windowPhysicalSize.Width);
        y = Math.Min(y, screenWorkingArea.Y + screenWorkingArea.Height - windowPhysicalSize.Height);

        return new PixelPoint(x, y);
    }
}
