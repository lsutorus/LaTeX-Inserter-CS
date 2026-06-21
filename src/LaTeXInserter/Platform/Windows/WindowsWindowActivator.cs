using LaTeXInserter.Abstractions;

namespace LaTeXInserter.Platform.Windows;

internal sealed class WindowsWindowActivator : IWindowActivator
{
    private IntPtr _previousWindow;

    public void CapturePrevious()
    {
        _previousWindow = NativeMethods.GetForegroundWindow();
    }

    public void Activate(IntPtr overlayHandle)
    {
        uint fgThread = NativeMethods.GetWindowThreadProcessId(_previousWindow, out _);
        uint myThread = NativeMethods.GetCurrentThreadId();

        NativeMethods.AttachThreadInput(fgThread, myThread, true);
        NativeMethods.SetForegroundWindow(overlayHandle);
        NativeMethods.SetActiveWindow(overlayHandle);
        NativeMethods.SetFocus(overlayHandle);
        NativeMethods.AttachThreadInput(fgThread, myThread, false);
    }

    public void Restore()
    {
        if (_previousWindow == IntPtr.Zero) return;

        uint fgThread = NativeMethods.GetWindowThreadProcessId(_previousWindow, out _);
        uint myThread = NativeMethods.GetCurrentThreadId();

        NativeMethods.AttachThreadInput(fgThread, myThread, true);
        NativeMethods.SetForegroundWindow(_previousWindow);
        NativeMethods.SetActiveWindow(_previousWindow);
        NativeMethods.SetFocus(_previousWindow);
        NativeMethods.AttachThreadInput(fgThread, myThread, false);
    }
}
