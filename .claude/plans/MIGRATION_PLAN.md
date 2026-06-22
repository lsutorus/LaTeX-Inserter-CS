# Migration Plan: Python (C:\Users\lucsu\Desktop\Dev\Projects\latex-inserter) → C# (.NET 10) Avalonia (current root C:\Users\lucsu\Desktop\Dev\Projects\latex-inserter-c#)

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
| 15 | Autocomplete keys | Tab = commit autocomplete only, Enter = always submit for paste (never commits autocomplete) | Prefix text preserved; Enter previously had dual behavior (commit-or-submit), which caused Enter to consume a fully-typed command as an autocomplete selection instead of submitting — fixed during Phase 4 debugging |
| 16 | Hotkey recording | Single hook, flag-based dispatch | No hook teardown; recording flag switches callback behavior |

---

## Phase 1: Foundation & Data Layer

- [x] Initialize Avalonia UI project (headless, no default main window) targeting .NET 10
- [x] Add `LaTeXInserter.sln` at repo root linking src + test projects
- [x] Add NuGet refs: `Avalonia`, `Avalonia.Desktop`, `CommunityToolkit.Mvvm`, `System.Text.Json`
- [x] Create `Assets/Commands.json` — extract 2,566-command dict from unicodeitplus into JSON
- [x] Set `Commands.json` as `<EmbeddedResource>` in `.csproj`
- [x] Create `JsonContext : JsonSerializerContext` with `[JsonSerializable]` for `Dictionary<string, string>`
- [x] Create `SettingsService` — read/write `settings.json` at `Environment.SpecialFolder.ApplicationData/LaTeX Inserter/`
- [x] Create `LatexConverterService` — hand-written recursive descent parser
  - [x] Port Lark grammar semantics: `start`, `atom`, `CHARACTER`, `ESCAPED`, `COMMAND`, `group`, `math`, `SUBSCRIPT`, `SUPERSCRIPT`
  - [x] Use `ReadOnlySpan<char>` for char-by-char iteration
  - [x] Handle nested groups `{...}` recursively (e.g. `x^{\alpha_{i}}`)
  - [x] Handle `HAS_ARG` set (45 commands: `\sqrt`, `\hat`, etc.) — these consume the next `{group}`
  - [x] Graceful fallback: malformed LaTeX → output raw text, no exceptions
  - [x] Load default commands from embedded JSON via `JsonSerializerContext`
  - [x] Load custom mappings from plain text file, merge (custom overrides built-in)
  - [x] Merge `HAS_ARG` set: custom mappings with `{` in key auto-added
- [x] Create `Models/HotkeyChord.cs` — `record HotkeyChord(ModifierMask Modifiers, KeyCode TriggerKey)`
- [x] Add `[Flags] enum ModifierMask` with `JsonStringEnumConverter` for readable JSON
- [x] Write unit tests for `LatexConverterService` — nested groups, unknown commands, escapes, malformed input

## Phase 2: Global Hotkey & Input Engine (SharpHook)

- [x] Add NuGet ref: `SharpHook` (already in csproj from Phase 1)
- [x] Create `IHotkeyService` interface — `IHotkeyService : IDisposable` with `CurrentHotkey`, `IsRecording`, events
- [x] Create `IClipboardProvider` interface — `SetTextAsync(string)`
- [x] Create `IInputSimulatorService` interface — `SimulatePasteAsync(string)`
- [x] Create `IWindowActivator` interface — `CapturePrevious()`, `Activate(IntPtr)`, `Restore()`
- [x] Create `IStartupRegistrar` interface — `GetIsRegisteredAsync()`, `RegisterAsync()`, `UnregisterAsync()`
- [x] Create `HotkeyNormalizer` — static utility
  - [x] `CollapseModifiers(EventMask)` — strip toggles/mouse/simulated flags, collapse left/right → our `ModifierMask`
  - [x] `Normalize(HotkeyChord)` — canonical sort: Ctrl → Alt → Shift → Windows, then trigger key
- [x] Create `HotkeyBlocklist` — `FrozenSet<HotkeyChord>` with 32 Windows-reserved combos
  - [x] System-critical: Ctrl+Alt+Delete, Ctrl+Shift+Escape
  - [x] Alt combos: Alt+Tab, Alt+Shift+Tab, Alt+F4, Alt+Space, Alt+Escape
  - [x] Ctrl combos: Ctrl+Escape, Ctrl+C, Ctrl+V, Ctrl+X, Ctrl+Z, Ctrl+A
  - [x] Win combos: Win+Tab, Win+L, Win+D, Win+E, Win+R, Win+I, Win+S, Win+A, Win+P, Win+V, Win+X, Win+G, Win+M, Win+Shift+S, Win+Ctrl+D, Win+Ctrl+F4, Win+Ctrl+Left, Win+Ctrl+Right, Win+Up, Win+Down, Win+Left, Win+Right
  - [x] `IsBlocked()` normalizes input before set lookup
