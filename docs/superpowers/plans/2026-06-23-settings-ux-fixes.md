# Settings & UX Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix accent color settings, fix superscript/subscript LaTeX parsing, add conversion hints, restructure settings window, and improve autocomplete spacing.

**Architecture:** Incremental fixes — parser gets a `CaptureRawGroup` helper for double-lookup, converter tracks unresolved commands, settings window gets restructured into Appearance/General sections with accent color fix and border ring selection, tray menu items move into settings.

**Tech Stack:** .NET 10 / Native AOT, Avalonia UI, CommunityToolkit.Mvvm, NSubstitute (tests)

## Global Constraints

- No reflection-based JSON — always `JsonSerializerContext` source generators
- No `[DllImport]` — always `[LibraryImport]` + partial methods
- DI strictness: constructor injection only, no service locator
- Source-gen everything: `[ObservableProperty]`, `[RelayCommand]`, etc.
- Accent colors stored as hex strings in `AppSettings`, parsed to `IBrush` on ViewModel (AOT-safe)
- No `DynamicResource` for accent bg in DataTemplate — use ListBox-level Resources + code-behind swap
- Custom type converters must be AOT-safe (no runtime reflection)

---

### Task 1: Default PreviewFontSize 14→20 + autocomplete spacing

**Files:**
- Modify: `src/LaTeXInserter/Models/AppSettings.cs:9`
- Modify: `src/LaTeXInserter/ViewModels/OverlayViewModel.cs:35`
- Modify: `src/LaTeXInserter/Views/OverlayWindow.axaml:73`
- Test: `tests/LaTeXInserter.Tests/SettingsServiceTests.cs`

**Interfaces:**
- Consumes: `AppSettings` record with `PreviewFontSize` default
- Produces: `PreviewFontSize = 20` default, autocomplete spacing in XAML

- [ ] **Step 1: Change AppSettings default**

In `src/LaTeXInserter/Models/AppSettings.cs`, change line 9 from `int PreviewFontSize = 14` to `int PreviewFontSize = 20`:

```csharp
public sealed record AppSettings(
    HotkeyChord Hotkey = default,
    bool StartOnStartup = false,
    int InputFontSize = 16,
    int PreviewFontSize = 20,
    string AccentColor = "#404040",
    bool AutocompleteEnabled = true
)
```

- [ ] **Step 2: Change OverlayViewModel default**

In `src/LaTeXInserter/ViewModels/OverlayViewModel.cs`, change line 35 from `_previewFontSize = 14` to `_previewFontSize = 20`:

```csharp
[ObservableProperty]
private int _previewFontSize = 20;
```

- [ ] **Step 3: Add spacing in autocomplete DataTemplate**

In `src/LaTeXInserter/Views/OverlayWindow.axaml`, add `Margin="8,0,0,0"` to the Unicode TextBlock (line 73):

```xml
<TextBlock Grid.Column="1"
           Text="{Binding Unicode}"
           Opacity="0.6"
           VerticalAlignment="Center"
           Margin="8,0,0,0" />
```

- [ ] **Step 4: Build and run tests**

Run: `dotnet test tests/LaTeXInserter.Tests`
Expected: All tests pass

- [ ] **Step 5: Commit**

```bash
git add src/LaTeXInserter/Models/AppSettings.cs src/LaTeXInserter/ViewModels/OverlayViewModel.cs src/LaTeXInserter/Views/OverlayWindow.axaml
git commit -m "fix: default preview font size 14→20, add autocomplete spacing"
```

---

### Task 2: Parser fix for ^{\\cmd} / _{\\cmd} double-lookup

**Files:**
- Modify: `src/LaTeXInserter/Services/LatexConverterService.cs:169-191, 297-317`
- Test: `tests/LaTeXInserter.Tests/LatexConverterServiceTests.cs`

**Interfaces:**
- Consumes: `HandleCmds(List<string>, string)` method, `Commands` dictionary
- Produces: Correct superscript/subscript of LaTeX commands (e.g. `x^{\gamma}` → `xᵞ`)

**Root cause:** `ParseGroup` resolves `\gamma` → Unicode `𝛾` before `HandleCmds` sees it. `HandleCmds` then looks up `^{𝛾}` (Unicode in braces) but the dictionary key is `^{\gamma}` (LaTeX form). No match → returns raw literal.

**Fix:** Capture raw group text (before command resolution) alongside resolved text. If the resolved form doesn't match in `HandleCmds`, retry with the raw form.

- [ ] **Step 1: Write the failing test**

In `tests/LaTeXInserter.Tests/LatexConverterServiceTests.cs`, add after line 33:

```csharp
[Fact]
public void SuperscriptCommand() => Assert.Equal("xᵞ", CreateService().Convert(@"x^{\gamma}"));

[Fact]
public void SubscriptCommand() => Assert.Equal("xᵧ", CreateService().Convert(@"x_{\gamma}"));
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/LaTeXInserter.Tests --filter "SuperscriptCommand|SubscriptCommand"`
Expected: FAIL — both return raw `^{𝛾}` / `_{𝛧}` instead of Unicode superscript/subscript

- [ ] **Step 3: Add CaptureRawGroup helper**

In `src/LaTeXInserter/Services/LatexConverterService.cs`, add the following static method after the `ParseGroup` method (after line 327):

```csharp
/// <summary>
/// Captures the raw text between braces at <paramref name="openBracePos"/>
/// without resolving any LaTeX commands inside. Does NOT advance <c>pos</c>.
/// </summary>
private static string CaptureRawGroup(ReadOnlySpan<char> span, int openBracePos)
{
    var depth = 1;
    var pos = openBracePos + 1;
    while (pos < span.Length && depth > 0)
    {
        if (span[pos] == '{') depth++;
        else if (span[pos] == '}') depth--;
        pos++;
    }
    // pos is now after the matching '}'; content is between openBracePos+1 and pos-1
    return span[(openBracePos + 1)..(pos - 1)].ToString();
}
```

