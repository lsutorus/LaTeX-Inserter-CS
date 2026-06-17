# Architecture

## Project File Tree

```
latex-inserter-c#/
├── LaTeXInserter.sln
├── MIGRATION_PLAN.md
├── CLAUDE.md
├── README.md
├── .github/
│   └── workflows/
│       └── release.yml
├── docs/
│   └── architecture.md          ← this file
├── src/
│   └── LaTeXInserter/
│       ├── LaTeXInserter.csproj
│       ├── Program.cs            (entry + VelopackApp.Build().Run())
│       ├── App.axaml             (Avalonia app + TrayIcon)
│       ├── App.axaml.cs
│       ├── Assets/
│       │   ├── Commands.json     (embedded resource, 2,566 LaTeX→Unicode mappings)
│       │   └── LaTeX-Inserter-icon-final.ico
│       ├── Services/
│       │   ├── LatexConverterService.cs   (recursive descent parser + merging)
│       │   ├── HotkeyService.cs           (SharpHook single-hook, flag-based dispatch)
│       │   ├── InputSimulatorService.cs   (Avalonia clipboard + SharpHook paste sim)
│       │   ├── SettingsService.cs          (JSON settings read/write)
│       │   └── UpdateService.cs            (Velopack GitHub Releases backend)
│       ├── Platform/
│       │   ├── IWindowActivator.cs         (focus-stealing abstraction)
│       │   ├── IStartupRegistrar.cs        (OS startup registration)
│       │   └── Windows/
│       │       ├── WindowsWindowActivator.cs   (AttachThreadInput + SetForegroundWindow)
│       │       └── WindowsStartupRegistrar.cs  (Windows Registry startup entry)
│       ├── Models/
│       │   ├── HotkeyChord.cs          (record HotkeyChord(ModifierMask, KeyCode))
│       │   ├── HotkeyBlocklist.cs      (FrozenSet<HotkeyChord>, 32 Windows-reserved combos)
│       │   └── JsonContext.cs          ([JsonSerializable] source-gen context)
│       ├── ViewModels/
│       │   ├── AppManager.cs           (orchestrator: services, tray, overlay lifecycle)
│       │   ├── OverlayViewModel.cs     (input, preview, autocomplete, keyboard routing)
│       │   └── TrayIconViewModel.cs    (tray menu commands, dynamic labels)
│       ├── Views/
│       │   ├── OverlayWindow.axaml(.cs)       (borderless topmost popup)
│       │   ├── HotkeyDialogWindow.axaml(.cs)  (recording dialog)
│       │   ├── UpToDateDialog.axaml(.cs)      (themed frameless "up to date")
│       │   └── UpdateDialog.axaml(.cs)        (themed frameless, progress + changelog)
│       └── Converters/                (Avalonia value converters if needed)
└── tests/
    └── LaTeXInserter.Tests/
        └── LaTeXInserter.Tests.csproj
```

## Design Decisions

### MVVM Pattern (CommunityToolkit.Mvvm)

All Views bind to ViewModels via `DataContext`. ViewModels use `ObservableObject`, `RelayCommand`, `[ObservableProperty]` source-gen. No code-behind logic except direct UI concerns (focus management, popup positioning).

**ViewModel responsibilities:**
- `OverlayViewModel`: input text, preview text, autocomplete collection/filter, keyboard routing (Tab/Enter/Escape)
- `TrayIconViewModel`: all tray menu commands as `IRelayCommand`, dynamic hotkey label, startup toggle
- `AppManager`: top-level orchestrator — wires services to VMs, manages overlay show/hide, coordinates hotkey → overlay → paste flow

### Dependency Injection (Microsoft.Extensions.DependencyInjection)

- **Composition root**: `Program.cs` builds `IServiceCollection`, registers all services + VMs
- **Strict constructor injection**: every service/VM declares deps in constructor params
- **No service locator**: no `App.ServiceProvider.GetService()` anywhere
- **View/VM wiring**: resolve VMs from `IServiceProvider` at root, assign to `DataContext`

Registered services:
- `ISettingsService` → `SettingsService` (singleton)
- `ILatexConverterService` → `LatexConverterService` (singleton)
- `IHotkeyService` → `HotkeyService` (singleton)
- `IInputSimulatorService` → `InputSimulatorService` (singleton)
- `IUpdateService` → `UpdateService` (singleton)
- `IWindowActivator` → `WindowsWindowActivator` (singleton, Windows-only for now)
- `IStartupRegistrar` → `WindowsStartupRegistrar` (singleton, Windows-only for now)
- `OverlayViewModel` (singleton)
- `TrayIconViewModel` (singleton)
- `AppManager` (singleton)

### SharpHook (Global Hotkey + Input Simulation)

**Single hook, flag-based dispatch:**
- One `SimpleGlobalHook` instance registered via DI
- `HotkeyService` subscribes to keyboard events
- Normal mode: match events against registered `HotkeyChord`
- Recording mode: accumulate pressed keys into candidate chord
- Thread-safety: hook callback runs on background thread → `Dispatcher.UIThread.Post` to UI thread

