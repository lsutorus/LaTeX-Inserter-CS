# Phase 1: Foundation & Data Layer — Detailed Implementation Plan

## Resolved Design Decisions (from grill session)

| # | Decision | Choice | Rationale |
|---|----------|--------|-----------|
| D1 | Project template | `avalonia.mvvm`, then delete MainWindow.axaml/.cs | Gives us CommunityToolkit.Mvvm + proper App.axaml scaffolding |
| D2 | Shutdown mode | `ShutdownMode.Explicit` in `OnFrameworkInitializationCompleted` | Prevents Avalonia from shutting down with no main window |
| D3 | DI composition root | `Program.cs` builds `IServiceCollection`, `App.axaml.cs` resolves VMs | Strict constructor injection, no service locator |
| D4 | LatexConverterService | Single class, no AST split | Lookup-based grammar, no need for separate pass |
| D5 | Parser perf | `ReadOnlySpan<char>` + `ref int` index + `StringBuilder` output, no `$` wrapping | Zero-allocation math-mode entry; no string concat for input |
| D6 | Custom mappings I/O | `ISettingsService.GetCustomMappingLines()` returns lines, converter parses | Testable: mock ISettingsService, no disk I/O in converter |
| D7 | JsonContext | Single `JsonContext : JsonSerializerContext` for all types | One generated context, no benefit splitting at this scale |
| D8 | AppSettings shape | `sealed record AppSettings(HotkeyChord Hotkey, bool StartOnStartup)` with defaults | Typed from day one; HotkeyChord used directly |
| D9 | ModifierMask | Our own `[Flags]` enum, not SharpHook's | Clean JSON ("Control, Alt"), abstracts left/right distinction |
| D10 | KeyCode | Use SharpHook's `KeyCode` enum directly | No point reimplementing 100+ keys |
| D11 | HotkeyBlocklist | Phase 2 only | Only consumed by HotkeyService |
| D12 | Namespaces | `LaTeXInserter.Services`, `LaTeXInserter.Models`, etc. | Conventional, scales with phases |
| D13 | Test framework | xUnit + NSubstitute + Microsoft.Extensions.DependencyInjection | NSubstitute for mocking ISettingsService; DI for integration tests |
| D14 | ISettingsService | Single interface: `Load`/`Save` + `GetCustomMappingLines()` | Custom mappings are part of settings — one type, one concern |
| D15 | Convert entry point | Enters math-mode parse directly, no `$` wrapping | Same behavior as Python, zero allocation |

---

## Files to Create / Modify

### 1. Solution: `LaTeXInserter.sln`

**Path**: `LaTeXInserter.sln` (repo root)
**Action**: Create
**Contents**: Links two projects:
- `src/LaTeXInserter/LaTeXInserter.csproj`
- `tests/LaTeXInserter.Tests/LaTeXInserter.Tests.csproj`

Create via `dotnet new sln` then `dotnet sln add` for each project.

---

### 2. Project: `src/LaTeXInserter/LaTeXInserter.csproj`

**Path**: `src/LaTeXInserter/LaTeXInserter.csproj`
**Action**: Create (from template), then modify
**Logic**:
- Generate via `dotnet new avalonia.mvvm` in a temp dir, copy `.csproj` to `src/LaTeXInserter/`
- Modify `.csproj`:
  - Set `<Version>0.0.1</Version>` (semver placeholder)
  - Add NuGet refs: `SharpHook`
  - Ensure `Avalonia` + `CommunityToolkit.Mvvm` already present (from template)
  - Add `<EmbeddedResource Include="Assets\Commands.json" />`
  - Set `<PublishAot>true</PublishAot>` for Native AOT (can be in a Release-time property, but declare intent)
- Delete template files we don't need: `MainWindow.axaml`, `MainWindow.axaml.cs`, `Views/MainWindow.axaml`, `Views/MainWindow.axaml.cs`, `ViewModels/MainWindowViewModel.cs`, `ViewModels/ViewModelBase.cs`, `ViewLocator.cs`, `Assets/avalonia-logo.ico`

---

### 3. Assets: `src/LaTeXInserter/Assets/Commands.json`

**Path**: `src/LaTeXInserter/Assets/Commands.json`
**Action**: Already created (extracted from `unicodeitplus.data.COMMANDS`)
**Contents**: 2,566-entry `Dictionary<string, string>` mapping LaTeX commands to Unicode characters
**.csproj note**: Registered as `<EmbeddedResource>` — loaded via `JsonSerializerContext` at runtime

