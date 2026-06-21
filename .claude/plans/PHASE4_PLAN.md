# Phase 4: The Avalonia Popup UI — Detailed Implementation Plan

## Resolved Design Decisions (from grill session)

| # | Decision | Choice | Rationale |
|---|----------|--------|-----------|
| D1 | Overlay window ownership | `AppManager` creates and caches singleton window instance | Orchestrator owns full lifecycle; no circular deps between VM and window |
| D2 | Window positioning logic | Static `OverlayPositioner` class in `Helpers/` (platform-agnostic) | Pure math, testable, called from code-behind; supports cross-platform |
| D3 | Positioning timing | Code-behind `IsVisible` PropertyChanged handler, not `Opened` | Cached window doesn't re-fire `Opened` on subsequent Show() |
| D4 | Autocomplete filter | Find trailing `\[a-zA-Z]+$` on every `InputText` change | Simple, handles multi-command input like `x = \alpha + \bet` |
| D5 | Autocomplete commit | Surgical substring replacement at prefix start index | Avoids global `.Replace()` corrupting earlier occurrences |
| D6 | Keyboard routing | Code-behind tunneling handler (`RoutingStrategies.Tunnel`) | TextBox swallows keys on bubbling; tunneling intercepts before TextBox |
| D7 | Submit-and-paste flow | VM raises `SubmitRequested` event → `AppManager` orchestrates full flow | VM stays thin; no clipboard/paste/window-activation deps in VM |
| D8 | Hide flow | VM raises `HideRequested` event → `AppManager.HideOverlay()` | Consistent event pattern, no service locator in view |
| D9 | Overlay input on show | Clear `InputText` every show, fresh start | Matches Python app; each hotkey press is a new entry |
| D10 | Preview update | Synchronous, every keystroke | Converter is sub-ms pure dictionary lookup; no debounce needed |
| D11 | Preview when empty | Empty TextBlock in fixed-height Border | No placeholder; fixed-height Border prevents layout jitter |
| Q12 | Hotkey wiring | `_hotkeyService.HotkeyPressed += ToggleOverlay` in `AppManager` | Essential for overlay to respond to hotkey at all |
| D12 | NativeMethods approach | Add `GetCursorPos` + `POINT` struct to `NativeMethods.cs` | One P/Invoke not worth a whole service |
| D13 | Window handle | `window.TryGetPlatformHandle()?.Handle` after `Show()` | Standard Avalonia 11+ API; HWND valid after Show() |
| D14 | Dialog ownership | `AppManager` creates/manages, singleton instance tracking | Consistent with all window ownership; prevents duplicates |
| D15 | Dialog display mode | `window.Show()` (modeless), not `ShowDialog()` | No valid parent for ShowDialog when overlay is hidden |
| D16 | Update dialog VMs | Dedicated `UpToDateViewModel` + `UpdateViewModel` | DI compliance; no hardcoded presentation data in code-behind |
| D17 | Compiled bindings | `x:DataType` on all `.axaml` files | Native AOT compatible; project has `AvaloniaUseCompiledBindingsByDefault` |
| D18 | Dialog VMs lifecycle | DI-resolved constructor deps on AppManager | Consistent with all-VMs-via-DI pattern; AppManager uses injected fields, not `new` |
| D19 | Overlay background | Solid `#2b2b2b`, no AcrylicBlur | Matches Python overlay; no OS version deps, no flicker, no hit-test issues |

---

## Critical Caveats (from grill session)

1. **DPI Scaling Mismatch**: `Window.Position` is physical pixels. `ClientSize` is DIPs. Pass `PixelSize windowPhysicalSize` (ClientSize * Screen.Scaling) to `OverlayPositioner`. Compute positioning strictly in physical pixels.

2. **DPI-Aware GetCursorPos**: Use `window.Screens.ScreenFromPoint(cursorPixelPoint) ?? window.Screens.Primary` to get the correct screen for working area.

3. **Re-entrancy Loop on Commit**: When autocomplete commits (Tab/Enter), programmatic `InputText` update re-triggers the setter. Use `_isCommitting` guard flag to suppress re-filtering.

4. **Strict Command Boundary**: Match prefix with `\\[a-zA-Z]+$` at absolute end of text. Trailing space after `\alpha` = no active prefix = autocomplete closed.

5. **Caret Reset on Commit**: After programmatic `InputText` update during commit, force caret to end of new text in code-behind.

6. **Autocomplete ListBox Focus**: Set `Focusable="False"` on ListBox. Arrow keys navigate selection via VM; TextBox retains focus and caret.

7. **TextBox Styling**: Override `BorderThickness="0"`, `Background="Transparent"`, suppress focus/pointer-over pseudoclass borders for frameless look.

8. **Preview TextBlock Jitter**: Wrap preview `TextBlock` in `<Border Height="24">` — don't use `MinHeight` on TextBlock directly. Empty text causes baseline metric miscalculation.

