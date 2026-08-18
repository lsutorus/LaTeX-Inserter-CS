<div align="center">
  <img src="LaTeX-Inserter-icon-final-png.png" width="256" height="256" alt="LaTeX Inserter logo">
</div>

<div align="center">
  <a href="https://github.com/lsutorus/LaTeX-Inserter-CS/releases/latest"><img src="https://img.shields.io/github/v/release/lsutorus/LaTeX-Inserter-CS?color=800f00&style=plastic" alt="GitHub release"></a>&nbsp;<img src="https://img.shields.io/badge/OS-Windows-blue?style=plastic&logo=windows&color=0a348f" alt="Windows OS">&nbsp;<a href="https://github.com/lsutorus/LaTeX-Inserter-CS/search?l=c%23"><img src="https://img.shields.io/badge/.NET-10-512BD4?style=plastic&logo=dotnet&logoColor=white" alt=".NET 10"></a>&nbsp;<a href="https://github.com/lsutorus/LaTeX-Inserter-CS/blob/master/LICENSE"><img src="https://img.shields.io/badge/license-MIT-C41E3A" alt="License"></a>
</div>

# LaTeX Inserter

A Windows system tray app that lets you type LaTeX and paste Unicode equivalents anywhere. Press **Ctrl+Alt+M**, type LaTeX-style commands, and hit Enter. The Unicode equivalent is pasted into whatever window you were in and copied to your clipboard.

## How it works

1. **Ctrl+Alt+M** opens a floating overlay near your cursor
2. Type LaTeX: e.g. `\alpha`, `\sqrt{x^2}`, `\sum`, `\Rightarrow`
3. See the Unicode preview in real time
4. Press **Enter**: the Unicode is copied to your clipboard and auto-pasted into whatever window you were in


## Install

> Requires Windows 10 or later. The app runs as admin (needed for global hotkey detection).

1. Go to [Releases](https://github.com/lsutorus/LaTeX-Inserter-CS/releases/latest).
2. Download **`LaTeXInserter-win-Setup.exe`**.
3. Run it. Velopack installs the app and launches it.
4. The app lives in system tray. Right-click the tray icon to configure it.

A portable build (`LaTeXInserter-win-Portable.zip`) is also available if you prefer not to install.

## Features

- Autocomplete (like IntelliSense)
- Live Unicode preview
- Editable commands/symbols (via custom mappings). Edit or create your own `\command char` pairs.
- Configurable hotkey
- In-app updates

## Editing custom mappings

Open **Edit Custom Mappings...** from the right-click settings window. Mappings are plain text. One per line, `\command` followed by two spaces and the character:

```
\myalpha  α
\heart    ❤
```

This file (`custom_mappings.txt`) lives in your AppData folder and takes precedence over the built-in defaults on save.

## Examples

https://github.com/user-attachments/assets/8c598fb8-487c-405b-b8f8-06479c37f005

| LaTeX | Unicode |
|-------|---------|
| `\alpha \beta \gamma` | 𝛼 𝛽 𝛾 |
| `\to \longrightarrow` | → ⟶ |
| `\infty` | ∞ |
| `\partial` | ∂ |
| `\sqrt(x^2)` | √(x²) |
| `\int x^2 dx = 1\fracslash3 x^3 + C` | ∫x² dx = 1⁄3 x³ + C |
| `\sum^n_{i=m} f(i) \longleftrightarrow\,\int^b_a f(x) dx` | ∑ⁿᵢ₌ₘ f(i) ⟷ ∫ᵇₐ f(x) dx |
| `\therefore\,a^2+b^2=c^2` | ∴ a²+b²=c² |

> TIP: '\\,' (backslash followed by a comma) produces a space.

## Uninstall

Use **Add/Remove Programs** in Windows Settings, or run the uninstaller shortcut the app created. Your `custom_mappings.txt` is left in AppData if you reinstall later.

## Building from source

```powershell
# Requires .NET 10 SDK
dotnet build LaTeXInserter.slnx
dotnet run --project src/LaTeXInserter
```

To publish a release build (Native AOT, win-x64):

```powershell
dotnet publish src/LaTeXInserter -c Release -r win-x64 -o publish
```

To package an installer with Velopack:

```powershell
dotnet tool install --global vpk
Copy-Item src/LaTeXInserter/Assets/LaTeX-Inserter-icon-final.ico publish/
vpk pack --packId LaTeXInserter --packVersion 0.0.13 --packDir publish `
  --mainExe LaTeXInserter.exe --icon publish/LaTeX-Inserter-icon-final.ico
```

Running locally on Windows requires elevation (global keyboard hooks need admin).

## License

This project is licensed under the [MIT](https://choosealicense.com/licenses/mit/) License. See [LICENSE](LICENSE) for details.
