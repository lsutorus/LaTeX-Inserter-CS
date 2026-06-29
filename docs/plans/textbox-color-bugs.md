# Plan: Fix Accent Color Selection Ring + TextBox Border

## Bugs

1. **No selection ring on swatches** — clicking a swatch changes the accent color but gives no visual indication of which is selected. `ContainerFromItem` + code-behind class toggling is an anti-pattern that consistently fails.
2. **TextBox border stuck on old color** — after saving a new accent color, the overlay TextBox focus border remains the Fluent default. Custom `DynamicResource` key swaps in `OverlayWindow.axaml.cs` don't work because the Fluent theme's internal template uses its own resource keys (`TextControlBorderBrushFocused` → `SystemControlHighlightAccentBrush` → `SystemAccentColor`).

## Design Decisions (from grilling session)

- **Selection ring**: Use Avalonia class binding in XAML (`Classes.accent-selected="{Binding IsSelected}"`) instead of code-behind `ContainerFromItem` + class toggling
- **AccentSwatchInfo**: Make it inherit `ObservableObject` so `IsSelected` raises `PropertyChanged` and the XAML class binding updates
- **TextBox border**: Override `SystemAccentColor` at `Application.Resources` level — the Fluent theme auto-cascades to all accent-derived brushes including `TextControlBorderBrushFocused`
- **Single source of truth**: Create `App.ApplyAccentColor(string hex)` static method that sets `SystemAccentColor` and `AccentBgBrush` in `Application.Resources`. Called from app startup and from `OverlayViewModel.ApplySettings()`
- **Clean up**: Remove all custom `AccentBorderBrush` resource/style machinery from `OverlayWindow.axaml` — Fluent theme handles it automatically via `SystemAccentColor`
- **Consolidate**: Move `AccentBgBrush` from `ListBox.Resources` (overlay-specific) to `Application.Resources` (global), so `App.ApplyAccentColor` handles both

## Implementation Steps

### Step 1: Make AccentSwatchInfo an ObservableObject

**File:** `src/LaTeXInserter/Models/AccentSwatchInfo.cs`