9. **(0,0) Flash Bug**: Set `Opacity="0"` in XAML. On show: set estimated position → `Show()` → calculate true position → set `Opacity="1"`.

10. **Opened Event Unreliable**: Use `PropertyChanged` + `IsVisible` check for cached window re-positioning, not `Opened`.

11. **Win32 Focus Race**: `IWindowActivator.Activate(handle)` before `TextBox.Focus()`. Wrap `TextBox.Focus()` in `Dispatcher.UIThread.Post` to ensure OS processes activation message first.

12. **Async Void Exception Safety**: `OnSubmitRequested` handler is `async void` — wrap entire body in `try/catch` to prevent process crash.

13. **Focus Transition Delay**: `await Task.Delay(50)` between `IWindowActivator.Restore()` and `IInputSimulatorService.SimulatePasteAsync()`.

14. **Deactivated Auto-Hide**: Subscribe to `OverlayWindow.Deactivated` → `vm.Cancel()` for click-away dismissal.

15. **Hide Re-entrancy**: In `Cancel()`/`ResetState()`, set `IsAutocompleteOpen = false` before clearing `InputText` to prevent setter re-triggering filter.

16. **Dialog Instance Tracking**: Null out `_activeUpToDateDialog` / `_activeUpdateDialog` on `Closed` event. Call `Activate()` on existing instance if already open.

17. **UI Thread Dispatch for Dialogs**: Wrap dialog creation in `Dispatcher.UIThread.Post()` for Phase 6 thread safety.

18. **Event Unsubscribe on Disposal**: In `AppManager.Dispose()`, unsubscribe all four events before window teardown to prevent post-disposal crashes.

19. **Double-Toggle Guard**: Boolean guard in `ToggleOverlay()` to prevent overlapping show/hide sequences from rapid hotkey input.

20. **POINT Struct for P/Invoke**: `[StructLayout(LayoutKind.Sequential)]` + `[return: MarshalAs(UnmanagedType.Bool)]` for Native AOT source-gen marshalling.

21. **Platform Guard**: `RuntimeInformation.IsOSPlatform(OSPlatform.Windows)` around `NativeMethods.GetCursorPos()`. Fallback: primary screen center.

22. **Parser Safety Guards**: Max nesting depth 30 on brace recursion. Iteration counter ≤ `input.Length` in all `while` loops. Return raw text on guard trigger.

23. **Handle Allocation Timing**: `Show()` must precede `TryGetPlatformHandle()`. HWND valid after first `Show()`, persists across hide/show cycles.

24. **Dialog Destroy vs Cache**: Dialogs hook `Closed` → null tracker reference. Do NOT reuse dialog windows — they're fully disposed on close.

25. **Graceful Platform Degradation**: Windows-specific features (Win32 focus stealing) no-op gracefully on other platforms via `RuntimeInformation.IsOSPlatform` guards. Never throw platform-unsupported exceptions.

---

## Files to Create

### 1. `src/LaTeXInserter/Helpers/OverlayPositioner.cs`

Platform-agnostic static utility. Pure math — flips and clamps.

**Logic:**
```csharp
namespace LaTeXInserter.Helpers;

public static class OverlayPositioner
{
    public static PixelPoint GetPosition(
        PixelPoint cursorPosition,
        PixelSize windowPhysicalSize,
        PixelRect screenWorkingArea)
    {
        var x = cursorPosition.X;
        var y = cursorPosition.Y;

        // Flip right if overflows right edge
        if (x + windowPhysicalSize.Width > screenWorkingArea.X + screenWorkingArea.Width)
            x = cursorPosition.X - windowPhysicalSize.Width;

        // Flip bottom if overflows bottom edge
        if (y + windowPhysicalSize.Height > screenWorkingArea.Y + screenWorkingArea.Height)
            y = cursorPosition.Y - windowPhysicalSize.Height;

        // Clamp left/top to screen bounds
        x = Math.Max(x, screenWorkingArea.X);
        y = Math.Max(y, screenWorkingArea.Y);

        // Clamp right/bottom to screen bounds
        x = Math.Min(x, screenWorkingArea.X + screenWorkingArea.Width - windowPhysicalSize.Width);
        y = Math.Min(y, screenWorkingArea.Y + screenWorkingArea.Height - windowPhysicalSize.Height);

        return new PixelPoint(x, y);
    }
}
```

**Key details:**
- All values in physical pixels — caller responsible for DPI conversion
- Cursor position = top-left corner of overlay (matches Python behavior)
- Flip both axes independently
- Clamp after flip to handle edge cases (tiny screens)

---

### 2. `src/LaTeXInserter/ViewModels/OverlayViewModel.cs`

Thin MVVM ViewModel. No clipboard/paste/window deps. Events for AppManager orchestration.

