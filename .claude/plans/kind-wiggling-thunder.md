# Architecture Cleanup Plan

## Context

AppManager is a shallow wiring layer with 12 constructor deps and 13 event subscriptions. Several single-method service interfaces exist solely for DI pass-through. Platform logic leaks into Views. The accent color flow touches 7 files. Startup sync logic is duplicated. HotkeyBlocklist has a layering violation (Model→Service).

8 candidates identified. Phased implementation, verified commit after each phase.

**Branch:** `refactor/architecture-cleanup` (create from `master`)

---

## Phase 1: Candidate 2 (SubmitPasteModule) then Candidate 7 (IOverlayPositioner)

### Step 1a: Create `ISubmitPasteService` + `SubmitPasteService`

**New files:**
- `src/LaTeXInserter/Abstractions/ISubmitPasteService.cs`
- `src/LaTeXInserter/Services/SubmitPasteService.cs`

**Interface:**
```csharp
public interface ISubmitPasteService
{
    event EventHandler? OverlayHideRequested;
    Task ExecuteAsync(string convertedText);
}
```

**Implementation:**
- Constructor: `IClipboardProvider`, `IInputSimulatorService`, `IWindowActivator`, `int pasteDelayMs = 50`
- `ExecuteAsync(string)`: clipboard set → window restore → raise `OverlayHideRequested` → delay → simulate paste
- Absorbs ordering/timing logic from `AppManager.OnSubmitRequested` (lines 302-325)

### Step 1b: Modify AppManager

- Add `ISubmitPasteService` constructor dep
- `OnSubmitRequested` → call `_submitPasteService.ExecuteAsync(convertedText)`
- Subscribe to `_submitPasteService.OverlayHideRequested` → call `HideOverlay()`
- Keep `IClipboardProvider`, `IInputSimulatorService` deps for now (other code may still need them) — but they no longer appear in the submit flow

### Step 1c: Register in DI

- `Program.cs`: register `ISubmitPasteService` as singleton with `IClipboardProvider`, `IInputSimulatorService`, `IWindowActivator` deps

### Step 1d: Verify — build + run, test submit-paste flow

---

### Step 2a: Create `IOverlayPositioner` + `WindowsOverlayPositioner`

**New files:**
- `src/LaTeXInserter/Abstractions/IOverlayPositioner.cs`
- `src/LaTeXInserter/Platform/Windows/WindowsOverlayPositioner.cs`

**Interface:**
```csharp
public interface IOverlayPositioner
{
    void PositionOverlay(Window window);
}
```

**Implementation (WindowsOverlayPositioner):**
- Constructor: `IWindowActivator`
- `PositionOverlay(Window window)`:
  - Get cursor pos via `NativeMethods.GetCursorPos`
  - Get screen via `window.Screens.ScreenFromPoint`
  - Compute position using `OverlayPositioner.GetPosition` (stays as static helper, called internally)
  - Set `window.Position`
  - If on Windows: get platform handle, call `_windowActivator.Activate(handle)`

### Step 2b: Modify OverlayWindow

