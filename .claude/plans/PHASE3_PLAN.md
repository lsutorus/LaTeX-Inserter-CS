# Phase 3: System Tray Lifecycle — Implementation Plan

All decisions grilled & locked. See grilling summary at bottom.

---

## Files to Create

### 1. `src/LaTeXInserter/ViewModels/TrayIconViewModel.cs`

Plain C# class, **not** `ObservableObject` — no bindable properties needed (menu is code-built, no XAML binding).

**Constructor deps** (injected):
- `IHotkeyService`
- `ISettingsService`
- `ILatexConverterService`
- `IStartupRegistrar`

**Public properties:**
- `NativeMenu TrayMenu { get; }` — built in constructor

**Private fields (menu item references for dynamic updates):**
- `NativeMenuItem _showHideOverlayItem`
- `NativeMenuItem _editMappingsItem`
- `NativeMenuItem _reloadMappingsItem`
- `NativeMenuItem _changeHotkeyItem`
- `NativeMenuItem _startupToggleItem`
- `NativeMenuItem _checkForUpdatesItem`
- `NativeMenuItem _quitItem`
- `bool _isSyncing` — guards startup toggle sync from firing command

**Events (consumed by AppManager):**
- `event EventHandler ShowOverlayRequested`
- `event EventHandler QuitRequested`

**Constructor logic:**
1. Build `NativeMenu` with structure:

```
Show/Hide Overlay (Ctrl+Alt+M)
───────────────────────────────
Edit Custom Mappings
Reload Custom Mappings
───────────────────────────────
Change Hotkey...
───────────────────────────────
Run on Startup  [✓]
───────────────────────────────
Check for Updates...
───────────────────────────────
Quit
```

2. Wire each `NativeMenuItem.Command` to a `[RelayCommand]` method
3. Set `_showHideOverlayItem.Header = $"Show/Hide Overlay ({_hotkeyService.CurrentHotkey})"`
4. `_startupToggleItem.ToggleType = MenuItemToggleType.Checkbox`
5. `_startupToggleItem.IsChecked = false` (default; async sync happens later)
6. Subscribe to `_hotkeyService.HotkeyChanged` → update `_showHideOverlayItem.Header` via `Dispatcher.UIThread.Post`

**Command methods:**
- `ShowHideOverlay` → raises `ShowOverlayRequested`
- `EditMappings` → `Process.Start(new ProcessStartInfo(_settingsService.GetCustomMappingsFilePath()) { UseShellExecute = true })`
- `ReloadMappings` → `_latexConverter.Reload()`
- `ChangeHotkey` → `throw new NotImplementedException("Phase 5")`
- `ToggleStartup` → if `!_isSyncing`: check `_startupToggleItem.IsChecked`, call `_startupRegistrar.RegisterAsync()/UnregisterAsync()`, save to `_settingsService`
- `CheckForUpdates` → `throw new NotImplementedException("Phase 6")`
- `Quit` → raises `QuitRequested`

**Public methods:**
- `void UpdateShowHideLabel(HotkeyChord chord)` — sets `_showHideOverlayItem.Header = $"Show/Hide Overlay ({chord})"`, wraps in `Dispatcher.UIThread.Post`
- `async Task SyncStartupToggleAsync()` — sets `_isSyncing = true`, reads `_startupRegistrar.GetIsRegisteredAsync()`, updates `_startupToggleItem.IsChecked` and `_settingsService`, resets `_isSyncing = false`. All `NativeMenuItem` mutations wrapped in `Dispatcher.UIThread.Post`

---

### 2. `src/LaTeXInserter/ViewModels/AppManager.cs`

Plain singleton service, `IDisposable`, not `ObservableObject`.

**Constructor deps** (injected):
- `ISettingsService`
- `IHotkeyService`
- `IStartupRegistrar`
- `TrayIconViewModel`

**Public methods:**
- `async Task InitializeAsync()` — full startup sequence
- `void Shutdown()` — ordered cleanup + `Application.Current.Shutdown()`
- `void Dispose()` — `IDisposable`, idempotent

