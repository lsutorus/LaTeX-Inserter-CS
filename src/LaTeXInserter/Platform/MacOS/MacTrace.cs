using System.Runtime.Versioning;

namespace LaTeXInserter.Platform.MacOS;

/// <summary>
/// On-device diagnostics for the macOS interop layer.
///
/// The overlay path runs inside a run-loop callback in a windowless agent app, so
/// there is no console to write to and Debug.WriteLine is compiled out of Release.
/// Tracing is off unless the marker file exists, which lets a build be switched
/// between quiet and verbose without a rebuild — and a rebuild would void the app's
/// TCC grants, which is the expensive part of any macOS debugging round.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class MacTrace
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library", "Logs", "LaTeX Inserter");

    private static readonly bool Enabled = File.Exists(Path.Combine(Dir, "trace.on"));

    private static readonly object Gate = new();

    public static void Write(string message)
    {
        if (!Enabled) return;

        try
        {
            lock (Gate)
            {
                File.AppendAllText(
                    Path.Combine(Dir, "trace.log"),
                    $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Diagnostics must never be the reason the app fails.
        }
    }
}
