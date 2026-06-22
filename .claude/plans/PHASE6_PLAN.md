# Phase 6 Plan: Velopack & Packaging

All decisions grilled & locked. No speculation below.

---

## Decisions Register

| # | Decision | Choice | Rationale |
|---|----------|--------|-----------|
| 1 | GitHub repo for updates | `lsutorus/LaTeX-Inserter-CS` | Separate C# product, not Python repo |
| 2 | IUpdateService interface | Yes, in Abstractions/ | Matches all other services; testability + DI strictness |
| 3 | Update check result type | `Models/UpdateCheckResult.cs` record with factory methods | Error/success states in one type; impossible states prevented via `Error()`, `UpToDate()`, `Available()` |
| 4 | Progress reporting | `IProgress<int>` | .NET canonical pattern; SynchronizationContext capture on UI thread |
| 5 | UpdateCheckResult location | `Models/` | Domain model, not abstraction concern; matches HotkeyChord/AppSettings |
| 6 | Check error handling | Error state in UpdateCheckResult, not exception | "No internet" is expected, not exceptional; avoids exception-driven control flow |
| 7 | Download error handling | Throws, AppManager catches | Mid-transfer failure is exceptional; AppManager resets VM state |
| 8 | UpdateManager creation | Inside UpdateService constructor | Velopack fully contained; no other class needs UpdateManager |
| 9 | Velopack v3 syntax | `new UpdateManager(new GithubSource(...))` | Not the old Squirrel static method |
| 10 | State storage in UpdateService | Instance fields `_updateManager` + `_latestUpdate` | Sequential check→download→apply flow; UpdateCheckResult stays clean of Velopack types |
| 11 | Update orchestration owner | AppManager | Matches existing pattern; TrayIconViewModel unchanged |
| 12 | Update check error UI | Reuse UpToDateDialog with different text | Same UX shape; AppManager sets VersionText/SubtitleText before showing |
| 13 | Update download flow | AppManager constructs Progress<int> on UI thread, sets VM properties | AppManager owns all VM state transitions; UpdateService just calls progress.Report() |
| 14 | Pre-restart status text | Set "Installing and restarting..." before ApplyUpdatesAndRestart, no new VM property | Process terminates immediately; text is safety net only |
| 15 | VelopackApp.Build().Run() | Very first line in Main, before DI/Avalonia | Velopack must handle pending installs before app starts |
| 16 | GitHub Actions trigger | Push tag `v*` on `windows-latest` | Native AOT + Velopack packaging is Windows-only |
| 17 | Velopack installer format | NSIS (`--format nsis`) | Traditional Setup.exe; matches Windows user expectations |
| 18 | SHA256 release asset | Yes, for user-facing Setup.exe only | Velopack handles its own package integrity; SHA256 is for manual downloaders |
| 19 | Temp download dir cleanup | Velopack handles it, no manual cleanup | VelopackApp.Build().Run() at startup manages pending updates/restarts |
| 20 | HasError on UpdateViewModel | Yes, "Later" button visible on error, no retry | User dismisses, can re-check from tray |
| 21 | Download guard | Check UpdateViewModel.IsDownloading in AppManager | Belt-and-suspenders; UI hiding Install button is primary guard |
| 22 | Later button + download | No cancellation; let download complete | Velopack downloads are atomic; no corruption risk |
| 23 | LaterRequested subscription | No; code-behind Close() sufficient | AppManager.Closed handler clears state; VM reset on next show |

---

## File Manifest

### New Files (4)

#### 1. `src/LaTeXInserter/Abstractions/IUpdateService.cs`

```csharp
namespace LaTeXInserter.Abstractions;

public interface IUpdateService
{
    Task<Models.UpdateCheckResult> CheckForUpdatesAsync();
    Task DownloadUpdatesAsync(IProgress<int> progress);
    void ApplyUpdatesAndRestart();
}
```

- `CheckForUpdatesAsync` — returns `UpdateCheckResult` (success or error, no throw for expected failures)
- `DownloadUpdatesAsync` — receives `IProgress<int>` for progress reporting; throws on failure
- `ApplyUpdatesAndRestart` — terminates process and restarts with updated version

