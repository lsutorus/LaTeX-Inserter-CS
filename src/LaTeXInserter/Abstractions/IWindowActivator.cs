namespace LaTeXInserter.Abstractions;

public interface IWindowActivator
{
    void CapturePrevious();
    void Activate(IntPtr overlayHandle);
    void Restore();
}
