using Avalonia;
using LaTeXInserter.Helpers;
using Xunit;

namespace LaTeXInserter.Tests;

public class OverlayPositionerTests
{
    private static readonly PixelRect DefaultScreen = new(0, 0, 1920, 1080);

    [Fact]
    public void DefaultPosition_CursorAtCenter_ReturnsCursorPos()
    {
        var result = OverlayPositioner.GetPosition(
            new PixelPoint(500, 500),
            new PixelSize(350, 60),
            DefaultScreen);

        Assert.Equal(new PixelPoint(500, 500), result);
    }

    [Fact]
    public void FlipRight_OverflowsRightEdge()
    {
        var result = OverlayPositioner.GetPosition(
            new PixelPoint(1700, 500),
            new PixelSize(350, 60),
            DefaultScreen);

        Assert.Equal(1350, result.X);
    }

    [Fact]
    public void FlipBottom_OverflowsBottomEdge()
    {
        var result = OverlayPositioner.GetPosition(
            new PixelPoint(500, 1050),
            new PixelSize(350, 60),
            DefaultScreen);

        Assert.Equal(990, result.Y);
    }

    [Fact]
    public void ClampLeft_NegativeCursor()
    {
        var result = OverlayPositioner.GetPosition(
            new PixelPoint(-10, 0),
            new PixelSize(350, 60),
            DefaultScreen);

        Assert.Equal(0, result.X);
    }

    [Fact]
    public void ClampRight_CursorBeyondScreen()
    {
        var result = OverlayPositioner.GetPosition(
            new PixelPoint(1920, 500),
            new PixelSize(350, 60),
            DefaultScreen);

        Assert.Equal(1570, result.X);
    }

    [Fact]
    public void MultiMonitor_OffsetWorkingArea()
    {
        var secondScreen = new PixelRect(1920, 0, 1920, 1080);

        var result = OverlayPositioner.GetPosition(
            new PixelPoint(2500, 500),
            new PixelSize(350, 60),
            secondScreen);

        Assert.Equal(2500, result.X);
    }

    [Fact]
    public void FlipAndClamp_BothAxes()
    {
        var result = OverlayPositioner.GetPosition(
            new PixelPoint(1700, 1050),
            new PixelSize(350, 60),
            DefaultScreen);

        Assert.Equal(1350, result.X);
        Assert.Equal(990, result.Y);
    }
}
