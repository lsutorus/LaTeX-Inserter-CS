namespace LaTeXInserter.Models;

public sealed record UpdateCheckResult(
    bool IsUpdateAvailable,
    string? Version,
    string? ReleaseNotes,
    bool IsError,
    string? ErrorMessage)
{
    public static UpdateCheckResult Error(string message) =>
        new(false, null, null, true, message);

    public static UpdateCheckResult UpToDate() =>
        new(false, null, null, false, null);

    public static UpdateCheckResult Available(string version, string releaseNotes) =>
        new(true, version, releaseNotes, false, null);
}
