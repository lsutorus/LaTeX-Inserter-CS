# Migration Plan: Python → C# (.NET 10) Avalonia

Rewrite of LaTeX Inserter from Python/PyQt5 to C#/.NET 10 with Avalonia UI.

## Architecture Decisions (grilled & locked)

| # | Decision | Choice | Rationale |
|---|----------|--------|-----------|
| 1 | LaTeX parser | Hand-written recursive descent (zero dep) | Regex can't handle nested `{}`; ANTLR overkill for lookup-based grammar |
| 2 | Platform scope | Windows-first, abstract behind interfaces | Ship Windows, architecture ready for macOS/Linux later |
| 3 | Clipboard | Avalonia TopLevel.Clipboard API | Cross-platform built-in; only paste-key simulation OS-aware |
| 4 | Paste key | SharpHook EventSimulator + `RuntimeInformation.IsOSPlatform` | Ctrl+V (Win) vs Cmd+V (macOS) — no separate interface needed |
| 5 | Default commands | `Commands.json` as EmbeddedResource, loaded via `JsonSerializerContext` source-gen | Native AOT compatible; no reflection-based serialization |
| 6 | Custom mappings | Plain text `\command char` at `Environment.SpecialFolder.ApplicationData` | Human-readable, user-editable by design |
| 7 | Window activation | `AttachThreadInput` + `SetForegroundWindow` behind `IWindowActivator` | Only reliable way to steal focus on Windows; isolated behind interface |
| 8 | Win32 P/Invoke | `[LibraryImport]` + partial methods | Native AOT source-generated marshalling; no legacy `[DllImport]` |
| 9 | Hotkey model | `record HotkeyChord(ModifierMask Modifiers, KeyCode TriggerKey)` with `[Flags]` enum | Typed, efficient bitwise ops; `JsonStringEnumConverter` for readable JSON |
| 10 | Velopack backend | GitHub Releases via `UpdateManager.GitHub()` | Matches current deployment; no infra needed |
| 11 | Update UI | Custom Avalonia dialogs (`UpToDateDialog`, `UpdateDialog`) | Matches app aesthetic; Velopack is backend only |
| 12 | DI | Microsoft.Extensions.DependencyInjection, strict constructor injection | .NET standard, AOT-compatible, no service locator |
| 13 | View/VM wiring | Resolve VMs at composition root, assign to `DataContext` | No service locator in views |
| 14 | Autocomplete | TextBox + Popup + ListBox (IntelliSense pattern) | `AutoCompleteBox` risks replacing prefix text in multi-command input |
| 15 | Autocomplete keys | Tab = commit autocomplete, Enter = commit autocomplete OR submit for paste | Prefix text preserved; dual Enter behavior |
| 16 | Hotkey recording | Single hook, flag-based dispatch | No hook teardown; recording flag switches callback behavior |

---

## Phase 1: Foundation & Data Layer

- [ ] Initialize Avalonia UI project (headless, no default main window) targeting .NET 10
- [ ] Add `LaTeXInserter.sln` at repo root linking src + test projects
- [ ] Add NuGet refs: `Avalonia`, `Avalonia.Desktop`, `CommunityToolkit.Mvvm`, `System.Text.Json`
- [ ] Create `Assets/Commands.json` — extract 2,566-command dict from unicodeitplus into JSON
- [ ] Set `Commands.json` as `<EmbeddedResource>` in `.csproj`
- [ ] Create `JsonContext : JsonSerializerContext` with `[JsonSerializable]` for `Dictionary<string, string>`
- [ ] Create `SettingsService` — read/write `settings.json` at `Environment.SpecialFolder.ApplicationData/LaTeX Inserter/`
- [ ] Create `LatexConverterService` — hand-written recursive descent parser
  - [ ] Port Lark grammar semantics: `start`, `atom`, `CHARACTER`, `ESCAPED`, `COMMAND`, `group`, `math`, `SUBSCRIPT`, `SUPERSCRIPT`
  - [ ] Use `ReadOnlySpan<char>` for char-by-char iteration
  - [ ] Handle nested groups `{...}` recursively (e.g. `x^{\alpha_{i}}`)
  - [ ] Handle `HAS_ARG` set (45 commands: `\sqrt`, `\hat`, etc.) — these consume the next `{group}`
  - [ ] Graceful fallback: malformed LaTeX → output raw text, no exceptions
  - [ ] Load default commands from embedded JSON via `JsonSerializerContext`
  - [ ] Load custom mappings from plain text file, merge (custom overrides built-in)
  - [ ] Merge `HAS_ARG` set: custom mappings with `{` in key auto-added
- [ ] Create `Models/HotkeyChord.cs` — `record HotkeyChord(ModifierMask Modifiers, KeyCode TriggerKey)`
- [ ] Add `[Flags] enum ModifierMask` with `JsonStringEnumConverter` for readable JSON
- [ ] Write unit tests for `LatexConverterService` — nested groups, unknown commands, escapes, malformed input

## Phase 2: Global Hotkey & Input Engine (SharpHook)