- [ ] **Step 4: Modify ^/_ handler in ParseMath to use double-lookup**

In `src/LaTeXInserter/Services/LatexConverterService.cs`, replace the `^`/`_` block in `ParseMath` (lines 169-191) with:

```csharp
else if (ch == '_' || ch == '^')
{
    pos++;
    var cmd = ch.ToString();
    if (_hasArg.Contains(cmd) && pos < span.Length && span[pos] == '{')
    {
        var openBrace = pos; // save position of '{'
        var rawGroupContent = CaptureRawGroup(span, openBrace);
        var groupContent = ParseGroup(span, ref pos, depth + 1);
        var result = HandleCmds([cmd], groupContent);

        // If unresolved (returned raw "^{...}" or "_{...}"), retry with raw group text.
        // This handles cases like ^{\gamma} where ParseGroup resolves \gamma→Unicode
        // before HandleCmds can look up the combined key "^{\gamma}".
        if (result == $"{cmd}{{{groupContent}}}")
        {
            var rawResult = HandleCmds([cmd], rawGroupContent);
            if (rawResult != $"{cmd}{{{rawGroupContent}}}")
                result = rawResult;
        }

        sb.Append(result);
    }
    else if (pos < span.Length)
    {
        // Subscript/superscript of single char
        var leaf = span[pos].ToString();
        pos++;
        var result = HandleCmds([cmd], leaf);
        sb.Append(result);
    }
    else
    {
        sb.Append(cmd);
    }
}
```

- [ ] **Step 5: Modify ^/_ handler in ParseGroup to use double-lookup**

In `ParseGroup` (lines 297-317), replace the `^`/`_` block with the same pattern:

```csharp
else if (ch == '_' || ch == '^')
{
    pos++;
    var cmd = ch.ToString();
    if (_hasArg.Contains(cmd) && pos < span.Length && span[pos] == '{')
    {
        var openBrace = pos;
        var rawGroupContent = CaptureRawGroup(span, openBrace);
        var groupContent = ParseGroup(span, ref pos, depth + 1);
        var result = HandleCmds([cmd], groupContent);

        if (result == $"{cmd}{{{groupContent}}}")
        {
            var rawResult = HandleCmds([cmd], rawGroupContent);
            if (rawResult != $"{cmd}{{{rawGroupContent}}}")
                result = rawResult;
        }

        sb.Append(result);
    }
    else if (pos < span.Length)
    {
        var leaf = span[pos].ToString();
        pos++;
        var result = HandleCmds([cmd], leaf);
        sb.Append(result);
    }
    else
    {
        sb.Append(cmd);
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/LaTeXInserter.Tests`
Expected: ALL tests pass, including new `SuperscriptCommand` and `SubscriptCommand`

- [ ] **Step 7: Commit**

```bash
git add src/LaTeXInserter/Services/LatexConverterService.cs tests/LaTeXInserter.Tests/LatexConverterServiceTests.cs
git commit -m "fix: resolve superscript/subscript of LaTeX commands via double-lookup"
```

---

### Task 3: Conversion hints for unresolved commands

**Files:**
- Modify: `src/LaTeXInserter/Abstractions/ILatexConverterService.cs:5-9`
- Modify: `src/LaTeXInserter/Services/LatexConverterService.cs:13, 104-113, 329-380`
- Modify: `src/LaTeXInserter/ViewModels/OverlayViewModel.cs:22-23, 61-103`
- Modify: `src/LaTeXInserter/Views/OverlayWindow.axaml:39-44`
- Test: `tests/LaTeXInserter.Tests/LatexConverterServiceTests.cs`
- Test: `tests/LaTeXInserter.Tests/OverlayViewModelTests.cs`

**Interfaces:**
- Consumes: `ILatexConverterService.Convert(string)` and `ILatexConverterService.LastUnresolvedCommands`
- Produces: `OverlayViewModel.ConversionHint` (string) + `HasConversionHint` (bool)

When `HandleCmds` can't resolve a command and returns the raw form (e.g. `^{\omega}` if no Unicode exists), the converter tracks it. The overlay shows a grey subtle hint below the preview.

- [ ] **Step 1: Write the failing test for unresolved tracking**

In `tests/LaTeXInserter.Tests/LatexConverterServiceTests.cs`, add:

```csharp
[Fact]
public void UnresolvedCommand_Tracked()
{
    var svc = CreateService();
    svc.Convert(@"\unknownfoo{x}");
    Assert.NotEmpty(svc.LastUnresolvedCommands);
    Assert.Contains(@"\unknownfoo{x}", svc.LastUnresolvedCommands);
}

[Fact]
public void ResolvedCommand_NotTracked()
{
    var svc = CreateService();
    svc.Convert(@"\alpha");
    Assert.Empty(svc.LastUnresolvedCommands);
}
```

- [ ] **Step 2: Run tests — expect compile error on LastUnresolvedCommands**

Run: `dotnet test tests/LaTeXInserter.Tests --filter "UnresolvedCommand_Tracked|ResolvedCommand_NotTracked"`
Expected: Compile error — `LastUnresolvedCommands` does not exist on interface

- [ ] **Step 3: Add LastUnresolvedCommands to interface**

In `src/LaTeXInserter/Abstractions/ILatexConverterService.cs`, add property:

```csharp
namespace LaTeXInserter.Abstractions;

public interface ILatexConverterService
{
    string Convert(string input);
    IReadOnlyDictionary<string, string> Commands { get; }
    IReadOnlyList<string> CommandNames { get; }
    void Reload();
    IReadOnlyList<string> LastUnresolvedCommands { get; }
}
```

- [ ] **Step 4: Implement tracking in LatexConverterService**

In `src/LaTeXInserter/Services/LatexConverterService.cs`, add field after line 15 (`_commandNames`):

```csharp
private readonly List<string> _unresolvedCommands = [];
```

Add property after `CommandNames` property (line 51):

```csharp
public IReadOnlyList<string> LastUnresolvedCommands => _unresolvedCommands;
```

Clear the list at the start of `Convert` method (line 104-113). Replace the method body:

```csharp
public string Convert(string input)
{
    if (string.IsNullOrEmpty(input))
    {
        _unresolvedCommands.Clear();
        return string.Empty;
    }

    _unresolvedCommands.Clear();
    var sb = new StringBuilder(input.Length);
    var pos = 0;
    var span = input.AsSpan();
    ParseMath(span, ref pos, sb, depth: 0);
    return sb.ToString();
}
```

In `HandleCmds`, add tracking at the unresolved return point. Replace the `HandleCmds` method (lines 329-380) with:

```csharp
private string HandleCmds(List<string> cmds, string leaf)
{
    if (cmds.Count == 0)
        return _commands.TryGetValue(leaf, out var v) ? v : leaf;

    var innermost = true;

    for (var i = cmds.Count - 1; i >= 0; i--)
    {
        var cmd = cmds[i];
        var combined = $"{cmd}{{{leaf}}}";

        // Step 1: try combined lookup first (e.g. \hat{a} → â)
        if (_commands.TryGetValue(combined, out var combinedResult))
        {
            leaf = combinedResult;
            innermost = false;
            continue;
        }

        // Step 2: resolve leaf if innermost (first pass)
        if (innermost && _commands.TryGetValue(leaf, out var leafResult))
        {
            leaf = leafResult;
        }

        // Step 3: pass-through commands
        if (cmd == "\\text" || cmd == "\\mathrm")
        {
            innermost = false;
            continue;
        }

        // Step 4: try cmd as modifier (e.g. \hat → combining circumflex)
        if (_commands.TryGetValue(cmd, out var cmdResult))
        {
            leaf = leaf + cmdResult;
            innermost = false;
            continue;
        }

        // Step 5: no mapping — track as unresolved and return raw
        if (!IgnoreAsFallback.Contains(cmd))
        {
            var unresolved = $"{cmd}{{{leaf}}}";
            _unresolvedCommands.Add(unresolved);
            return unresolved;
        }

        innermost = false;
    }

    return leaf;
}
```

- [ ] **Step 5: Run converter tests**

Run: `dotnet test tests/LaTeXInserter.Tests`
Expected: All tests pass, including new unresolved tracking tests

- [ ] **Step 6: Write failing test for OverlayViewModel conversion hint**

In `tests/LaTeXInserter.Tests/OverlayViewModelTests.cs`, update `CreateConverter` to also mock `LastUnresolvedCommands`:

```csharp
private static ILatexConverterService CreateConverter(
    string? convertResult = null,
    IReadOnlyList<string>? commandNames = null,
    IReadOnlyDictionary<string, string>? commands = null,
    IReadOnlyList<string>? unresolvedCommands = null)
{
    var mock = Substitute.For<ILatexConverterService>();
    mock.Convert(Arg.Any<string>()).Returns(convertResult ?? string.Empty);
    mock.CommandNames.Returns(commandNames ?? []);
    mock.Commands.Returns(commands ?? new Dictionary<string, string>());
    mock.LastUnresolvedCommands.Returns(unresolvedCommands ?? []);
    return mock;
}
```

Add test:

```csharp
[Fact]
public void UnresolvedCommand_ShowsConversionHint()
{
    var converter = CreateConverter(convertResult: @"^{\omega}", unresolvedCommands: new List<string> { @"^{\omega}" });
    converter.Convert(@"x^{\omega}").Returns(@"^{\omega}");
    converter.LastUnresolvedCommands.Returns(new List<string> { @"^{\omega}" }.AsReadOnly());
    var vm = new OverlayViewModel(converter, CreateSettings());

    vm.InputText = @"x^{\omega}";

    Assert.NotEmpty(vm.ConversionHint);
    Assert.True(vm.HasConversionHint);
}

[Fact]
public void ResolvedCommand_NoConversionHint()
{
    var converter = CreateConverter(convertResult: "α");
    var vm = new OverlayViewModel(converter, CreateSettings());

    vm.InputText = @"\alpha";

    Assert.Equal(string.Empty, vm.ConversionHint);
    Assert.False(vm.HasConversionHint);
}
```

- [ ] **Step 7: Run tests — expect compile error on ConversionHint**

Run: `dotnet test tests/LaTeXInserter.Tests --filter "UnresolvedCommand_ShowsConversionHint|ResolvedCommand_NoConversionHint"`
Expected: Compile error — `ConversionHint` and `HasConversionHint` do not exist

- [ ] **Step 8: Add ConversionHint and HasConversionHint to OverlayViewModel**

In `src/LaTeXInserter/ViewModels/OverlayViewModel.cs`, add properties after `_isAutocompleteEnabled` (line 47):

```csharp
[ObservableProperty]
private string _conversionHint = string.Empty;

public bool HasConversionHint => !string.IsNullOrEmpty(ConversionHint);
```

In `OnInputTextChanged`, after `PreviewText = _converter.Convert(value);` (line 72), add hint setup. Replace the beginning of `OnInputTextChanged`:

```csharp
partial void OnInputTextChanged(string value)
{
    if (string.IsNullOrEmpty(value))
    {
        PreviewText = string.Empty;
        ConversionHint = string.Empty;
        IsAutocompleteOpen = false;
        AutocompleteItems.Clear();
        SelectedAutocompleteItem = null;
        return;
    }

    PreviewText = _converter.Convert(value);
    ConversionHint = _converter.LastUnresolvedCommands.Count > 0
        ? string.Join(", ", _converter.LastUnresolvedCommands) + " — no Unicode equivalent"
        : string.Empty;
    OnPropertyChanged(nameof(HasConversionHint));

    // ... rest of autocomplete logic unchanged ...
```

- [ ] **Step 9: Clear hint on Cancel and ResetState**

In `Cancel()` and `ResetState()` methods, add `ConversionHint = string.Empty;`.

Replace `Cancel()`:

```csharp
public void Cancel()
{
    IsAutocompleteOpen = false;
    InputText = string.Empty;
    ConversionHint = string.Empty;
    PreviewText = string.Empty;
    HideRequested?.Invoke(this, EventArgs.Empty);
}
```

Replace `ResetState()`:

```csharp
public void ResetState()
{
    IsAutocompleteOpen = false;
    InputText = string.Empty;
    ConversionHint = string.Empty;
    PreviewText = string.Empty;
}
```

- [ ] **Step 10: Add hint TextBlock to OverlayWindow.axaml**

In `src/LaTeXInserter/Views/OverlayWindow.axaml`, add a hint TextBlock after the preview `Border` (after line 44, before the `Popup`):

```xml
<Border MinHeight="24" VerticalAlignment="Center" Margin="8,2,8,4">
    <TextBlock Text="{Binding PreviewText}"
               Foreground="#cccccc"
               FontSize="{Binding PreviewFontSize}"
               TextWrapping="NoWrap" />
</Border>
<TextBlock Text="{Binding ConversionHint}"
           FontSize="11"
           Foreground="#888888"
           Margin="8,0,8,4"
           IsVisible="{Binding HasConversionHint}" />
```

- [ ] **Step 11: Run all tests**

Run: `dotnet test tests/LaTeXInserter.Tests`
Expected: ALL tests pass

- [ ] **Step 12: Commit**

```bash
git add src/LaTeXInserter/Abstractions/ILatexConverterService.cs src/LaTeXInserter/Services/LatexConverterService.cs src/LaTeXInserter/ViewModels/OverlayViewModel.cs src/LaTeXInserter/Views/OverlayWindow.axaml tests/LaTeXInserter.Tests/LatexConverterServiceTests.cs tests/LaTeXInserter.Tests/OverlayViewModelTests.cs
git commit -m "feat: add conversion hints for unresolved LaTeX commands"
```

---

### Task 4: Settings restructure + accent color fix + move tray items

**Files:**
- Modify: `src/LaTeXInserter/Views/SettingsWindow.axaml` (full rewrite)
- Modify: `src/LaTeXInserter/Views/SettingsWindow.axaml.cs` (swatch rendering + selection)
- Modify: `src/LaTeXInserter/ViewModels/SettingsViewModel.cs` (add hotkey, startup, dependencies)
- Modify: `src/LaTeXInserter/ViewModels/TrayIconViewModel.cs` (remove hotkey + startup items)
- Modify: `src/LaTeXInserter/ViewModels/AppManager.cs` (rewire events, move startup sync)
- Modify: `src/LaTeXInserter/App.axaml` (add swatch styles)
- Test: `tests/LaTeXInserter.Tests/OverlayViewModelTests.cs` (update mock for new interface)

**Interfaces:**
- Consumes: `IHotkeyService`, `IStartupRegistrar`, `ISettingsService`, existing `SettingsViewModel`
- Produces: New `SettingsViewModel` with `CurrentHotkeyDisplay`, `StartOnStartup`, `ChangeHotkeyCommand`; `TrayIconViewModel` without hotkey/startup items

**Changes summary:**
1. Fix accent color swatch rendering (converter or code-behind approach)
2. Add white 2px border ring on selected swatch
3. Restructure settings into Appearance + General sections
4. Add hotkey row (current hotkey text + "Change..." button) in General
5. Add startup checkbox in General
6. Remove "Change Hotkey" + "Run on Startup" from tray menu
7. Fix separator alignment (align with text, not full-width)
8. Wire events in AppManager

- [ ] **Step 1: Add swatch styles to App.axaml**

In `src/LaTeXInserter/App.axaml`, add swatch button styles inside `<Application.Styles>` after the existing styles (before line 50):

```xml
<!-- Accent color swatch styles -->
<Style Selector="Button.accent-swatch">
    <Setter Property="Background" Value="Transparent" />
    <Setter Property="BorderBrush" Value="Transparent" />
    <Setter Property="BorderThickness" Value="2" />
    <Setter Property="CornerRadius" Value="4" />
    <Setter Property="Padding" Value="0" />
</Style>
<Style Selector="Button.accent-swatch.accent-selected">
    <Setter Property="BorderBrush" Value="White" />
</Style>
```

- [ ] **Step 2: Rewrite SettingsWindow.axaml**