**Constructor deps** (injected):
- `ILatexConverterService`
- `ISettingsService` (unused in Phase 4, but available for autocomplete data)

**Public bindable properties** (`[ObservableProperty]` source-gen):
- `string InputText` — bound to TextBox
- `string PreviewText` — Unicode preview, updated on every InputText change
- `bool IsAutocompleteOpen` — controls Popup visibility
- `int AutocompleteSelectedIndex` — tracks ListBox selection (-1 = none)

**Public collections:**
- `ObservableCollection<string> AutocompleteItems` — filtered command names

**Events (consumed by AppManager):**
- `event EventHandler<string> SubmitRequested` — carries converted Unicode text
- `event EventHandler HideRequested` — carries nothing

**Private fields:**
- `bool _isCommitting` — guard flag, suppresses re-filtering during programmatic InputText changes
- `string? _currentPrefix` — the trailing `\[a-zA-Z]+$` being filtered
- `int _currentPrefixStart` — start index of the prefix in InputText
- `int _navigationIndex` — tracks arrow-key navigation position in autocomplete list

**Partial method `OnInputTextChanged(string value)`:**
1. If `_isCommitting`, return immediately.
2. Set `IsAutocompleteOpen = false`, clear `AutocompleteItems`.
3. If `value` is null/empty, set `PreviewText = ""`, return.
4. Set `PreviewText = _converter.Convert(value)`.
5. Find trailing prefix: regex `\\[a-zA-Z]+$` at end of `value`.
6. If prefix found:
   a. Store `_currentPrefix = match.Value`, `_currentPrefixStart = match.Index`.
   b. Filter `_converter.CommandNames` where `name.StartsWith(prefix)`.
   c. If matches exist (max ~20 for UX): populate `AutocompleteItems`, set `IsAutocompleteOpen = true`, `AutocompleteSelectedIndex = 0`.
7. If no prefix: set `IsAutocompleteOpen = false`.

**Public methods:**
- `void CommitAutocomplete(string selectedCommand)`:
  1. Set `_isCommitting = true`.
  2. Build new text: `InputText[.._currentPrefixStart] + selectedCommand + InputText[(_currentPrefixStart + _currentPrefix.Length)..]`.
  3. Set `InputText = newText`.
  4. Set `IsAutocompleteOpen = false`.
  5. Set `_isCommitting = false`.
  6. Raise caret-reset need (code-behind handles actual caret position).

- `void NavigateAutocomplete(int delta)`: increment/decrement `_navigationIndex` within `AutocompleteItems` bounds, update `AutocompleteSelectedIndex`.

- `string GetSelectedAutocompleteItem()`: returns `AutocompleteItems[AutocompleteSelectedIndex]` or null.

- `void Submit()`: raises `SubmitRequested` with `_converter.Convert(InputText)`.

- `void Cancel()`: sets `IsAutocompleteOpen = false` first, then `InputText = ""`, raises `HideRequested`.

- `void ResetState()`: called by AppManager before show. Sets `IsAutocompleteOpen = false`, `InputText = ""`, `PreviewText = ""`.

---

### 3. `src/LaTeXInserter/Views/OverlayWindow.axaml`

Borderless topmost popup overlay.

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="using:LaTeXInserter.ViewModels"
        x:Class="LaTeXInserter.Views.OverlayWindow"
        x:DataType="vm:OverlayViewModel"
        Title="LaTeX Inserter"
        WindowStyle="None"
        SystemDecorations="None"
        Topmost="True"
        ShowInTaskbar="False"
        SizeToContent="Height"
        Width="350"
        Opacity="0"
        Background="#2b2b2b">

    <Window.Styles>
        <!-- TextBox: frameless, transparent, no focus border -->
        <Style Selector="TextBox.overlay-input">
            <Setter Property="BorderThickness" Value="0" />
            <Setter Property="Background" Value="Transparent" />
            <Setter Property="Foreground" Value="White" />
            <Setter Property="CaretBrush" Value="White" />
            <Setter Property="FontSize" Value="16" />
            <Setter Property="Padding" Value="8,6" />
        </Style>
        <Style Selector="TextBox.overlay-input:focus / .focus">
            <Setter Property="BorderThickness" Value="0" />
            <Setter Property="Background" Value="Transparent" />
        </Style>
        <Style Selector="TextBox.overlay-input:pointerover / .pointerover">
            <Setter Property="BorderThickness" Value="0" />
            <Setter Property="Background" Value="Transparent" />
        </Style>
    </Window.Styles>

    <StackPanel Margin="8">
        <TextBox x:Name="InputTextBox"
                 Class="overlay-input"
                 Text="{Binding InputText, Mode=TwoWay}"
                 Foreground="White" />

        <Border Height="24" VerticalAlignment="Center" Margin="8,2,8,4">
            <TextBlock Text="{Binding PreviewText}"
                       Foreground="#cccccc"
                       FontSize="14"
                       TextWrapping="NoWrap" />
        </Border>

        <Popup x:Name="AutocompletePopup"
               IsOpen="{Binding IsAutocompleteOpen}"
               PlacementTarget="{Binding #InputTextBox}"
               Placement="Bottom"
               IsLightDismissEnabled="False">
            <Border Background="#1e1e1e" BorderBrush="#444" BorderThickness="1"
                    CornerRadius="4" Padding="4" MaxHeight="200">
                <ListBox x:Name="AutocompleteListBox"
                         ItemsSource="{Binding AutocompleteItems}"
                         SelectedIndex="{Binding AutocompleteSelectedIndex}"
                         Focusable="False"
                         FontSize="13"
                         Foreground="#cccccc" />
            </Border>
        </Popup>
    </StackPanel>
