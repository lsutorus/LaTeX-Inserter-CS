# Phase 5 Plan: Change Hotkey Dialog & Settings Validation

## Decisions (grilled & locked)

| # | Decision | Choice | Rationale |
|---|----------|--------|-----------|
| 1 | Dialog ownership | TrayIconViewModel fires `ChangeHotkeyRequested` → AppManager shows dialog | Matches existing event-routing pattern; VMs stay view-agnostic |
| 2 | Show mode | `Show()` non-modal + `Topmost=True` | Matches existing dialog pattern; SharpHook is global anyway |
| 3 | Accept gesture | Explicit "Save" button | Safest UX; user reviews before committing |
| 4 | Recording vs normal mode | `IsRecording` flag suspends matching; no unregister/restore needed | Early-return in `OnKeyPressed` is sufficient guard; `_currentHotkey` unchanged during recording |
| 5 | Min chord validation location | HotkeyDialogViewModel (UI policy, not model invariant) | `Modifiers != None && TriggerKey != VcUndefined` is UI rule, not structural |
| 6 | Blocklist validation | Live during recording, inline warning + Save disabled | Real-time feedback; no surprise on Save click |
| 7 | Save pipeline | VM does both: `RegisterHotkey` + `SettingsService.Save` | Simplest; `HotkeyChanged` event auto-updates tray label |
| 8 | Dialog display | Prompt label + chord TextBlock + warning TextBlock + Cancel/Save buttons | Matches dark theme; class-based styling for chord state colors |
| 9 | Chord tracking | Two-layer: `_liveChord` (always updated) + `_snapshotChord` (only when valid) | Shows real-time feedback while pressing; stable display after release |
| 10 | Display routing | Show `_liveChord` when holding keys, `_snapshotChord` when idle, fallback when empty | Prevents "frozen" appearance during re-record; no partial-state gap |
| 11 | Bare Escape handling | VM detects `Modifiers == None && TriggerKey == VcEscape` → `CloseRequested` | Allows `Shift+Escape` as valid hotkey; SharpHook intercepts before Avalonia |
| 12 | Key suppression during recording | None — let OS handle Alt+F4, Alt+Tab etc. | Safer; `Closed` event guarantees cleanup; blocklist prevents saving reserved combos |
| 13 | Close event model | Single `CloseRequested` event for both Save and Cancel | VM does all business logic before firing; AppManager just calls `Close()` |
| 14 | Cleanup location | `Cleanup()` called by AppManager on `Closed` event only | Single teardown point: `IsRecording = false` + unsubscribe; idempotent |
| 15 | VM lifecycle | Singleton DI + `StartRecording()`/`Cleanup()` bracket | Constructor stores deps only; no event subscription or `IsRecording` touch at ctor time (avoids startup trap) |
| 16 | Re-entrancy guard | `_activeHotkeyDialog is not null` → `Activate()` + return | Matches existing dialog pattern; no re-init on Activate path |
| 17 | Overlay during recording | Hide overlay before showing dialog | No coexistence; cleaner UX |
| 18 | Chord color styling | XAML class-based: `recording-only` (gray), `valid` (white), `blocked` (amber) | VM stays Avalonia-free, testable, theme-adaptable |
| 19 | Button styles | Save = `dialog-install` (blue), Cancel = `dialog-later` (gray) | Reuse existing styles; no new button styles needed |
| 20 | Save CanExecute | `IsValidAndNotBlocked` + explicit `SaveCommand.NotifyCanExecuteChanged()` in `UpdateDisplay()` | AOT-friendly direct invocation; no string-based event routing |
| 21 | Thread dispatching | None needed in VM — `HotkeyRecorded` already dispatched by `HotkeyService` | `Dispatcher.UIThread.Post` in HotkeyService line 76; no double-dispatch |
| 22 | `ToString()` fix | Skip `VcUndefined` trigger key in `HotkeyChord.ToString()` | Prevents "Ctrl+Undefined" display; domain-driven fix |
| 23 | DPI scaling safety | `TextWrapping="Wrap"` + `TextAlignment="Center"` on prompt and warning | Handles 150-200% OS scaling without clipping |
| 24 | CloseRequested subscription | Named method `OnDialogCloseRequested`, subscribed before `Show()`, unsubscribed in `Closed` | Prevents event accumulation on singleton VM |
| 25 | App disposal | `_activeHotkeyDialog?.Close()` in `Dispose()` → cascades `Closed` → `Cleanup()` | Dialog teardown on app exit is automatic |

---

## New Files (3)

### 1. `src/LaTeXInserter/ViewModels/HotkeyDialogViewModel.cs`