Replace entire `src/LaTeXInserter/Views/SettingsWindow.axaml` with:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="using:LaTeXInserter.ViewModels"
        x:Class="LaTeXInserter.Views.SettingsWindow"
        x:DataType="vm:SettingsViewModel"
        Title="Settings"
        SizeToContent="Height"
        Width="350"
        WindowStartupLocation="CenterScreen">

    <ScrollViewer>
        <StackPanel Margin="16" Spacing="16">

            <!-- Appearance Section -->
            <StackPanel Spacing="8">
                <TextBlock Text="Appearance" FontWeight="SemiBold" FontSize="14" />

                <Grid ColumnDefinitions="Auto,*" RowDefinitions="Auto,Auto,Auto">
                    <TextBlock Grid.Row="0" Grid.Column="0"
                               Text="Input Font Size" VerticalAlignment="Center" Margin="0,0,12,0" />
                    <NumericUpDown Grid.Row="0" Grid.Column="1"
                                   Value="{Binding InputFontSize}"
                                   Minimum="12" Maximum="24" Increment="1" />

                    <TextBlock Grid.Row="1" Grid.Column="0"
                               Text="Preview Font Size" VerticalAlignment="Center" Margin="0,4,12,4" />
                    <NumericUpDown Grid.Row="1" Grid.Column="1"
                                   Value="{Binding PreviewFontSize}"
                                   Minimum="12" Maximum="24" Increment="1" />

                    <TextBlock Grid.Row="2" Grid.Column="0"
                               Text="Accent Color" VerticalAlignment="Center" Margin="0,0,12,0" />
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
                </Grid>
            </StackPanel>

            <Border Height="1" Background="#444444" Margin="-16,0" />

            <!-- General Section -->
            <StackPanel Spacing="8">
                <TextBlock Text="General" FontWeight="SemiBold" FontSize="14" />

                <Grid ColumnDefinitions="Auto,*,Auto" RowDefinitions="Auto,Auto,Auto">
                    <TextBlock Grid.Row="0" Grid.Column="0"
                               Text="Hotkey" VerticalAlignment="Center" Margin="0,0,12,0" />
                    <TextBlock Grid.Row="0" Grid.Column="1"
                               Text="{Binding CurrentHotkeyDisplay}"
                               VerticalAlignment="Center" />
                    <Button Grid.Row="0" Grid.Column="2"
                            Content="Change..."
                            Command="{Binding ChangeHotkeyCommand}"
                            HorizontalAlignment="Right" />

                    <CheckBox Grid.Row="1" Grid.Column="0" Grid.ColumnSpan="3"
                              Content="Enable Autocomplete"
                              IsChecked="{Binding AutocompleteEnabled}"
                              Margin="0,4,0,0" />

                    <CheckBox Grid.Row="2" Grid.Column="0" Grid.ColumnSpan="3"
                              Content="Start on Startup"
                              IsChecked="{Binding StartOnStartup}"
                              Margin="0,4,0,0" />
                </Grid>
            </StackPanel>

            <Border Height="1" Background="#444444" Margin="-16,0" />

            <!-- Action Buttons -->
            <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Spacing="8">
                <Button Content="Cancel" Command="{Binding CancelCommand}" />
                <Button Content="Save" Command="{Binding SaveCommand}" />
            </StackPanel>

        </StackPanel>
    </ScrollViewer>
</Window>
```

Key changes:
- **Separator fix**: `<Border Height="1" Background="#444444" Margin="-16,0" />` uses negative margin to align with text content, not full-width
- **General section** replaces "Behavior" label
- **Hotkey row** with `CurrentHotkeyDisplay` text + `ChangeHotkeyCommand` button
- **Start on Startup** checkbox bound to `StartOnStartup`
- **Swatch buttons** use `accent-swatch` class (no `Background="{Binding}"` — set in code-behind)

- [ ] **Step 3: Rewrite SettingsWindow.axaml.cs**

Replace entire `src/LaTeXInserter/Views/SettingsWindow.axaml.cs` with:

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using LaTeXInserter.ViewModels;

namespace LaTeXInserter.Views;

public partial class SettingsWindow : Window
{
    private Button? _selectedSwatchButton;

    public SettingsWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            vm.AccentColorChanged += OnAccentColorChanged;
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        InitializeSwatchColors();
        UpdateSwatchSelection();
    }

    private void InitializeSwatchColors()
    {
        // Set Background on each swatch button from its DataContext (hex string).
        // Avalonia doesn't auto-convert string→IBrush in binding expressions,
        // so we do it here.
        if (AccentSelector is null) return;

        foreach (var child in AccentSelector.GetLogicalChildren())
        {
            if (child is Button btn && btn.DataContext is string hex)
            {
                btn.Background = new SolidColorBrush(Color.Parse(hex));
            }
        }
    }

    private void UpdateSwatchSelection()
    {
        if (AccentSelector is null || DataContext is not SettingsViewModel vm) return;

        _selectedSwatchButton?.Classes.Remove("accent-selected");
        _selectedSwatchButton = null;

        foreach (var child in AccentSelector.GetLogicalChildren())
        {
            if (child is Button btn && btn.DataContext is string hex && hex == vm.AccentColor)
            {
                btn.Classes.Add("accent-selected");
                _selectedSwatchButton = btn;
                break;
            }
        }
    }

    private void OnAccentColorChanged(object? sender, string hex)
    {
        UpdateSwatchSelection();
    }

    private void OnSwatchClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is string hex)
        {
            var vm = DataContext as SettingsViewModel;
            if (vm is not null)
            {
                vm.AccentColor = hex;
            }
        }
    }
}
```

Key design:
- `InitializeSwatchColors()`: Sets `Background` from hex string in code-behind (avoids string→IBrush binding conversion issue)
- `UpdateSwatchSelection()`: Adds `accent-selected` CSS class to the button matching current `AccentColor`
- `OnSwatchClick()`: Reads hex from `DataContext` directly (not from `Background`)
- `AccentColorChanged` event: Updates selection ring when color changes