---

### 4. Entry Point: `src/LaTeXInserter/Program.cs`

**Path**: `src/LaTeXInserter/Program.cs`
**Action**: Create (rewrite from template)
**Logic**:
```csharp
using System;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;

namespace LaTeXInserter;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // DI composition root
        var services = new ServiceCollection();
        ConfigureServices(services);
        var serviceProvider = services.BuildServiceProvider();

        BuildAvaloniaApp()
            .WithInterFont()
            .StartWithClassicDesktopLifetime(args, shutdownMode: ShutdownMode.Explicit);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Services
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ILatexConverterService, LatexConverterService>();

        // ViewModels (Phase 3+ will add OverlayViewModel, TrayIconViewModel, AppManager)
    }

    private static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
```

**Key details**:
- Composition root here, **not** in `App.axaml.cs`. App.axaml.cs only resolves VMs from the built `IServiceProvider`.
- `ShutdownMode.Explicit` passed to `StartWithClassicDesktopLifetime` — keeps app alive with no main window.
- `VelopackApp.Build().Run()` placeholder for Phase 6 (must be first line of Main when added).

---

### 5. App: `src/LaTeXInserter/App.axaml`

**Path**: `src/LaTeXInserter/App.axaml`
**Action**: Create (from template), strip MainWindow refs
**Contents**:
```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="LaTeXInserter.App"
             RequestedThemeVariant="Dark">
    <Application.Styles>
        <FluentTheme />
    </Application.Styles>
</Application>
```

No TrayIcon yet (Phase 3). No window references.

---

### 6. App: `src/LaTeXInserter/App.axaml.cs`

**Path**: `src/LaTeXInserter/App.axaml.cs`
**Action**: Create (from template), modify
**Logic**:
```csharp
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;

namespace LaTeXInserter;

public partial class App : Application
{
    public IServiceProvider Services { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.Explicit;
            // Resolve VMs from DI, assign DataContexts (Phase 3+)
        }

        base.OnFrameworkInitializationCompleted();
    }

    internal void SetServiceProvider(IServiceProvider sp) => Services = sp;
}
```

**Key detail**: `SetServiceProvider` called from `Program.cs` after building the container. NOT a service locator — only used at composition root to make DI-available services accessible to Avalonia's view wiring. No `App.Services.GetService()` calls in random classes (anti-pattern in CLAUDE.md).

---

### 7. Model: `src/LaTeXInserter/Models/HotkeyChord.cs`

**Path**: `src/LaTeXInserter/Models/HotkeyChord.cs`
**Action**: Create
**Logic**:
```csharp
using System.Text.Json.Serialization;
using SharpHook.Native;

namespace LaTeXInserter.Models;

[Flags]
public enum ModifierMask
{
    None = 0,
    Control = 1,
    Alt = 2,
    Shift = 4,
    Windows = 8
}

public readonly record struct HotkeyChord(
    [property: JsonConverter(typeof(JsonStringEnumConverter<ModifierMask>))]
    ModifierMask Modifiers,
    KeyCode TriggerKey
);
```

**Key details**:
- Our own `ModifierMask`, not SharpHook's — clean JSON ("Control, Alt"), no left/right distinction
- `KeyCode` from SharpHook directly — no point reimplementing
- `record struct` — value type, no heap allocation for hotkey comparisons
- `JsonStringEnumConverter<ModifierMask>` attribute for human-readable settings.json
- `TriggerKey` serialization: registered in `JsonContext` with `JsonStringEnumConverter<KeyCode>`

---

### 8. Model: `src/LaTeXInserter/Models/AppSettings.cs`

**Path**: `src/LaTeXInserter/Models/AppSettings.cs`
**Action**: Create
**Logic**:
```csharp
namespace LaTeXInserter.Models;

public sealed record AppSettings(
    HotkeyChord Hotkey = default,
    bool StartOnStartup = false
)
{
    // Default hotkey: Ctrl+Alt+M
    public static AppSettings Default => new()
    {
        Hotkey = new HotkeyChord(ModifierMask.Control | ModifierMask.Alt, KeyCode.VcM)
    };
}
```

**Key detail**: `default(HotkeyChord)` would give `Modifiers=None, TriggerKey=0` — not valid. `AppSettings.Default` factory provides the real defaults. `SettingsService.Load()` falls back to `AppSettings.Default` when file missing.

---