```csharp
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaTeXInserter.Abstractions;
using LaTeXInserter.Models;
using SharpHook.Data;

namespace LaTeXInserter.ViewModels;

public sealed partial class HotkeyDialogViewModel : ObservableObject
{
    private readonly IHotkeyService _hotkeyService;
    private readonly ISettingsService _settingsService;

    private HotkeyChord _liveChord;
    private HotkeyChord? _snapshotChord;

    private const string FallbackText = "Press keys…";

    // ObservableProperties (source-gen)
    [ObservableProperty] private string _chordDisplay = FallbackText;
    [ObservableProperty] private bool _isBlocked;

    // Computed (not ObservableProperty; manual OnPropertyChanged)
    public bool IsValid =>
        _snapshotChord.HasValue
        && _snapshotChord.Value.Modifiers != ModifierMask.None
        && _snapshotChord.Value.TriggerKey != KeyCode.VcUndefined;

    public bool IsValidAndNotBlocked => IsValid && !IsBlocked;
    public bool IsRecordingOnly => !IsValid;

    // Events
    public event EventHandler? CloseRequested;

    // Ctor: deps only, no event subscription, no IsRecording touch
    public HotkeyDialogViewModel(IHotkeyService hotkeyService, ISettingsService settingsService)
    {
        _hotkeyService = hotkeyService;
        _settingsService = settingsService;
    }

    // Called by AppManager before dialog.Show()
    public void StartRecording()
    {
        _liveChord = default;
        _snapshotChord = null;
        ChordDisplay = FallbackText;
        IsBlocked = false;
        _hotkeyService.HotkeyRecorded += OnHotkeyRecorded;
        _hotkeyService.IsRecording = true;
        UpdateDisplay();
    }

    // Called by AppManager on dialog.Closed
    public void Cleanup()
    {
        _hotkeyService.IsRecording = false;
        _hotkeyService.HotkeyRecorded -= OnHotkeyRecorded;
    }

    [RelayCommand(CanExecute = nameof(IsValidAndNotBlocked))]
    private void Save()
    {
        var chord = _snapshotChord!.Value;
        _hotkeyService.RegisterHotkey(chord);
        var settings = _settingsService.Load();
        _settingsService.Save(settings with { Hotkey = chord });
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, EventArgs.Empty);

    // HotkeyRecorded arrives on UI thread (HotkeyService dispatches)
    private void OnHotkeyRecorded(object? sender, HotkeyChord chord)
    {
        _liveChord = chord;

        // Bare Escape → cancel (allows Shift+Escape as valid hotkey)
        if (chord.Modifiers == ModifierMask.None && chord.TriggerKey == KeyCode.VcEscape)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        // Update snapshot when structurally valid
        if (chord.Modifiers != ModifierMask.None && chord.TriggerKey != KeyCode.VcUndefined)
            _snapshotChord = chord;

        UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        // Show live chord when keys held, snapshot when idle, fallback when empty
        bool isHoldingKeys = _liveChord.Modifiers != ModifierMask.None
            || _liveChord.TriggerKey != KeyCode.VcUndefined;

        ChordDisplay = isHoldingKeys
            ? _liveChord.ToString()
            : (_snapshotChord.HasValue ? _snapshotChord.Value.ToString() : FallbackText);

        // Blocklist validation against snapshot
        IsBlocked = IsValid && HotkeyBlocklist.IsBlocked(_snapshotChord!.Value);

        // Notify XAML class bindings
        OnPropertyChanged(nameof(IsRecordingOnly));
        OnPropertyChanged(nameof(IsValidAndNotBlocked));

        // Notify SaveCommand CanExecute
        SaveCommand.NotifyCanExecuteChanged();
    }
}
```

### 2. `src/LaTeXInserter/Views/HotkeyDialogWindow.axaml`

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="using:LaTeXInserter.ViewModels"
        x:Class="LaTeXInserter.Views.HotkeyDialogWindow"
        x:DataType="vm:HotkeyDialogViewModel"
        Title="Change Hotkey"
        WindowDecorations="None"
        ShowInTaskbar="True"
        WindowStartupLocation="CenterScreen"
        Topmost="True"
        Width="320" SizeToContent="Height"
        Background="#2b2b2b">

    <StackPanel Margin="24" Spacing="12">
        <TextBlock Text="Press your desired hotkey combination…"
                   FontSize="13" Foreground="#aaaaaa"
                   HorizontalAlignment="Center"
                   TextAlignment="Center" TextWrapping="Wrap" />

        <TextBlock Text="{Binding ChordDisplay}"
                   FontWeight="Bold" FontSize="16"
                   HorizontalAlignment="Center"
                   Classes.blocked="{Binding IsBlocked}"
                   Classes.valid="{Binding IsValidAndNotBlocked}"
                   Classes.recording-only="{Binding IsRecordingOnly}" />

        <TextBlock Text="This shortcut is reserved by the system"
                   FontSize="12" Foreground="#ff5252"
                   HorizontalAlignment="Center"
                   TextAlignment="Center" TextWrapping="Wrap"
                   IsVisible="{Binding IsBlocked}" />

        <StackPanel Orientation="Horizontal" HorizontalAlignment="Center" Spacing="12">
            <Button Content="Cancel"
                    Command="{Binding CancelCommand}"
                    Classes="dialog-later" />
            <Button Content="Save"
                    Command="{Binding SaveCommand}"
                    Classes="dialog-install" />
        </StackPanel>
    </StackPanel>