- [ ] **Step 4: Add properties and events to SettingsViewModel**

Replace entire `src/LaTeXInserter/ViewModels/SettingsViewModel.cs` with:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaTeXInserter.Abstractions;
using LaTeXInserter.Models;

namespace LaTeXInserter.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IHotkeyService _hotkeyService;
    private readonly IStartupRegistrar _startupRegistrar;

    [ObservableProperty]
    private int _inputFontSize;

    [ObservableProperty]
    private int _previewFontSize;

    [ObservableProperty]
    private string _accentColor = "#404040";

    [ObservableProperty]
    private bool _autocompleteEnabled = true;

    [ObservableProperty]
    private bool _startOnStartup;

    [ObservableProperty]
    private string _currentHotkeyDisplay = string.Empty;

    public static IReadOnlyList<string> AccentPalette { get; } =
    [
        "#404040", "#D1D5DB", "#3B82F6", "#8B5CF6", "#EC4899",
        "#EF4444", "#F97316", "#F59E0B", "#10B981", "#06B6D4"
    ];

    public event EventHandler<AppSettings>? SettingsSaved;
    public event EventHandler? CloseRequested;
    public event EventHandler? ChangeHotkeyRequested;

    /// <summary>
    /// Fired when AccentColor changes (used by SettingsWindow code-behind
    /// to update the swatch selection border ring).
    /// </summary>
    public event EventHandler<string>? AccentColorChanged;

    public SettingsViewModel(
        ISettingsService settingsService,
        IHotkeyService hotkeyService,
        IStartupRegistrar startupRegistrar)
    {
        _settingsService = settingsService;
        _hotkeyService = hotkeyService;
        _startupRegistrar = startupRegistrar;

        var settings = _settingsService.Load();
        InputFontSize = settings.InputFontSize;
        PreviewFontSize = settings.PreviewFontSize;
        AccentColor = settings.AccentColor;
        AutocompleteEnabled = settings.AutocompleteEnabled;
        StartOnStartup = settings.StartOnStartup;
        CurrentHotkeyDisplay = _hotkeyService.CurrentHotkey.ToString();

        _hotkeyService.HotkeyChanged += OnHotkeyChanged;
    }

    partial void OnAccentColorChanged(string value)
    {
        AccentColorChanged?.Invoke(this, value);
    }

    private void OnHotkeyChanged(object? sender, HotkeyChord chord)
    {
        CurrentHotkeyDisplay = chord.ToString();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var settings = _settingsService.Load();
        var previousStartup = settings.StartOnStartup;
        var updated = settings with
        {
            InputFontSize = InputFontSize,
            PreviewFontSize = PreviewFontSize,
            AccentColor = AccentColor,
            AutocompleteEnabled = AutocompleteEnabled,
            StartOnStartup = StartOnStartup
        };
        _settingsService.Save(updated);

        // Sync startup registration if changed
        if (StartOnStartup != previousStartup)
        {
            try
            {
                if (StartOnStartup)
                    await _startupRegistrar.RegisterAsync();
                else
                    await _startupRegistrar.UnregisterAsync();
            }
            catch
            {
                // Registration failed, but settings are still saved
            }
        }

        SettingsSaved?.Invoke(this, updated);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ChangeHotkey() => ChangeHotkeyRequested?.Invoke(this, EventArgs.Empty);
}
```

Key changes:
- Added `IHotkeyService` + `IStartupRegistrar` dependencies
- Added `StartOnStartup` property bound to checkbox
- Added `CurrentHotkeyDisplay` property showing current hotkey text
- Added `ChangeHotkeyCommand` firing `ChangeHotkeyRequested` event
- Added `AccentColorChanged` event for swatch selection ring
- `SaveAsync` now syncs startup registration
- Subscribes to `HotkeyChanged` to update display text

- [ ] **Step 5: Remove hotkey + startup items from TrayIconViewModel**

In `src/LaTeXInserter/ViewModels/TrayIconViewModel.cs`, remove the following fields (lines 21-22):
- `_changeHotkeyItem`
- `_startupToggleItem`

Remove the `IStartupRegistrar` field and constructor parameter.

Remove events:
- `ChangeHotkeyRequested`

Remove commands and event handlers:
- `ChangeHotkeyCommand` / `ChangeHotkey()` method
- `ToggleStartupCommand` / `ToggleStartupAsync()` method
- `SyncStartupToggleAsync()` method
- `OnHotkeyChanged` handler

Remove menu entries from `TrayMenu` initialization (lines 69-82):
- `_changeHotkeyItem`
- `_startupToggleItem`

Replace entire `src/LaTeXInserter/ViewModels/TrayIconViewModel.cs` with:

```csharp
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using LaTeXInserter.Abstractions;
using LaTeXInserter.Models;

namespace LaTeXInserter.ViewModels;

public sealed partial class TrayIconViewModel
{
    private readonly IHotkeyService _hotkeyService;
    private readonly ISettingsService _settingsService;
    private readonly ILatexConverterService _latexConverter;

    private readonly NativeMenuItem _showHideOverlayItem;
    private readonly NativeMenuItem _settingsItem;
    private readonly NativeMenuItem _editMappingsItem;
    private readonly NativeMenuItem _reloadMappingsItem;
    private readonly NativeMenuItem _checkForUpdatesItem;
    private readonly NativeMenuItem _quitItem;

    public NativeMenu TrayMenu { get; }

    public event EventHandler? ShowOverlayRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? CheckForUpdatesRequested;
    public event EventHandler? QuitRequested;

