# Phase 2: Global Hotkey & Input Engine (SharpHook)

All decisions grilled and locked. See grilling transcript for rationale.

---

## File Inventory

### New files (13 source + 2 test)

| # | Path | Purpose |
|---|------|---------|
| 1 | `src/LaTeXInserter/Abstractions/IHotkeyService.cs` | Hotkey service interface |
| 2 | `src/LaTeXInserter/Abstractions/IClipboardProvider.cs` | Clipboard abstraction |
| 3 | `src/LaTeXInserter/Abstractions/IInputSimulatorService.cs` | Input sim interface |
| 4 | `src/LaTeXInserter/Abstractions/IWindowActivator.cs` | Window activation interface |
| 5 | `src/LaTeXInserter/Abstractions/IStartupRegistrar.cs` | Startup registration interface |
| 6 | `src/LaTeXInserter/Services/HotkeyNormalizer.cs` | Static modifier collapse + sort |
| 7 | `src/LaTeXInserter/Models/HotkeyBlocklist.cs` | Static FrozenSet, IsBlocked() |
| 8 | `src/LaTeXInserter/Services/HotkeyService.cs` | SharpHook single-hook, flag dispatch |
| 9 | `src/LaTeXInserter/Services/AvaloniaClipboardProvider.cs` | Lazy TopLevel clipboard |
| 10 | `src/LaTeXInserter/Services/InputSimulatorService.cs` | Clipboard + paste simulation |
| 11 | `src/LaTeXInserter/Platform/Windows/NativeMethods.cs` | [LibraryImport] P/Invoke |
| 12 | `src/LaTeXInserter/Platform/Windows/WindowsWindowActivator.cs` | AttachThreadInput pattern |
| 13 | `src/LaTeXInserter/Platform/Windows/WindowsStartupRegistrar.cs` | Registry startup entry |
| 14 | `tests/LaTeXInserter.Tests/HotkeyNormalizerTests.cs` | Normalizer unit tests |
| 15 | `tests/LaTeXInserter.Tests/HotkeyBlocklistTests.cs` | Blocklist unit tests |

### Modified files (2)

| # | Path | Change |
|---|-------|--------|
| 1 | `src/LaTeXInserter/Program.cs` | DI registrations for all Phase 2 services |
| 2 | `src/LaTeXInserter/LaTeXInserter.csproj` | Already has SharpHook 6.0.0 (no change needed) |

---

## Locked Decisions

### D1: IHotkeyService interface

```csharp
public interface IHotkeyService : IDisposable
{
    HotkeyChord CurrentHotkey { get; }
    bool IsRecording { get; set; }  // volatile backing field
    event EventHandler<HotkeyChord>? HotkeyPressed;   // normal mode
    event EventHandler<HotkeyChord>? HotkeyRecorded;  // recording mode, per-keypress
    event EventHandler<HotkeyChord>? HotkeyChanged;   // for tray label in Phase 3
    void RegisterHotkey(HotkeyChord chord);
    Task StartAsync(CancellationToken ct);
}
```

- `HotkeyPressed`: fires when registered chord matched in normal mode
- `HotkeyRecorded`: fires on every key-down/key-up in recording mode, payload = accumulated `HotkeyChord` so far (source of truth lives in service, not VM)
- `HotkeyChanged`: fires when `RegisterHotkey` called (tray label update in Phase 3)
- `IDisposable`: DI container handles `Dispose` → unhook SharpHook

### D2: HotkeyNormalizer — static utility class

Two methods:

```csharp
public static class HotkeyNormalizer
{
    // SharpHook adapter: collapse EventMask → our ModifierMask
    // Strips: NumLock, CapsLock, ScrollLock, SimulatedEvent, SuppressEvent, mouse buttons
    // Collapses: EventMask.Ctrl → ModifierMask.Control, etc.
    public static ModifierMask CollapseModifiers(EventMask rawMask);

    // Platform-agnostic: canonical sort
    // Sorts: Ctrl → Alt → Shift → Windows, then non-modifier trigger key
    public static HotkeyChord Normalize(HotkeyChord chord);
}
```

- `CollapseModifiers` is the SharpHook boundary — only `HotkeyService` calls it
- `Normalize` is platform-agnostic — used by blocklist, recording, settings

**Modifier collapse mapping:**
- `EventMask.Ctrl` (= `LeftCtrl|RightCtrl`, 0x22) → `ModifierMask.Control`
- `EventMask.Alt` (= `LeftAlt|RightAlt`, 0x88) → `ModifierMask.Alt`
- `EventMask.Shift` (= `LeftShift|RightShift`, 0x11) → `ModifierMask.Shift`
- `EventMask.Meta` (= `LeftMeta|RightMeta`, 0x44) → `ModifierMask.Windows`

**Strip mask (removed before collapse):**
```
EventMask.NumLock | EventMask.CapsLock | EventMask.ScrollLock
| EventMask.SimulatedEvent | EventMask.SuppressEvent
| EventMask.Button1 | EventMask.Button2 | EventMask.Button3
| EventMask.Button4 | EventMask.Button5
```

