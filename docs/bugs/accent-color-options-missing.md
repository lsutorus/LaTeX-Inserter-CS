# Bug: Accent Color Options Missing in Settings

**Status:** Open
**Introduced:** v0.0.7 (commit `19d330f` — "Restructure settings: accent color fix, hotkey/startup moved from tray")
**Severity:** High — accent color selection completely non-functional

## Symptoms

In Settings → Appearance → "Accent Color" row, the area next to the label is **completely empty**. No color swatch buttons render at all. No selection ring, no colored boxes — just blank space.

## Architecture (How It Should Work)

The swatch palette is an `ItemsControl` bound to a static list of hex strings:

**XAML** (`src/LaTeXInserter/Views/SettingsWindow.axaml`):
```xml
<ItemsControl x:Name="AccentSelector" Grid.Row="2" Grid.Column="1"
              ItemsSource="{x:Static vm:SettingsViewModel.AccentPalette}">
    <ItemsControl.ItemsPanel>
        <ItemsPanelTemplate>
            <WrapPanel />
        </ItemsPanelTemplate>
    </ItemsControl.ItemsPanel>
    <ItemsControl.ItemTemplate>
        <DataTemplate>
            <Button Width="24" Height="24" Margin="2"
                    Classes="accent-swatch"
                    Click="OnSwatchClick" />
        </DataTemplate>
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

**Static palette** (`src/LaTeXInserter/ViewModels/SettingsViewModel.cs:24`):
```csharp
public static IReadOnlyList<string> AccentPalette { get; } =
[
    "#404040", "#D1D5DB", "#3B82F6", "#8B5CF6", "#EC4899",
    "#EF4444", "#F97316", "#F59E0B", "#10B981", "#06B6D4"
];
```

**Code-behind** (`src/LaTeXInserter/Views/SettingsWindow.axaml.cs`):
- `InitializeComponent()` → subscribes to `AccentSelector.ItemsView.CollectionChanged` → calls `InitializeSwatchColors()`
- `InitializeSwatchColors()` iterates `AccentSelector.Items`, looks up `AccentSelector.ContainerFromItem(item)`, casts to `ContentControl { Content: Button }`, sets `Button.Background = new SolidColorBrush(Color.Parse(hex))` and `Button.DataContext = hex`
- `OnSwatchClick` reads `btn.DataContext` as hex string

## Likely Root Causes

### 1. `ContainerFromItem` returns null (MOST LIKELY)

`InitializeSwatchColors()` runs on `CollectionChanged`, but Avalonia may not have generated containers yet at that point. `ContainerFromItem()` returns `null` when the item container hasn't been realized. The method iterates `AccentSelector.Items` (the data strings) but can't find their visual containers, so `btn` is always null → no Background ever set → buttons render as transparent/invisible.

**Fix direction:** Delay `InitializeSwatchColors` until containers are ready. Options:
- Override `OnOpened` or use `LayoutUpdated` event
- Use `ItemsControl.ContainerGenerator` events (`Materialized`/`Dematerialized`)
- Use `Loaded` event on the ItemsControl itself
- Set Background in XAML via an `IBinding` or `ItemTemplate` binding instead of code-behind

### 2. `{x:Static}` binding issue

`{x:Static vm:SettingsViewModel.AccentPalette}` may not work with Avalonia's compiled bindings (`AvaloniaUseCompiledBindingsByDefault=true` in csproj). Compiled bindings might not support `{x:Static}` — the ItemsControl could have zero items because the binding silently fails.

**Fix direction:** Check if `{x:Static}` works with compiled bindings. If not, either:
- Set `ItemsSource` in code-behind: `AccentSelector.ItemsSource = SettingsViewModel.AccentPalette;`
- Or add `x:CompileBindings="False"` on the ItemsControl
- Or change to a regular binding on a ViewModel property

### 3. Button Background is Transparent

Even if containers exist, `Button.Background = Transparent` from the `accent-swatch` CSS style in `App.axaml`:
```xml
<Style Selector="Button.accent-swatch">
    <Setter Property="Background" Value="Transparent" />
    ...
</Style>
```
The code-behind `InitializeSwatchColors()` sets `Background` after `InitializeComponent`, but CSS styles may override it depending on Avalonia's style resolution order. If the style wins, buttons are invisible (transparent on dark background).

**Fix direction:** Set Background via a higher-priority mechanism, or verify style precedence vs code-behind property sets.

## Files to Investigate

| File | What to check |
|------|---------------|
| `src/LaTeXInserter/Views/SettingsWindow.axaml` | `{x:Static}` binding with compiled bindings, ItemsControl item rendering |
| `src/LaTeXInserter/Views/SettingsWindow.axaml.cs` | `InitializeSwatchColors()` — container generation timing, `ContainerFromItem` null returns |
| `src/LaTeXInserter/ViewModels/SettingsViewModel.cs` | `AccentPalette` static property — verify it's accessible |
| `src/LaTeXInserter/App.axaml` | `Button.accent-swatch` style — Transparent Background overriding code-behind |
| `src/LaTeXInserter/LaTeXInserter.csproj` | `AvaloniaUseCompiledBindingsByDefault=true` — may break `{x:Static}` |

## Recommended Debug Steps

1. Add `Console.WriteLine` in `InitializeSwatchColors()` to log whether `ContainerFromItem` returns null for each item
2. Check if `AccentSelector.Items` has 10 entries at runtime (rule out binding issue)
3. Try temporarily replacing `{x:Static}` with code-behind `AccentSelector.ItemsSource = SettingsViewModel.AccentPalette;` in constructor
4. If containers are null, move initialization to `OnOpened` override or `Loaded` event
5. If style override is the issue, use `!important` equivalent (Inline value > Style value in Avalonia, so code-behind `SetCurrentValue` should win — verify with a debugger)

## Previous Working State (v0.0.6)

In v0.0.6, `SettingsWindow.axaml` had the same `ItemsControl` + `{x:Static}` + `accent-swatch` pattern, but the code-behind was simpler:
```csharp
private void OnSwatchClick(object? sender, RoutedEventArgs e)
{
    if (sender is Button btn && btn.Background is Avalonia.Media.IBrush brush)
    {
        var vm = DataContext as SettingsViewModel;
        if (vm is not null && brush is Avalonia.Media.SolidColorBrush scb)
        {
            vm.AccentColor = $"#{scb.Color.R:X2}{scb.Color.G:X2}{scb.Color.B:X2}";
        }
    }
}
```
That version **also didn't work** per user report (colors appeared but clicking didn't do anything visible). The v0.0.7 rewrite moved swatch color initialization to code-behind but likely introduced the rendering regression. Both versions need the same fix: reliable swatch rendering that works at Avalonia container generation time.
