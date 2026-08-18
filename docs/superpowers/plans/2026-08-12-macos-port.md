# macOS Port Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship LaTeX Inserter as a signed, notarized, auto-updating macOS menu-bar app alongside the existing Windows release, with zero regression to Windows behavior or packaging.

**Architecture:** Existing platform seams (`IWindowActivator`, `IOverlayPositioner`, `IStartupRegistrar`) get macOS implementations under `Platform/MacOS/`, built on Objective-C runtime + CoreGraphics + IOKit P/Invoke via `[LibraryImport]`. DI registration in `Program.cs` becomes OS-dispatched. New seams are added for app-data paths, hotkey display formatting, reserved-shortcut validation, and macOS permission status. Packaging adds a second CI leg that publishes `osx-arm64` + `osx-x64` Native AOT builds, wraps them in a `.app` bundle via Velopack, code-signs + notarizes + staples, and uploads to the same GitHub release as the Windows leg.

**Tech Stack:** .NET 10 Native AOT, Avalonia 12.0.4, SharpHook 6.0.0 (libuiohook), Velopack 1.2.0, CommunityToolkit.Mvvm 8.4.1, xunit 2.9.3 + NSubstitute 5.3.0, GitHub Actions (`windows-latest` + macOS runners).

---

## Global Constraints

- **Never break Windows.** Every task must leave `dotnet build LaTeXInserter.slnx` and `dotnet test` green on Windows, and must not change any Windows-observable behavior unless the task explicitly says so.
- **The solution file is `LaTeXInserter.slnx`**, not `LaTeXInserter.sln`. (`CLAUDE.md` and `docs/architecture.md` both say `.sln` — stale; Task 17 fixes them.)
- **Native AOT rules hold on both platforms:** `[LibraryImport]` + `partial` only (no `[DllImport]`), `JsonSerializerContext` source-gen only (no reflection JSON), no `Activator.CreateInstance`.
- **No service locator.** All new services are constructor-injected and registered in `Program.cs`.
- **Models must not import Services.** `HotkeyBlocklist` / `HotkeyChord` may reference other Models and the BCL only.
- **Settings JSON compatibility is frozen.** `ModifierMask.Windows` is serialized *by name* (`JsonStringEnumConverter`). Do **not** rename the enum member. macOS display changes are formatting-only.
- **Default hotkey stays `Ctrl+Alt+M` on both platforms** (rendered `⌃⌥M` on macOS). Rationale: identical `AppSettings.Default`, no settings migration, and `⌃⌥` avoids the crowded `⌘` namespace. Do not change `AppSettings.Default`.
- **macOS deployment floor: 13.0 (Ventura).** Required by `SMAppService`. Set `LSMinimumSystemVersion` to `13.0`.
- **Menu-bar-only app:** `LSUIElement = true` in `Info.plist`. No Dock icon, no menu bar app menu.
- **Architectures:** both `osx-arm64` and `osx-x64`.
- **Installer format:** Velopack `.pkg` (+ portable `.zip`). Not DMG — DMG would mean hand-rolling and losing Velopack's installer/update path.
- **App Sandbox is forbidden.** Velopack's updater writes outside the sandbox and requests elevation; sandboxed builds cannot update. Do not add `com.apple.security.app-sandbox`.
- **Prefer `OperatingSystem.IsMacOS()` / `IsWindows()`** over `RuntimeInformation.IsOSPlatform` in new code — it is the AOT-trim-friendly form and drives platform-compatibility analyzers.
- Tasks tagged **[MAC-ONLY]** cannot be executed or verified from Windows. An agent on Windows must implement the code, mark the task blocked on hardware, and hand the verification steps to a human with a Mac.

---

## Corrections to the original assessment

The original 8-point assessment is accurate on every point. Corrections and precision:

1. **`Program.cs`** — correct. Note it also registers `SimpleGlobalHook` directly (line 37). Keep `SimpleGlobalHook` specifically: SharpHook suppresses events **only** with `SimpleGlobalHook`; `EventLoopGlobalHook` and `TaskPoolGlobalHook` silently ignore `SuppressEvent` because their handlers run off the hook thread.
2. **`Platform/Windows`** — correct. Nuance: `Microsoft.Win32.Registry` still *compiles* for `net10.0` on macOS (the APIs are `[SupportedOSPlatform("windows")]` and throw `PlatformNotSupportedException` at runtime). So `WindowsStartupRegistrar.cs` does not need `#if` guards — it just must never be **registered** on macOS.
3. **`InputSimulatorService`** — correct, and the Cmd+V branch is structurally right. Two real risks it does not cover: (a) the 10 ms hold may be too short for some macOS apps; (b) `CGEventPost` is blocked entirely while **Secure Event Input** is active (see #9).
4. **`HotkeyService`** — correct. Add: macOS returns `UioHookResult.ErrorAxApiDisabled` when Accessibility is not granted, and `RunAsync` is currently double-wrapped in `Task.Run` (harmless — SharpHook already spawns its own thread). The main-thread run-loop requirement is satisfied *in principle* because Avalonia owns a main-thread run loop, but this is the single highest-risk unverified assumption in the port and Task 6 exists to prove it.
5. **`HotkeyChord`** — correct.
6. **`HotkeyBlocklist`** — correct.
7. **`WindowsOverlayPositioner`** — correct.
8. **`SubmitPasteService`** — correct.

### Missing from the original assessment

9. **Secure Event Input blocks everything.** When macOS has `EnableSecureEventInput` active — any focused password field, and **Terminal.app with "Secure Keyboard Entry" enabled** — the CGEventTap receives no keystrokes *and* synthetic events are dropped. Terminal is item 3 of your definition-of-done. The app must detect this (`IsSecureEventInputEnabled()`) and tell the user why nothing happened, rather than appearing broken.
10. **App-data path is wrong on macOS.** `Environment.GetFolderPath(SpecialFolder.ApplicationData)` returns `~/.config` on macOS on some .NET/macOS combinations (behavior shifted between .NET 7 and 8). `settings.json` and `custom_mappings.txt` must live in `~/Library/Application Support/LaTeX Inserter/`. Needs an explicit path seam.
11. **Tray icon asset.** `App.axaml` hardcodes a `.ico` for `TrayIcon.Icon`. macOS menu-bar items need a small PNG. Avalonia cannot mark it an NSImage *template*, so it will not auto-invert with the menu-bar appearance — pick a mid-tone monochrome asset and accept it as a documented limitation.
12. **Overlay window key-focus and Spaces behavior.** `WindowDecorations="None"` + `Topmost` + an `LSUIElement` accessory app is the exact combination where macOS refuses key-window status. Also `Topmost` alone will not float the overlay over a full-screen Space — that needs `NSWindowCollectionBehavior.CanJoinAllSpaces | FullScreenAuxiliary`.
13. **`OverlayWindow.OnDeactivated → _vm.Cancel()`** is a live hazard on macOS: activation churn during app activation can dismiss the overlay the instant it appears.
14. **Coordinate-system mismatch.** `CGEventGetLocation` returns points in a top-left-origin global space; Avalonia's `PixelPoint` / `Screens.WorkingArea` on macOS need empirical calibration, especially with mixed-scaling displays and monitors placed above/left of the primary (negative coordinates).
15. **Cross-app activation is restricted on macOS 14+.** `-[NSRunningApplication activateWithOptions:]` is deprecated and may be refused. Pair it with `-[NSApplication deactivate]` (yield activation) so focus returns to the previous app.
16. **`Info.plist` is the mechanism** for menu-bar-only. Velopack generates a default `Info.plist`; to get `LSUIElement`, `LSMinimumSystemVersion`, `NSHighResolutionCapable` and matching `CFBundleShortVersionString`/`CFBundleVersion` you must pass `--plist`.
17. **`.icns` generation needs macOS tools** (`sips` + `iconutil`). Generate in CI rather than committing a binary.
18. **Release-upload race.** Two `vpk upload github` invocations against the same tag in parallel will race. Sequence the macOS job after the Windows job.
19. **Velopack channels.** Windows and each macOS arch need distinct channels so the client resolves the right feed. Do **not** pass `--channel` on the Windows leg — that would change the existing channel and break updates for installed 0.0.x users.
20. **`csproj` conditioning.** `ApplicationManifest` (Windows-only) and `ApplicationIcon` (`.ico`) must be conditioned off for macOS publishes. `OutputType=WinExe` is harmless cross-platform and can stay.
21. **Test-suite fallout.** `SettingsServiceTests` will break when the path seam lands; `HotkeyBlocklistTests` and `HotkeyChordTests` need platform-parameterized cases. Add a macOS `dotnet test` leg to CI.
22. **Apple credential plumbing.** Apple Developer Program membership, a *Developer ID Application* cert and a *Developer ID Installer* cert exported as `.p12`, plus a `notarytool` keychain profile — and the GitHub Actions secrets + temporary-keychain steps to use them. None of this exists yet.
23. **Signing changes the permission grant.** macOS TCC keys Accessibility/Input Monitoring grants to the code signature. Every unsigned local rebuild re-prompts, and switching from unsigned dev builds to a signed release invalidates the grant. This must be documented or it will read as a bug.

---

## File Structure

**New — platform-neutral seams**
- `src/LaTeXInserter/Abstractions/IAppDataPathProvider.cs` — where settings + custom mappings live
- `src/LaTeXInserter/Abstractions/IPermissionService.cs` — macOS input-permission status + remediation
- `src/LaTeXInserter/Models/PlatformKind.cs` — `Windows | MacOS`, plus `Current`
- `src/LaTeXInserter/Models/HotkeyChordFormatter.cs` — pure chord→string, platform-parameterized
- `src/LaTeXInserter/Models/PermissionStatus.cs` — record describing AX / Input-Monitoring / secure-input state
- `src/LaTeXInserter/Services/DefaultAppDataPathProvider.cs` — Windows `%APPDATA%`, macOS `~/Library/Application Support`
- `src/LaTeXInserter/Services/NoOpPermissionService.cs` — Windows: always granted
- `src/LaTeXInserter/Platform/PlatformServiceRegistration.cs` — OS-dispatched DI wiring

**New — macOS implementations**
- `src/LaTeXInserter/Platform/MacOS/ObjC.cs` — `objc_getClass` / `sel_registerName` / typed `objc_msgSend` overloads, framework `dlopen`
- `src/LaTeXInserter/Platform/MacOS/MacNativeMethods.cs` — CoreGraphics, IOKit, ApplicationServices, Carbon imports
- `src/LaTeXInserter/Platform/MacOS/MacPermissionService.cs` — `IPermissionService`
- `src/LaTeXInserter/Platform/MacOS/MacWindowActivator.cs` — `IWindowActivator`
- `src/LaTeXInserter/Platform/MacOS/MacOverlayPositioner.cs` — `IOverlayPositioner`
- `src/LaTeXInserter/Platform/MacOS/MacStartupRegistrar.cs` — `IStartupRegistrar` via `SMAppService`
- `src/LaTeXInserter/Platform/MacOS/MacWindowBehavior.cs` — Spaces/level/activation tweaks for the overlay

**New — packaging**
- `build/macos/Info.plist` — bundle template
- `build/macos/entitlements.plist` — hardened-runtime entitlements
- `build/macos/make-icns.sh` — PNG → `.icns`
- `build/macos/build-local.sh` — unsigned local `.app` for development
- `src/LaTeXInserter/Assets/tray-macos.png` — menu-bar icon (18×18 @1x source at 36×36)

**Modified**
- `src/LaTeXInserter/Program.cs` — delegate platform registrations
- `src/LaTeXInserter/LaTeXInserter.csproj` — condition Windows-only properties
- `src/LaTeXInserter/App.axaml` / `App.axaml.cs` — tray icon per OS
- `src/LaTeXInserter/Models/HotkeyChord.cs` — delegate `ToString()` to formatter
- `src/LaTeXInserter/Models/HotkeyBlocklist.cs` — platform-aware sets
- `src/LaTeXInserter/Services/SettingsService.cs` — take `IAppDataPathProvider`
- `src/LaTeXInserter/Services/InputSimulatorService.cs` — longer macOS hold, secure-input guard
- `src/LaTeXInserter/Services/HotkeyService.cs` — surface hook start failure
- `src/LaTeXInserter/Views/OverlayWindow.axaml.cs` — macOS activation/deactivation handling
- `src/LaTeXInserter/ViewModels/SettingsViewModel.cs` + `Views/SettingsWindow.axaml` — permission panel
- `.github/workflows/release.yml` — Windows + macOS legs
- `CLAUDE.md`, `docs/architecture.md`, `README.md`

---

## Phase 0 — Platform seams (Windows-only work, fully testable on Windows)

### Task 1: App-data path seam

**Files:**
- Create: `src/LaTeXInserter/Abstractions/IAppDataPathProvider.cs`
- Create: `src/LaTeXInserter/Models/PlatformKind.cs`
- Create: `src/LaTeXInserter/Services/DefaultAppDataPathProvider.cs`
- Modify: `src/LaTeXInserter/Services/SettingsService.cs:14-22`
- Modify: `src/LaTeXInserter/Program.cs:32`
- Test: `tests/LaTeXInserter.Tests/AppDataPathProviderTests.cs`
- Test: `tests/LaTeXInserter.Tests/SettingsServiceTests.cs` (repair)

**Interfaces:**
- Produces: `enum PlatformKind { Windows, MacOS }` with `static PlatformKind Current { get; }`; `interface IAppDataPathProvider { string GetAppDataDirectory(); }`; `sealed class DefaultAppDataPathProvider(PlatformKind platform) : IAppDataPathProvider` with a parameterless ctor defaulting to `PlatformKind.Current`.
- Consumes: nothing.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/LaTeXInserter.Tests/AppDataPathProviderTests.cs
using LaTeXInserter.Models;
using LaTeXInserter.Services;
using Xunit;

namespace LaTeXInserter.Tests;

public class AppDataPathProviderTests
{
    [Fact]
    public void MacOs_UsesLibraryApplicationSupport()
    {
        var home = "/Users/testuser";
        var sut = new DefaultAppDataPathProvider(PlatformKind.MacOS, home);

        Assert.Equal("/Users/testuser/Library/Application Support/LaTeX Inserter",
            sut.GetAppDataDirectory().Replace('\\', '/'));
    }

    [Fact]
    public void Windows_UsesRoamingAppData()
    {
        var appData = @"C:\Users\testuser\AppData\Roaming";
        var sut = new DefaultAppDataPathProvider(PlatformKind.Windows, appData);

        Assert.Equal(@"C:\Users\testuser\AppData\Roaming\LaTeX Inserter".Replace('\\', '/'),
            sut.GetAppDataDirectory().Replace('\\', '/'));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test tests/LaTeXInserter.Tests --filter FullyQualifiedName~AppDataPathProviderTests
```

Expected: FAIL — `The type or namespace name 'PlatformKind' could not be found` and `DefaultAppDataPathProvider` unresolved.

- [ ] **Step 3: Write `PlatformKind`**

```csharp
// src/LaTeXInserter/Models/PlatformKind.cs
namespace LaTeXInserter.Models;

public enum PlatformKind
{
    Windows,
    MacOS
}

public static class PlatformKinds
{
    /// <summary>
    /// The platform the process is running on. Linux is intentionally unsupported —
    /// libuiohook cannot suppress events on X11 and Wayland is unsupported entirely.
    /// </summary>
    public static PlatformKind Current =>
        OperatingSystem.IsMacOS() ? PlatformKind.MacOS
        : OperatingSystem.IsWindows() ? PlatformKind.Windows
        : throw new PlatformNotSupportedException(
            "LaTeX Inserter supports Windows and macOS only.");
}
```

- [ ] **Step 4: Write the abstraction and implementation**

```csharp
// src/LaTeXInserter/Abstractions/IAppDataPathProvider.cs
namespace LaTeXInserter.Abstractions;

public interface IAppDataPathProvider
{
    /// <summary>Directory holding settings.json and custom_mappings.txt. Created if missing.</summary>
    string GetAppDataDirectory();
}
```

```csharp
// src/LaTeXInserter/Services/DefaultAppDataPathProvider.cs
using System.IO;
using LaTeXInserter.Abstractions;
using LaTeXInserter.Models;

namespace LaTeXInserter.Services;

/// <summary>
/// Environment.SpecialFolder.ApplicationData maps to ~/.config on macOS on several
/// .NET/macOS combinations, which is not where a Mac app is expected to store data.
/// The macOS branch therefore builds ~/Library/Application Support explicitly.
/// </summary>
public sealed class DefaultAppDataPathProvider : IAppDataPathProvider
{
    private const string AppFolderName = "LaTeX Inserter";

    private readonly PlatformKind _platform;
    private readonly string _root;

    public DefaultAppDataPathProvider()
        : this(PlatformKinds.Current, ResolveRoot(PlatformKinds.Current))
    {
    }

    public DefaultAppDataPathProvider(PlatformKind platform, string root)
    {
        _platform = platform;
        _root = root;
    }

    public string GetAppDataDirectory()
    {
        var dir = _platform switch
        {
            PlatformKind.MacOS => Path.Combine(_root, "Library", "Application Support", AppFolderName),
            _ => Path.Combine(_root, AppFolderName)
        };
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string ResolveRoot(PlatformKind platform) => platform switch
    {
        PlatformKind.MacOS => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        _ => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
    };
}
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet test tests/LaTeXInserter.Tests --filter FullyQualifiedName~AppDataPathProviderTests
```

Expected: PASS, 2 tests.

Note: `GetAppDataDirectory()` calls `Directory.CreateDirectory` — the tests above will create `/Users/testuser/...` relative to the drive root on Windows. If that trips permissions in your environment, change the test roots to `Path.Combine(Path.GetTempPath(), "latexinserter-test")` and assert on the suffix instead.

- [ ] **Step 6: Rewire `SettingsService` to the seam**

```csharp
// src/LaTeXInserter/Services/SettingsService.cs — replace the constructor
    public SettingsService(IAppDataPathProvider appDataPathProvider)
    {
        _appDataPath = appDataPathProvider.GetAppDataDirectory();
        _settingsPath = Path.Combine(_appDataPath, "settings.json");
        _customMappingsPath = Path.Combine(_appDataPath, "custom_mappings.txt");
    }
```

Delete the `Directory.CreateDirectory(_appDataPath)` line — the provider now owns it. Add `using LaTeXInserter.Abstractions;` if not already present.

- [ ] **Step 7: Register in the composition root**

In `src/LaTeXInserter/Program.cs`, immediately above the `ISettingsService` registration:

```csharp
        services.AddSingleton<IAppDataPathProvider, DefaultAppDataPathProvider>();
```

- [ ] **Step 8: Repair `SettingsServiceTests`**

Open `tests/LaTeXInserter.Tests/SettingsServiceTests.cs`. Every `new SettingsService()` must become `new SettingsService(new DefaultAppDataPathProvider(PlatformKind.Windows, tempRoot))` where `tempRoot` is a per-test temp directory. If the existing tests wrote to the real `%APPDATA%`, this is a strict improvement — keep the temp-dir version.

- [ ] **Step 9: Run the full suite**

```bash
dotnet test LaTeXInserter.slnx
```

Expected: PASS, no regressions.

- [ ] **Step 10: Commit**

```bash
git add src/LaTeXInserter/Abstractions/IAppDataPathProvider.cs src/LaTeXInserter/Models/PlatformKind.cs src/LaTeXInserter/Services/DefaultAppDataPathProvider.cs src/LaTeXInserter/Services/SettingsService.cs src/LaTeXInserter/Program.cs tests/LaTeXInserter.Tests/AppDataPathProviderTests.cs tests/LaTeXInserter.Tests/SettingsServiceTests.cs
git commit -m "feat: add app-data path seam with macOS Application Support location"
```

---

### Task 2: Platform-aware hotkey chord display

**Files:**
- Create: `src/LaTeXInserter/Models/HotkeyChordFormatter.cs`
- Modify: `src/LaTeXInserter/Models/HotkeyChord.cs:23-39`
- Test: `tests/LaTeXInserter.Tests/HotkeyChordTests.cs`

**Interfaces:**
- Consumes: `PlatformKind`, `PlatformKinds.Current` (Task 1).
- Produces: `static class HotkeyChordFormatter` with `static string Format(HotkeyChord chord, PlatformKind platform)`. `HotkeyChord.ToString()` becomes `HotkeyChordFormatter.Format(this, PlatformKinds.Current)`.

Serialization is untouched — `ModifierMask.Windows` keeps its name in `settings.json`. Only display changes.

macOS convention: modifiers render as glyphs in the canonical Apple order `⌃⌥⇧⌘` with **no separators**, then the trigger key. Windows keeps `Ctrl+Alt+Shift+Win+M`.

- [ ] **Step 1: Write the failing tests**

Append to `tests/LaTeXInserter.Tests/HotkeyChordTests.cs`:

```csharp
    [Fact]
    public void Format_MacOs_UsesAppleGlyphsInCanonicalOrder()
    {
        var chord = new HotkeyChord(
            ModifierMask.Control | ModifierMask.Alt, KeyCode.VcM);

        Assert.Equal("⌃⌥M", HotkeyChordFormatter.Format(chord, PlatformKind.MacOS));
    }

    [Fact]
    public void Format_MacOs_MetaRendersAsCommandGlyph()
    {
        var chord = new HotkeyChord(
            ModifierMask.Windows | ModifierMask.Shift, KeyCode.VcK);

        Assert.Equal("⇧⌘K", HotkeyChordFormatter.Format(chord, PlatformKind.MacOS));
    }

    [Fact]
    public void Format_Windows_IsUnchanged()
    {
        var chord = new HotkeyChord(
            ModifierMask.Control | ModifierMask.Alt, KeyCode.VcM);

        Assert.Equal("Ctrl+Alt+M", HotkeyChordFormatter.Format(chord, PlatformKind.Windows));
    }

    [Fact]
    public void Format_MacOs_UndefinedTriggerOmitsKey()
    {
        var chord = new HotkeyChord(ModifierMask.Control, KeyCode.VcUndefined);

        Assert.Equal("⌃", HotkeyChordFormatter.Format(chord, PlatformKind.MacOS));
    }
```

Add `using LaTeXInserter.Models;` and `using SharpHook.Data;` if the file lacks them.

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/LaTeXInserter.Tests --filter FullyQualifiedName~HotkeyChordTests
```

Expected: FAIL — `HotkeyChordFormatter` does not exist.

- [ ] **Step 3: Write the formatter**

```csharp
// src/LaTeXInserter/Models/HotkeyChordFormatter.cs
using System.Text;
using SharpHook.Data;

namespace LaTeXInserter.Models;

/// <summary>
/// Renders a HotkeyChord for display. Purely presentational — serialization of
/// ModifierMask is unaffected, so settings.json stays compatible across platforms.
/// </summary>
public static class HotkeyChordFormatter
{
    public static string Format(HotkeyChord chord, PlatformKind platform) =>
        platform == PlatformKind.MacOS ? FormatMac(chord) : FormatWindows(chord);

    private static string FormatMac(HotkeyChord chord)
    {
        // Apple's canonical order: Control, Option, Shift, Command — no separators.
        var sb = new StringBuilder(8);
        if ((chord.Modifiers & ModifierMask.Control) != 0) sb.Append('⌃'); // ⌃
        if ((chord.Modifiers & ModifierMask.Alt) != 0) sb.Append('⌥');     // ⌥
        if ((chord.Modifiers & ModifierMask.Shift) != 0) sb.Append('⇧');   // ⇧
        if ((chord.Modifiers & ModifierMask.Windows) != 0) sb.Append('⌘'); // ⌘
        if (chord.TriggerKey != KeyCode.VcUndefined) sb.Append(FormatKeyCode(chord.TriggerKey));
        return sb.ToString();
    }

    private static string FormatWindows(HotkeyChord chord)
    {
        var parts = new List<string>(5);
        if ((chord.Modifiers & ModifierMask.Control) != 0) parts.Add("Ctrl");
        if ((chord.Modifiers & ModifierMask.Alt) != 0) parts.Add("Alt");
        if ((chord.Modifiers & ModifierMask.Shift) != 0) parts.Add("Shift");
        if ((chord.Modifiers & ModifierMask.Windows) != 0) parts.Add("Win");
        if (chord.TriggerKey != KeyCode.VcUndefined) parts.Add(FormatKeyCode(chord.TriggerKey));
        return string.Join("+", parts);
    }

    private static string FormatKeyCode(KeyCode key)
    {
        var name = key.ToString();
        return name.StartsWith("Vc") ? name[2..] : name;
    }
}
```

- [ ] **Step 4: Delegate `HotkeyChord.ToString()`**

Replace the body of `ToString()` and delete the now-duplicated `FormatKeyCode` from `HotkeyChord.cs`:

```csharp
    public override string ToString() =>
        HotkeyChordFormatter.Format(this, PlatformKinds.Current);
```

`HotkeyChord.cs` keeps `using System.Text.Json.Serialization;` and `using SharpHook.Data;`; the `List<string>` usage is gone.

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet test LaTeXInserter.slnx
```

Expected: PASS. Any existing `ToString()` assertion in `HotkeyChordTests` still passes on Windows because `FormatWindows` is a verbatim copy of the old logic.

- [ ] **Step 6: Commit**

```bash
git add src/LaTeXInserter/Models/HotkeyChordFormatter.cs src/LaTeXInserter/Models/HotkeyChord.cs tests/LaTeXInserter.Tests/HotkeyChordTests.cs
git commit -m "feat: render hotkey chords with Apple modifier glyphs on macOS"
```

---

### Task 3: Platform-aware reserved-shortcut blocklist

**Files:**
- Modify: `src/LaTeXInserter/Models/HotkeyBlocklist.cs` (whole file)
- Modify: `src/LaTeXInserter/ViewModels/HotkeyDialogViewModel.cs:94`
- Test: `tests/LaTeXInserter.Tests/HotkeyBlocklistTests.cs`

**Interfaces:**
- Consumes: `PlatformKind`, `PlatformKinds.Current`.
- Produces: `static bool HotkeyBlocklist.IsBlocked(HotkeyChord chord, PlatformKind platform)` plus the existing `static bool IsBlocked(HotkeyChord chord)` overload, which now delegates with `PlatformKinds.Current`. `HotkeyBlocklist` must still import only Models + BCL + `SharpHook.Data`.

The macOS list is **not** the Windows list. Reserved macOS combinations worth blocking (system-level, cannot be overridden or would be actively hostile to capture): `⌘Space` and `⌃Space` (Spotlight / input-source switching), `⌘Tab` and `⌃Tab`-class app/window switching, `⌘Q`, `⌘W`, `⌘H`, `⌘M`, `⌘,` (universal app shortcuts), `⌘C/V/X/Z/A` (universal edit), `⌃↑`/`⌃↓`/`⌃←`/`⌃→` (Mission Control / Spaces), `⌘⇧3`, `⌘⇧4`, `⌘⇧5` (screenshots), `⌘⌃Q` (lock screen), `⌘⌥Esc` (Force Quit), `⌘⇧/` (Help search).

- [ ] **Step 1: Write the failing tests**

Append to `tests/LaTeXInserter.Tests/HotkeyBlocklistTests.cs`:

```csharp
    [Fact]
    public void MacOs_BlocksCommandSpace()
    {
        var chord = new HotkeyChord(ModifierMask.Windows, KeyCode.VcSpace);
        Assert.True(HotkeyBlocklist.IsBlocked(chord, PlatformKind.MacOS));
    }

    [Fact]
    public void MacOs_BlocksCommandQ()
    {
        var chord = new HotkeyChord(ModifierMask.Windows, KeyCode.VcQ);
        Assert.True(HotkeyBlocklist.IsBlocked(chord, PlatformKind.MacOS));
    }

    [Fact]
    public void MacOs_DoesNotBlockWindowsOnlyCombos()
    {
        // Win+E is Explorer on Windows; ⌘E is unremarkable on macOS.
        var chord = new HotkeyChord(ModifierMask.Windows, KeyCode.VcE);
        Assert.False(HotkeyBlocklist.IsBlocked(chord, PlatformKind.MacOS));
        Assert.True(HotkeyBlocklist.IsBlocked(chord, PlatformKind.Windows));
    }

    [Fact]
    public void MacOs_AllowsDefaultHotkey()
    {
        Assert.False(HotkeyBlocklist.IsBlocked(
            AppSettings.Default.Hotkey, PlatformKind.MacOS));
    }

    [Fact]
    public void Windows_AllowsDefaultHotkey()
    {
        Assert.False(HotkeyBlocklist.IsBlocked(
            AppSettings.Default.Hotkey, PlatformKind.Windows));
    }
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/LaTeXInserter.Tests --filter FullyQualifiedName~HotkeyBlocklistTests
```

Expected: FAIL — no two-argument `IsBlocked` overload.

- [ ] **Step 3: Rewrite the blocklist**

```csharp
// src/LaTeXInserter/Models/HotkeyBlocklist.cs
using System.Collections.Frozen;
using SharpHook.Data;

namespace LaTeXInserter.Models;

public static class HotkeyBlocklist
{
    private static readonly FrozenSet<HotkeyChord> WindowsBlocked = CreateWindowsBlocklist();
    private static readonly FrozenSet<HotkeyChord> MacBlocked = CreateMacBlocklist();

    public static bool IsBlocked(HotkeyChord chord) => IsBlocked(chord, PlatformKinds.Current);

    public static bool IsBlocked(HotkeyChord chord, PlatformKind platform) => platform switch
    {
        PlatformKind.MacOS => MacBlocked.Contains(chord),
        _ => WindowsBlocked.Contains(chord)
    };

    private static FrozenSet<HotkeyChord> CreateWindowsBlocklist()
    {
        var entries = new HashSet<HotkeyChord>
        {
            // System-critical
            new(ModifierMask.Control | ModifierMask.Alt, KeyCode.VcDelete),
            new(ModifierMask.Control | ModifierMask.Shift, KeyCode.VcEscape),

            // Alt combos
            new(ModifierMask.Alt, KeyCode.VcTab),
            new(ModifierMask.Alt | ModifierMask.Shift, KeyCode.VcTab),
            new(ModifierMask.Alt, KeyCode.VcF4),
            new(ModifierMask.Alt, KeyCode.VcSpace),
            new(ModifierMask.Alt, KeyCode.VcEscape),

            // Ctrl combos
            new(ModifierMask.Control, KeyCode.VcEscape),
            new(ModifierMask.Control, KeyCode.VcC),
            new(ModifierMask.Control, KeyCode.VcV),
            new(ModifierMask.Control, KeyCode.VcX),
            new(ModifierMask.Control, KeyCode.VcZ),
            new(ModifierMask.Control, KeyCode.VcA),

            // Win combos
            new(ModifierMask.Windows, KeyCode.VcTab),
            new(ModifierMask.Windows, KeyCode.VcL),
            new(ModifierMask.Windows, KeyCode.VcD),
            new(ModifierMask.Windows, KeyCode.VcE),
            new(ModifierMask.Windows, KeyCode.VcR),
            new(ModifierMask.Windows, KeyCode.VcI),
            new(ModifierMask.Windows, KeyCode.VcS),
            new(ModifierMask.Windows, KeyCode.VcA),
            new(ModifierMask.Windows, KeyCode.VcP),
            new(ModifierMask.Windows, KeyCode.VcV),
            new(ModifierMask.Windows, KeyCode.VcX),
            new(ModifierMask.Windows, KeyCode.VcG),
            new(ModifierMask.Windows, KeyCode.VcM),
            new(ModifierMask.Windows | ModifierMask.Shift, KeyCode.VcS),
            new(ModifierMask.Windows | ModifierMask.Control, KeyCode.VcD),
            new(ModifierMask.Windows | ModifierMask.Control, KeyCode.VcF4),
            new(ModifierMask.Windows | ModifierMask.Control, KeyCode.VcLeft),
            new(ModifierMask.Windows | ModifierMask.Control, KeyCode.VcRight),
            new(ModifierMask.Windows, KeyCode.VcUp),
            new(ModifierMask.Windows, KeyCode.VcDown),
            new(ModifierMask.Windows, KeyCode.VcLeft),
            new(ModifierMask.Windows, KeyCode.VcRight),
        };

        return entries.ToFrozenSet();
    }

    // ModifierMask.Windows is the Meta key == Command on macOS.
    private static FrozenSet<HotkeyChord> CreateMacBlocklist()
    {
        var entries = new HashSet<HotkeyChord>
        {
            // Spotlight / input source
            new(ModifierMask.Windows, KeyCode.VcSpace),
            new(ModifierMask.Windows | ModifierMask.Alt, KeyCode.VcSpace),
            new(ModifierMask.Control, KeyCode.VcSpace),

            // App / window switching
            new(ModifierMask.Windows, KeyCode.VcTab),
            new(ModifierMask.Windows | ModifierMask.Shift, KeyCode.VcTab),
            new(ModifierMask.Windows, KeyCode.VcBackquote),

            // Universal app commands
            new(ModifierMask.Windows, KeyCode.VcQ),
            new(ModifierMask.Windows, KeyCode.VcW),
            new(ModifierMask.Windows, KeyCode.VcH),
            new(ModifierMask.Windows, KeyCode.VcM),
            new(ModifierMask.Windows, KeyCode.VcComma),

            // Universal edit commands
            new(ModifierMask.Windows, KeyCode.VcC),
            new(ModifierMask.Windows, KeyCode.VcV),
            new(ModifierMask.Windows, KeyCode.VcX),
            new(ModifierMask.Windows, KeyCode.VcZ),
            new(ModifierMask.Windows, KeyCode.VcA),

            // Mission Control / Spaces
            new(ModifierMask.Control, KeyCode.VcUp),
            new(ModifierMask.Control, KeyCode.VcDown),
            new(ModifierMask.Control, KeyCode.VcLeft),
            new(ModifierMask.Control, KeyCode.VcRight),

            // Screenshots
            new(ModifierMask.Windows | ModifierMask.Shift, KeyCode.Vc3),
            new(ModifierMask.Windows | ModifierMask.Shift, KeyCode.Vc4),
            new(ModifierMask.Windows | ModifierMask.Shift, KeyCode.Vc5),

            // Lock screen, Force Quit, Help
            new(ModifierMask.Windows | ModifierMask.Control, KeyCode.VcQ),
            new(ModifierMask.Windows | ModifierMask.Alt, KeyCode.VcEscape),
            new(ModifierMask.Windows | ModifierMask.Shift, KeyCode.VcSlash),
        };

        return entries.ToFrozenSet();
    }
}
```

If any `KeyCode` member above does not exist in SharpHook 6.0.0 (`VcBackquote`, `VcComma`, `VcSlash`, `Vc3`–`Vc5`), open the enum via your IDE or `grep` the SharpHook package and substitute the correct member name. Do not delete the entry — find its real name.

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test LaTeXInserter.slnx
```

Expected: PASS. Existing Windows blocklist tests are unaffected — the Windows set is byte-identical to the original.

- [ ] **Step 5: Commit**

```bash
git add src/LaTeXInserter/Models/HotkeyBlocklist.cs tests/LaTeXInserter.Tests/HotkeyBlocklistTests.cs
git commit -m "feat: split reserved-shortcut blocklist into Windows and macOS sets"
```

---

### Task 4: OS-dispatched DI registration

**Files:**
- Create: `src/LaTeXInserter/Abstractions/IPermissionService.cs`
- Create: `src/LaTeXInserter/Models/PermissionStatus.cs`
- Create: `src/LaTeXInserter/Services/NoOpPermissionService.cs`
- Create: `src/LaTeXInserter/Platform/PlatformServiceRegistration.cs`
- Modify: `src/LaTeXInserter/Program.cs:44-47`
- Test: `tests/LaTeXInserter.Tests/PlatformServiceRegistrationTests.cs`

**Interfaces:**
- Consumes: `PlatformKind` (Task 1).
- Produces:
  - `sealed record PermissionStatus(bool AccessibilityGranted, bool InputMonitoringGranted, bool SecureInputActive)` with `static PermissionStatus AllGranted { get; }` and `bool IsUsable => AccessibilityGranted && InputMonitoringGranted`.
  - `interface IPermissionService { PermissionStatus Query(); void OpenAccessibilitySettings(); void OpenInputMonitoringSettings(); bool RequiresUserAction { get; } }`.
  - `static class PlatformServiceRegistration` with `static void AddPlatformServices(this IServiceCollection services, PlatformKind platform)`.

This task registers macOS types that **do not exist yet**. To keep the build green, register the Windows branch fully and have the macOS branch throw `PlatformNotSupportedException("macOS platform services land in Task 9.")` for now; Task 9 replaces that throw with the real registrations. The test asserts the Windows branch only.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/LaTeXInserter.Tests/PlatformServiceRegistrationTests.cs
using LaTeXInserter.Abstractions;
using LaTeXInserter.Models;
using LaTeXInserter.Platform;
using LaTeXInserter.Platform.Windows;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LaTeXInserter.Tests;

public class PlatformServiceRegistrationTests
{
    [Fact]
    public void Windows_ResolvesWindowsImplementations()
    {
        var services = new ServiceCollection();
        services.AddPlatformServices(PlatformKind.Windows);
        using var sp = services.BuildServiceProvider();

        Assert.IsType<WindowsWindowActivator>(sp.GetRequiredService<IWindowActivator>());
        Assert.IsType<WindowsOverlayPositioner>(sp.GetRequiredService<IOverlayPositioner>());
        Assert.IsType<WindowsStartupRegistrar>(sp.GetRequiredService<IStartupRegistrar>());
        Assert.IsType<NoOpPermissionService>(sp.GetRequiredService<IPermissionService>());
    }

    [Fact]
    public void Windows_PermissionServiceReportsEverythingGranted()
    {
        var sut = new NoOpPermissionService();

        Assert.True(sut.Query().IsUsable);
        Assert.False(sut.RequiresUserAction);
    }
}
```

`WindowsWindowActivator`, `WindowsOverlayPositioner` and `WindowsStartupRegistrar` are `internal`; `InternalsVisibleTo("LaTeXInserter.Tests")` is already declared in the csproj, so this compiles.

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test tests/LaTeXInserter.Tests --filter FullyQualifiedName~PlatformServiceRegistrationTests
```

Expected: FAIL — `AddPlatformServices`, `IPermissionService`, `NoOpPermissionService` unresolved.

- [ ] **Step 3: Write the permission model and abstraction**

```csharp
// src/LaTeXInserter/Models/PermissionStatus.cs
namespace LaTeXInserter.Models;

/// <summary>
/// Snapshot of the OS input permissions the app needs. On Windows every field is
/// always granted; on macOS these map to TCC Accessibility, TCC Input Monitoring,
/// and the transient EnableSecureEventInput state.
/// </summary>
public sealed record PermissionStatus(
    bool AccessibilityGranted,
    bool InputMonitoringGranted,
    bool SecureInputActive)
{
    public static PermissionStatus AllGranted { get; } = new(true, true, false);

    /// <summary>True when the global hook and paste simulation can work right now.</summary>
    public bool IsUsable => AccessibilityGranted && InputMonitoringGranted;
}
```

```csharp
// src/LaTeXInserter/Abstractions/IPermissionService.cs
using LaTeXInserter.Models;

namespace LaTeXInserter.Abstractions;

public interface IPermissionService
{
    /// <summary>Cheap, synchronous, safe to poll. Never prompts.</summary>
    PermissionStatus Query();

    /// <summary>True when this platform can ever require the user to grant permissions.</summary>
    bool RequiresUserAction { get; }

    /// <summary>Opens the OS settings pane for Accessibility. No-op where unsupported.</summary>
    void OpenAccessibilitySettings();

    /// <summary>Opens the OS settings pane for Input Monitoring. No-op where unsupported.</summary>
    void OpenInputMonitoringSettings();
}
```

```csharp
// src/LaTeXInserter/Services/NoOpPermissionService.cs
using LaTeXInserter.Abstractions;
using LaTeXInserter.Models;

namespace LaTeXInserter.Services;

/// <summary>Windows: global hooks need no user-granted permission.</summary>
public sealed class NoOpPermissionService : IPermissionService
{
    public PermissionStatus Query() => PermissionStatus.AllGranted;
    public bool RequiresUserAction => false;
    public void OpenAccessibilitySettings() { }
    public void OpenInputMonitoringSettings() { }
}
```

- [ ] **Step 4: Write the registration dispatcher**

```csharp
// src/LaTeXInserter/Platform/PlatformServiceRegistration.cs
using LaTeXInserter.Abstractions;
using LaTeXInserter.Models;
using LaTeXInserter.Platform.Windows;
using LaTeXInserter.Services;
using Microsoft.Extensions.DependencyInjection;

namespace LaTeXInserter.Platform;

public static class PlatformServiceRegistration
{
    public static void AddPlatformServices(this IServiceCollection services, PlatformKind platform)
    {
        switch (platform)
        {
            case PlatformKind.Windows:
                services.AddSingleton<IWindowActivator, WindowsWindowActivator>();
                services.AddSingleton<IOverlayPositioner, WindowsOverlayPositioner>();
                services.AddSingleton<IStartupRegistrar, WindowsStartupRegistrar>();
                services.AddSingleton<IPermissionService, NoOpPermissionService>();
                break;

            case PlatformKind.MacOS:
                // Replaced with real registrations in Task 9.
                throw new PlatformNotSupportedException(
                    "macOS platform services land in Task 9.");

            default:
                throw new PlatformNotSupportedException(
                    $"Unsupported platform: {platform}");
        }
    }
}
```

- [ ] **Step 5: Rewire `Program.cs`**

Delete these three lines from `ConfigureServices`:

```csharp
        services.AddSingleton<IWindowActivator, WindowsWindowActivator>();
        services.AddSingleton<IOverlayPositioner, WindowsOverlayPositioner>();
        ...
        services.AddSingleton<IStartupRegistrar, WindowsStartupRegistrar>();
```

Replace with:

```csharp
        services.AddPlatformServices(PlatformKinds.Current);
```

Swap `using LaTeXInserter.Platform.Windows;` for `using LaTeXInserter.Platform;` and add `using LaTeXInserter.Models;`. Keep `ISubmitPasteService` where it is — it is platform-neutral.

- [ ] **Step 6: Run the full suite**

```bash
dotnet test LaTeXInserter.slnx
```

Expected: PASS.

- [ ] **Step 7: Smoke-test the Windows app**

```bash
dotnet run --project src/LaTeXInserter
```

Expected: tray icon appears, Ctrl+Alt+M opens the overlay, Enter pastes. Quit from the tray.

- [ ] **Step 8: Commit**

```bash
git add src/LaTeXInserter/Abstractions/IPermissionService.cs src/LaTeXInserter/Models/PermissionStatus.cs src/LaTeXInserter/Services/NoOpPermissionService.cs src/LaTeXInserter/Platform/PlatformServiceRegistration.cs src/LaTeXInserter/Program.cs tests/LaTeXInserter.Tests/PlatformServiceRegistrationTests.cs
git commit -m "feat: dispatch platform service registration by OS"
```

---

## Phase 1 — macOS native layer

Everything in this phase compiles on Windows (P/Invoke declarations bind lazily) but only *runs* on macOS. Write it anywhere; verify on a Mac.

### Task 5: Objective-C runtime and native-method interop core

**Files:**
- Create: `src/LaTeXInserter/Platform/MacOS/ObjC.cs`
- Create: `src/LaTeXInserter/Platform/MacOS/MacNativeMethods.cs`
- Test: `tests/LaTeXInserter.Tests/MacInteropTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `static class ObjC` — `IntPtr GetClass(string name)`, `IntPtr Sel(string name)`, `IntPtr Send(IntPtr recv, IntPtr sel)`, `IntPtr Send(IntPtr recv, IntPtr sel, IntPtr arg)`, `IntPtr Send(IntPtr recv, IntPtr sel, int arg)`, `bool SendBool(IntPtr recv, IntPtr sel, IntPtr arg)`, `bool SendBoolUlong(IntPtr recv, IntPtr sel, ulong arg)`, `long SendLong(IntPtr recv, IntPtr sel)`, `int SendInt(IntPtr recv, IntPtr sel)`, `void SendVoid(IntPtr recv, IntPtr sel, IntPtr arg)`, `void LoadFramework(string absolutePath)`.
  - `static class MacNativeMethods` — `CGPoint GetCursorLocation()`, `bool AXIsProcessTrusted()`, `uint IOHIDCheckAccess(uint requestType)`, `bool IOHIDRequestAccess(uint requestType)`, `bool IsSecureEventInputEnabled()`, `struct CGPoint { double X; double Y; }`.

**Why hand-rolled objc interop:** it is the only approach that is Native-AOT-safe without adding a C toolchain step to the build. `objc_msgSend` is **not** variadic-safe on arm64 — each distinct signature needs its own `[LibraryImport(..., EntryPoint = "objc_msgSend")]` declaration with exact parameter types. Do not add a generic/`params` wrapper.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/LaTeXInserter.Tests/MacInteropTests.cs
using LaTeXInserter.Platform.MacOS;
using Xunit;

namespace LaTeXInserter.Tests;

public class MacInteropTests
{
    [Fact]
    public void CGPoint_IsBlittableTwoDoubles()
    {
        Assert.Equal(16, System.Runtime.InteropServices.Marshal.SizeOf<MacNativeMethods.CGPoint>());
    }

    [SkippableFact]
    public void GetClass_ResolvesNSObject()
    {
        Skip.IfNot(OperatingSystem.IsMacOS());
        Assert.NotEqual(IntPtr.Zero, ObjC.GetClass("NSObject"));
    }

    [SkippableFact]
    public void Sel_ResolvesDescription()
    {
        Skip.IfNot(OperatingSystem.IsMacOS());
        Assert.NotEqual(IntPtr.Zero, ObjC.Sel("description"));
    }
}
```

`SkippableFact` comes from the `Xunit.SkippableFact` package. Add it:

```bash
dotnet add tests/LaTeXInserter.Tests package Xunit.SkippableFact
```

If you prefer no new dependency, replace `[SkippableFact]` + `Skip.IfNot(...)` with `[Fact]` + an early `if (!OperatingSystem.IsMacOS()) return;`.

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test tests/LaTeXInserter.Tests --filter FullyQualifiedName~MacInteropTests
```

Expected: FAIL — `ObjC` / `MacNativeMethods` unresolved.

- [ ] **Step 3: Write `ObjC.cs`**

```csharp
// src/LaTeXInserter/Platform/MacOS/ObjC.cs
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LaTeXInserter.Platform.MacOS;

/// <summary>
/// Minimal Objective-C runtime bridge. Native-AOT safe: no reflection, no dynamic
/// dispatch. objc_msgSend is not variadic-safe on arm64, so every call shape gets
/// its own [LibraryImport] declaration with exact parameter types.
/// </summary>
[SupportedOSPlatform("macos")]
internal static partial class ObjC
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";

    [LibraryImport(LibObjC, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr objc_getClass(string name);

    [LibraryImport(LibObjC, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr sel_registerName(string name);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial IntPtr msgSend_ptr(IntPtr receiver, IntPtr selector);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial IntPtr msgSend_ptr_ptr(IntPtr receiver, IntPtr selector, IntPtr arg1);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial IntPtr msgSend_ptr_int(IntPtr receiver, IntPtr selector, int arg1);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool msgSend_bool_ptr(IntPtr receiver, IntPtr selector, IntPtr arg1);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool msgSend_bool_ulong(IntPtr receiver, IntPtr selector, ulong arg1);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool msgSend_bool_byte(IntPtr receiver, IntPtr selector, byte arg1);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial long msgSend_long(IntPtr receiver, IntPtr selector);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial int msgSend_int(IntPtr receiver, IntPtr selector);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial void msgSend_void_ptr(IntPtr receiver, IntPtr selector, IntPtr arg1);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial void msgSend_void_ulong(IntPtr receiver, IntPtr selector, ulong arg1);

    public static IntPtr GetClass(string name) => objc_getClass(name);
    public static IntPtr Sel(string name) => sel_registerName(name);

    public static IntPtr Send(IntPtr recv, IntPtr sel) => msgSend_ptr(recv, sel);
    public static IntPtr Send(IntPtr recv, IntPtr sel, IntPtr arg) => msgSend_ptr_ptr(recv, sel, arg);
    public static IntPtr Send(IntPtr recv, IntPtr sel, int arg) => msgSend_ptr_int(recv, sel, arg);
    public static bool SendBool(IntPtr recv, IntPtr sel, IntPtr arg) => msgSend_bool_ptr(recv, sel, arg);
    public static bool SendBoolUlong(IntPtr recv, IntPtr sel, ulong arg) => msgSend_bool_ulong(recv, sel, arg);
    public static bool SendBoolByte(IntPtr recv, IntPtr sel, byte arg) => msgSend_bool_byte(recv, sel, arg);
    public static long SendLong(IntPtr recv, IntPtr sel) => msgSend_long(recv, sel);
    public static int SendInt(IntPtr recv, IntPtr sel) => msgSend_int(recv, sel);
    public static void SendVoid(IntPtr recv, IntPtr sel, IntPtr arg) => msgSend_void_ptr(recv, sel, arg);
    public static void SendVoidUlong(IntPtr recv, IntPtr sel, ulong arg) => msgSend_void_ulong(recv, sel, arg);

    /// <summary>
    /// Frameworks whose classes are not already loaded (ServiceManagement) must be
    /// dlopen'd before objc_getClass can find them. AppKit is already resident
    /// because Avalonia links it, but loading twice is harmless and cheap.
    /// </summary>
    public static void LoadFramework(string absolutePath)
    {
        if (!NativeLibrary.TryLoad(absolutePath, out _))
            throw new DllNotFoundException($"Failed to load framework: {absolutePath}");
    }

    public const string AppKitPath =
        "/System/Library/Frameworks/AppKit.framework/AppKit";
    public const string ServiceManagementPath =
        "/System/Library/Frameworks/ServiceManagement.framework/ServiceManagement";
}
```

- [ ] **Step 4: Write `MacNativeMethods.cs`**

```csharp
// src/LaTeXInserter/Platform/MacOS/MacNativeMethods.cs
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LaTeXInserter.Platform.MacOS;

[SupportedOSPlatform("macos")]
internal static partial class MacNativeMethods
{
    private const string CoreGraphics =
        "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string ApplicationServices =
        "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
    private const string IOKit =
        "/System/Library/Frameworks/IOKit.framework/IOKit";
    private const string Carbon =
        "/System/Library/Frameworks/Carbon.framework/Carbon";

    /// <summary>Global display coordinates, top-left origin, in points (not pixels).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CGPoint
    {
        public double X;
        public double Y;
    }

    // kIOHIDRequestTypeListenEvent
    public const uint IOHIDRequestTypeListenEvent = 1;
    // IOHIDAccessType
    public const uint IOHIDAccessTypeGranted = 0;
    public const uint IOHIDAccessTypeDenied = 1;
    public const uint IOHIDAccessTypeUnknown = 2;

    [LibraryImport(CoreGraphics)]
    private static partial IntPtr CGEventCreate(IntPtr source);

    [LibraryImport(CoreGraphics)]
    private static partial CGPoint CGEventGetLocation(IntPtr @event);

    [LibraryImport(CoreFoundation)]
    private static partial void CFRelease(IntPtr cf);

    [LibraryImport(ApplicationServices)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool AXIsProcessTrusted();

    [LibraryImport(IOKit)]
    public static partial uint IOHIDCheckAccess(uint requestType);

    [LibraryImport(IOKit)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool IOHIDRequestAccess(uint requestType);

    [LibraryImport(Carbon)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool IsSecureEventInputEnabled();

    /// <summary>Current mouse location in global display coordinates (top-left origin).</summary>
    public static CGPoint GetCursorLocation()
    {
        var evt = CGEventCreate(IntPtr.Zero);
        if (evt == IntPtr.Zero)
            return default;

        try
        {
            return CGEventGetLocation(evt);
        }
        finally
        {
            CFRelease(evt);
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet test LaTeXInserter.slnx
```

Expected: PASS on Windows — the `CGPoint` size test runs, the two macOS tests skip.

- [ ] **Step 6: Verify the AOT publish still succeeds**

```bash
dotnet publish src/LaTeXInserter -c Release -r win-x64 -o publish-check
```

Expected: no IL2xxx/AOT warnings introduced. Delete `publish-check` afterward.

- [ ] **Step 7: Commit**

```bash
git add src/LaTeXInserter/Platform/MacOS/ObjC.cs src/LaTeXInserter/Platform/MacOS/MacNativeMethods.cs tests/LaTeXInserter.Tests/MacInteropTests.cs tests/LaTeXInserter.Tests/LaTeXInserter.Tests.csproj
git commit -m "feat: add macOS Objective-C and native-method interop core"
```

---

### Task 6: macOS permission service + hook viability spike **[MAC-ONLY verification]**

**Files:**
- Create: `src/LaTeXInserter/Platform/MacOS/MacPermissionService.cs`
- Modify: `src/LaTeXInserter/Abstractions/IHotkeyService.cs`
- Modify: `src/LaTeXInserter/Services/HotkeyService.cs:50-55`
- Test: `tests/LaTeXInserter.Tests/HotkeyServiceStartTests.cs`

**Interfaces:**
- Consumes: `ObjC`, `MacNativeMethods` (Task 5); `IPermissionService`, `PermissionStatus` (Task 4).
- Produces:
  - `sealed class MacPermissionService : IPermissionService`.
  - `IHotkeyService` gains `event EventHandler<string>? HookFailed;` and `bool IsRunning { get; }`.

**This task contains the port's riskiest assumption.** SharpHook's macOS docs say the global hook needs a main-thread run loop and that UI frameworks normally provide one. Avalonia does own a main-thread run loop, but the hook is started from a thread-pool task in `HotkeyService.StartAsync`. Step 8 is a mandatory empirical check — if the hook does not deliver events, the fallback is to marshal `_hook.Run()` differently (see Step 9 notes) and this becomes its own task.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/LaTeXInserter.Tests/HotkeyServiceStartTests.cs
using LaTeXInserter.Services;
using SharpHook;
using Xunit;

namespace LaTeXInserter.Tests;

public class HotkeyServiceStartTests
{
    [Fact]
    public void HookFailed_RaisedWhenRunAsyncThrows()
    {
        // SimpleGlobalHook cannot be substituted (non-virtual members), so drive the
        // failure path through a disposed hook: RunAsync on a disposed hook faults.
        var hook = new SimpleGlobalHook();
        hook.Dispose();

        var sut = new HotkeyService(hook);
        string? reported = null;
        sut.HookFailed += (_, message) => reported = message;

        sut.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        SpinWait.SpinUntil(() => reported is not null, TimeSpan.FromSeconds(5));

        Assert.NotNull(reported);
        Assert.False(sut.IsRunning);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test tests/LaTeXInserter.Tests --filter FullyQualifiedName~HotkeyServiceStartTests
```

Expected: FAIL — `HookFailed` and `IsRunning` do not exist on `HotkeyService`.

- [ ] **Step 3: Extend `IHotkeyService`**

Add to `src/LaTeXInserter/Abstractions/IHotkeyService.cs`:

```csharp
    /// <summary>Raised when the global hook fails to start or dies. Argument is a user-facing message.</summary>
    event EventHandler<string>? HookFailed;

    /// <summary>True once the hook is running and delivering events.</summary>
    bool IsRunning { get; }
```

- [ ] **Step 4: Implement in `HotkeyService`**

Replace `StartAsync` and add the backing state:

```csharp
    private volatile bool _isRunning;

    public bool IsRunning => _isRunning;
    public event EventHandler<string>? HookFailed;

    public Task StartAsync(CancellationToken ct)
    {
        // Fire-and-forget on thread pool — RunAsync must not block caller.
        // SharpHook already runs the native hook on its own dedicated thread; the
        // Task.Run wrapper only keeps the synchronous setup off the caller.
        _ = Task.Run(async () =>
        {
            try
            {
                _isRunning = true;
                await _hook.RunAsync();
                _isRunning = false;
            }
            catch (Exception ex)
            {
                _isRunning = false;
                Dispatcher.UIThread.Post(() =>
                    HookFailed?.Invoke(this, DescribeFailure(ex)));
            }
        }, ct);

        return Task.CompletedTask;
    }

    private static string DescribeFailure(Exception ex)
    {
        // SharpHook surfaces macOS permission denial as HookException with
        // UioHookResult.ErrorAxApiDisabled.
        if (ex is HookException he && he.Result == UioHookResult.ErrorAxApiDisabled)
        {
            return "macOS denied access to keyboard events. Grant LaTeX Inserter "
                 + "Accessibility and Input Monitoring access in System Settings, "
                 + "then quit and reopen the app.";
        }

        return $"The global keyboard hook could not start: {ex.Message}";
    }
```

Add `using SharpHook.Data;` if `UioHookResult` is not already in scope. If `HookException` exposes the result under a different member name in SharpHook 6.0.0, adjust — do not delete the branch.

- [ ] **Step 5: Run test to verify it passes**

```bash
dotnet test tests/LaTeXInserter.Tests --filter FullyQualifiedName~HotkeyServiceStartTests
```

Expected: PASS.

- [ ] **Step 6: Write `MacPermissionService`**

```csharp
// src/LaTeXInserter/Platform/MacOS/MacPermissionService.cs
using System.Diagnostics;
using System.Runtime.Versioning;
using LaTeXInserter.Abstractions;
using LaTeXInserter.Models;

namespace LaTeXInserter.Platform.MacOS;

/// <summary>
/// Reads macOS TCC state for the two permissions libuiohook needs.
///
/// Query() never prompts — it is safe to poll from the UI. The system prompt is
/// raised by libuiohook itself the first time it tries to create an event tap
/// without Accessibility access; this service only reports status and deep-links
/// the user to the right settings pane.
///
/// TCC grants are keyed to the app's code signature. Unsigned local builds are
/// re-prompted on every rebuild, and moving from an unsigned dev build to a signed
/// release invalidates the existing grant. That is expected, not a bug.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacPermissionService : IPermissionService
{
    private const string AccessibilityPane =
        "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility";
    private const string InputMonitoringPane =
        "x-apple.systempreferences:com.apple.preference.security?Privacy_ListenEvent";

    public bool RequiresUserAction => true;

    public PermissionStatus Query()
    {
        bool accessibility = MacNativeMethods.AXIsProcessTrusted();

        uint hid = MacNativeMethods.IOHIDCheckAccess(
            MacNativeMethods.IOHIDRequestTypeListenEvent);
        // Unknown means "not yet asked" — treat as not granted so the UI prompts.
        bool inputMonitoring = hid == MacNativeMethods.IOHIDAccessTypeGranted;

        bool secureInput = MacNativeMethods.IsSecureEventInputEnabled();

        return new PermissionStatus(accessibility, inputMonitoring, secureInput);
    }

    public void OpenAccessibilitySettings() => OpenUrl(AccessibilityPane);
    public void OpenInputMonitoringSettings() => OpenUrl(InputMonitoringPane);

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo("open", url) { UseShellExecute = false });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open settings pane {url}: {ex}");
        }
    }
}
```

- [ ] **Step 7: Build and run the full suite**

```bash
dotnet test LaTeXInserter.slnx
```

Expected: PASS.

- [ ] **Step 8: [MAC-ONLY] Prove the hook works under Avalonia**

On a Mac with the repo checked out and .NET 10 installed:

```bash
dotnet run --project src/LaTeXInserter
```

Grant Accessibility + Input Monitoring when prompted (System Settings → Privacy & Security). Then verify, in order:

1. The app appears in the menu bar.
2. Pressing `Ctrl+Alt+M` opens the overlay. **If it does not, the hook is not receiving events — stop and go to Step 9.**
3. Pressing `Ctrl+Alt+M` does **not** type an `m` into whatever had focus (proves `SuppressEvent` works).
4. In Settings → Change Hotkey, pressed keys appear live in the dialog (proves recording mode works).
5. Revoke Accessibility in System Settings, relaunch, confirm the app reports the failure rather than silently doing nothing.

Record the results in `docs/macos-verification.md` (created in Task 16 — create the file early if you reach this step first).

- [ ] **Step 9: [MAC-ONLY] Fallback if Step 8.2 fails**

If the hook receives no events, the main-thread run loop is not being serviced for libuiohook. In order of preference:

1. Start the hook from the Avalonia UI thread instead of a thread-pool task: in `AppManager.InitializeAsync`, wrap the `StartAsync` call in `Dispatcher.UIThread.Post(...)`. SharpHook still moves the blocking native call to its own thread; this only changes which thread performs setup.
2. Construct `SimpleGlobalHook` with `runAsyncOnBackgroundThread: true` and re-test.
3. P/Invoke `CFRunLoopRun` on a dedicated thread that owns the hook, per SharpHook's OS-constraints guidance.

Whichever works, capture it as a code change plus a comment explaining why, and add a note to `docs/architecture.md`.

- [ ] **Step 10: Commit**

```bash
git add src/LaTeXInserter/Platform/MacOS/MacPermissionService.cs src/LaTeXInserter/Abstractions/IHotkeyService.cs src/LaTeXInserter/Services/HotkeyService.cs tests/LaTeXInserter.Tests/HotkeyServiceStartTests.cs
git commit -m "feat: add macOS permission service and surface global hook failures"
```

---

### Task 7: macOS window activator

**Files:**
- Create: `src/LaTeXInserter/Platform/MacOS/MacWindowActivator.cs`
- Test: none automatable — behavior is verified in Task 16's manual matrix.

**Interfaces:**
- Consumes: `ObjC` (Task 5), `IWindowActivator` (existing).
- Produces: `sealed class MacWindowActivator : IWindowActivator` with the existing three members: `CapturePrevious()`, `Activate(IntPtr overlayHandle)`, `Restore()`.

**Design:** store the previous app's **pid**, not an `NSRunningApplication*` — the pointer would need manual retain/release, the pid does not. `Activate` ignores the passed handle: on macOS the correct move is to activate the *application*, because Avalonia's `IPlatformHandle` on macOS is an `NSView*`, not the `NSWindow*` the Windows implementation assumes. `Restore` pairs `-[NSApplication deactivate]` (yield activation, which is always permitted) with `-[NSRunningApplication activateWithOptions:]` (request activation, which macOS 14+ may refuse). Doing both makes the common case correct and the restricted case degrade to "focus returns to whatever was in front", which is the previous app anyway.

- [ ] **Step 1: Write `MacWindowActivator`**

```csharp
// src/LaTeXInserter/Platform/MacOS/MacWindowActivator.cs
using System.Diagnostics;
using System.Runtime.Versioning;
using LaTeXInserter.Abstractions;

namespace LaTeXInserter.Platform.MacOS;

/// <summary>
/// Captures and restores the frontmost application around the overlay.
///
/// The overlayHandle argument is ignored: Avalonia's macOS platform handle is an
/// NSView*, and macOS activation is per-application, not per-window. Activating
/// NSApp is both correct and sufficient because the app is an LSUIElement accessory
/// with exactly one visible window at a time.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacWindowActivator : IWindowActivator
{
    // NSApplicationActivationOptions
    private const ulong ActivateAllWindows = 1UL << 0;
    private const ulong ActivateIgnoringOtherApps = 1UL << 1;

    private int _previousPid;

    static MacWindowActivator()
    {
        ObjC.LoadFramework(ObjC.AppKitPath);
    }

    public void CapturePrevious()
    {
        try
        {
            var workspaceClass = ObjC.GetClass("NSWorkspace");
            var workspace = ObjC.Send(workspaceClass, ObjC.Sel("sharedWorkspace"));
            var frontmost = ObjC.Send(workspace, ObjC.Sel("frontmostApplication"));

            _previousPid = frontmost == IntPtr.Zero
                ? 0
                : ObjC.SendInt(frontmost, ObjC.Sel("processIdentifier"));
        }
        catch (Exception ex)
        {
            _previousPid = 0;
            Debug.WriteLine($"CapturePrevious failed: {ex}");
        }
    }

    public void Activate(IntPtr overlayHandle)
    {
        try
        {
            var appClass = ObjC.GetClass("NSApplication");
            var app = ObjC.Send(appClass, ObjC.Sel("sharedApplication"));
            // -[NSApplication activateIgnoringOtherApps:] takes a BOOL.
            ObjC.SendBoolByte(app, ObjC.Sel("activateIgnoringOtherApps:"), 1);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Activate failed: {ex}");
        }
    }

    public void Restore()
    {
        if (_previousPid == 0) return;

        try
        {
            // 1. Yield activation. Always permitted, unlike cross-app activation.
            var appClass = ObjC.GetClass("NSApplication");
            var app = ObjC.Send(appClass, ObjC.Sel("sharedApplication"));
            ObjC.Send(app, ObjC.Sel("deactivate"));

            // 2. Ask the previous app to come forward. macOS 14+ may refuse this;
            //    step 1 already put us behind it, so the outcome is still correct.
            var runningAppClass = ObjC.GetClass("NSRunningApplication");
            var target = ObjC.Send(
                runningAppClass,
                ObjC.Sel("runningApplicationWithProcessIdentifier:"),
                _previousPid);

            if (target != IntPtr.Zero)
            {
                ObjC.SendBoolUlong(
                    target,
                    ObjC.Sel("activateWithOptions:"),
                    ActivateAllWindows | ActivateIgnoringOtherApps);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Restore failed: {ex}");
        }
    }
}
```

- [ ] **Step 2: Build**

```bash
dotnet build LaTeXInserter.slnx
```

Expected: success, no warnings about `[SupportedOSPlatform]` call sites (the class is only constructed from the macOS DI branch, which Task 9 guards).

- [ ] **Step 3: Commit**

```bash
git add src/LaTeXInserter/Platform/MacOS/MacWindowActivator.cs
git commit -m "feat: add macOS window activator using NSWorkspace and NSRunningApplication"
```

---

### Task 8: macOS overlay positioner and window behavior

**Files:**
- Create: `src/LaTeXInserter/Platform/MacOS/MacOverlayPositioner.cs`
- Create: `src/LaTeXInserter/Platform/MacOS/MacWindowBehavior.cs`
- Test: `tests/LaTeXInserter.Tests/OverlayPositionerTests.cs` (extend)

**Interfaces:**
- Consumes: `ObjC`, `MacNativeMethods` (Task 5); `MacWindowActivator` via `IWindowActivator` (Task 7); `OverlayPositioner.GetPosition` (existing static helper in `Helpers/`).
- Produces:
  - `sealed class MacOverlayPositioner(IWindowActivator windowActivator) : IOverlayPositioner`.
  - `static class MacWindowBehavior` with `static void ConfigureOverlay(Window window)` — sets collection behavior and window level so the overlay floats over full-screen Spaces.

**Coordinate note:** `CGEventGetLocation` returns points in a top-left-origin global space spanning all displays. Avalonia's `PixelPoint` on macOS is also top-left-origin, but is expressed in the same "point" units rather than physical pixels, so the two agree on non-Retina and disagree by the scale factor on Retina only if Avalonia reports physical pixels. Step 4 is an explicit calibration check — do not skip it, and do not "fix" a mismatch by guessing a multiplier without measuring.

- [ ] **Step 1: Write the failing test**

The pure geometry helper is already covered by `OverlayPositionerTests`. Add a regression case for a display placed above/left of the primary (negative origin), which is where sign errors show up:

```csharp
    [Fact]
    public void GetPosition_ClampsWithinScreenWithNegativeOrigin()
    {
        // Secondary display positioned above and left of the primary.
        var workingArea = new PixelRect(-1920, -1080, 1920, 1080);
        var cursor = new PixelPoint(-100, -100);
        var size = new PixelSize(350, 120);

        var result = OverlayPositioner.GetPosition(cursor, size, workingArea);

        Assert.True(result.X >= workingArea.X);
        Assert.True(result.Y >= workingArea.Y);
        Assert.True(result.X + size.Width <= workingArea.X + workingArea.Width);
        Assert.True(result.Y + size.Height <= workingArea.Y + workingArea.Height);
    }
```

- [ ] **Step 2: Run test**

```bash
dotnet test tests/LaTeXInserter.Tests --filter FullyQualifiedName~OverlayPositionerTests
```

If it FAILS, fix `Helpers/OverlayPositioner.GetPosition` to clamp against `workingArea.X`/`workingArea.Y` rather than assuming a zero origin, then re-run until PASS. If it already PASSES, the helper is origin-safe — record that and move on.

- [ ] **Step 3: Write `MacWindowBehavior`**

```csharp
// src/LaTeXInserter/Platform/MacOS/MacWindowBehavior.cs
using System.Diagnostics;
using System.Runtime.Versioning;
using Avalonia.Controls;

namespace LaTeXInserter.Platform.MacOS;

/// <summary>
/// macOS-specific NSWindow tweaks Avalonia does not expose.
///
/// Topmost alone does not float a window over a full-screen Space — that needs
/// CanJoinAllSpaces + FullScreenAuxiliary collection behavior. Without this the
/// overlay is invisible whenever the user is in a full-screen app, which is most
/// of the time for editors and browsers.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class MacWindowBehavior
{
    // NSWindowCollectionBehavior
    private const ulong CanJoinAllSpaces = 1UL << 0;
    private const ulong FullScreenAuxiliary = 1UL << 8;

    // NSStatusWindowLevel — above normal and floating windows, below screen saver.
    private const long StatusWindowLevel = 25;

    private static bool _configured;

    public static void ConfigureOverlay(Window window)
    {
        if (_configured) return;

        try
        {
            var handle = window.TryGetPlatformHandle();
            if (handle is null || handle.Handle == IntPtr.Zero) return;

            // Avalonia's macOS handle is an NSView*; -[NSView window] gives the NSWindow*.
            var nsWindow = ObjC.Send(handle.Handle, ObjC.Sel("window"));
            if (nsWindow == IntPtr.Zero) return;

            ObjC.SendVoidUlong(
                nsWindow,
                ObjC.Sel("setCollectionBehavior:"),
                CanJoinAllSpaces | FullScreenAuxiliary);

            ObjC.SendVoidUlong(
                nsWindow,
                ObjC.Sel("setLevel:"),
                unchecked((ulong)StatusWindowLevel));

            _configured = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ConfigureOverlay failed: {ex}");
        }
    }
}
```

If `handle.HandleDescriptor` turns out to be `"NSWindow"` rather than `"NSView"` in Avalonia 12.0.4, skip the `-[NSView window]` hop and use `handle.Handle` directly. Verify by logging `handle.HandleDescriptor` once on a Mac before trusting either branch.

- [ ] **Step 4: Write `MacOverlayPositioner`**

```csharp
// src/LaTeXInserter/Platform/MacOS/MacOverlayPositioner.cs
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using LaTeXInserter.Abstractions;
using LaTeXInserter.Helpers;

namespace LaTeXInserter.Platform.MacOS;

[SupportedOSPlatform("macos")]
internal sealed class MacOverlayPositioner : IOverlayPositioner
{
    private readonly IWindowActivator _windowActivator;

    public MacOverlayPositioner(IWindowActivator windowActivator)
    {
        _windowActivator = windowActivator;
    }

    public void PositionOverlay(Window window)
    {
        if (window.ClientSize.Height <= 0)
            return;

        MacWindowBehavior.ConfigureOverlay(window);

        var location = MacNativeMethods.GetCursorLocation();
        var cursorPos = new PixelPoint((int)location.X, (int)location.Y);

        var screen = window.Screens.ScreenFromPoint(cursorPos) ?? window.Screens.Primary!;
        var scaling = screen.Scaling;
        var physicalSize = new PixelSize(
            (int)(window.ClientSize.Width * scaling),
            (int)(window.ClientSize.Height * scaling));

        window.Position = OverlayPositioner.GetPosition(cursorPos, physicalSize, screen.WorkingArea);
        window.Opacity = 1;

        // Bring the app forward so the borderless overlay can become key window.
        // An LSUIElement app will not get key status otherwise.
        _windowActivator.Activate(IntPtr.Zero);
        window.Activate();
    }
}
```

- [ ] **Step 5: Build and test**

```bash
dotnet test LaTeXInserter.slnx
```

Expected: PASS.

- [ ] **Step 6: [MAC-ONLY] Calibrate coordinates**

Temporarily add, at the top of `PositionOverlay`:

```csharp
        System.Diagnostics.Debug.WriteLine(
            $"CG=({location.X},{location.Y}) " +
            $"screen={screen.Bounds} working={screen.WorkingArea} scaling={screen.Scaling}");
```

Run the app on a Mac with at least two displays of differing scale, trigger the overlay near each screen's corners, and confirm:

- `ScreenFromPoint` selects the display the cursor is actually on.
- The overlay appears adjacent to the cursor, not offset by a factor of the scale.
- The overlay never straddles a screen edge.

If the overlay is offset by exactly the scale factor, multiply `location.X`/`location.Y` by `screen.Scaling` before constructing `cursorPos` — but only after measuring, and add a comment recording the measurement. Remove the debug line when done.

- [ ] **Step 7: Commit**

```bash
git add src/LaTeXInserter/Platform/MacOS/MacOverlayPositioner.cs src/LaTeXInserter/Platform/MacOS/MacWindowBehavior.cs src/LaTeXInserter/Helpers/OverlayPositioner.cs tests/LaTeXInserter.Tests/OverlayPositionerTests.cs
git commit -m "feat: add macOS overlay positioner with Spaces-aware window behavior"
```

---

### Task 9: macOS login item registrar + activate the macOS DI branch

**Files:**
- Create: `src/LaTeXInserter/Platform/MacOS/MacStartupRegistrar.cs`
- Modify: `src/LaTeXInserter/Platform/PlatformServiceRegistration.cs`
- Test: `tests/LaTeXInserter.Tests/PlatformServiceRegistrationTests.cs` (extend)

**Interfaces:**
- Consumes: `ObjC` (Task 5), `IStartupRegistrar` (existing), `MacPermissionService` (Task 6), `MacWindowActivator` (Task 7), `MacOverlayPositioner` (Task 8).
- Produces: `sealed class MacStartupRegistrar : IStartupRegistrar` implementing the existing four members: `GetIsRegisteredAsync()`, `RegisterAsync()`, `UnregisterAsync()`, `SyncRegistrationAsync(bool desired)`.

**Design:** use `SMAppService.mainApp` (macOS 13+), per Apple's current guidance. `status` is the mechanism that satisfies your requirement to *reflect when the user disables it in System Settings* — `SMAppServiceStatus` returns `RequiresApproval` when the user has toggled the login item off, distinct from `NotRegistered`. `GetIsRegisteredAsync` therefore reports `Enabled` only.

`SMAppServiceStatus`: `NotRegistered = 0`, `Enabled = 1`, `RequiresApproval = 2`, `NotFound = 3`.

- [ ] **Step 1: Write `MacStartupRegistrar`**

```csharp
// src/LaTeXInserter/Platform/MacOS/MacStartupRegistrar.cs
using System.Diagnostics;
using System.Runtime.Versioning;
using LaTeXInserter.Abstractions;

namespace LaTeXInserter.Platform.MacOS;

/// <summary>
/// Login-item registration via ServiceManagement's SMAppService (macOS 13+),
/// the supported replacement for SMLoginItemSetEnabled and LaunchAgent plists.
///
/// Registration targets the running .app bundle, so it works from any location —
/// which matters because a Velopack .app is portable and need not live in
/// /Applications. Registering requires a real bundle: running loose from
/// `dotnet run` will fail here, and that failure is expected in development.
///
/// status() distinguishes "never registered" from "user switched it off in
/// System Settings" (RequiresApproval), so the Settings checkbox can reflect the
/// user's own choice instead of silently re-enabling it.
/// </summary>
[SupportedOSPlatform("macos13.0")]
internal sealed class MacStartupRegistrar : IStartupRegistrar
{
    private const long StatusNotRegistered = 0;
    private const long StatusEnabled = 1;
    private const long StatusRequiresApproval = 2;
    private const long StatusNotFound = 3;

    static MacStartupRegistrar()
    {
        ObjC.LoadFramework(ObjC.ServiceManagementPath);
    }

    public Task<bool> GetIsRegisteredAsync()
    {
        try
        {
            return Task.FromResult(GetStatus() == StatusEnabled);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GetIsRegisteredAsync failed: {ex}");
            return Task.FromResult(false);
        }
    }

    public Task RegisterAsync()
    {
        var service = MainAppService();
        // -[SMAppService registerAndReturnError:] — pass NULL for the error out-param.
        bool ok = ObjC.SendBool(service, ObjC.Sel("registerAndReturnError:"), IntPtr.Zero);
        if (!ok)
            Debug.WriteLine("SMAppService registerAndReturnError: returned NO.");
        return Task.CompletedTask;
    }

    public Task UnregisterAsync()
    {
        var service = MainAppService();
        bool ok = ObjC.SendBool(service, ObjC.Sel("unregisterAndReturnError:"), IntPtr.Zero);
        if (!ok)
            Debug.WriteLine("SMAppService unregisterAndReturnError: returned NO.");
        return Task.CompletedTask;
    }

    public async Task SyncRegistrationAsync(bool desired)
    {
        long status = GetStatus();

        // The user disabling the login item in System Settings must stick. Only
        // re-register when the app has genuinely never been registered.
        if (desired && status == StatusNotRegistered)
        {
            await RegisterAsync();
        }
        else if (!desired && status is StatusEnabled or StatusRequiresApproval)
        {
            await UnregisterAsync();
        }
    }

    /// <summary>Raw SMAppServiceStatus, for the Settings UI to explain RequiresApproval.</summary>
    public long GetStatus()
    {
        var service = MainAppService();
        return service == IntPtr.Zero ? StatusNotFound : ObjC.SendLong(service, ObjC.Sel("status"));
    }

    private static IntPtr MainAppService()
    {
        var cls = ObjC.GetClass("SMAppService");
        // +[SMAppService mainAppService] backs the `SMAppService.mainApp` property.
        return cls == IntPtr.Zero ? IntPtr.Zero : ObjC.Send(cls, ObjC.Sel("mainAppService"));
    }
}
```

If `+mainAppService` does not resolve at runtime (returns `nil`), dump the class's method list with `class_copyMethodList` once to find the real selector name before changing anything else — the Swift-visible `SMAppService.mainApp` property maps to an Objective-C class method whose name must be confirmed on-device.

- [ ] **Step 2: Activate the macOS DI branch**

Replace the `PlatformKind.MacOS` case in `PlatformServiceRegistration.cs`:

```csharp
            case PlatformKind.MacOS:
                services.AddSingleton<IWindowActivator, MacWindowActivator>();
                services.AddSingleton<IOverlayPositioner, MacOverlayPositioner>();
                services.AddSingleton<IStartupRegistrar, MacStartupRegistrar>();
                services.AddSingleton<IPermissionService, MacPermissionService>();
                break;
```

Add `using LaTeXInserter.Platform.MacOS;`. The `[SupportedOSPlatform]` annotations will produce CA1416 warnings at these call sites — suppress them for the case block with an explanatory comment rather than removing the annotations:

```csharp
#pragma warning disable CA1416 // Guarded by the PlatformKind.MacOS switch arm.
```

- [ ] **Step 3: Extend the registration test**

```csharp
    [Fact]
    public void MacOs_ResolvesMacImplementations()
    {
        var services = new ServiceCollection();
        services.AddPlatformServices(PlatformKind.MacOS);

        // Resolution is not attempted off-macOS — the static ctors dlopen frameworks.
        // Asserting on the descriptors keeps this test runnable on Windows CI.
        Assert.Equal(typeof(MacWindowActivator),
            services.Single(d => d.ServiceType == typeof(IWindowActivator)).ImplementationType);
        Assert.Equal(typeof(MacOverlayPositioner),
            services.Single(d => d.ServiceType == typeof(IOverlayPositioner)).ImplementationType);
        Assert.Equal(typeof(MacStartupRegistrar),
            services.Single(d => d.ServiceType == typeof(IStartupRegistrar)).ImplementationType);
        Assert.Equal(typeof(MacPermissionService),
            services.Single(d => d.ServiceType == typeof(IPermissionService)).ImplementationType);
    }
```

Add `using System.Linq;` and `using LaTeXInserter.Platform.MacOS;`.

- [ ] **Step 4: Run the full suite**

```bash
dotnet test LaTeXInserter.slnx
```

Expected: PASS on Windows. The macOS types are never instantiated there, so no framework loading occurs.

- [ ] **Step 5: Commit**

```bash
git add src/LaTeXInserter/Platform/MacOS/MacStartupRegistrar.cs src/LaTeXInserter/Platform/PlatformServiceRegistration.cs tests/LaTeXInserter.Tests/PlatformServiceRegistrationTests.cs
git commit -m "feat: register macOS platform services and add SMAppService login item"
```

---

## Phase 2 — macOS user experience

### Task 10: Permission status UX

**Files:**
- Modify: `src/LaTeXInserter/ViewModels/SettingsViewModel.cs`
- Modify: `src/LaTeXInserter/Views/SettingsWindow.axaml`
- Modify: `src/LaTeXInserter/ViewModels/AppManager.cs:66-117`
- Test: `tests/LaTeXInserter.Tests/SettingsViewModelTests.cs` (extend)

**Interfaces:**
- Consumes: `IPermissionService`, `PermissionStatus` (Task 4); `IHotkeyService.HookFailed` (Task 6).
- Produces: on `SettingsViewModel` — `bool ShowPermissionPanel`, `bool AccessibilityGranted`, `bool InputMonitoringGranted`, `string PermissionSummary`, `RelayCommand OpenAccessibilitySettingsCommand`, `RelayCommand OpenInputMonitoringSettingsCommand`, `RelayCommand RefreshPermissionsCommand`, and `void RefreshPermissions()`.

The panel is entirely hidden on Windows (`ShowPermissionPanel` is false when `IPermissionService.RequiresUserAction` is false), so the Windows Settings window is visually unchanged.

- [ ] **Step 1: Write the failing tests**

Append to `tests/LaTeXInserter.Tests/SettingsViewModelTests.cs`:

```csharp
    [Fact]
    public void PermissionPanel_HiddenWhenPlatformNeedsNoPermissions()
    {
        var permissions = Substitute.For<IPermissionService>();
        permissions.RequiresUserAction.Returns(false);
        permissions.Query().Returns(PermissionStatus.AllGranted);

        var sut = CreateSut(permissions);
        sut.RefreshPermissions();

        Assert.False(sut.ShowPermissionPanel);
    }

    [Fact]
    public void PermissionPanel_ShownWhenAccessibilityDenied()
    {
        var permissions = Substitute.For<IPermissionService>();
        permissions.RequiresUserAction.Returns(true);
        permissions.Query().Returns(new PermissionStatus(false, true, false));

        var sut = CreateSut(permissions);
        sut.RefreshPermissions();

        Assert.True(sut.ShowPermissionPanel);
        Assert.False(sut.AccessibilityGranted);
        Assert.True(sut.InputMonitoringGranted);
        Assert.Contains("Accessibility", sut.PermissionSummary);
    }

    [Fact]
    public void PermissionSummary_MentionsSecureInputWhenActive()
    {
        var permissions = Substitute.For<IPermissionService>();
        permissions.RequiresUserAction.Returns(true);
        permissions.Query().Returns(new PermissionStatus(true, true, true));

        var sut = CreateSut(permissions);
        sut.RefreshPermissions();

        Assert.True(sut.ShowPermissionPanel);
        Assert.Contains("Secure Keyboard Entry", sut.PermissionSummary);
    }

    [Fact]
    public void OpenAccessibilitySettings_DelegatesToPermissionService()
    {
        var permissions = Substitute.For<IPermissionService>();
        permissions.RequiresUserAction.Returns(true);
        permissions.Query().Returns(new PermissionStatus(false, false, false));

        var sut = CreateSut(permissions);
        sut.OpenAccessibilitySettingsCommand.Execute(null);

        permissions.Received(1).OpenAccessibilitySettings();
    }
```

`CreateSut` is whatever factory the existing tests in this file already use to build a `SettingsViewModel` with substituted dependencies — extend it to take the new `IPermissionService` parameter (defaulting to a substitute that reports `RequiresUserAction == false`) so existing tests keep compiling unchanged.

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/LaTeXInserter.Tests --filter FullyQualifiedName~SettingsViewModelTests
```

Expected: FAIL — the new members do not exist.

- [ ] **Step 3: Extend `SettingsViewModel`**

Add the dependency to the constructor (append the parameter; do not reorder existing ones) and the members:

```csharp
    private readonly IPermissionService _permissionService;

    [ObservableProperty] private bool _showPermissionPanel;
    [ObservableProperty] private bool _accessibilityGranted;
    [ObservableProperty] private bool _inputMonitoringGranted;
    [ObservableProperty] private string _permissionSummary = string.Empty;

    /// <summary>
    /// Re-reads OS permission state. Cheap and non-prompting, so it is safe to call
    /// on every window open and after the user returns from System Settings.
    /// </summary>
    public void RefreshPermissions()
    {
        if (!_permissionService.RequiresUserAction)
        {
            ShowPermissionPanel = false;
            AccessibilityGranted = true;
            InputMonitoringGranted = true;
            PermissionSummary = string.Empty;
            return;
        }

        var status = _permissionService.Query();
        AccessibilityGranted = status.AccessibilityGranted;
        InputMonitoringGranted = status.InputMonitoringGranted;
        ShowPermissionPanel = !status.IsUsable || status.SecureInputActive;
        PermissionSummary = BuildSummary(status);
    }

    private static string BuildSummary(PermissionStatus status)
    {
        if (!status.AccessibilityGranted && !status.InputMonitoringGranted)
            return "LaTeX Inserter needs Accessibility and Input Monitoring access to "
                 + "detect the hotkey and paste. Grant both, then quit and reopen the app.";

        if (!status.AccessibilityGranted)
            return "LaTeX Inserter needs Accessibility access to detect the hotkey and "
                 + "paste. Grant it, then quit and reopen the app.";

        if (!status.InputMonitoringGranted)
            return "LaTeX Inserter needs Input Monitoring access to detect the hotkey. "
                 + "Grant it, then quit and reopen the app.";

        if (status.SecureInputActive)
            return "Another app has Secure Keyboard Entry turned on, which blocks all "
                 + "hotkeys and pasting system-wide. Terminal enables it under "
                 + "Terminal ▸ Secure Keyboard Entry. Turn it off to use LaTeX Inserter.";

        return string.Empty;
    }

    [RelayCommand]
    private void OpenAccessibilitySettings() => _permissionService.OpenAccessibilitySettings();

    [RelayCommand]
    private void OpenInputMonitoringSettings() => _permissionService.OpenInputMonitoringSettings();

    [RelayCommand]
    private void RefreshPermissionsCmd() => RefreshPermissions();
```

Call `RefreshPermissions()` at the end of the existing `Open()` method so the panel is current every time the window is shown.

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/LaTeXInserter.Tests --filter FullyQualifiedName~SettingsViewModelTests
```

Expected: PASS.

- [ ] **Step 5: Add the panel to `SettingsWindow.axaml`**

Insert above the existing "General" section content. Bind visibility to `ShowPermissionPanel` so Windows renders nothing:

```xml
        <Border IsVisible="{Binding ShowPermissionPanel}"
                Background="#3A2F1E"
                BorderBrush="#FFB74D"
                BorderThickness="1"
                CornerRadius="4"
                Padding="12"
                Margin="0,0,0,16">
            <StackPanel Spacing="8">
                <TextBlock Text="Permissions needed"
                           FontWeight="SemiBold"
                           Foreground="#FFB74D" />
                <TextBlock Text="{Binding PermissionSummary}"
                           TextWrapping="Wrap"
                           Foreground="#DDDDDD" />
                <StackPanel Orientation="Horizontal" Spacing="8">
                    <Button Content="Open Accessibility…"
                            Command="{Binding OpenAccessibilitySettingsCommand}"
                            IsVisible="{Binding !AccessibilityGranted}" />
                    <Button Content="Open Input Monitoring…"
                            Command="{Binding OpenInputMonitoringSettingsCommand}"
                            IsVisible="{Binding !InputMonitoringGranted}" />
                    <Button Content="Re-check"
                            Command="{Binding RefreshPermissionsCmdCommand}" />
                </StackPanel>
            </StackPanel>
        </Border>
```

Note the generated command name is `RefreshPermissionsCmdCommand` because the method is `RefreshPermissionsCmd` — renaming the method to `RefreshPermissions` would collide with the public method of the same name. Keep them distinct.

- [ ] **Step 6: Surface hook failures from `AppManager`**

In `AppManager.InitializeAsync`, after wiring the other events:

```csharp
            _hotkeyService.HookFailed += OnHookFailed;
```

and add the handler:

```csharp
    private void OnHookFailed(object? sender, string message)
    {
        Debug.WriteLine($"Global hook failed: {message}");
        // Open Settings so the user sees the permission panel and the reason.
        OnSettingsRequested(this, EventArgs.Empty);
    }
```

Unsubscribe it in `Dispose()` alongside the other handlers.

- [ ] **Step 7: Run the full suite and smoke-test Windows**

```bash
dotnet test LaTeXInserter.slnx
```

```bash
dotnet run --project src/LaTeXInserter
```

Expected: tests PASS; the Windows Settings window looks exactly as before (no permission panel).

- [ ] **Step 8: Commit**

```bash
git add src/LaTeXInserter/ViewModels/SettingsViewModel.cs src/LaTeXInserter/Views/SettingsWindow.axaml src/LaTeXInserter/ViewModels/AppManager.cs tests/LaTeXInserter.Tests/SettingsViewModelTests.cs
git commit -m "feat: add macOS permission status panel and hook-failure recovery path"
```

---

### Task 11: macOS menu-bar icon

**Files:**
- Create: `src/LaTeXInserter/Assets/tray-macos.png` (36×36 monochrome, transparent background)
- Modify: `src/LaTeXInserter/App.axaml` (remove hardcoded `Icon`)
- Modify: `src/LaTeXInserter/App.axaml.cs:33-35`

**Interfaces:**
- Consumes: `PlatformKinds.Current` (Task 1).
- Produces: no new public API.

**Known limitation to document, not fix:** Avalonia loads the tray image from a stream, so it cannot be marked an NSImage *template*. The icon will not auto-invert between light and dark menu bars. Ship a mid-tone monochrome glyph that reads acceptably on both, and record the limitation in `docs/architecture.md` (Task 17).

- [ ] **Step 1: Produce the asset**

Derive `tray-macos.png` from the existing `LaTeX-Inserter-icon-final.png`: 36×36 px, transparent background, single flat colour `#8A8A8A`, glyph occupying ~28×28 px with padding. Any image editor works; commit the PNG.

- [ ] **Step 2: Make the tray icon platform-conditional**

In `App.axaml`, delete the `Icon` attribute from the `TrayIcon` element, leaving:

```xml
    <TrayIcon.Icons>
        <TrayIcons>
            <TrayIcon ToolTipText="LaTeX Inserter" />
        </TrayIcons>
    </TrayIcon.Icons>
```

In `App.axaml.cs`, where the tray menu is wired:

```csharp
            var trayIcon = TrayIcon.GetIcons(this)?[0];
            if (trayIcon is not null)
            {
                trayIcon.Menu = trayIconViewModel.TrayMenu;
                trayIcon.Icon = LoadTrayIcon();
            }
```

and add:

```csharp
    /// <summary>
    /// Windows keeps the multi-resolution .ico. macOS needs a small PNG — Avalonia
    /// cannot decode .ico into an NSStatusItem image reliably, and the menu bar
    /// wants a ~18pt glyph rather than a full-colour app icon.
    /// </summary>
    private static WindowIcon LoadTrayIcon()
    {
        var uri = PlatformKinds.Current == PlatformKind.MacOS
            ? new Uri("avares://LaTeXInserter/Assets/tray-macos.png")
            : new Uri("avares://LaTeXInserter/Assets/LaTeX-Inserter-icon-final.ico");

        using var stream = AssetLoader.Open(uri);
        return new WindowIcon(stream);
    }
```

Add `using Avalonia.Platform;` and `using LaTeXInserter.Models;`.

- [ ] **Step 3: Build and smoke-test Windows**

```bash
dotnet run --project src/LaTeXInserter
```

Expected: the Windows tray icon is unchanged.

- [ ] **Step 4: Commit**

```bash
git add src/LaTeXInserter/Assets/tray-macos.png src/LaTeXInserter/App.axaml src/LaTeXInserter/App.axaml.cs
git commit -m "feat: load a macOS-specific menu bar icon"
```

---

### Task 12: Overlay focus behavior and paste timing on macOS

**Files:**
- Modify: `src/LaTeXInserter/Views/OverlayWindow.axaml.cs:88-91`
- Modify: `src/LaTeXInserter/Services/InputSimulatorService.cs`
- Modify: `src/LaTeXInserter/Services/SubmitPasteService.cs:19`
- Test: `tests/LaTeXInserter.Tests/InputSimulatorServiceTests.cs`

**Interfaces:**
- Consumes: `IEventSimulator` (SharpHook), `PlatformKinds.Current` (Task 1), `IPermissionService` (Task 4).
- Produces: `InputSimulatorService` gains a constructor overload `InputSimulatorService(IEventSimulator simulator, PlatformKind platform, int modifierHoldMs)`; the DI-visible constructor keeps its current single-parameter shape plus the new `IPermissionService`.

Three changes:

1. **Deactivation guard.** `OnDeactivated → _vm.Cancel()` dismisses the overlay whenever it loses focus. On macOS, activation churn while the app comes forward can fire this before the user types anything. Ignore deactivations that arrive within a short grace window after the overlay becomes visible.
2. **Modifier hold duration.** 10 ms is marginal on macOS. Use 10 ms on Windows, 40 ms on macOS.
3. **Secure-input guard.** If Secure Event Input is active, `CGEventPost` is silently dropped. Detect and report instead of failing invisibly.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/LaTeXInserter.Tests/InputSimulatorServiceTests.cs
using LaTeXInserter.Models;
using LaTeXInserter.Services;
using NSubstitute;
using SharpHook;
using SharpHook.Data;
using Xunit;

namespace LaTeXInserter.Tests;

public class InputSimulatorServiceTests
{
    [Fact]
    public async Task Windows_PressesControlV()
    {
        var sim = Substitute.For<IEventSimulator>();
        var permissions = Substitute.For<IPermissionService>();
        permissions.Query().Returns(PermissionStatus.AllGranted);

        var sut = new InputSimulatorService(sim, permissions, PlatformKind.Windows, modifierHoldMs: 0);
        await sut.SimulatePasteAsync("x");

        Received.InOrder(() =>
        {
            sim.SimulateKeyPress(KeyCode.VcLeftControl);
            sim.SimulateKeyPress(KeyCode.VcV);
            sim.SimulateKeyRelease(KeyCode.VcV);
            sim.SimulateKeyRelease(KeyCode.VcLeftControl);
        });
    }

    [Fact]
    public async Task MacOs_PressesCommandV()
    {
        var sim = Substitute.For<IEventSimulator>();
        var permissions = Substitute.For<IPermissionService>();
        permissions.Query().Returns(PermissionStatus.AllGranted);

        var sut = new InputSimulatorService(sim, permissions, PlatformKind.MacOS, modifierHoldMs: 0);
        await sut.SimulatePasteAsync("x");

        Received.InOrder(() =>
        {
            sim.SimulateKeyPress(KeyCode.VcLeftMeta);
            sim.SimulateKeyPress(KeyCode.VcV);
            sim.SimulateKeyRelease(KeyCode.VcV);
            sim.SimulateKeyRelease(KeyCode.VcLeftMeta);
        });
    }

    [Fact]
    public async Task SecureInputActive_SkipsSimulationAndReportsBlocked()
    {
        var sim = Substitute.For<IEventSimulator>();
        var permissions = Substitute.For<IPermissionService>();
        permissions.Query().Returns(new PermissionStatus(true, true, SecureInputActive: true));

        var sut = new InputSimulatorService(sim, permissions, PlatformKind.MacOS, modifierHoldMs: 0);

        string? blocked = null;
        sut.PasteBlocked += (_, reason) => blocked = reason;

        await sut.SimulatePasteAsync("x");

        sim.DidNotReceiveWithAnyArgs().SimulateKeyPress(default);
        Assert.NotNull(blocked);
        Assert.Contains("Secure Keyboard Entry", blocked);
    }
```

Add `using LaTeXInserter.Abstractions;`.

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/LaTeXInserter.Tests --filter FullyQualifiedName~InputSimulatorServiceTests
```

Expected: FAIL — no such constructor, no `PasteBlocked` event.

- [ ] **Step 3: Rewrite `InputSimulatorService`**

```csharp
using LaTeXInserter.Abstractions;
using LaTeXInserter.Models;
using SharpHook;
using SharpHook.Data;

namespace LaTeXInserter.Services;

internal sealed class InputSimulatorService : IInputSimulatorService
{
    // macOS drops synthetic modifier chords that are held too briefly; 10ms is
    // reliable on Windows but marginal on macOS.
    private const int WindowsHoldMs = 10;
    private const int MacHoldMs = 40;

    private readonly IEventSimulator _simulator;
    private readonly IPermissionService _permissions;
    private readonly PlatformKind _platform;
    private readonly int _modifierHoldMs;

    /// <summary>Raised when the paste could not be simulated. Argument is user-facing.</summary>
    public event EventHandler<string>? PasteBlocked;

    public InputSimulatorService(IEventSimulator simulator, IPermissionService permissions)
        : this(simulator, permissions, PlatformKinds.Current,
               PlatformKinds.Current == PlatformKind.MacOS ? MacHoldMs : WindowsHoldMs)
    {
    }

    public InputSimulatorService(
        IEventSimulator simulator,
        IPermissionService permissions,
        PlatformKind platform,
        int modifierHoldMs)
    {
        _simulator = simulator;
        _permissions = permissions;
        _platform = platform;
        _modifierHoldMs = modifierHoldMs;
    }

    public async Task SimulatePasteAsync(string unicodeText)
    {
        // EnableSecureEventInput makes CGEventPost a no-op. Without this check the
        // app looks broken: the clipboard is set but nothing is ever pasted.
        var status = _permissions.Query();
        if (status.SecureInputActive)
        {
            PasteBlocked?.Invoke(this,
                "Paste was blocked because Secure Keyboard Entry is active. The text is "
              + "on the clipboard — press Cmd+V, or turn off Terminal ▸ Secure Keyboard Entry.");
            return;
        }

        var modifier = _platform == PlatformKind.MacOS
            ? KeyCode.VcLeftMeta
            : KeyCode.VcLeftControl;

        _simulator.SimulateKeyPress(modifier);
        _simulator.SimulateKeyPress(KeyCode.VcV);
        _simulator.SimulateKeyRelease(KeyCode.VcV);
        if (_modifierHoldMs > 0)
            await Task.Delay(_modifierHoldMs);
        _simulator.SimulateKeyRelease(modifier);
    }
}
```

- [ ] **Step 4: Increase the focus-settle delay on macOS**

In `SubmitPasteService`, the `pasteDelayMs` default of 50 ms is tuned for Win32 foreground transitions. macOS app activation is slower. Change the default parameter to be platform-derived:

```csharp
    public SubmitPasteService(
        IClipboardProvider clipboardProvider,
        IWindowActivator windowActivator,
        IInputSimulatorService inputSimulator,
        int? pasteDelayMs = null)
    {
        _clipboardProvider = clipboardProvider;
        _windowActivator = windowActivator;
        _inputSimulator = inputSimulator;
        _pasteDelayMs = pasteDelayMs
            ?? (PlatformKinds.Current == PlatformKind.MacOS ? 120 : 50);
    }
```

Add `using LaTeXInserter.Models;`. Windows behavior is unchanged at 50 ms. The macOS value is a starting point — Task 16 measures whether 120 ms is enough.

- [ ] **Step 5: Add the deactivation grace window**

In `OverlayWindow.axaml.cs`:

```csharp
    // macOS raises Deactivated transiently while the app comes forward, which would
    // dismiss the overlay the instant it appears. Ignore deactivations that land
    // within this window of the overlay becoming visible.
    private static readonly TimeSpan DeactivateGrace = TimeSpan.FromMilliseconds(400);
    private DateTime _shownAtUtc = DateTime.MinValue;
```

Set `_shownAtUtc = DateTime.UtcNow;` in the `IsVisibleProperty` branch of `OnPropertyChanged`, and guard the handler:

```csharp
    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (DateTime.UtcNow - _shownAtUtc < DeactivateGrace)
            return;

        _vm?.Cancel();
    }
```

- [ ] **Step 6: Run tests to verify they pass**

```bash
dotnet test LaTeXInserter.slnx
```

Expected: PASS. If `OverlayViewModelTests` asserted immediate cancel-on-deactivate, update it to reflect the grace window.

- [ ] **Step 7: Wire `PasteBlocked` to the UI**

`IInputSimulatorService` gains the event declaration; `AppManager` subscribes and surfaces the message the same way it handles `HookFailed` (open Settings, which shows the secure-input line in `PermissionSummary`). Unsubscribe in `Dispose()`.

- [ ] **Step 8: Smoke-test Windows**

```bash
dotnet run --project src/LaTeXInserter
```

Expected: hotkey → type `\alpha` → Enter → `α` pastes, exactly as before.

- [ ] **Step 9: Commit**

```bash
git add src/LaTeXInserter/Views/OverlayWindow.axaml.cs src/LaTeXInserter/Services/InputSimulatorService.cs src/LaTeXInserter/Services/SubmitPasteService.cs src/LaTeXInserter/Abstractions/IInputSimulatorService.cs src/LaTeXInserter/ViewModels/AppManager.cs tests/LaTeXInserter.Tests/InputSimulatorServiceTests.cs
git commit -m "feat: harden overlay focus and paste simulation for macOS"
```

---

## Phase 3 — Packaging and release

### Task 13: Project file conditioning and macOS bundle assets

**Files:**
- Modify: `src/LaTeXInserter/LaTeXInserter.csproj:2-14`
- Create: `build/macos/Info.plist`
- Create: `build/macos/entitlements.plist`
- Create: `build/macos/make-icns.sh`

**Interfaces:**
- Consumes: `<Version>` from the csproj.
- Produces: `build/macos/*` consumed by Tasks 14 and 15.

- [ ] **Step 1: Condition the Windows-only properties**

Replace the `PropertyGroup` in `LaTeXInserter.csproj`:

```xml
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>
    <Version>0.0.13</Version>
    <PublishAot>true</PublishAot>
    <IsAotCompatible>true</IsAotCompatible>
  </PropertyGroup>

  <!-- Windows-only: the manifest governs window transparency and DPI awareness,
       and ApplicationIcon only accepts .ico. Both break an osx-* publish. -->
  <PropertyGroup Condition="'$(RuntimeIdentifier)' == '' or $(RuntimeIdentifier.StartsWith('win'))">
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <ApplicationIcon>Assets\LaTeX-Inserter-icon-final.ico</ApplicationIcon>
  </PropertyGroup>
```

`OutputType=WinExe` stays: on non-Windows it is equivalent to `Exe`.

- [ ] **Step 2: Verify both publishes configure**

```bash
dotnet publish src/LaTeXInserter -c Release -r win-x64 -o publish-win-check
```

Expected: succeeds, `publish-win-check/LaTeXInserter.exe` exists with the app icon.

```bash
dotnet build src/LaTeXInserter -c Release -r osx-arm64 /p:PublishAot=false
```

Expected: succeeds from Windows (a full AOT *publish* for osx-arm64 cannot cross-compile from Windows; a build validates the MSBuild conditioning). Delete `publish-win-check`.

- [ ] **Step 3: Write `build/macos/Info.plist`**

`__VERSION__` is substituted by the build scripts.

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>LaTeX Inserter</string>
    <key>CFBundleDisplayName</key>
    <string>LaTeX Inserter</string>
    <key>CFBundleIdentifier</key>
    <string>io.github.lsutorus.latexinserter</string>
    <key>CFBundleExecutable</key>
    <string>LaTeXInserter</string>
    <key>CFBundleIconFile</key>
    <string>LaTeXInserter.icns</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleShortVersionString</key>
    <string>__VERSION__</string>
    <key>CFBundleVersion</key>
    <string>__VERSION__</string>
    <key>CFBundleInfoDictionaryVersion</key>
    <string>6.0</string>
    <key>LSMinimumSystemVersion</key>
    <string>13.0</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <!-- Menu-bar-only agent app: no Dock icon, no app menu. -->
    <key>LSUIElement</key>
    <true/>
    <key>NSHumanReadableCopyright</key>
    <string>MIT License</string>
</dict>
</plist>
```

- [ ] **Step 4: Write `build/macos/entitlements.plist`**

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <!--
      Deliberately minimal.

      App Sandbox is NOT declared: Velopack's updater writes outside the sandbox and
      may request elevation, both of which a sandboxed app cannot do.

      Posting CGEvents and creating an event tap need no entitlement — they are
      gated by TCC (Accessibility / Input Monitoring), which the user grants.

      Native AOT produces no JIT pages, so no allow-jit or
      allow-unsigned-executable-memory entitlement is required.

      If notarization fails complaining about the bundled libuiohook/Skia dylibs,
      the fix is to let vpk deep-sign them (the default), not to add
      disable-library-validation here.
    -->
</dict>
</plist>
```

- [ ] **Step 5: Write `build/macos/make-icns.sh`**

```bash
#!/usr/bin/env bash
# Generates LaTeXInserter.icns from the 1024px source PNG.
# macOS only — uses sips and iconutil.
set -euo pipefail

SRC="${1:-src/LaTeXInserter/Assets/LaTeX-Inserter-icon-final.png}"
OUT="${2:-build/macos/LaTeXInserter.icns}"

WORK="$(mktemp -d)/LaTeXInserter.iconset"
mkdir -p "$WORK"

for size in 16 32 128 256 512; do
  sips -z $size $size        "$SRC" --out "$WORK/icon_${size}x${size}.png"        >/dev/null
  sips -z $((size*2)) $((size*2)) "$SRC" --out "$WORK/icon_${size}x${size}@2x.png" >/dev/null
done

mkdir -p "$(dirname "$OUT")"
iconutil --convert icns "$WORK" --output "$OUT"
echo "Wrote $OUT"
```

```bash
chmod +x build/macos/make-icns.sh
```

- [ ] **Step 6: Commit**

```bash
git add src/LaTeXInserter/LaTeXInserter.csproj build/macos/Info.plist build/macos/entitlements.plist build/macos/make-icns.sh
git commit -m "build: condition Windows-only project properties and add macOS bundle assets"
```

---

### Task 14: Local unsigned macOS build **[MAC-ONLY]**

**Files:**
- Create: `build/macos/build-local.sh`

**Interfaces:**
- Consumes: `build/macos/Info.plist`, `build/macos/make-icns.sh` (Task 13).
- Produces: `artifacts/LaTeX Inserter.app`, an unsigned bundle for development.

An unsigned bundle is enough to exercise everything except notarization and the update flow. TCC will re-prompt on every rebuild because the ad-hoc signature changes — that is expected.

- [ ] **Step 1: Write the script**

```bash
#!/usr/bin/env bash
# Builds an unsigned .app bundle for local development.
# Not a release artifact — see .github/workflows/release.yml for the signed path.
set -euo pipefail

RID="${1:-osx-arm64}"
VERSION="$(grep -o '<Version>[^<]*</Version>' src/LaTeXInserter/LaTeXInserter.csproj \
           | sed 's/<[^>]*>//g')"
PUBLISH_DIR="artifacts/publish-$RID"
APP="artifacts/LaTeX Inserter.app"

echo "==> Publishing $RID (Native AOT), version $VERSION"
rm -rf "$PUBLISH_DIR" "$APP"
dotnet publish src/LaTeXInserter -c Release -r "$RID" --self-contained -o "$PUBLISH_DIR"

echo "==> Generating icon"
./build/macos/make-icns.sh

echo "==> Assembling bundle"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$PUBLISH_DIR"/* "$APP/Contents/MacOS/"
cp build/macos/LaTeXInserter.icns "$APP/Contents/Resources/"
sed "s/__VERSION__/$VERSION/g" build/macos/Info.plist > "$APP/Contents/Info.plist"

echo "==> Ad-hoc signing (development only)"
codesign --force --deep --sign - "$APP"

echo "==> Built: $APP"
echo "    Run with: open '$APP'"
echo "    Note: TCC permissions reset on every rebuild because the ad-hoc"
echo "          signature changes. Re-grant Accessibility + Input Monitoring."
```

```bash
chmod +x build/macos/build-local.sh
```

- [ ] **Step 2: [MAC-ONLY] Build and launch**

```bash
./build/macos/build-local.sh osx-arm64
```

Expected: `artifacts/LaTeX Inserter.app` exists. Then:

```bash
open "artifacts/LaTeX Inserter.app"
```

Expected: a menu-bar icon appears, **no Dock icon** (proves `LSUIElement`), and macOS prompts for Accessibility on first hotkey attempt.

- [ ] **Step 3: [MAC-ONLY] Verify the app-data location**

```bash
ls -la ~/Library/Application\ Support/LaTeX\ Inserter/
```

Expected: `settings.json` after changing any setting. Confirm nothing was written to `~/.config/LaTeX Inserter`.

- [ ] **Step 4: [MAC-ONLY] Verify the login item**

Toggle "Start on Startup" in Settings, then:

```bash
sfltool dumpbtm | grep -A5 -i latexinserter
```

Expected: an entry for the bundle. Also confirm it appears under System Settings ▸ General ▸ Login Items. Toggle it off *in System Settings*, reopen the app's Settings window, and confirm the checkbox reflects the off state rather than silently re-enabling.

- [ ] **Step 5: Commit**

```bash
git add build/macos/build-local.sh
git commit -m "build: add local unsigned macOS app bundle script"
```

---

### Task 15: Release workflow — Windows + macOS

**Files:**
- Modify: `.github/workflows/release.yml` (whole file)

**Interfaces:**
- Consumes: `build/macos/*` (Task 13).
- Produces: a single GitHub release per tag carrying Windows and macOS assets.

**Required repository secrets** (set these before running; the workflow fails fast without them):

| Secret | What it is |
| --- | --- |
| `MACOS_CERT_P12` | base64 of the *Developer ID Application* cert `.p12` |
| `MACOS_CERT_P12_PASSWORD` | password for the above |
| `MACOS_INSTALLER_CERT_P12` | base64 of the *Developer ID Installer* cert `.p12` |
| `MACOS_INSTALLER_CERT_P12_PASSWORD` | password for the above |
| `MACOS_SIGN_IDENTITY` | e.g. `Developer ID Application: Your Name (TEAMID)` |
| `MACOS_INSTALL_IDENTITY` | e.g. `Developer ID Installer: Your Name (TEAMID)` |
| `APPLE_ID` | Apple ID email for notarytool |
| `APPLE_TEAM_ID` | 10-character team ID |
| `APPLE_APP_PASSWORD` | app-specific password for the Apple ID |

Both certs require an active Apple Developer Program membership. There is no unsigned public-release path.

**Two structural rules:**
1. The macOS job `needs: windows` so the two `vpk upload github` calls never race on the same tag.
2. The Windows `vpk pack` call gains **no** `--channel` flag. Adding one would change the channel installed 0.0.x users are subscribed to and break their updates. Each macOS arch gets an explicit distinct channel.

- [ ] **Step 1: Replace `.github/workflows/release.yml`**

```yaml
name: Release

on:
  push:
    tags:
      - 'v*'

permissions:
  contents: write

jobs:
  windows:
    runs-on: windows-latest

    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Setup .NET 10
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Test
        run: dotnet test LaTeXInserter.slnx -c Release

      - name: Publish (Native AOT)
        run: dotnet publish src/LaTeXInserter -c Release -r win-x64 -o publish

      - name: Install Velopack CLI
        run: dotnet tool install --global vpk

      - name: Pack with Velopack
        shell: pwsh
        run: |
          $version = "${{ github.ref_name }}" -replace '^v',''
          Copy-Item src/LaTeXInserter/Assets/LaTeX-Inserter-icon-final.ico publish/
          vpk pack --packId LaTeXInserter --packVersion $version --packDir publish `
                   --mainExe LaTeXInserter.exe --icon publish/LaTeX-Inserter-icon-final.ico

      - name: Upload to GitHub Releases
        shell: pwsh
        run: |
          $version = "${{ github.ref_name }}" -replace '^v',''
          vpk upload github --repoUrl https://github.com/lsutorus/LaTeX-Inserter-CS `
                            --token ${{ secrets.GITHUB_TOKEN }} --releaseName "v$version"

      - name: Generate SHA256 for Setup.exe
        shell: pwsh
        run: |
          $tag = "${{ github.ref_name }}" -replace '^v',''
          $setup = Get-ChildItem -Path . -Filter "*Setup.exe" -Recurse | Select-Object -First 1
          if ($setup) {
            $hash = (Get-FileHash -Path $setup.FullName -Algorithm SHA256).Hash
            "$hash  $($setup.Name)" | Out-File -FilePath "$($setup.Name).sha256" -Encoding utf8NoBOM
            gh release upload $tag "$($setup.Name).sha256" --repo lsutorus/LaTeX-Inserter-CS
          }
        env:
          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}

  macos:
    # Sequential, not parallel: two vpk uploads against one tag will race.
    needs: windows
    runs-on: macos-14
    strategy:
      # Also sequential — same reason.
      max-parallel: 1
      matrix:
        include:
          - rid: osx-arm64
            channel: osx-arm64
          - rid: osx-x64
            channel: osx-x64

    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Setup .NET 10
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Test
        run: dotnet test LaTeXInserter.slnx -c Release

      - name: Publish (Native AOT, ${{ matrix.rid }})
        run: |
          dotnet publish src/LaTeXInserter -c Release -r ${{ matrix.rid }} \
                 --self-contained -o publish

      - name: Generate .icns
        run: ./build/macos/make-icns.sh

      - name: Prepare Info.plist
        run: |
          VERSION="${GITHUB_REF_NAME#v}"
          sed "s/__VERSION__/$VERSION/g" build/macos/Info.plist > build/macos/Info.generated.plist

      - name: Import signing certificates
        env:
          CERT_P12: ${{ secrets.MACOS_CERT_P12 }}
          CERT_P12_PASSWORD: ${{ secrets.MACOS_CERT_P12_PASSWORD }}
          INSTALLER_P12: ${{ secrets.MACOS_INSTALLER_CERT_P12 }}
          INSTALLER_P12_PASSWORD: ${{ secrets.MACOS_INSTALLER_CERT_P12_PASSWORD }}
        run: |
          set -euo pipefail
          KEYCHAIN="$RUNNER_TEMP/build.keychain-db"
          KEYCHAIN_PASSWORD="$(uuidgen)"

          security create-keychain -p "$KEYCHAIN_PASSWORD" "$KEYCHAIN"
          security set-keychain-settings -lut 3600 "$KEYCHAIN"
          security unlock-keychain -p "$KEYCHAIN_PASSWORD" "$KEYCHAIN"

          echo "$CERT_P12" | base64 --decode > "$RUNNER_TEMP/app.p12"
          echo "$INSTALLER_P12" | base64 --decode > "$RUNNER_TEMP/installer.p12"

          security import "$RUNNER_TEMP/app.p12" -k "$KEYCHAIN" \
            -P "$CERT_P12_PASSWORD" -T /usr/bin/codesign -T /usr/bin/productbuild
          security import "$RUNNER_TEMP/installer.p12" -k "$KEYCHAIN" \
            -P "$INSTALLER_P12_PASSWORD" -T /usr/bin/codesign -T /usr/bin/productbuild

          security set-key-partition-list -S apple-tool:,apple:,codesign: \
            -s -k "$KEYCHAIN_PASSWORD" "$KEYCHAIN"
          security list-keychains -d user -s "$KEYCHAIN" login.keychain-db

          rm -f "$RUNNER_TEMP/app.p12" "$RUNNER_TEMP/installer.p12"

      - name: Store notarytool credentials
        env:
          APPLE_ID: ${{ secrets.APPLE_ID }}
          APPLE_TEAM_ID: ${{ secrets.APPLE_TEAM_ID }}
          APPLE_APP_PASSWORD: ${{ secrets.APPLE_APP_PASSWORD }}
        run: |
          xcrun notarytool store-credentials "latexinserter-notary" \
            --apple-id "$APPLE_ID" \
            --team-id "$APPLE_TEAM_ID" \
            --password "$APPLE_APP_PASSWORD"

      - name: Install Velopack CLI
        run: dotnet tool install --global vpk

      - name: Pack, sign, and notarize
        env:
          SIGN_IDENTITY: ${{ secrets.MACOS_SIGN_IDENTITY }}
          INSTALL_IDENTITY: ${{ secrets.MACOS_INSTALL_IDENTITY }}
        run: |
          set -euo pipefail
          export PATH="$PATH:$HOME/.dotnet/tools"
          VERSION="${GITHUB_REF_NAME#v}"

          vpk pack \
            --packId LaTeXInserter \
            --packVersion "$VERSION" \
            --packDir publish \
            --mainExe LaTeXInserter \
            --icon build/macos/LaTeXInserter.icns \
            --plist build/macos/Info.generated.plist \
            --bundleId io.github.lsutorus.latexinserter \
            --channel ${{ matrix.channel }} \
            --signAppIdentity "$SIGN_IDENTITY" \
            --signInstallIdentity "$INSTALL_IDENTITY" \
            --signEntitlements build/macos/entitlements.plist \
            --notaryProfile latexinserter-notary

      - name: Verify signature, notarization, and stapling
        run: |
          set -euo pipefail
          APP="$(find . -name '*.app' -maxdepth 4 -type d | head -1)"
          PKG="$(find . -name '*.pkg' -maxdepth 4 -type f | head -1)"

          echo "== codesign =="
          codesign --verify --deep --strict --verbose=2 "$APP"

          echo "== Gatekeeper =="
          spctl --assess --type execute --verbose=4 "$APP"

          echo "== staple =="
          xcrun stapler validate "$APP"
          xcrun stapler validate "$PKG"

      - name: Upload to GitHub Releases
        run: |
          set -euo pipefail
          export PATH="$PATH:$HOME/.dotnet/tools"
          VERSION="${GITHUB_REF_NAME#v}"
          vpk upload github \
            --repoUrl https://github.com/lsutorus/LaTeX-Inserter-CS \
            --token ${{ secrets.GITHUB_TOKEN }} \
            --releaseName "v$VERSION" \
            --channel ${{ matrix.channel }}

      - name: Generate SHA256 for .pkg
        env:
          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
        run: |
          set -euo pipefail
          TAG="${GITHUB_REF_NAME#v}"
          PKG="$(find . -name '*.pkg' -maxdepth 4 -type f | head -1)"
          NAME="$(basename "$PKG")"
          shasum -a 256 "$PKG" | awk -v n="$NAME" '{print $1 "  " n}' > "$NAME.sha256"
          gh release upload "$TAG" "$NAME.sha256" --repo lsutorus/LaTeX-Inserter-CS
```

- [ ] **Step 2: Validate the workflow syntax**

```bash
gh workflow view release.yml --repo lsutorus/LaTeX-Inserter-CS
```

If `actionlint` is available, prefer:

```bash
actionlint .github/workflows/release.yml
```

- [ ] **Step 3: Verify the Windows leg is byte-compatible**

Diff the `windows` job against the original file. The only intended additions are the `Test` step and the `.slnx` filename. If anything else changed — particularly the `vpk pack` arguments — revert it. A changed Velopack channel silently breaks updates for every existing user.

- [ ] **Step 4: Confirm `macos-14` can cross-compile `osx-x64`**

`macos-14` runners are Apple Silicon. Native AOT for `osx-x64` requires clang to target `x86_64-apple-macos`, which the Xcode toolchain supports. Verify with a scratch run:

```bash
dotnet publish src/LaTeXInserter -c Release -r osx-x64 --self-contained -o /tmp/x64check
```

If it fails, change the matrix entry for `osx-x64` to `runs-on: macos-13` (Intel) by promoting `runs-on` into the matrix:

```yaml
        include:
          - rid: osx-arm64
            channel: osx-arm64
            runner: macos-14
          - rid: osx-x64
            channel: osx-x64
            runner: macos-13
```

and set `runs-on: ${{ matrix.runner }}`. Check the GitHub-hosted runner deprecation schedule before relying on `macos-13`.

- [ ] **Step 5: Dry-run against a pre-release tag**

Do **not** test against a real version tag. Bump `<Version>` to `0.0.14` locally, push tag `v0.0.14-rc1`, and confirm the workflow completes end to end. Delete the tag and draft release afterward:

```bash
gh release delete v0.0.14-rc1 --repo lsutorus/LaTeX-Inserter-CS --yes
git push --delete origin v0.0.14-rc1
```

- [ ] **Step 6: Commit**

```bash
git add .github/workflows/release.yml
git commit -m "ci: add signed and notarized macOS release leg alongside Windows"
```

---

## Phase 4 — Verification and documentation

### Task 16: Real-device verification matrix **[MAC-ONLY execution]**

**Files:**
- Create: `docs/macos-verification.md`

**Interfaces:**
- Consumes: the signed build produced by Task 15.
- Produces: the signed-off definition of done.

This document is created early (an agent can write it from Windows) and *filled in* on hardware. Task 6 Step 8 already writes its first results here.

- [ ] **Step 1: Write the checklist document**

````markdown
# macOS Verification

Run the full matrix on **Apple Silicon** and **Intel**, against the signed,
notarized build from CI — not a local ad-hoc build. Record macOS version, chip,
app version, and date for each column.

| | Apple Silicon | Intel |
| --- | --- | --- |
| macOS version | | |
| App version | | |
| Date | | |
| Tester | | |

## 1. Permission lifecycle

- [ ] **Denied.** Fresh install, deny Accessibility. App launches, shows a menu-bar
      icon, and the Settings window explains what is missing with working
      "Open Accessibility…" / "Open Input Monitoring…" buttons. The app does not
      crash, hang, or fail silently.
- [ ] **Granted.** Grant both, quit, reopen. Hotkey works; paste works.
- [ ] **Revoked.** Revoke Accessibility while running. The app reports the failure
      (Settings opens with the panel) rather than silently doing nothing.
- [ ] **Re-granted.** Grant again, quit, reopen. Full function restored.

## 2. Multi-monitor cursor positioning

- [ ] Overlay opens adjacent to the cursor on the primary display.
- [ ] Overlay opens adjacent to the cursor on a secondary display placed to the right.
- [ ] Overlay opens adjacent to the cursor on a secondary display placed **above and
      to the left** (negative coordinates).
- [ ] Overlay opens correctly on displays with **different** scale factors (Retina +
      non-Retina attached simultaneously).
- [ ] Overlay never straddles a screen edge; it flips rather than clipping.
- [ ] Overlay appears **over a full-screen app** (open Safari full screen, trigger the
      hotkey). This is what the Spaces collection behavior is for.

## 3. Paste targets

Type `\alpha` then Enter; expect `α`. Then `x^2` → `x²`.

- [ ] TextEdit
- [ ] Safari text field
- [ ] Chrome text field
- [ ] VS Code editor
- [ ] Terminal — **Secure Keyboard Entry OFF**
- [ ] Terminal — **Secure Keyboard Entry ON**: the app must explain that paste was
      blocked and that the text is on the clipboard. It must not appear broken.
- [ ] Focus returns to the correct app in every case (nothing pastes into the wrong
      window).
- [ ] No stuck modifier key afterwards (press a letter — it must not arrive as a
      Command chord).

## 4. App shell

- [ ] Menu-bar icon present and legible on a **light** menu bar.
- [ ] Menu-bar icon present and legible on a **dark** menu bar.
- [ ] No Dock icon at any point.
- [ ] Menu shows: Show/Hide Overlay (⌃⌥M), Settings…, Edit Custom Mappings…,
      Check for Updates…, Quit.
- [ ] The hotkey label renders Apple glyphs (`⌃⌥M`), not `Ctrl+Alt+M`.
- [ ] Settings window opens, is not resizable, and saves.
- [ ] Accent color applies on Save and reverts on Cancel/X.
- [ ] Change Hotkey records live and rejects reserved macOS combos (try `⌘Space`).
- [ ] Custom Mappings window: add, edit, delete, save, reload all work.
- [ ] Settings and mappings persist to `~/Library/Application Support/LaTeX Inserter/`
      and **not** `~/.config`.
- [ ] Start on Startup registers a login item visible in System Settings ▸ General ▸
      Login Items.
- [ ] Disabling that login item **in System Settings** is reflected in the app's
      checkbox and is not silently re-enabled.
- [ ] Quit fully terminates the process (`pgrep LaTeXInserter` returns nothing).

## 5. Install and update

- [ ] `.pkg` installs without a Gatekeeper warning.
- [ ] App launches from `/Applications` after install.
- [ ] App launches from a **non**-`/Applications` location (Velopack bundles are
      portable) and still updates.
- [ ] Downgrade the installed version, then "Check for Updates" finds the release.
- [ ] Download shows progress, install completes, app relaunches.
- [ ] After update: signature still valid (`codesign --verify --deep --strict`), and
      TCC permissions survive (same signing identity).
- [ ] Settings and custom mappings survive the update.

## 6. Windows regression

- [ ] `dotnet test LaTeXInserter.slnx` green on Windows.
- [ ] Windows Setup.exe installs and launches.
- [ ] Ctrl+Alt+M opens the overlay; Enter pastes into Notepad and a browser.
- [ ] Windows Settings window has **no** permission panel.
- [ ] Windows tray icon unchanged.
- [ ] Existing `settings.json` from 0.0.13 loads without migration.
- [ ] "Check for Updates" from an installed 0.0.13 finds the new release — proves the
      Velopack channel did not change.
````

- [ ] **Step 2: Commit**

```bash
git add docs/macos-verification.md
git commit -m "docs: add macOS real-device verification matrix"
```

- [ ] **Step 3: [MAC-ONLY] Execute the matrix**

Run every item on both architectures. Any unchecked box blocks the release. File failures as GitHub issues in `lsutorus/LaTeX-Inserter-CS` with the triage labels described in `docs/agents/triage-labels.md`.

---

### Task 17: Documentation

**Files:**
- Modify: `CLAUDE.md`
- Modify: `docs/architecture.md`
- Modify: `README.md`

**Interfaces:**
- Consumes: everything above.
- Produces: no code.

- [ ] **Step 1: Fix the stale solution filename**

`CLAUDE.md` says `dotnet build LaTeXInserter.sln`; `docs/architecture.md`'s file tree lists `LaTeXInserter.sln`. The actual file is `LaTeXInserter.slnx`. Correct both.

- [ ] **Step 2: Update `CLAUDE.md`**

Add to **Tech Stack**: macOS support via `Platform/MacOS` using Objective-C runtime + CoreGraphics + IOKit `[LibraryImport]`; `SMAppService` for login items; menu-bar-only via `LSUIElement`.

Add to **Key Conventions**:
- Platform DI goes through `PlatformServiceRegistration.AddPlatformServices(PlatformKinds.Current)` — never register a platform implementation directly in `Program.cs`.
- App-data path comes from `IAppDataPathProvider`, never `Environment.SpecialFolder.ApplicationData` directly (that maps to `~/.config` on macOS).
- Chord display goes through `HotkeyChordFormatter`; `ModifierMask.Windows` keeps its serialized name on every platform.
- `objc_msgSend` needs one `[LibraryImport]` declaration per exact signature — no generic wrapper (arm64 has no variadic promotion).

Add to **Anti-patterns**:
- **No direct platform registration in `Program.cs`** — use `AddPlatformServices`.
- **No renaming `ModifierMask.Windows`** — it is the serialized settings key on both platforms.
- **No App Sandbox entitlement** — Velopack's updater cannot run sandboxed.
- **No `--channel` on the Windows `vpk pack`** — it would orphan installed users.
- **No parallel `vpk upload github`** — the macOS job must `needs:` the Windows job.

Update **Versioning & Release** to describe the two-platform release: Windows `Setup.exe` + sha256, macOS `.pkg` + `.zip` + sha256 per arch, channels `osx-arm64` / `osx-x64`.

- [ ] **Step 3: Update `docs/architecture.md`**

Add the new files to the tree (`Platform/MacOS/*`, `Platform/PlatformServiceRegistration.cs`, `build/macos/*`, the new Abstractions and Models). Add a **macOS Platform Layer** section covering: the objc interop approach and why; window activation via pid + `deactivate`; Spaces collection behavior; the TCC permission model and the signature-keyed grant caveat; Secure Event Input; `SMAppService` status semantics; and the tray-icon template limitation from Task 11.

Update the **Registered services** list — `IWindowActivator` / `IOverlayPositioner` / `IStartupRegistrar` are no longer "Windows-only for now", and `IAppDataPathProvider` + `IPermissionService` are new.

- [ ] **Step 4: Update `README.md`**

Add a macOS install section: download the `.pkg`, install, grant Accessibility **and** Input Monitoring, quit and reopen. State the macOS 13+ requirement, that the app lives in the menu bar with no Dock icon, that the default hotkey is `⌃⌥M`, and the Secure Keyboard Entry caveat for Terminal.

- [ ] **Step 5: Commit**

```bash
git add CLAUDE.md docs/architecture.md README.md
git commit -m "docs: document the macOS platform layer, packaging, and install steps"
```

---

## Decisions already made (override before starting if you disagree)

| Decision | Chosen | Why |
| --- | --- | --- |
| macOS default hotkey | `Ctrl+Alt+M` (shown `⌃⌥M`) | Identical `AppSettings.Default`, no migration, avoids the crowded `⌘` namespace |
| Architectures | `osx-arm64` **and** `osx-x64` | Intel Macs are in your definition of done |
| Dock icon | None (`LSUIElement`) | Menu-bar utility |
| Installer | Velopack `.pkg` | DMG means hand-rolling and losing the update path |
| macOS floor | 13.0 Ventura | `SMAppService` requirement |
| Login items | `SMAppService.mainApp` | Apple's current guidance; `status` lets the UI honor a user opt-out |
| Interop | Hand-rolled `objc_msgSend` P/Invoke | AOT-safe with no C toolchain in the build |
| Linux | Explicitly unsupported | libuiohook cannot suppress on X11; Wayland unsupported |

## Known risks, ranked

1. **Global hook under Avalonia's run loop** (Task 6 Step 8). Everything depends on it. Prove it before building anything downstream — if you are sequencing tasks by risk rather than by dependency, do Task 5 → Task 6 first.
2. **Borderless overlay taking key focus** as an `LSUIElement` app (Task 8, Task 12). Fallbacks: `NSWindow` style-mask tweak, or an `NSPanel`-backed window.
3. **Cross-app activation on macOS 14+** (Task 7). The `deactivate` + `activateWithOptions:` pair is the mitigation; if focus still lands wrong, fall back to `-[NSApplication hide:]`.
4. **`SMAppService` selector resolution** (Task 9). The Swift `mainApp` property's Objective-C name must be confirmed on-device.
5. **Notarization** (Task 15). First run always surfaces surprises. Budget a day and dry-run against an `-rc` tag.
6. **`osx-x64` AOT cross-compile on Apple Silicon runners** (Task 15 Step 4). Has a clean fallback (`macos-13`), but that runner image may be retired.

## Self-review

- **Spec coverage.** All eight original findings map to tasks (1→Task 4; 2→Tasks 5–9; 3→Task 12; 4→Task 6; 5→Task 2; 6→Task 3; 7→Task 8; 8→Tasks 7 & 12). All five macOS requirements map (permissions→Tasks 6 & 10; default shortcut→Task 2 + decision table; login item→Task 9; menu-bar-only→Tasks 11 & 13; icns/bundle→Tasks 13–15). All five release items map (csproj→Task 13; workflow→Task 15; both arches→Task 15; Velopack validation→Tasks 13–15; signing/notarization→Task 15). All six definition-of-done items map to Task 16's matrix.
- **Additions beyond the original list.** App-data path (Task 1), Secure Event Input (Task 12), Spaces/full-screen overlay behavior (Task 8), deactivation race (Task 12), tray-icon asset (Task 11), release-upload sequencing and channel preservation (Task 15), Apple credential plumbing (Task 15), TCC-vs-code-signature caveat (Tasks 6, 14, 17).
- **Type consistency.** `PlatformKind`/`PlatformKinds.Current`, `IAppDataPathProvider.GetAppDataDirectory()`, `IPermissionService.Query()/RequiresUserAction/OpenAccessibilitySettings()/OpenInputMonitoringSettings()`, `PermissionStatus(AccessibilityGranted, InputMonitoringGranted, SecureInputActive)` + `IsUsable`, `HotkeyChordFormatter.Format(chord, platform)`, `HotkeyBlocklist.IsBlocked(chord, platform)`, `IHotkeyService.HookFailed/IsRunning`, `IInputSimulatorService.PasteBlocked`, `AddPlatformServices(this IServiceCollection, PlatformKind)` are used identically wherever they appear.
- **Known gap.** Task 6 Step 8 writes results into `docs/macos-verification.md`, which Task 16 creates. If you execute strictly in order, create that file's skeleton during Task 6 or simply record the results in the Task 6 commit message and fold them in at Task 16.

---

## Execution Progress

Branch: `feat/macos-port`. Execute one task per session, clear context between tasks.

| Task | Status | Commit | Notes |
| --- | --- | --- | --- |
| 1 — App-data path seam | ✅ done | `cd5da35` | Tests use temp paths (plan's permission caveat). `SettingsServiceTests` repaired off real `%APPDATA%`. 133 tests pass. |
| 2 — Hotkey chord display | ✅ done | `98d1a6e` | `HotkeyChordFormatter` added; `HotkeyChord.ToString()` delegates to it. 137 tests pass (+4). |
| 3 — Reserved-shortcut blocklist | ✅ done | `f7e1b38` | `VcBackQuote` (capital Q), not `VcBackquote` — plan had wrong case, corrected. `VcComma`/`VcSlash`/`Vc3`–`Vc5` valid. `HotkeyDialogViewModel:94` uses 1-arg overload, no change needed. 142 tests pass (+5). |
| 4 — OS-dispatched DI registration | ✅ done | `7d0dfc7` | `PlatformServiceRegistration.AddPlatformServices` added; `Program.cs` swapped `using LaTeXInserter.Platform.Windows` → `LaTeXInserter.Platform` + `Models`, 3 direct regs replaced with `AddPlatformServices(PlatformKinds.Current)`. macOS branch throws `PlatformNotSupportedException` until Task 9. Test needed `using LaTeXInserter.Services` (NoOpPermissionService) — plan omitted it. Smoke test skipped (Windows needs admin elevation + interactive tray). 144 tests pass (+2). |
| 5 — ObjC + native interop core | ✅ done | `1810655` | `Xunit.SkippableFact` 1.5.61 added to test csproj; `MacInteropTests` needs `using Xunit.Sdk;` for `Skip` (plan omitted it). `ObjC.cs` + `MacNativeMethods.cs` written verbatim from plan. New CA1416 warnings on the test call sites (same pre-existing pattern as `WindowsStartupRegistrar`) — warnings only, not errors. Step 6 AOT publish: ILC emitted **0** `IL####` warnings, but the native link step fails in this environment with `MSB3073 ... 'vswhere.exe' is not recognized` — a PATH/toolchain issue unrelated to this task, reproduces from both shells. 145 tests pass (+1), 2 skipped on Windows. |
| 6 — macOS perms + hook spike | ✅ done (code) — **blocked on hardware for Steps 8–9** | `6ed7fa1` | `IHotkeyService` gained `HookFailed` + `IsRunning`; `HotkeyService.StartAsync` now catches and marshals failures via `DescribeFailure`. `MacPermissionService` written verbatim from plan. **Deviation:** plan's test used a bare `SpinWait.SpinUntil(() => reported is not null, ...)`, which can never pass — `Dispatcher.UIThread.Post` queues the callback but the xunit host has no Avalonia message loop, so nothing pumps it (verified: disposed hook does throw `ObjectDisposedException`, but the posted job never ran). Test now calls `Dispatcher.UIThread.RunJobs()` inside the spin predicate to drain queued jobs; production code kept the plan's `Post` (correct for the real app). Needed `using Avalonia.Threading;` in the test. `MacPermissionService` is not registered in DI yet — Task 9 owns that. Steps 8 (hook viability on Mac) and 9 (fallback if 8.2 fails) **not executed: require macOS hardware**; `docs/macos-verification.md` still uncreated. 146 tests pass (+1), 2 skipped. |
| 7 — macOS window activator | ⏳ next session | — | |
| 8 — macOS overlay positioner | pending | — | Negative-origin clamp test; MAC-ONLY calibration. |
| 9 — macOS startup registrar + DI branch | pending | — | Activates macOS DI branch. Verify `+mainAppService` selector on-device. |
| 10 — Permission status UX | pending | — | Extends modified `SettingsViewModel`/`SettingsWindow.axaml` (already carry `CurrentVersion` WIP from prior commit). |
| 11 — macOS menu-bar icon | pending | — | Needs `tray-macos.png` asset (36×36 #8A8A8A). You produce the PNG. |
| 12 — Overlay focus + paste timing | pending | — | Rewrite `InputSimulatorService`; check `IInputSimulatorService` event declaration. |
| 13 — csproj conditioning + bundle assets | pending | — | |
| 14 — Local unsigned macOS build | pending [MAC-ONLY] | — | |
| 15 — Release workflow | pending | — | Requires Apple secrets before real run. Dry-run against `-rc` tag. |
| 16 — Real-device verification matrix | pending [MAC-ONLY execution] | — | Create `docs/macos-verification.md` early (Task 6 Step 8 writes into it). |
| 17 — Documentation | pending | — | Fix stale `.sln` → `.slnx` in `CLAUDE.md` + `docs/architecture.md`. |

**Resume state:** Tasks 1–6 committed on `feat/macos-port` (Task 6 code only — Steps 8–9 await a Mac). Working tree clean. Start Task 7 in a fresh session. Run `dotnet test LaTeXInserter.slnx` first to confirm green baseline (146 passing, 2 skipped) before changes.

**Outstanding MAC-ONLY debt:** Task 6 Steps 8–9 (prove libuiohook delivers events under Avalonia's run loop; apply the run-loop fallback if it does not). This is the port's riskiest unverified assumption and gates Tasks 12/14/16.

