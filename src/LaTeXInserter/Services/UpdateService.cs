using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using Velopack;
using Velopack.Sources;
using LaTeXInserter.Abstractions;
using LaTeXInserter.Models;

namespace LaTeXInserter.Services;

public sealed class UpdateService : IUpdateService
{
    private const string RepoUrl = "https://github.com/lsutorus/LaTeX-Inserter-CS";
    private const string ReleasesLatestUrl =
        "https://api.github.com/repos/lsutorus/LaTeX-Inserter-CS/releases/latest";
    private const string UserAgent = "LaTeXInserter";

    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(10);

    // Single reusable HttpClient. GitHub rejects requests without a User-Agent.
    private static readonly HttpClient s_http = new()
    {
        Timeout = TimeSpan.FromSeconds(8),
        DefaultRequestHeaders =
        {
            UserAgent = { ProductInfoHeaderValue.Parse($"{UserAgent}/1.0") },
            Accept = { new MediaTypeWithQualityHeaderValue("application/vnd.github+json") },
        },
    };

    private readonly UpdateManager _updateManager;
    private UpdateInfo? _latestUpdate;
    private Task<UpdateInfo?>? _pendingResolve;

    public UpdateService()
    {
        _updateManager = new UpdateManager(
            new GithubSource(RepoUrl, null, false));
        _latestUpdate = null;
    }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync()
    {
        // Use the GitHub Releases API for a fast, single-call version check.
        // Velopack's CheckForUpdatesAsync downloads the per-release feed
        // (releases.<channel>.json) for EVERY release serially. Each GitHub
        // signed-asset download can stall ~20s (token nbf / clock-skew retry),
        // so the full walk regularly blows past any reasonable timeout, making
        // "Check for Updates" always report a timeout even when up-to-date.
        // The check only needs "latest stable tag vs installed"; Velopack is
        // still used for the actual download/install below.
        using var cts = new CancellationTokenSource(CheckTimeout);
        try
        {
            var apiTask = s_http.GetAsync(
                ReleasesLatestUrl, HttpCompletionOption.ResponseHeadersRead, cts.Token);

            if (await Task.WhenAny(apiTask, Task.Delay(CheckTimeout, cts.Token)) != apiTask)
                return UpdateCheckResult.Error("Update check timed out. Please try again later.");

            using var resp = await apiTask;
            if ((int)resp.StatusCode == 404)
                return UpdateCheckResult.UpToDate(); // no published releases
            if (!resp.IsSuccessStatusCode)
                return UpdateCheckResult.Error(
                    $"Unable to check for updates: GitHub returned {resp.StatusCode}.");

            await using var stream = await resp.Content.ReadAsStreamAsync(cts.Token);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token);

            string tagName = doc.RootElement.GetProperty("tag_name").GetString() ?? string.Empty;
            string notes = doc.RootElement.TryGetProperty("body", out var bodyEl)
                ? bodyEl.GetString() ?? string.Empty : string.Empty;

            var latest = ParseVersion(tagName);
            if (latest is null)
                return UpdateCheckResult.Error($"Unable to parse latest version tag '{tagName}'.");

            var current = _updateManager.CurrentVersion
                          ?? SemanticVersion.Parse(GetAssemblyVersion());

            if (latest.CompareTo(current) <= 0)
                return UpdateCheckResult.UpToDate();

            // Resolve Velopack's UpdateInfo in the background so the install path
            // doesn't have to wait on the slow serial feed walk at click time.
            _pendingResolve ??= _updateManager.CheckForUpdatesAsync();
            return UpdateCheckResult.Available(latest.ToString(), notes);
        }
        catch (OperationCanceledException)
        {
            return UpdateCheckResult.Error("Update check timed out. Please try again later.");
        }
        catch (Exception ex)
        {
            return UpdateCheckResult.Error($"Unable to check for updates: {ex.Message}");
        }
    }

    public async Task DownloadUpdatesAsync(IProgress<int> progress)
    {
        var info = await ResolveUpdateInfoAsync();
        if (info is null)
            throw new InvalidOperationException("No update available. Call CheckForUpdatesAsync first.");

        await _updateManager.DownloadUpdatesAsync(info, progress.Report);
    }

    public void ApplyUpdatesAndRestart()
    {
        _updateManager.ApplyUpdatesAndRestart(_latestUpdate!);
    }

    private async Task<UpdateInfo?> ResolveUpdateInfoAsync()
    {
        if (_latestUpdate is not null)
            return _latestUpdate;

        // Prefer the background check already started during CheckForUpdatesAsync.
        if (_pendingResolve is not null)
        {
            try
            {
                _latestUpdate = await _pendingResolve;
                return _latestUpdate;
            }
            catch
            {
                _pendingResolve = null; // fall back to a fresh check
            }
        }

        _latestUpdate = await _updateManager.CheckForUpdatesAsync();
        return _latestUpdate;
    }

    private static SemanticVersion? ParseVersion(string tag)
    {
        var s = tag.AsSpan().Trim();
        if (s.Length > 0 && (s[0] == 'v' || s[0] == 'V'))
            s = s[1..];
        try { return SemanticVersion.Parse(s.ToString()); }
        catch { return null; }
    }

    private static string GetAssemblyVersion() =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.0.0";
}