### 9. Model: `src/LaTeXInserter/Models/JsonContext.cs`

**Path**: `src/LaTeXInserter/Models/JsonContext.cs`
**Action**: Create
**Logic**:
```csharp
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using SharpHook.Native;

namespace LaTeXInserter.Models;

[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(ModifierMask))]
[JsonSerializable(typeof(KeyCode))]
internal partial class JsonContext : JsonSerializerContext;
```

**Key details**:
- Single context for all source-gen JSON — Commands dict + settings
- `KeyCode` registered here so SharpHook's enum serializes as string in settings.json
- `internal` — not exposed outside the project
- `partial` — source generator fills in the implementation

---

### 10. Service Interface: `src/LaTeXInserter/Services/ISettingsService.cs`

**Path**: `src/LaTeXInserter/Services/ISettingsService.cs`
**Action**: Create
**Logic**:
```csharp
using LaTeXInserter.Models;

namespace LaTeXInserter.Services;

public interface ISettingsService
{
    AppSettings Load();
    void Save(AppSettings settings);
    IEnumerable<string> GetCustomMappingLines();
}
```

**Key details**:
- `Load`/`Save` for typed settings round-trip
- `GetCustomMappingLines()` — reads `custom_mappings.txt` lines from AppData. Returns empty if file doesn't exist. `LatexConverterService` parses the content (keeps disk I/O out of converter).
- Single interface — custom mappings are part of the settings concern (AppData folder).

---

### 11. Service: `src/LaTeXInserter/Services/SettingsService.cs`

**Path**: `src/LaTeXInserter/Services/SettingsService.cs`
**Action**: Create
**Logic**:
```csharp
using System.IO;
using System.Text.Json;
using LaTeXInserter.Models;

namespace LaTeXInserter.Services;

public sealed class SettingsService : ISettingsService
{
    private readonly string _appDataPath;
    private readonly string _settingsPath;
    private readonly string _customMappingsPath;

    public SettingsService()
    {
        _appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LaTeX Inserter");
        Directory.CreateDirectory(_appDataPath); // ensure exists
        _settingsPath = Path.Combine(_appDataPath, "settings.json");
        _customMappingsPath = Path.Combine(_appDataPath, "custom_mappings.txt");
    }

    public AppSettings Load()
    {
        if (!File.Exists(_settingsPath))
            return AppSettings.Default;

        try
        {
            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize(json, JsonContext.Default.AppSettings)
                   ?? AppSettings.Default;
        }
        catch
        {
            return AppSettings.Default;
        }
    }

    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonContext.Default.AppSettings);
        File.WriteAllText(_settingsPath, json);
    }

    public IEnumerable<string> GetCustomMappingLines()
    {
        if (!File.Exists(_customMappingsPath))
            return [];
        return File.ReadLines(_customMappingsPath);
    }
}
```