- Change base class from plain object → `ObservableObject` (CommunityToolkit.Mvvm)
- Change `IsSelected` from auto-property → `[ObservableProperty]` private `_isSelected` field
- Keep `Hex` and `Brush` as init-only (they don't change after construction)

### Step 2: Add class binding for selection ring in DataTemplate

**File:** `src/LaTeXInserter/Views/SettingsWindow.axaml`

- On the `Border` element in the `DataTemplate`, add: `Classes.accent-selected="{Binding IsSelected}"`
- This replaces all code-behind `ApplySwatchRings()` / `UpdateSwatchSelection()` logic

### Step 3: Simplify SettingsWindow code-behind

**File:** `src/LaTeXInserter/Views/SettingsWindow.axaml.cs`

- Remove `ApplySwatchRings()` method entirely
- Remove `OnAccentColorChanged` handler (no longer needed for ring updates)
- Simplify `OnSwatchClick` — just call `vm.SelectSwatch(swatch)` (no manual ring toggling)
- Remove `AccentSelector.Loaded` handler (no longer needed for initial ring)
- Remove `OnDataContextChanged` wiring of `AccentColorChanged` (no longer needed)
- Remove unused `Avalonia.Interactivity` / `Avalonia.Media` imports if no longer referenced

### Step 4: Create App.ApplyAccentColor static method

**File:** `src/LaTeXInserter/App.axaml.cs`

- Add `using Avalonia.Media;`
- Add public static method `ApplyAccentColor(string hex)`:
  - Parse hex to `Color`
  - Set `Application.Current.Resources["SystemAccentColor"]` = parsed color
  - Create `SolidColorBrush` with 0.25 opacity from parsed color
  - Set `Application.Current.Resources["AccentBgBrush"]` = the brush

### Step 5: Call App.ApplyAccentColor on startup

**File:** `src/LaTeXInserter/App.axaml.cs`

- In `OnFrameworkInitializationCompleted` (or wherever App is initialized), load settings and call `ApplyAccentColor(settings.AccentColor)`

### Step 6: Call App.ApplyAccentColor from OverlayViewModel.ApplySettings

**File:** `src/LaTeXInserter/ViewModels/OverlayViewModel.cs`

- In `ApplySettings()`, add `App.ApplyAccentColor(settings.AccentColor)` call
- Keep `UpdateBrushes()` for the VM's own `AccentBrush` / `AccentBackgroundBrush` properties (used for direct bindings if any remain)
- Remove `AccentBackgroundBrush` property if no longer needed (check usages first)

### Step 7: Move AccentBgBrush to Application.Resources

**File:** `src/LaTeXInserter/App.axaml`

- Add `<SolidColorBrush x:Key="AccentBgBrush" Color="#404040" Opacity="0.25" />` to `<Application.Resources>` (or `<Application.Styles>` — needs verification)

**File:** `src/LaTeXInserter/Views/OverlayWindow.axaml`

- Remove `<ListBox.Resources>` block containing `AccentBgBrush`
- The `DynamicResource AccentBgBrush` in the ListBox style setter will now resolve from Application.Resources

### Step 8: Remove custom AccentBorderBrush machinery from OverlayWindow

**File:** `src/LaTeXInserter/Views/OverlayWindow.axaml`

- Remove `<Window.Resources>` block containing `AccentBorderBrush`
- Remove `BorderBrush="{DynamicResource AccentBorderBrush}"` from `TextBox.overlay-input:focus` and `:pointerover` style setters — the Fluent theme handles it automatically now
- Remove inline `BorderBrush` property from `TextBox` if still present

**File:** `src/LaTeXInserter/Views/OverlayWindow.axaml.cs`

- Remove `UpdateAccentResources()` method
- Remove `OnVmPropertyChanged` handler
- Remove `_vm.PropertyChanged` wiring in `OnPropertyChanged`
- The overlay code-behind becomes simple again — just positioning + key handling

### Step 9: Build and verify

- `dotnet build src/LaTeXInserter/LaTeXInserter.csproj`
- Run app, test:
  1. Open Settings → Appearance → Accent Color: colored swatches visible with white ring on selected
  2. Click a different swatch → ring moves to new selection
  3. Click Save → open overlay → TextBox border matches new accent color
  4. Autocomplete selection bg matches new accent color

## Files Changed (summary)

| File | Change |
|------|--------|
| `Models/AccentSwatchInfo.cs` | Inherit ObservableObject, `[ObservableProperty] IsSelected` |
| `ViewModels/SettingsViewModel.cs` | No changes (SelectSwatch already updates IsSelected) |
| `Views/SettingsWindow.axaml` | Add `Classes.accent-selected="{Binding IsSelected}"` on Border |
| `Views/SettingsWindow.axaml.cs` | Remove ApplySwatchRings, OnAccentColorChanged, Loaded handler |
| `App.axaml.cs` | Add `ApplyAccentColor(string hex)` static method, call on startup |
| `App.axaml` | Add `AccentBgBrush` to Application.Resources |
| `ViewModels/OverlayViewModel.cs` | Add `App.ApplyAccentColor()` call in ApplySettings |
| `Views/OverlayWindow.axaml` | Remove AccentBorderBrush resources + style setters |
| `Views/OverlayWindow.axaml.cs` | Remove UpdateAccentResources, OnVmPropertyChanged, PropertyChanged wiring |

## Root Causes (for commit message)

- Selection ring: `ContainerFromItem` returns null before containers realized → code-behind class toggling never applies. Fix: XAML class binding with `INotifyPropertyChanged`
- TextBox border: Custom `AccentBorderBrush` DynamicResource doesn't override Fluent theme's `TextControlBorderBrushFocused` → `SystemControlHighlightAccentBrush` → `SystemAccentColor` chain. Fix: override `SystemAccentColor` at Application level