#### 2. `src/LaTeXInserter/Services/UpdateService.cs`

Implementation details:
- Constructor: `_updateManager = new UpdateManager(new GithubSource("lsutorus/LaTeX-Inserter-CS"))`
- Constructor: `_latestUpdate = null`
- `CheckForUpdatesAsync()`:
  - try: call `_updateManager.CheckForUpdatesAsync()`
  - If result is null or no updates → return `UpdateCheckResult.UpToDate()`
  - If updates available → cache result in `_latestUpdate`, return `UpdateCheckResult.Available(version, releaseNotes)`
  - catch: return `UpdateCheckResult.Error($"Unable to check for updates: {ex.Message}")`
- `DownloadUpdatesAsync(IProgress<int> progress)`:
  - Call `_updateManager.DownloadUpdatesAsync(_latestUpdate, progress.Report)` (Velopack v3 API)
  - Throws on failure (network loss, corrupt download, etc.)
- `ApplyUpdatesAndRestart()`:
  - Call `_updateManager.ApplyUpdatesAndRestart(_latestUpdate)` — terminates process

#### 3. `src/LaTeXInserter/Models/UpdateCheckResult.cs`

```csharp
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
```

#### 4. `.github/workflows/release.yml`

Tag-triggered CI pipeline:
- **Trigger:** `push tags: v*`
- **Runner:** `windows-latest`
- **Steps:**
  1. Checkout code
  2. Setup .NET 10 SDK
  3. `dotnet publish src/LaTeXInserter -c Release -r win-x64` (Native AOT)
  4. Install Velopack CLI: `dotnet tool install --global vpk`
  5. `vpk pack --packId LaTeXInserter --packVersion ${{ github.ref_name }} --packDir <publish-dir> --format nsis`
  6. `vpk upload github --repoUrl https://github.com/lsutorus/LaTeX-Inserter-CS --token ${{ secrets.GITHUB_TOKEN }} --releaseName ${{ github.ref_name }}`
  7. Compute SHA256 of Setup.exe: `Get-FileHash` → upload `.sha256` as release asset via `gh release upload`

Note: Verify NSIS toolchain availability on runner. Velopack usually fetches it, but add `choco install nsis` as fallback if first CI run fails.

---

### Modified Files (5)

#### 5. `src/LaTeXInserter/LaTeXInserter.csproj`

Add Velopack NuGet reference:
```xml
<PackageReference Include="Velopack" Version="0.0.xxx" /> <!-- latest stable -->
```

#### 6. `src/LaTeXInserter/Program.cs`

Changes:
1. Add `using Velopack;` at top
2. Replace Phase 6 placeholder comment as very first line:
   ```csharp
   VelopackApp.Build().Run();
   ```
3. Add `IUpdateService` → `UpdateService` singleton registration in `ConfigureServices`:
   ```csharp
   services.AddSingleton<IUpdateService, UpdateService>();
   ```

#### 7. `src/LaTeXInserter/ViewModels/UpdateViewModel.cs`

Add property:
```csharp
[ObservableProperty]
private bool _hasError;
```

No other changes. `InstallRequested`/`LaterRequested` events already exist.

#### 8. `src/LaTeXInserter/Views/UpdateDialog.axaml`

Change "Later" button visibility from:
```xml
IsVisible="{Binding !IsDownloading}"
```
To:
```xml
IsVisible="{Binding !IsDownloading || HasError}"
```

This uses Avalonia's compiled binding — may need an `Any` converter or restructure as:
```xml
<StackPanel Orientation="Horizontal" HorizontalAlignment="Center" Spacing="12">
    <Button Content="Install Update" Click="OnInstallClick"
            Classes="dialog-install"
            IsVisible="{Binding !IsDownloading}" />
    <Button Content="Later" Click="OnLaterClick"
            Classes="dialog-later"
            IsVisible="{Binding !IsDownloading, Converter=...}" />
</StackPanel>
```