**Key details**:
- Constructor creates AppData directory if missing
- `Load` returns `AppSettings.Default` on missing/corrupt file — graceful degradation
- `Save` uses source-gen `JsonContext.Default.AppSettings`
- `GetCustomMappingLines` uses `File.ReadLines` (lazy, doesn't load entire file into memory)
- Thread safety: not needed — single UI thread access in this app

---

### 12. Service Interface: `src/LaTeXInserter/Services/ILatexConverterService.cs`

**Path**: `src/LaTeXInserter/Services/ILatexConverterService.cs`
**Action**: Create
**Logic**:
```csharp
namespace LaTeXInserter.Services;

public interface ILatexConverterService
{
    string Convert(string input);
    IReadOnlyDictionary<string, string> Commands { get; }
    IReadOnlyList<string> CommandNames { get; }
}
```

**Key details**:
- `Convert(string input)` — main entry point. Input is `string` (not span) because consumers pass user text; internally uses span.
- `Commands` — merged command dictionary (built-in + custom). Phase 4's autocomplete uses this.
- `CommandNames` — sorted list of `\`-prefixed command names for autocomplete filtering.

---

### 13. Service: `src/LaTeXInserter/Services/LatexConverterService.cs`

**Path**: `src/LaTeXInserter/Services/LatexConverterService.cs`
**Action**: Create
**Logic**:

This is the largest file in Phase 1. Detailed internals:

**Constructor**:
- Takes `ISettingsService` via constructor injection
- Loads `Commands.json` from embedded resource via `JsonContext`
- Reads custom mappings via `_settingsService.GetCustomMappingLines()`
- Merges custom over built-in (custom overrides same key)
- Auto-adds keys with `{` in name to `HAS_ARG` set
- Builds sorted `_commandNames` list (all keys starting with `\`)

**Private static data**:
- `ESCAPED` — `Dictionary<string, string>` mapping escaped sequences to chars: `\\` → `\`, `\#` → `#`, `\%` → `%`, `\&` → `&`, `\{` → `{`, `\}` → `}`, `\_` → `_`, `\,` → thin space
- `HAS_ARG` — `FrozenSet<string>` of 45 command names that consume the next `{group}`
- `IGNORE_AS_FALLBACK` — `FrozenSet<string>` of commands silently dropped when no Unicode mapping (15 entries: `\text`, `\mathbb`, `\left`, `\mathrm`, etc.)

**Convert(string input)** — public entry point:
- Internally calls `ParseMath(input.AsSpan())` with a `StringBuilder` — enters math-mode directly, no `$` wrapping

**ParseMath(ReadOnlySpan<char>, ref int, StringBuilder)** — core math-mode parser:
- Iterates chars via `ref int position`
- Dispatches:
  - `\` → `ParseCommand()` (reads `\WORD`, optional trailing whitespace)
  - `{` → `ParseGroup()` (recursive, returns string)
  - `}` → stop (end of group)
  - `$` → stop (end of math — but our entry doesn't emit `$`, so this is a terminal)
  - `_` or `^` → treated as commands (in HAS_ARG)
  - Other → append directly (run of plain chars)
- After collecting items, runs the **command-stack** algorithm (port of Python's `math` visitor):
  - Walk items, building `List<List<string>>` of command stacks ending in a leaf
  - For each stack, call `HandleCmds(cmds, leaf)` (port of `_handle_cmds`)

**ParseCommand(ReadOnlySpan<char>, ref int)** — reads `\WORD`:
- Advance past `\`
- Read word chars (`a-z`, `A-Z`)
- Strip optional trailing whitespace
- Return the command string (e.g. `\alpha`)

**ParseGroup(ReadOnlySpan<char>, ref int, StringBuilder)** — reads `{item*}`:
- Recursive call back into the item-level parse
- Returns the group content as a string
- Tracks nesting depth for `{`/`}` matching

**HandleCmds(List<string> cmds, string leaf)** — port of `_handle_cmds`:
- `innermost = true`
- Walk `cmds` in reverse:
  - Try `cmd{leaf}` as combined lookup in command map
  - If found → leaf = result
  - If not found, and cmd is `\text`/`\mathrm` → pass through (leaf unchanged)
  - If not found, and innermost → look up leaf alone in command map
  - If cmd itself is in command map → append cmd's Unicode (diacritical modifier)
  - If cmd not in IGNORE_AS_FALLBACK → return raw `cmd{leaf}` LaTeX
  - `innermost = false`
- If no cmds → simple lookup: `COMMANDS.Get(leaf, leaf)`

**Embedded resource loading**:
```csharp
private static Dictionary<string, string> LoadDefaultCommands()
{
    var assembly = typeof(LatexConverterService).Assembly;
    using var stream = assembly.GetManifestResourceStream("LaTeXInserter.Assets.Commands.json")!;
    return JsonSerializer.Deserialize(stream, JsonContext.Default.DictionaryStringString)!;
}
```

**Graceful degradation**:
- Unknown command → output raw text (e.g. `\unknownfoo` stays as `\unknownfoo`)
- Malformed LaTeX (unmatched `{`) → output raw text, never throw
- Empty input → return empty string

---

### 14. Test Project: `tests/LaTeXInserter.Tests/LaTeXInserter.Tests.csproj`

**Path**: `tests/LaTeXInserter.Tests/LaTeXInserter.Tests.csproj`
**Action**: Create
**Logic**:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsAotCompatible>true</IsAotCompatible>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="NSubstitute" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\LaTeXInserter\LaTeXInserter.csproj" />
  </ItemGroup>
</Project>
```

---

### 15. Tests: `tests/LaTeXInserter.Tests/LatexConverterServiceTests.cs`

**Path**: `tests/LaTeXInserter.Tests/LatexConverterServiceTests.cs`
**Action**: Create
**Logic**:

Tests for `LatexConverterService` with mocked `ISettingsService`:

1. **Simple command** — `\alpha` → `α`
2. **Plain text** — `hello` → `hello`
3. **Mixed text + command** — `x = \alpha + \beta` → `x = α + β`
4. **Superscript** — `x^2` → `x²`
5. **Subscript** — `x_i` → `xᵢ`
6. **Nested groups** — `x^{\alpha_{i}}` → `x^{αᵢ}` (group contents rendered, braces preserved)
7. **Command with argument** — `\hat{a}` → `â`
8. **Nested command with argument** — `\vec{\alpha}` → `α⃗`
9. **sqrt special case** — `\sqrt{x}` → uses `\overline` mapping for `x`, then appends √
10. **Escaped characters** — `\{\}` → `{}`
11. **Unknown command** — `\unknownfoo` → `\unknownfoo` (raw output)
12. **Malformed input** — `x^{` (unmatched brace) → graceful, no exception
13. **Empty input** — `` → ``
14. **Custom mapping override** — mock returns `\alpha β`, overrides built-in `α` → `β`
15. **Custom mapping with `{` auto-adds to HAS_ARG** — mock returns `\myhat{ x̂`, `\myhat{x}` processes as HAS_ARG
16. **IGNORE_AS_FALLBACK** — `\text{hello}` → `hello` (text stripped, content preserved)
17. **Combined command lookup** — `\hat{a}` looks up `\hat{a}` first, falls back to modifier

---

### 16. Tests: `tests/LaTeXInserter.Tests/SettingsServiceTests.cs`

**Path**: `tests/LaTeXInserter.Tests/SettingsServiceTests.cs`
**Action**: Create
**Logic**:

Lightweight integration tests with temp directory:

1. **Default settings on missing file** — `Load()` returns `AppSettings.Default` when no file exists
2. **Round-trip** — `Save()` then `Load()` preserves values
3. **Corrupt file** — `Load()` with invalid JSON returns defaults
4. **GetCustomMappingLines — no file** — returns empty
5. **GetCustomMappingLines — with file** — returns file lines
6. **GetCustomMappingLines — comments skipped** — (no, actually parsing is in converter — this just returns raw lines)

---

### 17. Tests: `tests/LaTeXInserter.Tests/HotkeyChordTests.cs`

**Path**: `tests/LaTeXInserter.Tests/HotkeyChordTests.cs`
**Action**: Create
**Logic**:

1. **Default matches expected** — `AppSettings.Default.Hotkey` == `new HotkeyChord(ModifierMask.Control | ModifierMask.Alt, KeyCode.VcM)`
2. **ModifierMask flags** — bitwise ops work: `ModifierMask.Control | ModifierMask.Alt` has both flags
3. **Equality** — two identical HotkeyChords are equal (record struct)
4. **JSON round-trip** — `HotkeyChord` serializes/deserializes via JsonContext with string enums

---

## Execution Order

1. Create solution + main project from template
2. Clean up template files (delete MainWindow, ViewLocator, etc.)
3. Modify `.csproj` (NuGet refs, embedded resource, version)
4. Create `Models/HotkeyChord.cs` + `ModifierMask` enum
5. Create `Models/AppSettings.cs`
6. Create `Models/JsonContext.cs`
7. Create `Assets/Commands.json` (already done)
8. Create `Services/ISettingsService.cs`
9. Create `Services/SettingsService.cs`
10. Create `Services/ILatexConverterService.cs`
11. Create `Services/LatexConverterService.cs` (biggest file)
12. Rewrite `Program.cs` (DI composition root)
13. Modify `App.axaml` + `App.axaml.cs` (shutdown mode, service provider wiring)
14. Create test project
15. Write tests
16. `dotnet build` + `dotnet test` — verify everything compiles and passes

---

## Files NOT Created in Phase 1

- `HotkeyBlocklist.cs` (Phase 2)
- `HotkeyService.cs`, `InputSimulatorService.cs` (Phase 2)
- `IWindowActivator.cs`, `WindowsWindowActivator.cs` (Phase 2)
- `TrayIconViewModel.cs`, `AppManager.cs` (Phase 3)
- `OverlayWindow.axaml`, `OverlayViewModel.cs` (Phase 4)
- `HotkeyDialogWindow.axaml` (Phase 5)
- `UpdateService.cs` (Phase 6)

---

## Build Verification

After all files created:
1. `dotnet build LaTeXInserter.sln` — must compile with zero errors
2. `dotnet test LaTeXInserter.sln` — all tests pass
3. Verify `Commands.json` is embedded: `dotnet run --project src/LaTeXInserter` should not crash (app starts, no window, exits — that's OK for Phase 1)
