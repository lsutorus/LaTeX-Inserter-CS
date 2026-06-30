# CLAUDE.md

Guidance for Claude Code when working on this repository.

## What This App Does

Cross-platform system-tray utility. Configurable hotkey (default Ctrl+Alt+M) opens overlay near cursor. User types LaTeX (e.g. `\sqrt{x^2}`), sees Unicode preview, hits Enter → Unicode copied to clipboard and auto-pasted into previous window.

## Tech Stack

- **Runtime**: .NET 10 with Native AOT
- **UI**: Avalonia UI (headless, borderless floating popup, TrayIcon)
- **MVVM**: CommunityToolkit.Mvvm (source-gen `ObservableObject`, `RelayCommand`)
- **Hotkeys**: SharpHook (global hooks + input simulation)
- **Updater**: Velopack 1.2.0 (GitHub Releases backend, `GithubSource`, 10s timeout on check)
- **DI**: Microsoft.Extensions.DependencyInjection (strict constructor injection)
- **Data**: System.Text.Json with source generators (`JsonSerializerContext`)
- **P/Invoke**: `[LibraryImport]` only (no legacy `[DllImport]`)

## Build & Run

```bash
dotnet build LaTeXInserter.sln
dotnet run --project src/LaTeXInserter
```

Requires admin on Windows (keyboard hooks require elevation).

## Versioning & Release

- Version in `LaTeXInserter.csproj` as `<Version>x.y.z</Version>` (semver)
- Tag format: `vx.y.z` (push tag → GitHub Actions CI)
- Velopack creates GitHub releases with tag name WITHOUT `v` prefix (e.g. `0.0.5` not `v0.0.5`)
- Release assets: Velopack bundle + SHA256
- Draft releases auto-created by `vpk upload`; publish manually via `gh release edit <tag> --draft=false`
- Current version: 0.0.7

## Architecture & Documentation

**For structural context, file tree, and design decisions, read:**
- [`docs/architecture.md`](docs/architecture.md) — project structure, MVVM pattern, DI wiring, SharpHook details, Velopack integration, parser grammar, autocomplete design, window activation

**Migration tracking:**
- [`MIGRATION_PLAN.md`](MIGRATION_PLAN.md) — 6-phase plan with checkboxes, architectural constraints, anti-patterns. After completing tasks/phases, mark them as done ([x]).

## Key Conventions

- **Native AOT**: No reflection-based JSON, no `[DllImport]`, no runtime IL emission
- **Source-gen everything**: `JsonSerializerContext`, `[ObservableProperty]`, `[RelayCommand]`, `[LibraryImport]`
- **DI strictness**: Constructor injection only. No service locator. Composition root in `Program.cs`
- **Single SharpHook instance**: Flag-based dispatch for normal vs recording mode
- **Hotkey model**: `record HotkeyChord(ModifierMask Modifiers, KeyCode TriggerKey)` with `[Flags]` enum
- **Custom mappings**: Plain text `\command char` at AppData, NOT JSON (user-editable by design). `LatexConverterService.DefaultCommands` exposes pre-merge defaults for CustomMappingsWindow Tab 2.
- **Custom mappings window**: Non-modal singleton via AppManager (same pattern as SettingsWindow). Tabbed UI: Tab 1 "Custom Mappings" (from `custom_mappings.txt`), Tab 2 "Default Mappings" (from `Commands.json`). Inline edit via MappingItem.IsEditing flag. Save overwrites file + calls `LatexConverterService.Reload()`. Cancel discards staged changes. `Reload()` on open refreshes from disk.
- **Default commands**: `Commands.json` embedded resource, loaded via source-gen deserializer
- **Autocomplete**: TextBox + Popup + ListBox (IntelliSense pattern), NOT AutoCompleteBox
- **Settings window**: Non-modal singleton via AppManager, native OS chrome, `CanResize = false` in code-behind (Avalonia 12 ResizeMode XML attribute fails with compiled bindings AVLN2000). Live reload overlay via `SettingsSaved` event → `OverlayViewModel.ApplySettings()`. Hotkey change + startup toggle moved from tray to Settings (ChangeHotkeyRequested routed via AppManager). Startup sync via `IStartupRegistrar.SyncRegistrationAsync()` on init and save.
- **Accent color**: hex string in settings → `IAccentColorModule.Apply(hex)` sets App resources + persists + raises `AccentColorApplied` event. `OverlayViewModel` subscribes to event, parses hex to `IBrush` (solid + 0.25 opacity for selection bg). No `App.ApplyAccentColor()` static, no `AccentColorChanged` event on SettingsViewModel.
- **Font sizes**: Two independent settings — `InputFontSize` (input TextBox + autocomplete dropdown) and `PreviewFontSize` (unicode preview TextBlock)
- **Update check UX**: Immediate dialog with indeterminate progress bar on "Check for Updates" click; content swaps to result once check completes (up-to-date / error / update available)

## Anti-patterns

- **No hotkey polling** — event-driven via SharpHook, no timer loops
- **No hotkey/startup in tray** — moved to Settings window; tray only has Show/Hide, Settings, Edit Custom Mappings..., Check for Updates..., Quit
- **No Reload Custom Mappings in tray** — removed; reload happens automatically on Save in CustomMappingsWindow
- **No `AutoCompleteBox`** — replaces prefix text in multi-command input
- **No accent color as `Color` struct in AppSettings** — use hex string, parse to `IBrush` on ViewModel (AOT-safe, no custom JsonConverter needed)
- **No `DynamicResource` for accent bg in DataTemplate** — DataContext shifts inside DataTemplate; use ListBox-level Resources + code-behind swap instead. For swatch selection ring, use code-behind CSS class toggle (`accent-selected`), not DynamicResource
- **No shipping loose exes** — release assets = Velopack bundle + sha256 only
- **No `.bak` file swap** — Velopack handles in-place update
- **No dummy releases for testing** — downgrade local version instead
- **No service locator** — no `App.ServiceProvider.GetService()` in random classes
- **No `[DllImport]`** — always `[LibraryImport]` + partial methods
- **No reflection-based JSON** — always `JsonSerializerContext` source generators
- **No `ResizeMode` in AXAML** — Avalonia 12 compiled bindings fail with AVLN2000; set `CanResize` property in code-behind instead
- **No Models importing Services** — `HotkeyBlocklist` uses record value equality, not `HotkeyNormalizer.Normalize()`. Services→Models dependency direction is forbidden.
- **No `App.ApplyAccentColor()` static** — use `IAccentColorModule.Apply(hex)` from DI
- **No `AccentColorChanged` event** — removed; `SettingsViewModel.SelectSwatch()` calls `IAccentColorModule.Apply()` which raises `AccentColorApplied`. OverlayVM subscribes directly.
- **No inline startup sync** — use `IStartupRegistrar.SyncRegistrationAsync(bool desired)` instead of compare-then-register/unregister pattern
- **No platform P/Invoke in Views** — overlay positioning uses `IOverlayPositioner.PositionOverlay(window)`, not direct `NativeMethods.GetCursorPos` in code-behind

## Agent skills

### Issue tracker

Issues tracked in GitHub (lsutorus/LaTeX-Inserter-CS). See `docs/agents/issue-tracker.md`.

### Triage labels

Using default Matt Pocock triage labels. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context layout. See `docs/agents/domain.md`.