If Avalonia compiled bindings don't support `||` directly, use a computed VM property:
```csharp
// In UpdateViewModel
public bool IsLaterVisible => !IsDownloading || HasError;
```
And bind `IsVisible="{Binding IsLaterVisible}"`.

#### 9. `src/LaTeXInserter/ViewModels/AppManager.cs`

Changes:

**Constructor:** Add `IUpdateService updateService` parameter, store as `_updateService`.

**`InitializeAsync`:**
- Remove "clean temp download dir" (Velopack owns this lifecycle)
- Subscribe to `_updateViewModel.InstallRequested += OnInstallRequested`

**`OnCheckForUpdatesRequested`** — rewrite from `ShowUpToDateDialog()` to async flow:
```csharp
private async void OnCheckForUpdatesRequested(object? sender, EventArgs _)
{
    var result = await _updateService.CheckForUpdatesAsync();

    if (result.IsError)
    {
        _upToDateViewModel.VersionText = "Unable to Check for Updates";
        _upToDateViewModel.SubtitleText = result.ErrorMessage ?? "Unknown error";
        ShowUpToDateDialog();
    }
    else if (result.IsUpdateAvailable)
    {
        _updateViewModel.HeadingText = $"Version {result.Version} is Available";
        _updateViewModel.SubtitleText = $"Current: v{currentVersion}";
        _updateViewModel.ChangelogText = result.ReleaseNotes ?? string.Empty;
        _updateViewModel.IsDownloading = false;
        _updateViewModel.DownloadProgress = 0;
        _updateViewModel.StatusText = string.Empty;
        _updateViewModel.HasError = false;
        ShowUpdateDialog();
    }
    else
    {
        _upToDateViewModel.VersionText = "You are running the latest version";
        _upToDateViewModel.SubtitleText = $"v{currentVersion}";
        ShowUpToDateDialog();
    }
}
```

**`OnInstallRequested`** — new handler:
```csharp
private async void OnInstallRequested(object? sender, EventArgs _)
{
    if (_updateViewModel.IsDownloading) return;

    _updateViewModel.IsDownloading = true;
    _updateViewModel.HasError = false;
    _updateViewModel.StatusText = "Downloading update...";

    try
    {
        var progress = new Progress<int>(p =>
        {
            _updateViewModel.DownloadProgress = p;
        });

        await _updateService.DownloadUpdatesAsync(progress);

        _updateViewModel.StatusText = "Installing and restarting...";
        _updateService.ApplyUpdatesAndRestart();
    }
    catch (Exception ex)
    {
        _updateViewModel.IsDownloading = false;
        _updateViewModel.HasError = true;
        _updateViewModel.StatusText = $"Download failed: {ex.Message}";
    }
}
```

**`Dispose`:** Unsubscribe `_updateViewModel.InstallRequested -= OnInstallRequested`.

---

## Local Testing Procedure

Validate entire update flow without pushing anything to GitHub:

1. **Temporarily** change `UpdateService` to use local directory:
   ```csharp
   _updateManager = new UpdateManager(@"C:\path\to\local\Releases");
   // _updateManager = new UpdateManager(new GithubSource("lsutorus/LaTeX-Inserter-CS"));
   ```

2. Leave `.csproj` at `0.0.1`. Run `vpk pack` → install the resulting Setup.exe.

3. Change `.csproj` to `0.0.2`. Run `vpk pack` again → generates v2 packages, updates releases index.

4. Run installed v1 app → "Check for Updates" → finds v2 locally → entire AppManager/UI flow runs.

5. Once verified, revert `UpdateService` back to `GithubSource` and commit.

---

## Out of Scope

- Manual temp download directory cleanup (Velopack owns this)
- `TrayIconViewModel` changes (event already exists)
- `UpToDateViewModel` / `UpToDateDialog` structural changes (AppManager sets text properties differently, no new members)
- Download cancellation (Velopack downloads are atomic, no corruption risk)
- Retry mechanism on download failure (user can re-check from tray)
- `LaterRequested` subscription in AppManager (code-behind + Closed event sufficient)
