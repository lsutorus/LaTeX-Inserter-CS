namespace LaTeXInserter.Abstractions;

public interface IUpdateService
{
    Task<Models.UpdateCheckResult> CheckForUpdatesAsync();
    Task DownloadUpdatesAsync(IProgress<int> progress);
    void ApplyUpdatesAndRestart();
}