</Window>
```

**Key details:**
- `Opacity="0"` — prevents (0,0) flash, set to 1 after positioning in code-behind
- `SizeToContent="Height"` — auto-height, fixed width 350
- `TransparencyLevelHint="AcrylicBlur"` — translucent dark bg matching Python overlay
- Preview in fixed-height `Border` (caveat #8)
- `ListBox.Focusable="False"` — prevents focus stealing from TextBox (caveat #6)
- `IsLightDismissEnabled="False"` — Popup stays open during typing; only VM controls visibility
- Compiled binding via `x:DataType`
- Window icon: set in code-behind from `LaTeX-Inserter-icon-final.ico`

---

### 4. `src/LaTeXInserter/Views/OverlayWindow.axaml.cs`

Code-behind — key routing, positioning, focus management, caret reset.

**Private fields:**
- `OverlayViewModel _vm` — cached from `DataContext`

**`OnPropertyChanged` override (or `PropertyChanged` subscription):**
Monitor `IsVisible` changes:
```csharp
if (e.Property == Visual.IsVisibleProperty && IsVisible)
{
    // Position window
    PositionOverlay();

    // Fade in (make visible)
    Opacity = 1;

    // Steal focus via Win32, then focus TextBox
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle != IntPtr.Zero)
            _windowActivator.Activate(handle);
    }

    // Post TextBox.Focus to ensure OS processed activation
    Dispatcher.UIThread.Post(() => InputTextBox.Focus(), DispatcherPriority.Input);
}
```

**`PositionOverlay()` private method:**
```csharp
PixelPoint cursorPos;
if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
{
    NativeMethods.GetCursorPos(out var pt);
    cursorPos = new PixelPoint(pt.X, pt.Y);
}
else
{
    var screen = Screens.Primary;
    cursorPos = screen?.WorkingArea.Center ?? new PixelPoint(0, 0);
}

var screen = Screens.ScreenFromPoint(cursorPos) ?? Screens.Primary!;
var scaling = screen.Scaling;
var physicalSize = new PixelSize(
    (int)(ClientSize.Width * scaling),
    (int)(ClientSize.Height * scaling));

