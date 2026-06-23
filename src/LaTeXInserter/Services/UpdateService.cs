using System.Diagnostics;
using Velopack;
using Velopack.Sources;
using LaTeXInserter.Abstractions;
using LaTeXInserter.Models;

namespace LaTeXInserter.Services;

public sealed class UpdateService : IUpdateService
{
    private readonly UpdateManager _updateManager;
    private UpdateInfo? _latestUpdate;
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(10);

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
            using var cts = new CancellationTokenSource(CheckTimeout);

            var checkTask = _updateManager.CheckForUpdatesAsync();
            var completed = await Task.WhenAny(checkTask, Task.Delay(CheckTimeout, cts.Token));

            if (completed != checkTask)
                return UpdateCheckResult.Error("Update check timed out. Please try again later.");

            var result = await checkTask;

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