    public TrayIconViewModel(
        IHotkeyService hotkeyService,
        ISettingsService settingsService,
        ILatexConverterService latexConverter)
    {
        _hotkeyService = hotkeyService;
        _settingsService = settingsService;
        _latexConverter = latexConverter;

        _showHideOverlayItem = new NativeMenuItem($"Show/Hide Overlay ({hotkeyService.CurrentHotkey})");
        _settingsItem = new NativeMenuItem("Settings...");
        _editMappingsItem = new NativeMenuItem("Edit Custom Mappings");
        _reloadMappingsItem = new NativeMenuItem("Reload Custom Mappings");
        _checkForUpdatesItem = new NativeMenuItem("Check for Updates...");
        _quitItem = new NativeMenuItem("Quit");

        _showHideOverlayItem.Command = ShowHideOverlayCommand;
        _settingsItem.Command = SettingsCommand;
        _editMappingsItem.Command = EditMappingsCommand;
        _reloadMappingsItem.Command = ReloadMappingsCommand;
        _checkForUpdatesItem.Command = CheckForUpdatesCommand;
        _quitItem.Command = QuitCommand;

        TrayMenu = new NativeMenu
        {
            _showHideOverlayItem,
            new NativeMenuItemSeparator(),
            _settingsItem,
            _editMappingsItem,
            _reloadMappingsItem,
            new NativeMenuItemSeparator(),
            _checkForUpdatesItem,
            new NativeMenuItemSeparator(),
            _quitItem
        };

        _hotkeyService.HotkeyChanged += OnHotkeyChanged;
    }

    private void OnHotkeyChanged(object? sender, HotkeyChord chord)
    {
        UpdateShowHideLabel(chord);
    }

    public void UpdateShowHideLabel(HotkeyChord chord)
    {
        Dispatcher.UIThread.Post(() =>
            _showHideOverlayItem.Header = $"Show/Hide Overlay ({chord})");
    }