var position = OverlayPositioner.GetPosition(cursorPos, physicalSize, screen.WorkingArea);
Position = position;
```

**Key routing — tunneling handler (in constructor):**
```csharp
AddHandler(InputElement.KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
```

**`OnPreviewKeyDown` logic:**
```csharp
var vm = (OverlayViewModel)DataContext!;

switch (e.Key)
{
    case Key.Escape:
        vm.Cancel();
        e.Handled = true;
        break;

    case Key.Tab:
        if (vm.IsAutocompleteOpen)
        {
            var selected = vm.GetSelectedAutocompleteItem();
            if (selected is not null)
                vm.CommitAutocomplete(selected);
            e.Handled = true;
            // Reset caret to end
            Dispatcher.UIThread.Post(() =>
                InputTextBox.CaretIndex = InputTextBox.Text?.Length ?? 0);
        }
        break;

    case Key.Enter:
        if (vm.IsAutocompleteOpen)
        {
            var selected = vm.GetSelectedAutocompleteItem();
            if (selected is not null)
            {
                vm.CommitAutocomplete(selected);
                e.Handled = true;
                Dispatcher.UIThread.Post(() =>
                    InputTextBox.CaretIndex = InputTextBox.Text?.Length ?? 0);
            }
            else
            {
                vm.Submit();
                e.Handled = true;
            }
        }
        else
        {
            vm.Submit();
            e.Handled = true;
        }
        break;

    case Key.Up:
        if (vm.IsAutocompleteOpen)
        {
            vm.NavigateAutocomplete(-1);
            e.Handled = true;
        }
        break;

    case Key.Down:
        if (vm.IsAutocompleteOpen)
        {
            vm.NavigateAutocomplete(1);
            e.Handled = true;
        }
        break;
}
```

**Deactivated handler (in constructor):**
```csharp
Deactivated += (_, _) =>
{
    var vm = DataContext as OverlayViewModel;
    vm?.Cancel();
};
```

**IWindowActivator injection:**
The code-behind needs `IWindowActivator`. Two options:
- Pass through constructor (requires custom `ActivableViewWindow` base or factory pattern)
- Resolve from `App.Services` — but this is service locator anti-pattern

**Resolution:** `OverlayWindow` constructor takes `IWindowActivator` param. `AppManager` passes it when creating: `new OverlayWindow(windowActivator)`. Then `AppManager` sets `DataContext = _viewModel` after construction.

Wait — Avalonia XAML windows must have parameterless constructors for the XAML loader. Alternative: set `IWindowActivator` via a public property after construction, before Show().

**Final approach:** `AppManager` creates window with parameterless constructor, then sets a public property:
```csharp
_overlayWindow = new OverlayWindow { DataContext = _viewModel };
_overlayWindow.WindowActivator = _windowActivator;
```

`OverlayWindow.axaml.cs` has:
```csharp
public IWindowActivator? WindowActivator { get; set; }
```

---

### 5. `src/LaTeXInserter/ViewModels/UpToDateViewModel.cs`

Thin ViewModel for the "up to date" dialog.

**Public bindable properties** (`[ObservableProperty]`):
- `string VersionText` — e.g. "You are running the latest version"
- `string SubtitleText` — e.g. "v0.0.1"

**Constructor deps:**
- `ISettingsService` (for version — actually version comes from assembly, not settings. Use `Assembly.GetEntryAssembly()?.GetName().Version`)

Actually — version is in `.csproj`. Access via:
```csharp
typeof(UpToDateViewModel).Assembly.GetName().Version?.ToString()
```

No deps needed. Simple `ObservableObject` with properties set by AppManager before showing.

---

### 6. `src/LaTeXInserter/Views/UpToDateDialog.axaml`

Themed frameless dialog.

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="using:LaTeXInserter.ViewModels"
        x:Class="LaTeXInserter.Views.UpToDateDialog"
        x:DataType="vm:UpToDateViewModel"
        Title="Check for Updates"
        WindowStyle="None"
        SystemDecorations="None"
        ShowInTaskbar="True"
        WindowStartupLocation="CenterScreen"
        Width="320" SizeToContent="Height"
        Background="#2b2b2b">

    <StackPanel Margin="24" Spacing="12" HorizontalAlignment="Center">
        <TextBlock Text="{Binding VersionText}"
                   FontWeight="Bold" FontSize="16"
                   Foreground="White" HorizontalAlignment="Center" />
        <TextBlock Text="{Binding SubtitleText}"
                   FontSize="13" Foreground="#aaaaaa"
                   HorizontalAlignment="Center" />
        <Button Content="OK" Click="OnOkClick"
                HorizontalAlignment="Center"
                Background="#4CAF50" Foreground="White"
                FontWeight="SemiBold" MinWidth="80" />
    </StackPanel>
</Window>
```

---

### 7. `src/LaTeXInserter/Views/UpToDateDialog.axaml.cs`

Code-behind — minimal.

```csharp
public partial class UpToDateDialog : Window
{
    public UpToDateDialog() => InitializeComponent();

    private void OnOkClick(object? sender, RoutedEventArgs e) => Close();
}
```

---

### 8. `src/LaTeXInserter/ViewModels/UpdateViewModel.cs`

ViewModel for the update available dialog.

**Public bindable properties** (`[ObservableProperty]`):
- `string HeadingText` — e.g. "Version 0.0.2 is available"
- `string SubtitleText` — e.g. "Current: v0.0.1"
- `string ChangelogMarkdown` — release notes (rendered in View)
- `bool IsDownloading` — controls progress bar visibility
- `double DownloadProgress` — 0-100
- `string StatusText` — e.g. "Downloading...", "Installing..."

**Events:**
- `event EventHandler InstallRequested` — fires when user clicks "Install Update"
- `event EventHandler LaterRequested` — fires when user clicks "Later"

**Constructor:** No deps. Properties set by AppManager before showing.

---

### 9. `src/LaTeXInserter/Views/UpdateDialog.axaml`

Themed frameless dialog with changelog + progress.

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="using:LaTeXInserter.ViewModels"
        x:Class="LaTeXInserter.Views.UpdateDialog"
        x:DataType="vm:UpdateViewModel"
        Title="Update Available"
        WindowStyle="None"
        SystemDecorations="None"
        ShowInTaskbar="True"
        WindowStartupLocation="CenterScreen"
        Width="400" SizeToContent="Height"
        Background="#2b2b2b">

    <StackPanel Margin="24" Spacing="12">
        <TextBlock Text="{Binding HeadingText}"
                   FontWeight="Bold" FontSize="16"
                   Foreground="White" HorizontalAlignment="Center" />
        <TextBlock Text="{Binding SubtitleText}"
                   FontSize="13" Foreground="#aaaaaa"
                   HorizontalAlignment="Center" />

        <!-- Changelog area: simple scrollable text block (Phase 6 can add markdown) -->
        <ScrollViewer MaxHeight="150">
            <TextBlock Text="{Binding ChangelogMarkdown}"
                       Foreground="#cccccc" FontSize="12"
                       TextWrapping="Wrap" />
        </ScrollViewer>

        <StackPanel Orientation="Horizontal" HorizontalAlignment="Center" Spacing="12">
            <Button Content="Install Update" Click="OnInstallClick"
                    Background="#2196F3" Foreground="White"
                    FontWeight="SemiBold" MinWidth="120"
                    IsVisible="{Binding !IsDownloading}" />
            <Button Content="Later" Click="OnLaterClick"
                    Foreground="#aaaaaa" MinWidth="80"
                    IsVisible="{Binding !IsDownloading}" />
        </StackPanel>

        <!-- Progress section (visible during download) -->
        <StackPanel IsVisible="{Binding IsDownloading}" Spacing="8">
            <ProgressBar Value="{Binding DownloadProgress}" Minimum="0" Maximum="100" />
            <TextBlock Text="{Binding StatusText}"
                       Foreground="#aaaaaa" FontSize="12"
                       HorizontalAlignment="Center" />
        </StackPanel>
    </StackPanel>
</Window>
```

**Key note:** Changelog rendering is plain text in Phase 4. Phase 6 can add a proper Markdown renderer or formatted text. "Orange links" from migration plan requires Markdown parsing — deferred to Phase 6 when UpdateService provides real release notes.

---

### 10. `src/LaTeXInserter/Views/UpdateDialog.axaml.cs`

Code-behind — minimal.

```csharp
public partial class UpdateDialog : Window
{
    public UpdateDialog() => InitializeComponent();

    private void OnInstallClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is UpdateViewModel vm)
            vm.RequestInstall();
    }

    private void OnLaterClick(object? sender, RoutedEventArgs e) => Close();
}
```

`UpdateViewModel.RequestInstall()` raises `InstallRequested`. `AppManager` subscribes and triggers Phase 6 download flow.

---

## Files to Modify

### 11. `src/LaTeXInserter/Platform/Windows/NativeMethods.cs`

**Add:**
```csharp
[StructLayout(LayoutKind.Sequential)]
public struct POINT
{
    public int X;
    public int Y;
}

