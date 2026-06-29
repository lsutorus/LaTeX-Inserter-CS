# Plan: Settings Resize Lock + Custom Mappings Popup

> **Status: Implemented** (2026-06-26). Build succeeds. See Known Issues below.

## Feature 1: Remove Settings Window Resize

`CanResize = false` set in `SettingsWindow.axaml.cs` code-behind (not AXAML — Avalonia 12 compiled bindings reject `ResizeMode` attribute with AVLN2000).

## Feature 2: Custom Mappings Popup

### Overview

Replace "Edit Custom Mappings" (opens notepad) + "Reload Custom Mappings" tray items with a standalone singleton popup window (`CustomMappingsWindow`) managed by `AppManager`, same pattern as `SettingsWindow`.

### Data Model

- **No new data files.** Uses existing `custom_mappings.txt` and embedded `Commands.json`.
- All changes in popup are **staged locally**. Nothing writes to `custom_mappings.txt` until Save.
- Cancel discards all staged changes.
- Editing a default in Tab 2 = writing an override to `custom_mappings.txt` (same as current merge behavior).
- "Revert to Default" strips overrides of default commands from `custom_mappings.txt`. Purely custom entries survive.

### Window Chrome

- Title: "Custom Mappings"
- `Width="450"`, `Height="600"`, `WindowStartupLocation="CenterScreen"`
- `ResizeMode="CanResize"` — user can make taller to see more items
- Singleton via AppManager (same as SettingsWindow)

### Tabbed UI

Two tabs. Bottom button bar is **separate from tabs** (always visible regardless of tab).

#### Tab 1: Custom Mappings (default/starting view)

- Shows contents of `custom_mappings.txt`
- Types of entries:
  - **Purely custom** — user-added commands not in defaults
  - **Overrides** — edited defaults (shown with asterisk `*` at far left)
- Add, Edit, Delete all work
- Add inserts new row at index 0 with inline editing active, cursor focused
- Delete removes selected row (immediate, no confirmation — Cancel undoes everything)

#### Tab 2: Default Mappings

