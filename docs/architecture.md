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
│       │   ├── HotkeyNormalizer.cs        (CollapseModifiers for EventMask→ModifierMask, Normalize for HotkeyChord)
│       │   ├── InputSimulatorService.cs   (Avalonia clipboard + SharpHook paste sim)
│       │   ├── SettingsService.cs          (JSON settings read/write)
│       │   ├── SubmitPasteService.cs       (deep module: clipboard→activate→hide→delay→paste pipeline)
│       │   ├── AccentColorModule.cs        (deep module: Apply hex → App resources + persist + event)
│       │   ├── UpdateService.cs            (Velopack GitHub Releases backend)
│       │   ├── Abstractions/
│       │   │   ├── ISubmitPasteService.cs    (ExecuteAsync + OverlayHideRequested event)
│       │   │   ├── IAccentColorModule.cs    (Apply + AccentColorApplied event)
│       │   │   └── IUpdateCoordinator.cs    (CheckForUpdatesAsync + InstallUpdateAsync)
│       ├── Platform/
│       │   ├── IWindowActivator.cs         (focus-stealing abstraction)
│       │   ├── IOverlayPositioner.cs       (PositionOverlay(Window) — cursor pos + screen flip + activate)
│       │   ├── IStartupRegistrar.cs        (OS startup registration + SyncRegistrationAsync)
│       │   └── Windows/
│       │       ├── WindowsWindowActivator.cs   (AttachThreadInput + SetForegroundWindow)
│       │       ├── WindowsOverlayPositioner.cs (cursor pos + OverlayPositioner.GetPosition + IWindowActivator.Activate)
│       │       └── WindowsStartupRegistrar.cs  (Windows Registry startup entry + SyncRegistrationAsync)
│       ├── Models/
│       │   ├── AppSettings.cs          (record: Hotkey, StartOnStartup, InputFontSize, PreviewFontSize, AccentColor, AutocompleteEnabled)
│       │   ├── AutocompleteItem.cs    (record: Command + Unicode, UI-only, no JsonContext)
│       │   ├── HotkeyChord.cs          (record HotkeyChord(ModifierMask, KeyCode))
│       │   ├── HotkeyBlocklist.cs      (FrozenSet<HotkeyChord>, 32 Windows-reserved combos; no Services import)
│       │   ├── AccentSwatchInfo.cs     (ObservableObject: Hex, Brush, IsSelected — settings swatch model)
│       │   ├── MappingItem.cs          (ObservableObject: Command, Character, IsOverride, IsEditing, HasValidationError, DefaultCommand, DefaultCharacter — custom mappings row model)
│       │   └── JsonContext.cs          ([JsonSerializable] source-gen context)
│       ├── ViewModels/
│       │   ├── AppManager.cs           (orchestrator: services, tray, overlay lifecycle, settings/custom mappings window singletons; 9 deps)
│       │   ├── OverlayViewModel.cs     (input, preview, autocomplete, accent brushes via IAccentColorModule, settings binding)
│       │   ├── SettingsViewModel.cs    (editable settings copy, Save/Cancel, uses IStartupRegistrar.SyncRegistrationAsync)
│       │   ├── CustomMappingsViewModel.cs (staged CRUD for custom/default mappings, Save/Cancel/Reload, tab awareness, inline edit commit)
│       │   ├── UpdateCoordinator.cs    (deep module: owns UpToDateVM + UpdateVM + dialog lifecycle, check/download/install flow)
│       │   └── TrayIconViewModel.cs    (tray menu commands, dynamic labels; no ILatexConverterService dep)
│       ├── Views/
│       │   ├── OverlayWindow.axaml(.cs)       (borderless topmost popup)
│       │   ├── SettingsWindow.axaml(.cs)      (native OS chrome, CanResize=false in code-behind, font sizes + accent swatches + autocomplete toggle)
│       │   ├── CustomMappingsWindow.axaml(.cs) (tabbed: Custom Mappings + Default Mappings, ListBox inline edit, bottom button bar, validation)
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
- `OverlayViewModel`: input text, preview text, autocomplete collection/filter, keyboard routing (Tab/Enter/Escape), conversion hints for unresolved commands. Subscribes to `IAccentColorModule.AccentColorApplied` for live accent updates.
- `TrayIconViewModel`: tray menu commands, dynamic hotkey label, fires EditMappingsRequested/SettingsRequested events (no longer opens notepad or calls Reload directly). No `ILatexConverterService` dependency.
- `CustomMappingsViewModel`: staged CRUD for custom and default mapping items, reload on open, write to file + LatexConverterService.Reload() on save, tab-aware button states, inline edit commit tracking, validation
- `SettingsViewModel`: editable settings with hotkey change + startup toggle, Save persists + calls `IStartupRegistrar.SyncRegistrationAsync`, fires SettingsSaved/ChangeHotkeyRequested events. Swatch selection calls `IAccentColorModule.Apply(hex)` directly (no `AccentColorChanged` event).
- `AppManager`: top-level orchestrator — wires services to VMs, manages overlay show/hide, delegates submit-paste to `ISubmitPasteService`, delegates update checks to `IUpdateCoordinator`, settings window singleton, custom mappings window singleton. 9 constructor deps (down from 12 pre-refactor).