[LibraryImport("user32.dll")]
[return: MarshalAs(UnmanagedType.Bool)]
public static partial bool GetCursorPos(out POINT lpPoint);
```

---

### 12. `src/LaTeXInserter/ViewModels/AppManager.cs`

**Major rewrite** — replace Phase 3 stubs with full implementation.

**New constructor deps** (added to existing):
- `IClipboardProvider _clipboardProvider`
- `IInputSimulatorService _inputSimulator`
- `IWindowActivator _windowActivator`
- `OverlayViewModel _viewModel`
- `UpToDateViewModel _upToDateViewModel`
- `UpdateViewModel _updateViewModel`

**New private fields:**
- `OverlayWindow? _overlayWindow` — singleton cached window
- `UpToDateDialog? _activeUpToDateDialog` — singleton tracker
- `UpdateDialog? _activeUpdateDialog` — singleton tracker
- `bool _isToggling` — double-toggle guard
- `IWindowActivator _windowActivator`
- `IClipboardProvider _clipboardProvider`
- `IInputSimulatorService _inputSimulator`
- `OverlayViewModel _viewModel`

**Modified `InitializeAsync()`:**
Add after existing event subscriptions:
```csharp
_hotkeyService.HotkeyPressed += OnHotkeyPressed;
_trayIconViewModel.CheckForUpdatesRequested += OnCheckForUpdatesRequested;
_viewModel.SubmitRequested += OnSubmitRequested;
_viewModel.HideRequested += OnHideRequested;
```

**New event handlers:**
- `OnHotkeyPressed(object? sender, HotkeyChord _)` → `ToggleOverlay()`
- `OnCheckForUpdatesRequested(object? sender, EventArgs _)` → `ShowUpToDateDialog()` (Phase 4 placeholder; Phase 6 adds real check)
- `OnHideRequested(object? sender, EventArgs _)` → `HideOverlay()`
- `OnSubmitRequested(object? sender, string convertedText)` → async void, try-catch wrapped:
  ```csharp
  private async void OnSubmitRequested(object? sender, string convertedText)
  {
      try
      {
          await _clipboardProvider.SetTextAsync(convertedText);
          HideOverlay();
          _windowActivator.Restore();
          await Task.Delay(50);
          await _inputSimulator.SimulatePasteAsync(convertedText);
      }
      catch (Exception ex)
      {
          Debug.WriteLine($"SubmitAndPaste failed: {ex}");
      }
  }
  ```

**Full `ShowOverlay()` implementation:**
```csharp
public void ShowOverlay()
{
    _windowActivator.CapturePrevious();
    _viewModel.ResetState();

    if (_overlayWindow is null)
    {
        _overlayWindow = new OverlayWindow { DataContext = _viewModel };
        _overlayWindow.WindowActivator = _windowActivator;
    }

    _overlayWindow.Show();

    // Activate and focus handled in window's IsVisible handler
    IsOverlayVisible = true;
    OverlayVisibilityChanged?.Invoke(this, EventArgs.Empty);
}
```

**Full `HideOverlay()` implementation:**
```csharp
public void HideOverlay()
{
    _overlayWindow?.Hide();
    IsOverlayVisible = false;
    OverlayVisibilityChanged?.Invoke(this, EventArgs.Empty);
}
```

**Modified `ToggleOverlay()`:**
```csharp
public void ToggleOverlay()
{
    if (_isToggling) return;
    _isToggling = true;
    try
    {
        if (IsOverlayVisible) HideOverlay();
        else ShowOverlay();
    }
    finally
    {
        _isToggling = false;
    }
}
```

**Dialog methods:**
```csharp
public void ShowUpToDateDialog()
{
    Dispatcher.UIThread.Post(() =>
    {
        if (_activeUpToDateDialog != null)
        {
            _activeUpToDateDialog.Activate();
            return;
        }

        var vm = _upToDateViewModel;
        _activeUpToDateDialog = new UpToDateDialog { DataContext = vm };
        _activeUpToDateDialog.Closed += (_, _) => _activeUpToDateDialog = null;
        _activeUpToDateDialog.Show();
    });
}