- Shows all commands from `Commands.json` (read-only source)
- Entries with custom overrides show asterisk `*` at far left
- Edit: double-click or select + Edit button → inline edit mode. Saving writes override to `custom_mappings.txt`
- **Add button: NOT available** (greyed out/disabled). New entries go in Tab 1.
- **Delete button: active only for asterisked items**. Clicking Delete on an overridden default removes the override, removes the asterisk, snaps row back to original default value. Non-overridden defaults: Delete is greyed out (can't delete hardcoded defaults).
- **Revert to Default button: active and red.** Strips ALL overrides of default commands from `custom_mappings.txt`. Purely custom entries survive. Confirmation dialog:

  > "This will remove all custom overrides of default commands. Your purely custom mappings will be kept. Continue?"

### Row Interaction Model

- **Single click** = select row (highlights it, enables Edit/Delete)
- **Double click** = jump straight to inline edit mode
- **Edit button** = enter inline edit mode on selected row
- **ListBox** with custom `DataTemplate` (two TextBoxes per row + asterisk indicator)

### Inline Editing UX (Windows Env Var editor style)

Each row has two input fields: **Command** and **Character**.

1. Add inserts new row at index 0, cursor immediately focused in Command field
2. Tab moves between Command ↔ Character fields
3. Enter confirms edit (commits inline, not to file — Save does that)
4. Click away commits the edit
5. Double-click or Edit button enters inline edit mode on existing row

### Validation

- Inline, not popup dialogs
- Command must start with `\` and not be empty
- Invalid → red border on TextBox, **Save button greyed out** until all errors fixed
- Tab switch while editing: auto-commit if valid, block switch if validation fails

### Bottom Button Bar

```
[Add] [Edit] [Delete] [Revert to Default]          [Save] [Cancel]
```

- **Left group**: action buttons (contextual)
- **Right group**: commit buttons (always visible)
- **Add**: active in Tab 1, disabled in Tab 2
- **Edit**: active when a row is selected, disabled otherwise
- **Delete**:
  - Tab 1: active when row selected, red styling
  - Tab 2: active only for asterisked items (single-item revert), greyed for non-overridden defaults
- **Revert to Default**: active and red in Tab 2, greyed in Tab 1
- **Save**: always active unless validation errors exist
- **Cancel**: always active, discards all staged changes

### Save Flow

1. Write all staged entries to `custom_mappings.txt` (overwrite entire file)
2. Call `LatexConverterService.Reload()` — reloads defaults + re-merges custom
3. Close window

### Tray Menu Changes

- "Edit Custom Mappings" → "Edit Custom Mappings..." (add ellipsis)
- Remove "Reload Custom Mappings" item entirely
- Clicking "Edit Custom Mappings..." opens the singleton `CustomMappingsWindow`

### Has_ARG

No special UI handling. User can type `{` and `}` in Command field. Existing merge/detection logic in `LatexConverterService.MergeCustomMappings()` handles it on reload after Save.

---

## Implementation Steps

### Step 1: Add ResizeMode="NoResize" to SettingsWindow.axaml

**File:** `src/LaTeXInserter/Views/SettingsWindow.axaml`

- Add `ResizeMode="NoResize"` to Window element

### Step 2: Create CustomMappingsWindow.axaml

**File:** `src/LaTeXInserter/Views/CustomMappingsWindow.axaml` (new)

- Window: Title="Custom Mappings", Width=450, Height=600, CenterScreen, CanResize
- TabControl with two tabs: "Custom Mappings", "Default Mappings"
- Each tab: ListBox with ScrollViewer (ListBox provides its own)
- ListBox ItemTemplate: Grid with asterisk TextBlock, Command TextBox, Character TextBox, separator
- Bottom DockPanel/StackPanel pinned to bottom with button bar

### Step 3: Create CustomMappingsWindow.axaml.cs

**File:** `src/LaTeXInserter/Views/CustomMappingsWindow.axaml.cs` (new)

- Code-behind: tab switch handling, button enable/disable logic, inline edit entry/exit
- Selection tracking: which row is selected, enabling Edit/Delete
- Inline edit mode: track editing state, handle Tab/Enter/click-away
- Delete: remove from staged list (Tab 1), remove override (Tab 2 asterisked items)
- Revert to Default: confirm dialog, strip all overrides from staged custom entries
- Save: write to `custom_mappings.txt`, call `LatexConverterService.Reload()`,
  close window
- Cancel: discard staged state, close window

### Step 4: Create CustomMappingsViewModel

**File:** `src/LaTeXInserter/ViewModels/CustomMappingsViewModel.cs` (new)

- ObservableObject with source-gen
- Staged collections: `ObservableCollection<MappingItem>` for Tab 1 (custom) and Tab 2 (defaults)
- `MappingItem` model: `Command`, `Character`, `IsOverride` (has asterisk), `IsEditing`, `HasValidationError`
- Load: read `custom_mappings.txt` and `Commands.json` on init
- `SaveCommand`: write staged custom entries to file, call Reload()
- `CancelCommand`: close window
- `AddCommand`: insert row at index 0, enter edit mode
- `EditCommand`: enter inline edit on selected row
- `DeleteCommand`: remove entry (Tab 1) or remove override (Tab 2)
- `RevertToDefaultCommand`: strip all overrides with confirmation
- Tab awareness: which tab is active, affects button availability

### Step 5: Create MappingItem model

**File:** `src/LaTeXInserter/Models/MappingItem.cs` (new)

- Partial class, ObservableObject
- `[ObservableProperty]` fields: `_command`, `_character`, `_isOverride`, `_isEditing`, `_hasValidationError`
- For Tab 2 default items: `_defaultCommand`, `_defaultCharacter` (original values for revert)

### Step 6: Update TrayIconViewModel

**File:** `src/LaTeXInserter/ViewModels/TrayIconViewModel.cs`

- Rename: "Edit Custom Mappings" → "Edit Custom Mappings..."
- Remove "Reload Custom Mappings" menu item and `ReloadMappings()` method
- Change `EditMappings()` to open `CustomMappingsWindow` singleton via `AppManager`

### Step 7: Update AppManager

**File:** `src/LaTeXInserter/AppManager.cs`

- Add `ShowCustomMappingsWindow()` method (same pattern as `ShowSettingsWindow`)
- Singleton: track instance, bring to front if already open

### Step 8: Register in DI

**File:** `src/LaTeXInserter/Program.cs`

- Register `CustomMappingsViewModel` in DI container
- Register `CustomMappingsWindow` if needed (or construct via AppManager)

### Step 9: Build and verify

- `dotnet build src/LaTeXInserter/LaTeXInserter.csproj`
- Manual test:
  1. Settings window: cannot resize
  2. Tray → "Edit Custom Mappings..." → popup opens
  3. Tab 1: add/edit/delete custom mappings
  4. Tab 2: view defaults, edit (creates override with asterisk), delete override
  5. Revert to Default with confirmation
  6. Save writes to file + reloads
  7. Cancel discards changes
  8. Second click on tray item brings existing window to front

## Files Changed (summary)

| File | Change |
|------|--------|
| `Views/SettingsWindow.axaml.cs` | Add `CanResize = false` in code-behind |
| `Models/MappingItem.cs` | New — observable item model for mapping rows |
| `ViewModels/CustomMappingsViewModel.cs` | New — staged CRUD, save/cancel, tab awareness |
| `Views/CustomMappingsWindow.axaml` | New — tabbed UI, ListBox, inline edit template, button bar |
| `Views/CustomMappingsWindow.axaml.cs` | New — code-behind, inline edit, validation |
| `ViewModels/TrayIconViewModel.cs` | Rename menu item, remove Reload, EditMappings fires event |
| `ViewModels/AppManager.cs` | Add CustomMappingsWindow singleton + OnEditMappingsRequested |
| `Services/LatexConverterService.cs` | Add `DefaultCommands` property |
| `Abstractions/ILatexConverterService.cs` | Add `DefaultCommands` to interface |
| `Program.cs` | Register CustomMappingsViewModel in DI |

## Known Issues (post-implementation)

1. **No confirmation dialog on Revert to Default** — plan calls for "This will remove all custom overrides..." confirmation; currently strips overrides immediately
2. **Tab switch while editing doesn't block on validation failure** — plan says "block switch if validation fails"; currently allows switch
3. **Tab 1 Add UX** — new row starts with Command=`\` which triggers HasValidationError=true immediately; may need placeholder or better default
4. **Validation error propagation** — `HasValidationErrors` on VM only updates via `CheckValidation()` (called from `OnItemEditCommitted` and `Add`), not automatically when MappingItem.HasValidationError changes independently
5. **Re-open window** — VM is singleton but collections are cleared/re-populated by `Reload()` on each open; AppManager creates new `CustomMappingsWindow` instance each time (old window is Closed, new one created)