- [x] Create `HotkeyService` — SharpHook single-hook, flag-based dispatch
  - [x] Constructor: receive `SimpleGlobalHook`, subscribe to `KeyPressed`/`KeyReleased`
  - [x] Normal mode: exact equality match on collapsed modifiers + trigger key, `e.SuppressEvent = true`, dispatch via `Dispatcher.UIThread.Post`
  - [x] Recording mode: `List<KeyCode>` accumulator, `volatile bool IsRecording`, clear list on `IsRecording = false`
  - [x] `lock(_accumulatorLock)` around all list mutations and chord building
  - [x] `BuildChordFromHeld()`: iterate list, map modifiers via bitwise OR, last non-modifier = trigger
  - [x] `StartAsync`: `Task.Run(() => _hook.RunAsync())` fire-and-forget on thread pool
  - [x] `Dispose`: unhook events, dispose hook
- [x] Create `AvaloniaClipboardProvider` — lazy `TopLevel.GetTopLevel` → `IClipboard.SetTextAsync`
- [x] Create `InputSimulatorService`
  - [x] Constructor: inject `IClipboardProvider` + `IEventSimulator`
  - [x] `SimulatePasteAsync`: clipboard set → left-specific modifier+V key press/release → `Task.Delay(10)` → modifier release
  - [x] `RuntimeInformation.IsOSPlatform`: Ctrl+V (Windows/Linux) vs Cmd+V (macOS)
- [x] Create `NativeMethods.cs` — `[LibraryImport]` + partial methods (no `[DllImport]`)
  - [x] `GetForegroundWindow`, `SetForegroundWindow`, `AttachThreadInput`, `GetWindowThreadProcessId`, `SetFocus`, `SetActiveWindow`, `GetCurrentThreadId`
- [x] Create `WindowsWindowActivator` — `AttachThreadInput` + `SetForegroundWindow` + `SetFocus` pattern
  - [x] Store `_previousWindow` handle, capture on `CapturePrevious()`
  - [x] `Restore()`: re-activate previous window, no-op on zero handle
- [x] Create `WindowsStartupRegistrar` — registry `HKCU\...\Run` with double-quoted exe path
- [x] Register all Phase 2 services in `Program.cs` DI container (all singletons)
  - [x] `SimpleGlobalHook`, `IEventSimulator → EventSimulator`, all 5 service interfaces
- [x] Write unit tests for `HotkeyNormalizer` and `HotkeyBlocklist` — 59/59 passing

## Phase 3: System Tray Lifecycle

- [x] Configure `App.axaml` to use `Avalonia.Controls.TrayIcon`
- [x] Create `TrayIconViewModel` (CommunityToolkit.Mvvm `ObservableObject`)
  - [x] Commands: ShowHideOverlay, EditMappings, ReloadMappings, ChangeHotkey, CheckForUpdates, Quit
  - [x] Dynamic hotkey label: "Show/Hide Overlay (Ctrl+Alt+M)" updates on hotkey change
  - [x] Startup toggle: checkable menu item, persisted via `SettingsService`
- [x] Wire tray menu actions to `TrayIconViewModel` via `RelayCommand`
- [x] Lifecycle: prevent GC of menu items — store as fields on ViewModel (same pattern as Python `self.*`)
- [x] Create `AppManager` orchestrator — coordinates services, tray, overlay
- [x] On startup: validate stored hotkey against blocklist, reset if blocked
- [x] On startup: clean temp download dir from previous updates
- [x] On quit: unhook all SharpHook listeners

## Phase 4: The Avalonia Popup UI

- [x] Create `OverlayWindow.axaml` — borderless, translucent, topmost, frameless
  - [x] Set `WindowDecorations="None"`, `Topmost="True"`
  - [x] Window icon from `LaTeX-Inserter-icon-final.ico`
  - [x] Semi-transparent dark background `#2b2b2b` (match Python overlay)
- [x] Create `OverlayViewModel`
  - [x] `InputText` (bound to TextBox)
  - [x] `PreviewText` (Unicode preview, updated on every keystroke via `LatexConverterService`)
  - [x] `AutocompleteItems` — `ObservableCollection<string>` filtered by trailing `\word`
  - [x] `IsAutocompleteOpen` — controls Popup visibility
  - [x] `AutocompleteSelectedIndex` — tracks ListBox selection
- [x] Smart window positioning (port from Python):
  - [x] Cursor position as default top-left corner
  - [x] Flip right if overflows right edge
  - [x] Flip bottom if overflows bottom edge
  - [x] Clamp to screen bounds
- [x] Auto-focus TextBox on window show
- [x] Focus-stealing: call `IWindowActivator.Activate()` on show (Win32 `AttachThreadInput` pattern)
- [x] Keyboard routing in overlay:
  - [x] Escape → hide window
  - [x] Tab (popup open) → commit autocomplete, preserve prefix, move caret to end
  - [x] Enter (always, regardless of popup state) → set clipboard (while window visible) → restore focus → hide → simulate paste keystroke
- [x] Create `UpToDateDialog.axaml` — port themed frameless dialog
  - [x] Bold heading "You are running the latest version"
  - [x] Subtitle with current version
  - [x] Green OK button