public void ShowUpdateDialog()
{
    Dispatcher.UIThread.Post(() =>
    {
        if (_activeUpdateDialog != null)
        {
            _activeUpdateDialog.Activate();
            return;
        }

        var vm = _updateViewModel;
        _activeUpdateDialog = new UpdateDialog { DataContext = vm };
        _activeUpdateDialog.Closed += (_, _) => _activeUpdateDialog = null;
        _activeUpdateDialog.Show();
    });
}
```

**Modified `Dispose()`:**
Add event unsubscriptions before existing disposal:
```csharp
_hotkeyService.HotkeyPressed -= OnHotkeyPressed;
_trayIconViewModel.ShowOverlayRequested -= OnShowOverlayRequested;
_trayIconViewModel.CheckForUpdatesRequested -= OnCheckForUpdatesRequested;
_viewModel.SubmitRequested -= OnSubmitRequested;
_viewModel.HideRequested -= OnHideRequested;

_overlayWindow?.Close();
_overlayWindow = null;
```

---

### 13. `src/LaTeXInserter/ViewModels/TrayIconViewModel.cs`

**Add event:**
```csharp
public event EventHandler? CheckForUpdatesRequested;
```

**Replace stub `CheckForUpdates` command:**
```csharp
[RelayCommand]
private void CheckForUpdates() => CheckForUpdatesRequested?.Invoke(this, EventArgs.Empty);
```

---

### 14. `src/LaTeXInserter/Program.cs`

**Add DI registrations:**
```csharp
services.AddSingleton<OverlayViewModel>();
services.AddSingleton<UpToDateViewModel>();
services.AddSingleton<UpdateViewModel>();
```

---

### 15. `src/LaTeXInserter/Services/LatexConverterService.cs`

**Add safety guards to parser:**

In `ParseMath`:
- Add `int depth = 0` parameter (default 0 on external call)
- Before recursing into `ParseGroup`, check `if (++depth > 30) { sb.Append(span[pos]); pos++; continue; }`

In `ParseGroup`:
- Same depth tracking, pass incremented depth to recursive calls

In all `while` loops:
- Add `int safetyCounter = 0` before loop
- At loop start: `if (++safetyCounter > span.Length) break;`

These prevent infinite loops and stack overflows from malformed input.

---

### 16. `src/LaTeXInserter/App.axaml`

**Add shared styles** for dialog UI elements (green OK button, blue Install button, custom text styles). Added to `<Application.Styles>` section:

```xml
<!-- Shared dialog styles -->
<Style Selector="Button.dialog-ok">
    <Setter Property="Background" Value="#4CAF50" />
    <Setter Property="Foreground" Value="White" />
    <Setter Property="FontWeight" Value="SemiBold" />
    <Setter Property="CornerRadius" Value="4" />
    <Setter Property="Padding" Value="16,8" />
</Style>
<Style Selector="Button.dialog-install">
    <Setter Property="Background" Value="#2196F3" />
    <Setter Property="Foreground" Value="White" />
    <Setter Property="FontWeight" Value="SemiBold" />
    <Setter Property="CornerRadius" Value="4" />
    <Setter Property="Padding" Value="16,8" />