    [RelayCommand]
    private void ShowHideOverlay() => ShowOverlayRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Settings() => SettingsRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void EditMappings()
    {
        var path = _settingsService.GetCustomMappingsFilePath();
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    [RelayCommand]
    private void ReloadMappings() => _latexConverter.Reload();

    [RelayCommand]
    private void CheckForUpdates() => CheckForUpdatesRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Quit() => QuitRequested?.Invoke(this, EventArgs.Empty);
}
```

- [ ] **Step 6: Update AppManager — rewire events, move startup sync**

In `src/LaTeXInserter/ViewModels/AppManager.cs`:

1. Remove `_trayIconViewModel.ChangeHotkeyRequested` subscription
2. Add `_settingsViewModel.ChangeHotkeyRequested` subscription
3. Add startup sync in `InitializeAsync` (replace `_trayIconViewModel.SyncStartupToggleAsync()`)
4. Update `Dispose` to unsubscribe from moved events

Replace `InitializeAsync` method (lines 70-116):

```csharp
public async Task InitializeAsync()
{
    try
    {
        // 1. Load settings
        var settings = _settingsService.Load();

        // 2. Validate hotkey against blocklist
        if (HotkeyBlocklist.IsBlocked(settings.Hotkey))
        {
            settings = settings with { Hotkey = AppSettings.Default.Hotkey };
            _settingsService.Save(settings);
        }

        // 3. Register hotkey
        _hotkeyService.RegisterHotkey(settings.Hotkey);

        // 4. Start hook (fire-and-forget)
#pragma warning disable CS4014
        _hotkeyService.StartAsync(CancellationToken.None);
#pragma warning restore CS4014

        // 5. Sync startup setting with OS truth
        try
        {
            var isRegistered = await _startupRegistrar.GetIsRegisteredAsync();
            if (settings.StartOnStartup != isRegistered)
            {
                _settingsService.Save(settings with { StartOnStartup = isRegistered });
            }
        }
        catch
        {
            // Startup registration check failed, continue with saved settings
        }

        // 6. Wire events
        _hotkeyService.HotkeyPressed += OnHotkeyPressed;
        _trayIconViewModel.ShowOverlayRequested += OnShowOverlayRequested;
        _trayIconViewModel.SettingsRequested += OnSettingsRequested;
        _settingsViewModel.ChangeHotkeyRequested += OnChangeHotkeyRequested;
        _trayIconViewModel.CheckForUpdatesRequested += OnCheckForUpdatesRequested;
        _updateViewModel.InstallRequested += OnInstallRequested;
        _trayIconViewModel.QuitRequested += OnQuitRequested;
        _overlayViewModel.SubmitRequested += OnSubmitRequested;
        _overlayViewModel.HideRequested += OnHideRequested;
        _settingsViewModel.SettingsSaved += OnSettingsSaved;
        _settingsViewModel.CloseRequested += OnSettingsCloseRequested;

        // Set version text on dialog VM
        var version = typeof(AppManager).Assembly.GetName().Version?.ToString() ?? "unknown";
        _upToDateViewModel.SubtitleText = $"v{version}";
    }
    catch (Exception ex)
    {
        Debug.WriteLine($"AppManager.InitializeAsync failed: {ex}");
    }
}
```

Update `Dispose` method to fix event unsubscriptions (replace entire method):

```csharp
public void Dispose()
{
    if (_isDisposed) return;
    _isDisposed = true;

    if (!_isShutdown)
    {
        _hotkeyService.HotkeyPressed -= OnHotkeyPressed;
        _trayIconViewModel.ShowOverlayRequested -= OnShowOverlayRequested;
        _trayIconViewModel.SettingsRequested -= OnSettingsRequested;
        _settingsViewModel.ChangeHotkeyRequested -= OnChangeHotkeyRequested;
        _trayIconViewModel.CheckForUpdatesRequested -= OnCheckForUpdatesRequested;
        _trayIconViewModel.QuitRequested -= OnQuitRequested;
        _overlayViewModel.SubmitRequested -= OnSubmitRequested;
        _overlayViewModel.HideRequested -= OnHideRequested;
        _updateViewModel.InstallRequested -= OnInstallRequested;
        _settingsViewModel.SettingsSaved -= OnSettingsSaved;
        _settingsViewModel.CloseRequested -= OnSettingsCloseRequested;
        _hotkeyService.Dispose();
    }

    _overlayWindow?.Close();
    _overlayWindow = null;
    _activeHotkeyDialog?.Close();
    _activeSettingsWindow?.Close();
}
```

- [ ] **Step 7: Build and run tests**

Run: `dotnet test tests/LaTeXInserter.Tests`
Expected: All tests pass. The `OverlayViewModelTests.CreateConverter` mock now needs `LastUnresolvedCommands` — but this was added in Task 3. If running Tasks out of order, add the mock now.

- [ ] **Step 8: Manual verification checklist**

Run: `dotnet run --project src/LaTeXInserter`
Verify:
- [ ] Settings window shows Appearance + General sections (no "Behavior")
- [ ] Accent color swatches render with correct colors
- [ ] Clicking a swatch shows white border ring on that swatch
- [ ] Clicking Save applies the accent color to the overlay border
- [ ] Settings window shows current hotkey display text
- [ ] "Change..." button opens HotkeyDialogWindow; after saving, display text updates
- [ ] "Start on Startup" checkbox registers/unregisters OS startup
- [ ] Tray menu no longer has "Change Hotkey..." or "Run on Startup" items
- [ ] Separators align with text content (not full-width)

- [ ] **Step 9: Commit**

```bash
git add src/LaTeXInserter/Views/SettingsWindow.axaml src/LaTeXInserter/Views/SettingsWindow.axaml.cs src/LaTeXInserter/ViewModels/SettingsViewModel.cs src/LaTeXInserter/ViewModels/TrayIconViewModel.cs src/LaTeXInserter/ViewModels/AppManager.cs src/LaTeXInserter/App.axaml
git commit -m "feat: restructure settings, fix accent colors, move hotkey/startup from tray"
```

---

### Task 5: Update CLAUDE.md and docs

**Files:**
- Modify: `CLAUDE.md`
- Modify: `docs/architecture.md`

- [ ] **Step 1: Update CLAUDE.md**

In `CLAUDE.md`, update the **Versioning & Release** section current version from `0.0.6` to next version (if bumping).

In **Key Conventions**, update the **Settings window** bullet:

```markdown
- **Settings window**: Non-modal singleton via AppManager, native OS chrome, live reload overlay via `SettingsSaved` event → `OverlayViewModel.ApplySettings()`. Two sections: **Appearance** (font sizes, accent color with swatch border ring selection) and **General** (hotkey display + change button, autocomplete toggle, startup toggle). Accent color stored as hex string, parsed to `SolidColorBrush` in code-behind. "Change Hotkey" and "Start on Startup" live in settings only (not in tray menu).
```

Update the **Accent color** bullet:

```markdown
- **Accent color**: hex string in settings → swatch buttons render via code-behind `SolidColorBrush` (binding string→IBrush doesn't auto-convert in Avalonia). Selected swatch gets white 2px border ring via `accent-selected` CSS class. `AccentColorChanged` event on `SettingsViewModel` notifies code-behind to update ring.
```

Add new convention:

```markdown
- **Conversion hints**: `ILatexConverterService.LastUnresolvedCommands` populated during `Convert()`, read by `OverlayViewModel` to show grey hint text below preview for any unresolved LaTeX forms.
```

- [ ] **Step 2: Update docs/architecture.md**

Update the settings window section and tray icon section to reflect the new structure. Add notes about `CaptureRawGroup` for parser double-lookup.

- [ ] **Step 3: Commit**

```bash
git add CLAUDE.md docs/architecture.md
git commit -m "docs: update CLAUDE.md and architecture docs for v0.0.7 settings changes"
```

---

## Self-Review

**1. Spec coverage check:**

| Requirement | Task |
|---|---|
| Installer options | **Deferred** — separate phase after feature work |
| `x^{\gamma}` parser bug | Task 2 (double-lookup) |
| No-Unicode hint | Task 3 (grey hint below preview) |
| Command-character spacing | Task 1 (Margin on Unicode TextBlock) |
| Accent color checkmark | Task 4 (white border ring) |
| Default preview font = 20 | Task 1 |
| Move hotkey to settings | Task 4 |
| Remove "Change Hotkey" from tray | Task 4 |
| Remove "Behavior" label | Task 4 (replaced by "General") |
| Fix accent color options | Task 4 (code-behind + DataContext) |
| Start on Startup in settings | Task 4 |
| Remove startup from tray | Task 4 |
| Separator alignment | Task 4 (negative margin Border) |

**2. Placeholder scan:** No TBD/TODO/fill-in-later found.

**3. Type consistency check:**
- `SettingsViewModel` uses `IHotkeyService` + `IStartupRegistrar` — both registered as singletons in `Program.cs` ✓
- `ILatexConverterService.LastUnresolvedCommands` added in Task 3, used in `OverlayViewModel` ✓
- `OverlayViewModel.ConversionHint` string + `HasConversionHint` bool — consistent between VM and XAML ✓
- `SettingsViewModel.AccentColorChanged` event typed as `EventHandler<string>` — matches `OnAccentColorChanged` handler signature in code-behind ✓
- `HandleCmds` signature unchanged — `CaptureRawGroup` is static, no interface change needed ✓