**Private fields:**
- `bool _isShutdown`
- `CancellationTokenSource? _hookCts`

**Events (for future Phase 4 use):**
- `event EventHandler OverlayVisibilityChanged`

**Stub methods (Phase 4):**
- `void ToggleOverlay()` — stub
- `void ShowOverlay()` — stub
- `void HideOverlay()` — stub
- `bool IsOverlayVisible { get; private set; }` — `false`, stub

**`InitializeAsync()` sequence:**
1. `LoadSettings()` → `_settingsService.Load()`
2. `ValidateHotkey()` → if `HotkeyBlocklist.IsBlocked(settings.Hotkey)`: reset to `AppSettings.Default.Hotkey`, save via `_settingsService`
3. `_hotkeyService.RegisterHotkey(settings.Hotkey)`
4. `_hookCts = new CancellationTokenSource()` → `_hotkeyService.StartAsync(_hookCts.Token)`
5. `await _trayIconViewModel.SyncStartupToggleAsync()`
6. `CleanupTempUpdateDir()` — stub, returns `Task.CompletedTask`
7. Subscribe to `_trayIconViewModel.ShowOverlayRequested` → `ToggleOverlay()`
8. Subscribe to `_trayIconViewModel.QuitRequested` → `Shutdown()`

**`Shutdown()` logic:**
1. Guard: if `_isShutdown` return; set `_isShutdown = true`
2. `_hookCts?.Cancel()`
3. `_hotkeyService.Dispose()` — unhook SharpHook
4. Save settings via `_settingsService`
5. `Application.Current?.ApplicationLifetime` → cast to `IClassicDesktopStyleApplicationLifetime` → `Shutdown()`

**Safety net:** In `App.axaml.cs`, subscribe to `lifetime.Exit` → call `_appManager.Dispose()` (idempotent).

---

### 3. `src/LaTeXInserter/Models/HotkeyChord.cs` — Modify

Add `ToString()` override to `HotkeyChord` record struct.

**Format:** Canonical Windows order — `Ctrl+Alt+Shift+Win+Key`

Logic:
1. Build `ModifierMask` display parts in order: `Control` → `"Ctrl"`, `Alt` → `"Alt"`, `Shift` → `"Shift"`, `Windows` → `"Win"`
2. Join with `+`
3. Append `+` + `TriggerKey` name (strip `KeyCode.` prefix, convert to display string)
4. Example: `HotkeyChord(ModifierMask.Control | ModifierMask.Alt, KeyCode.M)` → `"Ctrl+Alt+M"`

---

## Files to Modify

### 4. `src/LaTeXInserter/Abstractions/ISettingsService.cs` — Modify

Add method:
```csharp
string GetCustomMappingsFilePath();
```

### 5. `src/LaTeXInserter/Services/SettingsService.cs` — Modify

Implement `GetCustomMappingsFilePath()` — return the path to `custom_mappings.txt` under AppData. Extract the path computation (currently inline in `GetCustomMappingLines()`) into a shared private field/method.

### 6. `src/LaTeXInserter/Abstractions/ILatexConverterService.cs` — Modify

Add method:
```csharp
void Reload();
```

### 7. `src/LaTeXInserter/Services/LatexConverterService.cs` — Modify

Implement `Reload()`:
1. Re-read custom mappings via `_settingsService.GetCustomMappingLines()`
2. Re-merge custom over default commands (same logic as constructor)
3. Rebuild `Commands` dictionary and `CommandNames` list
4. Re-merge `HAS_ARG` set

### 8. `src/LaTeXInserter/App.axaml` — Modify

Add `TrayIcon` shell in XAML:
```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="LaTeXInserter.App"
             RequestedThemeVariant="Dark">

    <Application.Styles>
        <FluentTheme />
    </Application.Styles>

    <TrayIcon.Icons>
        <TrayIcons>
            <TrayIcon Icon="/Assets/LaTeX-Inserter-icon-final.ico"
                      ToolTipText="LaTeX Inserter"
                      x:Name="AppTrayIcon" />
        </TrayIcons>
    </TrayIcon.Icons>
</Application>
```

