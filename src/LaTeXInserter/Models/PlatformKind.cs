namespace LaTeXInserter.Models;

public enum PlatformKind
{
    Windows,
    MacOS
}

public static class PlatformKinds
{
    /// <summary>
    /// The platform the process is running on. Linux is intentionally unsupported —
    /// libuiohook cannot suppress events on X11 and Wayland is unsupported entirely.
    /// </summary>
    public static PlatformKind Current =>
        OperatingSystem.IsMacOS() ? PlatformKind.MacOS
        : OperatingSystem.IsWindows() ? PlatformKind.Windows
        : throw new PlatformNotSupportedException(
            "LaTeX Inserter supports Windows and macOS only.");
}