**Hotkey model:**
- `record HotkeyChord(ModifierMask Modifiers, KeyCode TriggerKey)`
- `[Flags] enum ModifierMask` — bitwise efficient
- `JsonStringEnumConverter` for human-readable JSON (`"Control, Alt"` not `3`)
- Normalization: collapse left/right modifiers, sort: Ctrl → Alt → Shift → Windows, then non-modifiers

**Paste simulation:**
- `InputSimulatorService` uses Avalanche `TopLevel.Clipboard` for clipboard write
- SharpHook `IEventSimulator.SimulateKeyPress/Release` for paste keystroke
- `RuntimeInformation.IsOSPlatform` toggles Ctrl+V vs Cmd+V

### Velopack (Auto-updater)

- **Backend**: GitHub Releases via `UpdateManager.GitHub("lsutorus/LaTeX-Inserter")`
- **Startup hook**: `VelopackApp.Build().Run()` at absolute start of `Program.cs`, before Avalonia init
- **UI**: Custom Avalonia dialogs (`UpToDateDialog`, `UpdateDialog`), not Velopack's built-in UI
- **Thread safety**: Velopack download runs on background thread → `Dispatcher.UIThread.Post` for progress updates
- **Graceful degradation**: try/catch wraps update checks; no internet → friendly message, no crash
- **Build pipeline**: `vpk pack` (Windows) → `vpk upload github` → tag-triggered CI

### Native AOT Constraints

- **JSON**: `JsonSerializerContext` source generators only — no reflection-based serialization
- **P/Invoke**: `[LibraryImport]` + partial methods only — no legacy `[DllImport]`
- **No runtime reflection**: no `Activator.CreateInstance`, dynamic assemblies, or reflection-based DI

### LaTeX Parser (Recursive Descent)

Hand-written zero-dependency parser replacing the Python Lark LALR parser + `ToUnicode` transformer.

**Grammar semantics (ported from Python):**
- `start`: `(item | math)*`
- `atom`: `CHARACTER` | `COMMAND`
- `CHARACTER`: any char not in `%#&{}^_` (or escaped equivalents)
- `ESCAPED`: `\\`, `\#`, `\%`, `\&`, `\{`, `\}`, `\_`, `\,`
- `group`: `{ item* }`
- `math`: `$ item* $`
- `SUBSCRIPT`: `_`
- `SUPERSCRIPT`: `^`
- `COMMAND`: `\WORD` (optional trailing whitespace)

**HAS_ARG set** (45 commands that consume the next `{group}`):
`\Big`, `\Bigg`, `\LVec`, `\acute`, `\bar`, `\big`, `\breve`, `\check`, `\ddddot`, `\dddot`, `\ddot`, `\dot`, `\grave`, `\hat`, `\left`, `\lvec`, `\mathbb`, `\mathbf`, `\mathbfit`, `\mathcal`, `\mathfrak`, `\mathring`, `\mathrm`, `\mathsf`, `\mathsfbf`, `\mathsfbfit`, `\mathsfit`, `\mathtt`, `\not`, `\overleftrightarrow`, `\overline`, `\right`, `\slash`, `\spddot`, `\sqrt`, `\text`, `\tilde`, `\underbar`, `\underleftarrow`, `\underline`, `\underrightarrow`, `\utilde`, `\vec`, `^`, `_`

**Implementation:**
- `ReadOnlySpan<char>` for char-by-char iteration
- Recursive for nested `{...}` (e.g. `x^{\alpha_{i}}`)
- Malformed input → output raw text, never throw

### Autocomplete (IntelliSense Pattern)

- `TextBox` for input + `Popup` containing `ListBox` above/below
- `Popup.IsOpen` bound to VM boolean
- `ListBox.ItemsSource` bound to `ObservableCollection<string>` filtered by trailing `\word`
- **Keyboard routing:**
  - Up/Down: navigate ListBox
  - Tab: commit autocomplete (replace trailing `\word`, preserve prefix)
  - Enter (popup open): commit autocomplete
  - Enter (popup closed): convert → clipboard → hide → activate previous window → paste
- NOT using `AutoCompleteBox` — it risks replacing prefix text in multi-command input like `x = \alpha + \beta`

### Window Activation (Windows)

`IWindowActivator` interface with `WindowsWindowActivator` implementation:
- Stores previously-active `IntPtr` window handle
- `Activate()`: `AttachThreadInput` + `SetForegroundWindow` + `SetActiveWindow` + `SetFocus`
- `Restore()`: restore focus to previous window before paste
- All Win32 calls via `[LibraryImport]` + partial methods
- Bypasses Windows foreground lock restriction

### Custom Mappings

- Plain text format: `\command Unicode_char` per line, `#` comments
- Path: `Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "/LaTeX Inserter/custom_mappings.txt"`
- Merged over built-in commands at load time
- Lines with `{` in command name auto-added to `HAS_ARG` set
- Override built-ins for same key