### D3: HotkeyBlocklist — static class

```csharp
public static class HotkeyBlocklist
{
    public static bool IsBlocked(HotkeyChord chord);
}
```

- `FrozenSet<HotkeyChord>` populated from 32 hardcoded entries
- Every entry passed through `HotkeyNormalizer.Normalize()` before freezing
- `IsBlocked` normalizes the input chord, then checks set containment
- Future: `RuntimeInformation.IsOSPlatform` routing to Windows/macOS sets

**32 blocklist entries (from Python):**
- System-critical: Ctrl+Alt+Delete, Ctrl+Shift+Escape
- Alt combos: Alt+Tab, Alt+Shift+Tab, Alt+F4, Alt+Space, Alt+Escape
- Ctrl combos: Ctrl+Escape, Ctrl+C, Ctrl+V, Ctrl+X, Ctrl+Z, Ctrl+A
- Win combos: Win+Tab, Win+L, Win+D, Win+E, Win+R, Win+I, Win+S, Win+A, Win+P, Win+V, Win+X, Win+G, Win+M, Win+Shift+S, Win+Ctrl+D, Win+Ctrl+F4, Win+Ctrl+Left, Win+Ctrl+Right, Win+Up, Win+Down, Win+Left, Win+Right

### D4: HotkeyService implementation

**Constructor injection:**
- `SimpleGlobalHook` (DI singleton, registered in Program.cs)
- Subscribes to `hook.KeyPressed` and `hook.KeyReleased` in constructor (hook not yet running)

**StartAsync:**
- Fire-and-forget: `_ = hook.RunAsync()` — must NOT await (deadlocks app startup)
- CancellationToken ignored (Dispose handles teardown)

**Normal mode matching (on KeyPressed):**
1. Read `e.RawEvent.Keyboard.Mask` (raw `EventMask`)
2. Strip toggle flags + non-modifier mask bits
3. `CollapseModifiers` → our `ModifierMask`
4. **Exact equality**: `collapsed == CurrentHotkey.Modifiers && e.Key == CurrentHotkey.TriggerKey`
5. On match: `e.Suppress()` (prevent event reaching other apps)
6. `Dispatcher.UIThread.Post(() => HotkeyPressed?.Invoke(...))`

**Recording mode (on KeyPressed + KeyReleased):**
- `volatile` backing field for `IsRecording`
- `List<KeyCode> _heldKeys` — ordered accumulator, locked for thread safety
- `KeyPressed`: if not in list, add to end. Build chord. Fire `HotkeyRecorded`.
- `KeyReleased`: remove from list. Build chord. Fire `HotkeyRecorded`.
- **Build chord from held list:**
  1. Iterate list. Map modifier keys to `ModifierMask` via bitwise OR.
  2. Last non-modifier in list = `TriggerKey` (or `KeyCode.VcUndefined` if none).
  3. `HotkeyNormalizer.Normalize()` the result.
- **Safety:** clear `_heldKeys` when `IsRecording` set to false (prevents ghost keys from dropped KeyReleased events).
- **Do NOT suppress events** during recording mode.
- `lock (_accumulatorLock)` around all list mutations and chord building.

**RegisterHotkey:**
- Sets `CurrentHotkey`, fires `HotkeyChanged`

**Dispose:**
- Calls `hook.Dispose()` — stops hook and unregisters

**Thread model:**
- Hook callbacks fire on background thread
- Normalize + match runs on background thread (pure CPU, zero UI state)
- Event invocation via `Dispatcher.UIThread.Post`

### D5: IClipboardProvider + AvaloniaClipboardProvider

```csharp
public interface IClipboardProvider
{
    Task SetTextAsync(string text);
}
```

```csharp
internal sealed class AvaloniaClipboardProvider : IClipboardProvider
{
    public async Task SetTextAsync(string text)
    {
        var topLevel = TopLevel.GetTopLevel(
            ((IClassicDesktopStyleApplicationLifetime?)
                Application.Current!.ApplicationLifetime!)?.MainWindow);
        var clipboard = topLevel!.Clipboard!;
        await clipboard.SetTextAsync(text);
    }
}
```

- Lazy resolves `TopLevel` via `Application.Current.ApplicationLifetime`
- No Window references stored — resolved at call time

### D6: IInputSimulatorService + InputSimulatorService

```csharp
public interface IInputSimulatorService
{
    Task SimulatePasteAsync(string unicodeText);
}
```