- [ ] Add NuGet ref: `SharpHook`
- [ ] Create `HotkeyService`
  - [ ] Subscribe to `SimpleGlobalHook` keyboard events (single hook, flag-based dispatch)
  - [ ] Normal mode: match key events against registered `HotkeyChord`, fire event/callback
  - [ ] Recording mode: accumulate pressed keys into a candidate `HotkeyChord`
  - [ ] Thread-safety: hook callback runs on background thread → dispatch to UI thread via `Dispatcher.UIThread.Post`
- [ ] Create `HotkeyNormalizer` — collapses left/right modifiers, sorts: Ctrl → Alt → Shift → Windows, then non-modifiers alphabetically
- [ ] Create `HotkeyBlocklist` — `FrozenSet<HotkeyChord>` with 32 Windows-reserved combos (port from Python)
  - [ ] System-critical: Ctrl+Alt+Delete, Ctrl+Shift+Escape
  - [ ] Alt combos: Alt+Tab, Alt+Shift+Tab, Alt+F4, Alt+Space, Alt+Escape
  - [ ] Ctrl combos: Ctrl+Escape, Ctrl+C, Ctrl+V, Ctrl+X, Ctrl+Z, Ctrl+A
  - [ ] Win combos: Win+Tab, Win+L, Win+D, Win+E, Win+R, Win+I, Win+S, Win+A, Win+P, Win+V, Win+X, Win+G, Win+M, Win+Shift+S, Win+Ctrl+D, Win+Ctrl+F4, Win+Ctrl+Left, Win+Ctrl+Right, Win+Up, Win+Down, Win+Left, Win+Right
  - [ ] On startup: validate stored hotkey against blocklist; silently reset to default if blocked
- [ ] Create `InputSimulatorService`
  - [ ] Use Avalonia `TopLevel.Clipboard` API to write Unicode text to clipboard
  - [ ] Use SharpHook `IEventSimulator.SimulateKeyPress` / `SimulateKeyRelease` for paste
  - [ ] `RuntimeInformation.IsOSPlatform` check: Ctrl+V on Windows, Cmd+V on macOS
- [ ] Create `Platform/IWindowActivator.cs` interface
- [ ] Create `Platform/IStartupRegistrar.cs` interface
- [ ] Implement `WindowsWindowActivator.cs` — `AttachThreadInput` + `SetForegroundWindow` pattern
  - [ ] Use `[LibraryImport]` + partial methods for all Win32 P/Invoke (no `[DllImport]`)
  - [ ] Store previously-active `IntPtr` window handle
  - [ ] Restore focus to previous window before simulating paste
- [ ] Implement `WindowsStartupRegistrar.cs` — Windows Registry startup entry
- [ ] Write unit tests for `HotkeyNormalizer` and `HotkeyBlocklist`

## Phase 3: System Tray Lifecycle

- [ ] Configure `App.axaml` to use `Avalonia.Controls.TrayIcon`
- [ ] Create `TrayIconViewModel` (CommunityToolkit.Mvvm `ObservableObject`)
  - [ ] Commands: ShowHideOverlay, EditMappings, ReloadMappings, ChangeHotkey, CheckForUpdates, Quit
  - [ ] Dynamic hotkey label: "Show/Hide Overlay (Ctrl+Alt+M)" updates on hotkey change
  - [ ] Startup toggle: checkable menu item, persisted via `SettingsService`
- [ ] Wire tray menu actions to `TrayIconViewModel` via `RelayCommand`
- [ ] Lifecycle: prevent GC of menu items — store as fields on ViewModel (same pattern as Python `self.*`)
- [ ] Create `AppManager` orchestrator — coordinates services, tray, overlay
- [ ] On startup: validate stored hotkey against blocklist, reset if blocked
- [ ] On startup: clean temp download dir from previous updates
- [ ] On quit: unhook all SharpHook listeners

## Phase 4: The Avalonia Popup UI

- [ ] Create `OverlayWindow.axaml` — borderless, translucent, topmost, frameless
  - [ ] Set `WindowStyle="None"`, `SystemDecorations="None"`, `Topmost="True"`
  - [ ] Window icon from `LaTeX-Inserter-icon-final.ico`
  - [ ] Semi-transparent dark background `#2b2b2b` (match Python overlay)
- [ ] Create `OverlayViewModel`
  - [ ] `InputText` (bound to TextBox)
  - [ ] `PreviewText` (Unicode preview, updated on every keystroke via `LatexConverterService`)
  - [ ] `AutocompleteItems` — `ObservableCollection<string>` filtered by trailing `\word`
  - [ ] `IsAutocompleteOpen` — controls Popup visibility
  - [ ] `AutocompleteSelectedItem` — tracks ListBox selection
- [ ] Smart window positioning (port from Python):
  - [ ] Cursor position as default top-left corner
  - [ ] Flip right if overflows right edge
  - [ ] Flip bottom if overflows bottom edge
  - [ ] Clamp to screen bounds