</Window>
```

### 3. `src/LaTeXInserter/Views/HotkeyDialogWindow.axaml.cs`

```csharp
using Avalonia.Controls;

namespace LaTeXInserter.Views;

public partial class HotkeyDialogWindow : Window
{
    public HotkeyDialogWindow()
    {
        InitializeComponent();
    }
}
```

---

## Modified Files (6)

### 4. `src/LaTeXInserter/Models/HotkeyChord.cs`

**Change:** Skip `VcUndefined` trigger key in `ToString()`.

```csharp
// Line 31: replace
parts.Add(FormatKeyCode(TriggerKey));
// with
if (TriggerKey != KeyCode.VcUndefined) parts.Add(FormatKeyCode(TriggerKey));
```

### 5. `src/LaTeXInserter/ViewModels/TrayIconViewModel.cs`

**Changes:**
1. Add event: `public event EventHandler? ChangeHotkeyRequested;`
2. Replace empty `ChangeHotkey()` body:
```csharp
[RelayCommand]
private void ChangeHotkey() => ChangeHotkeyRequested?.Invoke(this, EventArgs.Empty);
```

### 6. `src/LaTeXInserter/ViewModels/AppManager.cs`

**Changes:**
1. Add ctor param: `HotkeyDialogViewModel hotkeyDialogViewModel` + field `_hotkeyDialogViewModel`
2. Add field: `HotkeyDialogWindow? _activeHotkeyDialog`
3. In `InitializeAsync()`: subscribe `_trayIconViewModel.ChangeHotkeyRequested += OnChangeHotkeyRequested`
4. Add method:
```csharp
private void OnChangeHotkeyRequested(object? sender, EventArgs _)
{
    if (_activeHotkeyDialog is not null)
    {
        _activeHotkeyDialog.Activate();
        return;
    }

    if (IsOverlayVisible) HideOverlay();

    Dispatcher.UIThread.Post(() =>
    {
        _hotkeyDialogViewModel.StartRecording();

        _activeHotkeyDialog = new HotkeyDialogWindow
        {
            DataContext = _hotkeyDialogViewModel
        };

        _activeHotkeyDialog.Closed += (_, _) =>
        {
            _hotkeyDialogViewModel.Cleanup();
            _hotkeyDialogViewModel.CloseRequested -= OnDialogCloseRequested;
            _activeHotkeyDialog = null;
        };

        _hotkeyDialogViewModel.CloseRequested += OnDialogCloseRequested;
        _activeHotkeyDialog.Show();
    });
}

private void OnDialogCloseRequested(object? sender, EventArgs _)
{
    _activeHotkeyDialog?.Close();
}
```
5. In `Dispose()`: unsubscribe `ChangeHotkeyRequested`, close `_activeHotkeyDialog`

### 7. `src/LaTeXInserter/Program.cs`

**Change:** Add singleton registration:
```csharp
services.AddSingleton<HotkeyDialogViewModel>();
```
Add `HotkeyDialogViewModel` to `AppManager` constructor resolution.

### 8. `src/LaTeXInserter/App.axaml`

**Change:** Add three style selectors inside `<Application.Styles>`:
```xml
<!-- Hotkey dialog chord display states (mutually exclusive) -->
<Style Selector="TextBlock.recording-only">
    <Setter Property="Foreground" Value="#666666" />
</Style>
<Style Selector="TextBlock.valid">
    <Setter Property="Foreground" Value="White" />
</Style>
<Style Selector="TextBlock.blocked">
    <Setter Property="Foreground" Value="#FFB74D" />
</Style>
```

---

## Edge Cases Verified

| Scenario | Expected Behavior |
|----------|-------------------|
| Bare letter key (e.g. "A") | Shows "A" while held, reverts to fallback on release, Save disabled (no modifier) |
| Only modifier held (e.g. "Ctrl") | Shows "Ctrl" while held (ToString skips Undefined), reverts to fallback on release |
| Full chord held + released (Ctrl+Alt+M) | Shows "Ctrl+Alt+M" while held, stays "Ctrl+Alt+M" on release (snapshot), Save enabled |
| Blocklisted chord (Ctrl+C) | Live shows "Ctrl+C", warning appears, Save disabled |
| Re-record after valid chord | New keypresses update live display; new valid chord replaces snapshot |
| Bare Escape | Dialog closes, Cleanup fires, recording stops |
| Shift+Escape | Recorded as valid hotkey candidate (not treated as cancel) |
| Alt+F4 during recording | OS closes window → Closed → Cleanup fires; chord not saved |
| Alt+Tab during recording | Dialog loses focus; Topmost keeps it visible; click to resume |
| App shutdown while dialog open | Dispose → Close() → Closed → Cleanup; clean teardown |
| Double "Change Hotkey" click | Re-entrancy guard activates existing dialog |
| 150-200% OS scaling | TextWrapping on prompt/warning prevents clipping |
