using Velopack;
using Velopack.Sources;
using LaTeXInserter.Abstractions;
using LaTeXInserter.Models;

namespace LaTeXInserter.Services;

public sealed class UpdateService : IUpdateService
{
    private readonly UpdateManager _updateManager;
    private UpdateInfo? _latestUpdate;

    public UpdateService()
    {
        _updateManager = new UpdateManager(
            new GithubSource("https://github.com/lsutorus/LaTeX-Inserter-CS", null, false));
        _latestUpdate = null;
    }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync()
    {
        try
        {
            var result = await _updateManager.CheckForUpdatesAsync();

            if (result is null)
                return UpdateCheckResult.UpToDate();

            _latestUpdate = result;
            var version = result.TargetFullRelease.Version.ToString();
            var notes = result.TargetFullRelease.NotesMarkdown ?? string.Empty;
            return UpdateCheckResult.Available(version, notes);
        }
        catch (Exception ex)
        {
            return UpdateCheckResult.Error($"Unable to check for updates: {ex.Message}");
        }
    }

    public async Task DownloadUpdatesAsync(IProgress<int> progress)
    {
        if (_latestUpdate is null)
            throw new InvalidOperationException("No update available. Call CheckForUpdatesAsync first.");

        await _updateManager.DownloadUpdatesAsync(_latestUpdate, progress.Report);
    }

    public void ApplyUpdatesAndRestart()
    {
        _updateManager.ApplyUpdatesAndRestart(_latestUpdate!);
    }
}