- [ ] Auto-focus TextBox on window show
- [ ] Focus-stealing: call `IWindowActivator.Activate()` on show (Win32 `AttachThreadInput` pattern)
- [ ] Keyboard routing in overlay:
  - [ ] Escape → hide window
  - [ ] Tab (popup open) → commit autocomplete, preserve prefix
  - [ ] Enter (popup open) → commit autocomplete, preserve prefix
  - [ ] Enter (popup closed) → convert → clipboard → hide → activate previous window → simulate paste
- [ ] Create `UpToDateDialog.axaml` — port themed frameless dialog
  - [ ] Bold heading "You are running the latest version"
  - [ ] Subtitle with current version
  - [ ] Green OK button
- [ ] Create `UpdateDialog.axaml` — port themed frameless dialog
  - [ ] Bold heading "Version X.Y.Z is available"
  - [ ] Subtitle with current version
  - [ ] Changelog rendered as markdown with orange links
  - [ ] Blue "Install Update" button + "Later" button
  - [ ] Progress bar + status label

## Phase 5: Change Hotkey Dialog & Settings Validation

- [ ] Create `HotkeyDialogWindow.axaml` — themed frameless dialog for recording
- [ ] Create `HotkeyDialogViewModel`
  - [ ] On open: set `HotkeyService.IsRecording = true` (flag-based dispatch)
  - [ ] Accumulate key presses into candidate `HotkeyChord`
  - [ ] Show recorded keys in real-time
  - [ ] On accept: validate against `HotkeyBlocklist`; if blocked, show warning + re-record
  - [ ] On accept (valid): save to `settings.json` via `SettingsService`, re-register hotkey in `HotkeyService`
  - [ ] On reject/close: restore previous hotkey in `HotkeyService`, set `IsRecording = false`
  - [ ] `finally` pattern: recording flag always reset, hotkey always re-registered
- [ ] Minimum chord: ≥1 modifier + 1 non-modifier key
- [ ] Port the complete blocklist validation (32 entries)

## Phase 6: Velopack & Packaging

- [ ] Add NuGet ref: `Velopack`
- [ ] Call `VelopackApp.Build().Run()` at absolute start of `Program.cs`, before any Avalonia init
- [ ] Create `UpdateService`
  - [ ] `UpdateManager.GitHub("lsutorus/LaTeX-Inserter")` for update checks
  - [ ] try/catch wrap: no internet / API down → show "Unable to check for updates" in dialog, no crash
  - [ ] `Dispatcher.UIThread.Post` for all progress bar / status label updates during download
- [ ] Wire tray "Check for Updates" → `UpdateService.CheckForUpdatesAsync()`
  - [ ] Up to date → show `UpToDateDialog`
  - [ ] Update available → show `UpdateDialog`
  - [ ] Install clicked → `DownloadUpdatesAsync()` (progress via `Dispatcher.UIThread`) → `ApplyUpdatesAndRestart()`
- [ ] Configure Velopack build pipeline
  - [ ] `vpk pack` for Windows (NSIS or default Velopack installer format)
  - [ ] `vpk upload github` to publish to GitHub Releases
  - [ ] Release assets: Velopack bundle + SHA256
- [ ] Create `.github/workflows/release.yml` — tag-triggered CI: build + Velopack pack + upload to GitHub Release
- [ ] Test: downgrade `__version__` locally, run app, verify update UI appears (never push dummy releases)

---

## Architectural Constraints (must follow)

### Native AOT Compatibility
- **No reflection-based JSON** — always use `JsonSerializerContext` source generators
- **No legacy `[DllImport]`** — always use `[LibraryImport]` + partial methods for P/Invoke
- **No runtime reflection** — avoid `Activator.CreateInstance`, `Assembly.Load`, dynamic types

### Dependency Injection
- **Strict constructor injection** — all services/VMs request deps via constructor
- **No service locator** — no global `App.ServiceProvider.GetService()` calls
- **Composition root** — build `IServiceCollection` / `IServiceProvider` at app start
- **View/VM resolution** — resolve VMs from `IServiceProvider` at root, assign to `DataContext`

### Velopack
- **Startup hook** — `VelopackApp.Build().Run()` before any Avalonia init
- **UI thread safety** — `Dispatcher.UIThread.Post` / `InvokeAsync` for Velopack progress updates
- **Graceful degradation** — wrap `UpdateManager` calls in try/catch, show friendly message on failure

### Hotkey System
- **Single hook** — one SharpHook instance, flag-based dispatch (no hook teardown during recording)
- **Constructor injection** — `HotkeyService` receives `SimpleGlobalHook` via DI
- **Blocklist startup check** — validate stored hotkey on launch, silently reset if blocked

### Anti-patterns (from Python CLAUDE.md, still apply)
- **No hotkey polling** — event-driven via SharpHook, no timer-based `IsPressed` loops
- **No local-only menu items** — store all tray menu items as fields on ViewModel (prevent GC)
- **Unregister before recording** — in C# context: set recording flag (same hook, skips matching)
- **Canonical hotkey sort** — deterministic normalization: modifiers first (fixed order), then non-modifiers
- **No shipping loose exes** — release assets = Velopack bundle + sha256 only
- **No `.bak` file swap** — Velopack handles in-place update, no renames
- **No dummy releases** — test update UI by downgrading local version, never push fake tags