No `NativeMenu` in XAML — menu is code-built by `TrayIconViewModel`.

### 9. `src/LaTeXInserter/App.axaml.cs` — Modify

**Add static property:**
```csharp
public static IServiceProvider Services { get; private set; } = null!;
```

**`OnFrameworkInitializationCompleted()` logic:**
1. Set `desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown` (already exists)
2. Resolve `TrayIconViewModel` and `AppManager` from `Services`
3. Find `TrayIcon` via `TrayIcon.GetIcons(this)?[0]`
4. Assign `trayIcon.Menu = trayIconViewModel.TrayMenu`
5. Subscribe `lifetime.Exit` → `_appManager.Dispose()` (safety net)
6. Fire `_ = _appManager.InitializeAsync()` (fire-and-forget, with try/catch inside)

### 10. `src/LaTeXInserter/Program.cs` — Modify

**Add DI registrations:**
```csharp
services.AddSingleton<TrayIconViewModel>();
services.AddSingleton<AppManager>();
```

**Before `BuildAvaloniaApp()`:**
```csharp
App.Services = serviceProvider;
```

---

## File Change Summary

| Action | File | What |
|--------|------|------|
| Create | `ViewModels/TrayIconViewModel.cs` | Tray menu commands, NativeMenu builder, events |
| Create | `ViewModels/AppManager.cs` | Lifecycle orchestrator, startup/shutdown |
| Modify | `Models/HotkeyChord.cs` | Add `ToString()` override |
| Modify | `Abstractions/ISettingsService.cs` | Add `GetCustomMappingsFilePath()` |
| Modify | `Services/SettingsService.cs` | Implement `GetCustomMappingsFilePath()` |
| Modify | `Abstractions/ILatexConverterService.cs` | Add `Reload()` |
| Modify | `Services/LatexConverterService.cs` | Implement `Reload()` |
| Modify | `App.axaml` | Add TrayIcon shell (icon + tooltip) |
| Modify | `App.axaml.cs` | Add `Services` static, wire VM/menu, call `InitializeAsync` |
| Modify | `Program.cs` | Register VMs + AppManager, set `App.Services` |

Total: **2 new files, 8 modified files**

---

## Grilling — Locked Decisions

| # | Decision | Choice |
|---|----------|--------|
| 1 | Menu construction | Full code-built `NativeMenu` in C#, bypasses Avalonia bug #8570 |
| 2 | TrayIcon ownership | `App.axaml` shell, `TrayIconViewModel` builds `NativeMenu`, `App.axaml.cs` wires |
| 3 | Menu structure | 7 items + 4 separators, ellipsis on dialog-opening items |
| 4 | TrayIconViewModel deps | `IHotkeyService`, `ISettingsService`, `ILatexConverterService`, `IStartupRegistrar` |
| 5 | VM→AppManager | Events: `ShowOverlayRequested`, `QuitRequested` (breaks circular dep) |
| 6 | AppManager type | Plain service, `IDisposable`, not `ObservableObject` |
| 7 | Startup sequence | `AppManager.InitializeAsync()`: load→validate→register→start hook→sync toggle→cleanup |
| 8 | Startup truth source | OS is truth; settings synced to match OS state on startup |
| 9 | Toggle sync guard | `_isSyncing` flag prevents command re-trigger during init |
| 10 | Shutdown | Ordered `Shutdown()` + lifetime exit safety net (idempotent) |
| 11 | HotkeyChord display | `ToString()` override: Ctrl→Alt→Shift→Win + Key |
| 12 | UI thread safety | All `NativeMenuItem` mutations via `Dispatcher.UIThread.Post` |
| 13 | Service provider access | `App.Services` static, composition-root-only |
| 14 | TrayIconViewModel disposal | No `IDisposable` — singletons share lifetime |
| 15 | Interface additions | `ISettingsService.GetCustomMappingsFilePath()`, `ILatexConverterService.Reload()` |
| 16 | Placeholder commands | Change Hotkey / Check for Updates throw `NotImplementedException` |
| 17 | TrayIcon tooltip | "LaTeX Inserter" |