- [x] Create `UpdateDialog.axaml` — port themed frameless dialog
  - [x] Bold heading "Version X.Y.Z is available"
  - [x] Subtitle with current version
  - [x] Changelog rendered as plain text (Phase 6 can add markdown renderer)
  - [x] Blue "Install Update" button + "Later" button
  - [x] Progress bar + status label

## Phase 5: Change Hotkey Dialog & Settings Validation

- [x] Create `HotkeyDialogWindow.axaml` — themed frameless dialog for recording (non-modal, `Topmost="True"`)
- [x] Create `HotkeyDialogViewModel` (Registered as Singleton in DI)
  - [x] Implement `StartRecording()` / `Cleanup()` lifecycle to prevent Singleton startup trap (no global hook hijacking on app launch)
  - [x] On open: set `HotkeyService.IsRecording = true` (flag-based dispatch naturally suspends old hotkey; no explicit unregister/restore needed)
  - [x] Real-time tracking: implement two-layer `_liveChord` (updates instantly) and `_snapshotChord` (locks on valid chord release)
  - [x] Display formatting: gracefully handle partial states and skip `VcUndefined` in `HotkeyChord.ToString()`
  - [x] Live validation: enforce minimum chord (≥1 modifier + 1 non-modifier) and 32-entry `HotkeyBlocklist` in real-time
  - [x] Live feedback: disable Save button and show inline amber/red warning instantly if chord is blocked
  - [x] On accept (Save button): call `RegisterHotkey`, save to `settings.json` via `SettingsService`, fire `CloseRequested`
  - [x] On cancel (Cancel button / bare Escape key): fire `CloseRequested` (explicitly checking bare Escape allows `Shift+Escape` as valid hotkey)
  - [x] Ironclad cleanup: `AppManager` routes the `Window.Closed` event to `Cleanup()` (`IsRecording = false` + unsubscribe) guaranteeing teardown across ALL close paths (Cancel, Escape, Alt+F4)
- [x] AppManager Orchestration: wire `TrayIconViewModel.ChangeHotkeyRequested` to show dialog with re-entrancy guard and `HideOverlay()` integration

## Phase 6: Velopack & Packaging

- [x] Add NuGet ref: `Velopack`
- [x] Call `VelopackApp.Build().Run()` at absolute start of `Program.cs`, before any Avalonia init
- [x] Create `UpdateService`
  - [x] `UpdateManager.GitHub("lsutorus/LaTeX-Inserter")` for update checks
  - [x] CONFIRM: should this point to `lsutorus/LaTeX-Inserter-CS` instead? Repo name elsewhere in this doc is `-CS`; decide deliberately before Phase 6, don't carry forward as a copy-paste artifact — **Confirmed: using `lsutorus/LaTeX-Inserter-CS`**
  - [x] try/catch wrap: no internet / API down → show "Unable to check for updates" in dialog, no crash
  - [x] `Dispatcher.UIThread.Post` for all progress bar / status label updates during download
- [x] Wire tray "Check for Updates" → `UpdateService.CheckForUpdatesAsync()`
  - [x] Up to date → show `UpToDateDialog`
  - [x] Update available → show `UpdateDialog`
  - [x] Install clicked → `DownloadUpdatesAsync()` (progress via `Dispatcher.UIThread`) → `ApplyUpdatesAndRestart()`
- [x] Configure Velopack build pipeline
  - [x] `vpk pack` for Windows (NSIS or default Velopack installer format)
  - [x] `vpk upload github` to publish to GitHub Releases
  - [x] Release assets: Velopack bundle + SHA256
- [x] Create `.github/workflows/release.yml` — tag-triggered CI: build + Velopack pack + upload to GitHub Release
- [ ] Test: push v0.0.1 tag → CI builds release → install app → bump to v0.0.2 → push tag → verify in-app updater detects and downloads update

## Phase 7: macOS Porting & Platform Parity

- [ ] Create `MacosWindowActivator : IWindowActivator`
  - [ ] Implement native macOS window focus capture on hotkey trigger
  - [ ] Implement native focus restoration back to the previously active application bundle before pasting
- [ ] Create `MacosStartupRegistrar : IStartupRegistrar`
  - [ ] Implement programmatic generation of a standard launch agent `.plist` file
  - [ ] Deploy and manage the `.plist` file at `~/Library/LaunchAgents/` to run on user login
- [ ] Implement native macOS cursor tracking
  - [ ] Add `[LibraryImport]` bindings targeting the macOS CoreGraphics framework (e.g., `CGEventSource` or `CGEvent` APIs)
  - [ ] Replace the center-screen fallback in `OverlayWindow.axaml.cs` with the native coordinate lookup
- [ ] Configure conditional DI registration in `Program.cs`
  - [ ] Register `IWindowActivator` and `IStartupRegistrar` dynamically using `RuntimeInformation.IsOSPlatform` checks on startup
- [ ] Verify macOS Security & Privacy setup
  - [ ] Document the necessary macOS permission prompts (Accessibility and Input Monitoring) required for SharpHook to capture global hotkeys and simulate events

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
- **No dummy releases for unit tests** — don't push fake tags to test CI wiring; real end-to-end test flow is: push real release → install → bump version → push new tag → verify in-app updater detects and downloads update