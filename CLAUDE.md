# CLAUDE.md

Guidance for Claude Code when working on this repository.

## What This App Does

Cross-platform system-tray utility. Configurable hotkey (default Ctrl+Alt+M) opens overlay near cursor. User types LaTeX (e.g. `\sqrt{x^2}`), sees Unicode preview, hits Enter → Unicode copied to clipboard and auto-pasted into previous window.

## Tech Stack

- **Runtime**: .NET 10 with Native AOT
- **UI**: Avalonia UI (headless, borderless floating popup, TrayIcon)
- **MVVM**: CommunityToolkit.Mvvm (source-gen `ObservableObject`, `RelayCommand`)
- **Hotkeys**: SharpHook (global hooks + input simulation)
- **Updater**: Velopack (GitHub Releases backend)
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
- Tag format: `vx.y.z`
- Push tag → GitHub Actions: build + Velopack pack + upload to GitHub Release
- Release assets: Velopack bundle + SHA256

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
- **Custom mappings**: Plain text `\command char` at AppData, NOT JSON (user-editable by design)
- **Default commands**: `Commands.json` embedded resource, loaded via source-gen deserializer
- **Autocomplete**: TextBox + Popup + ListBox (IntelliSense pattern), NOT AutoCompleteBox

## Anti-patterns

- **No hotkey polling** — event-driven via SharpHook, no timer loops
- **No local-only tray menu items** — store all as fields on ViewModel (prevent GC)
- **No `AutoCompleteBox`** — replaces prefix text in multi-command input
- **No shipping loose exes** — release assets = Velopack bundle + sha256 only
- **No `.bak` file swap** — Velopack handles in-place update
- **No dummy releases for testing** — downgrade local version instead
- **No service locator** — no `App.ServiceProvider.GetService()` in random classes
- **No `[DllImport]`** — always `[LibraryImport]` + partial methods
- **No reflection-based JSON** — always `JsonSerializerContext` source generators

## Agent skills

### Issue tracker

Issues tracked in GitHub (lsutorus/LaTeX-Inserter-CS). See `docs/agents/issue-tracker.md`.

### Triage labels

Using default Matt Pocock triage labels. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context layout. See `docs/agents/domain.md`.