### Dependency Injection (Microsoft.Extensions.DependencyInjection)

- **Composition root**: `Program.cs` builds `IServiceCollection`, registers all services + VMs
- **Strict constructor injection**: every service/VM declares deps in constructor params
- **No service locator**: no `App.ServiceProvider.GetService()` anywhere
- **View/VM wiring**: resolve VMs from `IServiceProvider` at root, assign to `DataContext`

Registered services:
- `ISettingsService` → `SettingsService` (singleton)
- `ILatexConverterService` → `LatexConverterService` (singleton)
- `IAccentColorModule` → `AccentColorModule` (singleton; deep module: Apply hex → App resources + persist + event)
- `IHotkeyService` → `HotkeyService` (singleton)
- `IInputSimulatorService` → `InputSimulatorService` (singleton)
- `IUpdateService` → `UpdateService` (singleton)
- `IWindowActivator` → `WindowsWindowActivator` (singleton, Windows-only for now)
- `IStartupRegistrar` → `WindowsStartupRegistrar` (singleton, includes `SyncRegistrationAsync`)
- `ISubmitPasteService` → `SubmitPasteService` (singleton; deep module: clipboard→activate→hide→delay→paste)
- `IOverlayPositioner` → `WindowsOverlayPositioner` (singleton; cursor pos + screen flip + window activate)
- `IUpdateCoordinator` → `UpdateCoordinator` (singleton; deep module: check/download/install + VM + dialog lifecycle)
- `OverlayViewModel` (singleton)
- `TrayIconViewModel` (singleton)
- `SettingsViewModel` (singleton)
- `CustomMappingsViewModel` (singleton)
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
- Normalization: collapse left/right modifiers, sort: Ctrl → Alt → Shift → Windows, then non-modifiers. `HotkeyNormalizer.Normalize()` in Services (used by `HotkeyService`). `HotkeyBlocklist` in Models does NOT import Services — uses record value equality directly.

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
- `ListBox.ItemsSource` bound to `ObservableCollection<AutocompleteItem>` filtered by trailing `\word`
- `AutocompleteItem`: `sealed record(string Command, string Unicode)` — UI-only model, no JsonContext entry. Built on-the-fly from `IConverterService.Commands`
- `ListBox.ItemTemplate`: `DataTemplate` with Grid — Command (left) + Unicode (right, Opacity=0.6). FontSize inherits from ListBox (bound to `InputFontSize`)
- **Accent color selection**: `DynamicResource` key `AccentBgBrush` in `ListBox.Resources`, target `ListBoxItem:selected /template/ ContentPresenter#PART_ContentPresenter`. Code-behind swaps resource on `AccentBackgroundBrush` change (explicit DataContext in DataTemplate can't reach ViewModel)
- **Keyboard routing:**
  - Up/Down: navigate ListBox
  - Tab: commit autocomplete (replace trailing `\word`, preserve prefix)
  - Enter (popup open): commit autocomplete
  - Enter (popup closed): convert → clipboard → hide → activate previous window → paste
- **Autocomplete disabled**: when `IsAutocompleteEnabled = false`, dropdown never opens. Full-convert still works on Enter.
- NOT using `AutoCompleteBox` — it risks replacing prefix text in multi-command input like `x = \alpha + \beta`

### Overlay Window Positioning

`OverlayWindow` code-behind delegates to `IOverlayPositioner` (property-injected by AppManager):
- `OnOpened` → `OverlayPositioner?.PositionOverlay(this)`
- `OnPropertyChanged(IsVisible)` → `OverlayPositioner?.PositionOverlay(this)` when becoming visible
- No direct P/Invoke or `OverlayPositioner.GetPosition` calls in code-behind
- Opacity fade logic remains in code-behind (visual concern)
- Keyboard routing (Tab/Enter/Escape) remains in code-behind

### Window Activation (Windows)

`IWindowActivator` interface with `WindowsWindowActivator` implementation:
- Stores previously-active `IntPtr` window handle
- `Activate()`: `AttachThreadInput` + `SetForegroundWindow` + `SetActiveWindow` + `SetFocus`
- `Restore()`: restore focus to previous window before paste
- All Win32 calls via `[LibraryImport]` + partial methods
- Bypasses Windows foreground lock restriction

### Submit-Paste Pipeline (ISubmitPasteService)

`SubmitPasteService` is a deep module absorbing the 5-step clipboard→activate→hide→delay→paste flow:
1. Set clipboard text via `IClipboardProvider`
2. Restore previous window via `IWindowActivator.Restore()`
3. Raise `OverlayHideRequested` event (AppManager subscribes → `HideOverlay()`)
4. Delay (`pasteDelayMs`, default 50ms) for focus stabilization
5. Simulate Ctrl+V via `IInputSimulatorService`

AppManager's `OnSubmitRequested` just calls `_submitPasteService.ExecuteAsync(convertedText)`.

### Overlay Positioning (IOverlayPositioner)

`WindowsOverlayPositioner` absorbs cursor-position P/Invoke + screen-edge flip + window activation:
1. Get cursor pos via `NativeMethods.GetCursorPos`
2. Determine screen via `window.Screens.ScreenFromPoint`
3. Compute position with `OverlayPositioner.GetPosition` (static helper, internal)
4. Set `window.Position`
5. Activate window via `IWindowActivator.Activate()`

`OverlayWindow` code-behind calls `OverlayPositioner?.PositionOverlay(this)` in `OnOpened` and on `IsVisible` change. No direct P/Invoke calls in the View.

### Update Coordination (IUpdateCoordinator)

`UpdateCoordinator` is a deep module owning the entire check/download/install flow:
- Constructor: `IUpdateService`, `UpToDateViewModel`, `UpdateViewModel`
- Subscribes to `UpdateViewModel.InstallRequested` internally
- `CheckForUpdatesAsync()`: manages VM property mutations + dialog lifecycle (previously inlined in AppManager)
- `InstallUpdateAsync()`: progress reporting, download, apply
- `CloseDialogs()`: public cleanup method for AppManager disposal

AppManager's `OnCheckForUpdatesRequested` just calls `_updateCoordinator.CheckForUpdatesAsync()`.

### Accent Color Module (IAccentColorModule)

`AccentColorModule` is a deep module unifying accent color application:
- `Apply(string hex)`: parse → set `App.Current.Resources["AccentBgBrush"]` → persist to settings via `ISettingsService` → raise `AccentColorApplied` event
- Replaces the old `App.ApplyAccentColor()` static method and scattered event chains
- `OverlayViewModel` injects `IAccentColorModule`, subscribes to `AccentColorApplied` → updates brushes (layer-safe: service does NOT reference ViewModel)
- `SettingsViewModel.SelectSwatch()` calls `_accentColorModule.Apply(swatch.Hex)` directly (no `AccentColorChanged` event)

### Custom Mappings

- Plain text format: `\command Unicode_char` per line, `#` comments
- Path: `Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "/LaTeX Inserter/custom_mappings.txt"`
- Merged over built-in commands at load time
- Lines with `{` in command name auto-added to `HAS_ARG` set
- Override built-ins for same key
- `LatexConverterService.DefaultCommands` exposes pre-merge defaults (for CustomMappingsWindow Tab 2)

### Custom Mappings Window

- **Non-modal singleton**: `AppManager` holds `_activeCustomMappingsWindow`, activates existing if re-clicked (matches SettingsWindow pattern)
- **Tabbed UI**: Tab 1 "Custom Mappings" (user's `custom_mappings.txt` entries), Tab 2 "Default Mappings" (from `Commands.json`, read-only source)
- **Override tracking**: asterisk `*` on rows where custom entry overrides a default
- **Row model**: `MappingItem` (ObservableObject) — `Command`, `Character`, `IsOverride`, `IsEditing`, `HasValidationError`, `DefaultCommand`, `DefaultCharacter`
- **Inline editing**: double-click or Edit button enters edit mode; Enter commits, Escape cancels, Tab moves between fields; click-away commits
- **Validation**: Command must start with `\` and not be empty; invalid → red border via `mapping-error` CSS class; Save disabled during errors
- **Tab 1**: Add (inserts at index 0 with edit mode), Edit, Delete (immediate, Cancel undoes everything)
- **Tab 2**: Edit creates override (writes to `custom_mappings.txt` on Save), Delete removes override (reverts to default), Add disabled, Revert to Default strips all overrides
- **Bottom button bar**: `[Add] [Edit] [Delete] [Revert to Default]` left group, `[Save] [Cancel]` right group
- **Save flow**: overwrites `custom_mappings.txt` with all staged custom entries → calls `LatexConverterService.Reload()` → closes window
- **Cancel**: discards all staged changes, closes window
- **Reload on open**: `CustomMappingsViewModel.Reload()` clears and re-populates from files each time window opens
- **Code-behind**: `DoubleTapped` → enter edit, `KeyDown` (Enter/Escape) → commit/cancel edit, `LostFocus` → commit edit, calls `vm.OnItemEditCommitted()`

### Settings Window

- **Non-modal singleton**: `AppManager` holds `_activeSettingsWindow`, activates existing if re-clicked (matches HotkeyDialog pattern)
- **Native OS chrome**: standard window decorations, `CanResize = false` set in code-behind (Avalonia 12 `ResizeMode` XML attribute not supported with compiled bindings)
- **Layout**: Appearance section (input/preview font size NumericUpDowns, accent color swatch grid) + General section (hotkey display + Change button, autocomplete checkbox, start on startup checkbox)
- **ViewModel**: `SettingsViewModel` — holds editable copy of `AppSettings` with `IHotkeyService` + `IStartupRegistrar` + `IAccentColorModule` deps. Save persists via `SettingsService`, syncs OS startup via `_startupRegistrar.SyncRegistrationAsync()`, fires `SettingsSaved` event. ChangeHotkeyCommand fires `ChangeHotkeyRequested` (routed through AppManager to show HotkeyDialogWindow). Swatch selection calls `_accentColorModule.Apply(hex)` directly (no `AccentColorChanged` event).
- **AppManager orchestration**: `SettingsSaved` → `OverlayViewModel.ApplySettings()` for live reload. `ChangeHotkeyRequested` (from Settings) → same `OnChangeHotkeyRequested` handler (shared with tray). Startup sync on init via `_startupRegistrar.SyncRegistrationAsync()`.
- **Accent color model**: settings store hex string (`"#EF4444"`). `IAccentColorModule.Apply(hex)` sets App resources + persists + raises `AccentColorApplied`. `OverlayViewModel` subscribes to that event and parses hex to two `IBrush` properties:
  - `AccentBrush` — solid color, bound to TextBox `BorderBrush`
  - `AccentBackgroundBrush` — same color at 0.25 opacity, for autocomplete selected item background
- **Swatch rendering**: code-behind `InitializeSwatchColors()` sets `Button.Background = new SolidColorBrush(Color.Parse(hex))` from DataContext on each swatch. `OnSwatchClick` reads hex from `btn.DataContext`, calls `vm.SelectSwatch(swatch)`. Selected swatch gets `accent-selected` CSS class (white border ring). `SettingsViewModel.SelectSwatch()` calls `_accentColorModule.Apply(swatch.Hex)` which raises `AccentColorApplied` → `OverlayViewModel` updates brushes.
- **Swatch palette**: 10 preset colors (Modern Dark UI palette guaranteeing WCAG contrast on #2b2b2b): `#404040`, `#D1D5DB`, `#3B82F6`, `#8B5CF6`, `#EC4899`, `#EF4444`, `#F97316`, `#F59E0B`, `#10B981`, `#06B6D4`
- **Settings persistence**: all settings in single `settings.json`. New fields use C# record defaults — missing fields in old JSON auto-fill, no migration code needed
- **Tray menu items**: Show/Hide Overlay, Settings..., Edit Custom Mappings..., Check for Updates..., Quit (hotkey/startup moved to Settings; Reload Custom Mappings removed — reload happens on Save)
