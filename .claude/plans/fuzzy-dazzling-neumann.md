# Settings Window & Autocomplete Unicode Preview

## Context

Overlay has hardcoded font sizes (input=16, preview=14), hardcoded red accent on TextBox border, and autocomplete dropdown showing only command names with no unicode preview. Users need a settings window to customize these, plus unicode chars shown in autocomplete items.

## New Files

### `src/LaTeXInserter/Models/AutocompleteItem.cs`
```csharp
public sealed record AutocompleteItem(string Command, string Unicode);
```
No JsonContext entry — UI-only model, built on-the-fly from `IConverterService.Commands`.

### `src/LaTeXInserter/ViewModels/SettingsViewModel.cs`
- Injected: `ISettingsService`
- Holds editable copy of `AppSettings` (loaded on construction)
- `[ObservableProperty]` for each editable field: `InputFontSize`, `PreviewFontSize`, `AccentColor`, `AutocompleteEnabled`
- `[RelayCommand] Save` — write to `SettingsService`, fire `SettingsSaved` event, close window
- `[RelayCommand] Cancel` — fire `CloseRequested` event
- Event: `event EventHandler<AppSettings>? SettingsSaved`
- Event: `event EventHandler? CloseRequested`

### `src/LaTeXInserter/Views/SettingsWindow.axaml` + `.cs`
- Native OS chrome (standard window decorations), not frameless
- Title: "Settings", SizeToContent="Height", Width ~350
- Two sections with headers:

**Appearance section:**
- "Input Font Size" — `NumericUpDown` Min=12 Max=24 Step=1, bound to `InputFontSize`
- "Preview Font Size" — `NumericUpDown` Min=12 Max=24 Step=1, bound to `PreviewFontSize`
- "Accent Color" — 10 swatch buttons (palette below), selected one highlighted. Bound to `AccentColor` string
- Palette: `#404040`, `#D1D5DB`, `#3B82F6`, `#8B5CF6`, `#EC4899`, `#EF4444`, `#F97316`, `#F59E0B`, `#10B981`, `#06B6D4`

**Behavior section:**
- "Enable Autocomplete" — `CheckBox`, bound to `AutocompleteEnabled`

**Bottom bar:**
- Cancel button, Save button (Save calls `SaveCommand`)

## Modified Files

### `src/LaTeXInserter/Models/AppSettings.cs`
Add optional record params with defaults:
```csharp
public sealed record AppSettings(
    HotkeyChord Hotkey = default,
    bool StartOnStartup = false,
    int InputFontSize = 16,
    int PreviewFontSize = 14,
    string AccentColor = "#404040",
    bool AutocompleteEnabled = true
)
```
No migration code — `System.Text.Json` uses default values for missing fields.

### `src/LaTeXInserter/ViewModels/OverlayViewModel.cs`
- `AutocompleteItems` → `ObservableCollection<AutocompleteItem>` (was `ObservableCollection<string>`)
- Replace `AutocompleteSelectedIndex` with `[ObservableProperty] AutocompleteItem? _selectedAutocompleteItem`
- Add `[ObservableProperty] int _inputFontSize = 16`
- Add `[ObservableProperty] int _previewFontSize = 14`
- Add `[ObservableProperty] IBrush _accentBrush = new SolidColorBrush(Color.Parse("#404040"))`
- Add `[ObservableProperty] IBrush _accentBackgroundBrush = new SolidColorBrush(Color.Parse("#404040"), 0.25)`
- Add `[ObservableProperty] bool _isAutocompleteEnabled = true`
- New method `ApplySettings(AppSettings settings)` — update all `[ObservableProperty]` fields, call `UpdateBrushes()`
- New method `UpdateBrushes()` — parse `AccentColor`, create `AccentBrush` (solid) and `AccentBackgroundBrush` (0.25 opacity)
- `OnInputTextChanged`: when building autocomplete items, map `_converter.CommandNames` → `AutocompleteItem(name, _converter.Commands[name])`. Skip autocomplete logic entirely when `!IsAutocompleteEnabled`
- `CommitAutocomplete(AutocompleteItem item)` — commit `item.Command` (was `string selectedCommand`)
- `NavigateAutocomplete` — clamp `SelectedAutocompleteItem` by index manipulation on collection
- `GetSelectedAutocompleteItem()` — return `SelectedAutocompleteItem?.Command` (string, for code-behind Tab commit)
- Inject `ISettingsService` — call `ApplySettings(settingsService.Load())` in constructor