**Constructor injection:**
- `IClipboardProvider`
- `IEventSimulator` (from SharpHook's `SimpleGlobalHook.EventSimulator` or DI-registered)

**SimulatePasteAsync algorithm:**
1. `await _clipboard.SetTextAsync(unicodeText)`
2. If `RuntimeInformation.IsOSPlatform(OSPlatform.OSX)`:
   - `SimulateKeyPress(KeyCode.VcLeftMeta)`
   - `SimulateKeyPress(KeyCode.VcV)`
   - `SimulateKeyRelease(KeyCode.VcV)`
   - `await Task.Delay(10)`
   - `SimulateKeyRelease(KeyCode.VcLeftMeta)`
3. Else (Windows/Linux):
   - `SimulateKeyPress(KeyCode.VcLeftControl)`
   - `SimulateKeyPress(KeyCode.VcV)`
   - `SimulateKeyRelease(KeyCode.VcV)`
   - `await Task.Delay(10)`
   - `SimulateKeyRelease(KeyCode.VcLeftControl)`

- Left-specific keys for deterministic scan codes
- 10ms delay before modifier release guarantees OS processes the chord

### D7: IWindowActivator + WindowsWindowActivator

```csharp
public interface IWindowActivator
{
    void CapturePrevious();
    void Activate(IntPtr overlayHandle);
    void Restore();
}
```

**WindowsWindowActivator internals:**
- `IntPtr _previousWindow` — internal state, encapsulated
- `CapturePrevious()`: calls `NativeMethods.GetForegroundWindow()`, stores result
- `Activate(IntPtr overlayHandle)`:
  1. `uint fgThread = NativeMethods.GetWindowThreadProcessId(previousHandle, out _)`
  2. `uint myThread = NativeMethods.GetCurrentThreadId()` (or P/Invoke `GetCurrentThread`)
  3. `NativeMethods.AttachThreadInput(fgThread, myThread, true)`
  4. `NativeMethods.SetForegroundWindow(overlayHandle)`
  5. `NativeMethods.SetActiveWindow(overlayHandle)`
  6. `NativeMethods.SetFocus(overlayHandle)`
  7. `NativeMethods.AttachThreadInput(fgThread, myThread, false)`
- `Restore()`: if `_previousWindow != IntPtr.Zero`, call activation sequence targeting `_previousWindow`. Gracefully no-ops on zero handle.
- ViewModel never touches window handles — `AppManager` or view code-behind extracts handle via `TryGetPlatformHandle()`

### D8: IStartupRegistrar + WindowsStartupRegistrar

```csharp
public interface IStartupRegistrar
{
    Task<bool> GetIsRegisteredAsync();
    Task RegisterAsync();
    Task UnregisterAsync();
}
```

**WindowsStartupRegistrar internals:**
- Registry key: `HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Run`
- Value name: `"LaTeX Inserter"` (display name, user-friendly)
- Value data: quoted executable path — `$"\"{exePath}\""`
- **Path resolution:**
  - If running in Velopack installed context: resolve root stub executable (the launcher outside `app-x.y.z/`)
  - Otherwise: `Process.GetCurrentProcess().MainModule.FileName`
- Uses `Registry.CurrentUser.OpenSubKey(..., writable: true)` for register/unregister
- Async methods are for interface consistency (registry ops are sync internally)

### D9: NativeMethods — P/Invoke

```csharp
internal static partial class NativeMethods
{
    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(IntPtr hWnd);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial IntPtr SetFocus(IntPtr hWnd);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial IntPtr SetActiveWindow(IntPtr hWnd);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial uint GetCurrentThreadId();
}
```

- `internal static partial class` — CA1060 compliant, no public exposure
- All `[LibraryImport]` with `SetLastError = true`
- Separate file: `Platform/Windows/NativeMethods.cs`

### D10: DI Registration (Program.cs)

Add to `ConfigureServices`:

```csharp
// SharpHook
services.AddSingleton<SimpleGlobalHook>();

// Phase 2 services
services.AddSingleton<IHotkeyService, HotkeyService>();
services.AddSingleton<IClipboardProvider, AvaloniaClipboardProvider>();
services.AddSingleton<IInputSimulatorService, InputSimulatorService>();
services.AddSingleton<IWindowActivator, WindowsWindowActivator>();
services.AddSingleton<IStartupRegistrar, WindowsStartupRegistrar>();
```

- All singletons, strict constructor injection
- `SimpleGlobalHook` registered explicitly → `HotkeyService` receives it
- `IEventSimulator` NOT registered separately — `InputSimulatorService` gets it from `SimpleGlobalHook.EventSimulator` (injected hook provides it)

### D11: Test Plan

**HotkeyNormalizerTests:**
- Left/right modifier collapse (separate left-variant, right-variant, both)
- Toggle flags stripped (NumLock, CapsLock, ScrollLock)
- Mouse button flags stripped
- Sort order: Ctrl before Alt before Shift before Windows
- No-modifier chord (TriggerKey only)
- Empty/zero mask chord

**HotkeyBlocklistTests:**
- All 32 blocklist entries match after normalization
- Non-blocked chord returns false
- Normalized lookup (un-normalized input still blocked because IsBlocked normalizes)
- VcUndefined trigger key not blocked

**No unit tests for:** HotkeyService (hook threading), InputSimulatorService (clipboard + sim), WindowsWindowActivator (Win32), WindowsStartupRegistrar (registry) — integration-test only.