</Style>
<Style Selector="Button.dialog-later">
    <Setter Property="Background" Value="Transparent" />
    <Setter Property="Foreground" Value="#aaaaaa" />
    <Setter Property="CornerRadius" Value="4" />
    <Setter Property="Padding" Value="16,8" />
</Style>
<Style Selector="Button.dialog-install:pointerover / .pointerover">
    <Setter Property="Background" Value="#1976D2" />
</Style>
<Style Selector="Button.dialog-ok:pointerover / .pointerover">
    <Setter Property="Background" Value="#388E3C" />
</Style>
```

Refactor `UpToDateDialog.axaml` and `UpdateDialog.axaml` to use these style classes instead of inline setters.

---

## Files NOT Created in Phase 4

- `HotkeyDialogWindow.axaml` / `HotkeyDialogViewModel` (Phase 5)
- `UpdateService.cs` (Phase 6)
- `Converters/` directory (no value converters needed yet)
- Markdown renderer for changelog (Phase 6)

---

## Unit Tests

### `tests/LaTeXInserter.Tests/OverlayPositionerTests.cs`

Test `OverlayPositioner.GetPosition()`:
1. **Default position** — cursor at (500, 500), window 350x60, screen 0,0→1920x1080 → returns (500, 500)
2. **Flip right** — cursor at 1700, window 350 → flips to (1350, y)
3. **Flip bottom** — cursor at 1050, window 60 → flips to (x, 990)
4. **Clamp left** — cursor at (-10, 0) → clamps to (0, y)
5. **Clamp right** — cursor at 1920, window 350 → clamps to (1570, y)
6. **Multi-monitor offset** — screen working area starts at (1920, 0), cursor at (2500, 500) → positions correctly relative to second monitor

### `tests/LaTeXInserter.Tests/OverlayViewModelTests.cs`

Test `OverlayViewModel` state transitions:
1. **Empty input** — `InputText = ""` → `PreviewText = ""`, no autocomplete
2. **Plain text** — `InputText = "hello"` → `PreviewText = "hello"`, no autocomplete
3. **Command triggers autocomplete** — `InputText = "\alp"` → autocomplete opens with `\alpha` etc.
4. **Complete command closes autocomplete** — `InputText = "\alpha "` (trailing space) → autocomplete closed
5. **Commit autocomplete** — `CommitAutocomplete("\alpha")` replaces `\alp` prefix, `_isCommitting` suppresses re-filter
6. **Commit does not corrupt earlier occurrences** — `InputText = "x = \alp + \alp"` → commit replaces only last `\alp`
7. **Navigate autocomplete** — `NavigateAutocomplete(1)` increments index, wraps/clamps at bounds
8. **Cancel resets state** — `Cancel()` sets `IsAutocompleteOpen = false` before clearing `InputText`

---

## Execution Order

1. Add `POINT` struct + `GetCursorPos` to `NativeMethods.cs`
2. Create `Helpers/OverlayPositioner.cs`
3. Add parser safety guards to `LatexConverterService.cs`
4. Create `ViewModels/OverlayViewModel.cs`
5. Create `Views/OverlayWindow.axaml` + `.axaml.cs`
6. Create `ViewModels/UpToDateViewModel.cs`
7. Create `Views/UpToDateDialog.axaml` + `.axaml.cs`
8. Create `ViewModels/UpdateViewModel.cs`
9. Create `Views/UpdateDialog.axaml` + `.axaml.cs`
10. Add shared styles to `App.axaml`
11. Add `CheckForUpdatesRequested` event to `TrayIconViewModel.cs`
12. Rewrite `AppManager.cs` with full overlay lifecycle
13. Add `OverlayViewModel` + dialog VMs to `Program.cs` DI
14. Write `OverlayPositionerTests.cs`
15. Write `OverlayViewModelTests.cs`
16. `dotnet build LaTeXInserter.sln` — zero errors
17. `dotnet test LaTeXInserter.sln` — all tests pass
18. Manual verification: hotkey opens overlay at cursor, typing shows preview + autocomplete, Enter converts and pastes, Escape hides

---

## Build Verification

After all files created/modified:
1. `dotnet build LaTeXInserter.sln` — must compile with zero errors
2. `dotnet test LaTeXInserter.sln` — all tests pass (previous + new)
3. Manual smoke test:
   - Run app, system tray icon appears
   - Press Ctrl+Alt+M → overlay appears near cursor
   - Type `\alp` → preview shows Unicode, autocomplete popup opens
   - Press Tab/Enter → autocomplete commits
   - Type more, press Enter (popup closed) → text converted, clipboard set, overlay hides, previous window activated, Ctrl+V simulated
   - Press Escape → overlay hides
   - Click outside overlay → overlay hides
   - Tray → "Check for Updates" → shows UpToDateDialog
   - Close UpToDateDialog, click "Check for Updates" again → single dialog (no duplicate)