- Replace `public IWindowActivator? WindowActivator { get; set; }` with `public IOverlayPositioner? OverlayPositioner { get; set; }`
- `OnOpened` → call `OverlayPositioner?.PositionOverlay(this)`
- `OnPropertyChanged(IsVisible)` → call `OverlayPositioner?.PositionOverlay(this)` instead of inline cursor/position/activate logic
- Remove direct `NativeMethods.GetCursorPos` call, `OverlayPositioner.GetPosition` call, and `WindowActivator.Activate` call from code-behind
- Keep `Opacity` fade logic in code-behind (it's a visual concern)

### Step 2c: Modify AppManager

- `ShowOverlay()` → set `_overlayWindow.OverlayPositioner = _overlayPositioner` instead of `WindowActivator`
- Add `IOverlayPositioner` constructor dep
- `IWindowActivator` stays in AppManager (still needed for `CapturePrevious()` + `Restore()` in submit-paste and show overlay)

### Step 2d: Register in DI

- `Program.cs`: register `IOverlayPositioner` → `WindowsOverlayPositioner` with `IWindowActivator` dep

### Step 2e: Verify — build + run, test overlay positioning on different screen edges

**Commit:** `refactor: extract SubmitPasteService and IOverlayPositioner (Phase 1)`

---

## Phase 2: Candidate 1 (UpdateCoordinator) then Candidate 4 (AccentColorModule)

### Step 1a: Create `IUpdateCoordinator` + `UpdateCoordinator`

**New files:**
- `src/LaTeXInserter/Abstractions/IUpdateCoordinator.cs`
- `src/LaTeXInserter/ViewModels/UpdateCoordinator.cs` (lives in ViewModels — it owns VMs + dialog lifecycle, same layer as AppManager)

**Interface:**
```csharp
public interface IUpdateCoordinator
{
    Task CheckForUpdatesAsync();
    Task InstallUpdateAsync();
}
```

**Implementation:**
- Constructor: `IUpdateService`, `UpToDateViewModel`, `UpdateViewModel`
- `CheckForUpdatesAsync()`: moves logic from `AppManager.OnCheckForUpdatesRequested` (lines 138-173). Manages VM property mutations + dialog lifecycle internally.
- `InstallUpdateAsync()`: moves logic from `AppManager.OnInstallRequested` (lines 177-203). Progress reporting, download, apply.
- Exposes `ShowUpToDateDialog()` / `ShowUpdateDialog()` internally (dialog window lifecycle moves here)

### Step 1b: Modify AppManager

- Add `IUpdateCoordinator` constructor dep
- Remove `IUpdateService`, `UpToDateViewModel`, `UpdateViewModel` constructor deps
- `OnCheckForUpdatesRequested` → call `_updateCoordinator.CheckForUpdatesAsync()`
- `OnInstallRequested` → call `_updateCoordinator.InstallUpdateAsync()`
- Remove `ShowUpToDateDialog()`, `ShowUpdateDialog()`, `_activeUpToDateDialog`, `_activeUpdateDialog` fields
- Dispose: close update dialogs via coordinator (add `Dispose()` to coordinator if needed)

### Step 1c: Register in DI

- `Program.cs`: register `IUpdateCoordinator` as singleton

### Step 1d: Verify — build + run, test update check flow

---

### Step 2a: Create `IAccentColorModule` + `AccentColorModule`

**New files:**
- `src/LaTeXInserter/Abstractions/IAccentColorModule.cs`
- `src/LaTeXInserter/Services/AccentColorModule.cs`

**Interface:**
```csharp
public interface IAccentColorModule
{
    event EventHandler<string>? AccentColorApplied;
    void Apply(string hex);
}
```

**Implementation:**
- Constructor: `ISettingsService`
- `Apply(string hex)`:
  - Parse hex → set `App.Current.Resources["AccentBgBrush"]` (replaces `App.ApplyAccentColor()` static)
  - Persist to settings via `_settingsService`
  - Raise `AccentColorApplied` event with hex
- `App.ApplyAccentColor()` static method deleted

### Step 2b: Modify OverlayViewModel

- Add `IAccentColorModule` constructor dep
- Subscribe to `_accentColorModule.AccentColorApplied` in constructor → call `UpdateBrushes()` with the new hex
- `ApplySettings()` → remove `App.ApplyAccentColor(settings.AccentColor)` call
- `UpdateBrushes()` → make it accept optional hex param (or just read from `AccentColor` property which gets set before `UpdateBrushes` is called)

### Step 2c: Modify SettingsViewModel

- Add `IAccentColorModule` constructor dep
- `SelectSwatch()` → call `_accentColor.Apply(hex)` instead of firing `AccentColorChanged` event
- Remove `AccentColorChanged` event from SettingsViewModel

### Step 2d: Modify SettingsWindow code-behind

- `OnSwatchClick` → calls `vm.SelectSwatch(swatch)` (unchanged API, but now it triggers `IAccentColor.Apply` internally instead of firing event)
- Remove `AccentColorChanged` subscription from code-behind if present

### Step 2e: Clean up App

- Delete `App.ApplyAccentColor()` static method
- Remove any `AccentColorChanged` event wiring in AppManager

### Step 2f: Register in DI

- `Program.cs`: register `IAccentColorModule` → `AccentColorModule`

### Step 2g: Verify — build + run, test accent color change (swatch click + save + verify overlay updates)

**Commit:** `refactor: extract UpdateCoordinator and AccentColorModule (Phase 2)`

---

## Phase 3: Candidates 5, 8, 6 (in that order)

### Step 1: Fix HotkeyBlocklist layering (Candidate 5)

- Inline `HotkeyNormalizer.Normalize()` 3-line logic into `HotkeyBlocklist.CreateBlocklist()` — just sort modifiers on each `HotkeyChord`
- Remove `using LaTeXInserter.Services;` from `HotkeyBlocklist.cs`
- Keep `HotkeyNormalizer.CollapseModifiers()` in Services (different concern, used by hotkey service)
- Verify: build, ensure no compile errors

### Step 2: Add `SyncRegistrationAsync` to `IStartupRegistrar` (Candidate 8)

- Add `Task SyncRegistrationAsync(bool desired)` to `IStartupRegistrar`
- Implement in `WindowsStartupRegistrar`: call `GetIsRegisteredAsync()`, compare, conditionally register/unregister
- Update `AppManager.InitializeAsync()` lines 97-109 → call `_startupRegistrar.SyncRegistrationAsync(settings.StartOnStartup)`
- Update `SettingsViewModel.SaveAsync()` → call `_startupRegistrar.SyncRegistrationAsync(settings.StartOnStartup)` instead of inline sync logic
- Verify: build, test startup toggle

### Step 3: Remove TrayIconViewModel dead dependency (Candidate 6)

- Remove `ILatexConverterService _latexConverter` field and constructor param from `TrayIconViewModel`
- Update `Program.cs` DI registration
- Verify: build

**Commit:** `refactor: fix HotkeyBlocklist layering, deduplicate startup sync, remove dead dep (Phase 3)`

---

## Verification

After each phase:
1. `dotnet build LaTeXInserter.sln` — must pass with zero errors
2. `dotnet run --project src/LaTeXInserter` — manual smoke test
3. Phase-specific checks noted inline above

Final state: AppManager goes from 12 deps → ~7 deps, 13 events → ~7 events. 3 shallow interfaces become internal seams. Platform logic leaves Views. Accent color is 1 module instead of 7 files. Startup sync has 1 implementation. No layering violations.