### `src/LaTeXInserter/Views/OverlayWindow.axaml`
- TextBox: add `BorderThickness="1"`, `BorderBrush="{Binding AccentBrush}"` (remove from style override, keep in direct attribute)
- TextBox style: change `BorderThickness=0` → remove (or set to 1 to match); remove `FontSize=16` from style, let binding override
- Preview TextBlock: remove `Height="24"`, add `MinHeight="24"`, bind `FontSize="{Binding PreviewFontSize}"`
- Autocomplete ListBox: bind `FontSize="{Binding InputFontSize}"`, `SelectedItem="{Binding SelectedAutocompleteItem}"`, remove hardcoded `FontSize=13`
- ListBox: add `ItemTemplate` with `DataTemplate x:DataType="models:AutocompleteItem"`, Grid with Command (left) + Unicode (right, Opacity=0.6)
- ListBox: add `Resources` with `SolidColorBrush x:Key="AccentBgBrush"` and `Styles` targeting `ListBoxItem:selected /template/ ContentPresenter#PART_ContentPresenter` with `Background={DynamicResource AccentBgBrush}`
- Remove `SelectedIndex` binding (replaced by `SelectedItem`)

### `src/LaTeXInserter/Views/OverlayWindow.axaml.cs`
- `OnPreviewKeyDown` Tab case: call `_vm.CommitAutocomplete(_vm.SelectedAutocompleteItem)` (now passes item, not string)
- Up/Down: call `_vm.NavigateAutocomplete(-1/1)` (still index-based internally)
- Add method `UpdateAccentResource(IBrush brush)` — sets `AutocompleteListBox.Resources["AccentBgBrush"] = brush`
- Subscribe to `_vm.PropertyChanged` — when `AccentBackgroundBrush` changes, call `UpdateAccentResource`
- Wire this in `OnPropertyChanged` when VM is set

### `src/LaTeXInserter/ViewModels/TrayIconViewModel.cs`
- Add `private readonly NativeMenuItem _settingsItem` — "Settings..."
- Add `_settingsItem.Command = SettingsCommand` with `[RelayCommand] Settings()`
- Add `event EventHandler? SettingsRequested`
- Insert `_settingsItem` in menu at position 2 (after Show/Hide, before Edit Mappings)
- Add separator after it grouping with the existing config items

### `src/LaTeXInserter/ViewModels/AppManager.cs`
- Add field `SettingsViewModel _settingsViewModel` (injected)
- Add field `SettingsWindow? _activeSettingsWindow`
- Subscribe to `_trayIconViewModel.SettingsRequested` → `OnSettingsRequested`
- `OnSettingsRequested`: singleton check (activate if already open), else create window with VM, `.Show()`, cleanup on `Closed`
- Subscribe to `_settingsViewModel.SettingsSaved` → `OnSettingsSaved`
  - Call `_overlayViewModel.ApplySettings(e.Settings)` (live reload)
- Subscribe to `_settingsViewModel.CloseRequested` → close settings window
- `Dispose`: close `_activeSettingsWindow`
- Unsubscribe events in `Dispose`

### `src/LaTeXInserter/Program.cs`
- Add: `services.AddSingleton<SettingsViewModel>()`

## Verification

1. `dotnet build` — must pass with zero errors (AOT-compatible, no reflection)
2. Run app, right-click tray → "Settings..." appears at position 2
3. Settings window opens with native chrome, two sections
4. Change preview font size → Save → hit hotkey → preview text uses new size
5. Change accent color to Azure Blue → Save → overlay TextBox border is blue, autocomplete selected item has blue tint (0.25 opacity)
6. Disable autocomplete → Save → type `\alp` → no dropdown appears, Enter still converts full input
7. Autocomplete items show unicode characters right-aligned with 0.6 opacity
8. Existing `settings.json` with only Hotkey + StartOnStartup loads without error — new fields default
