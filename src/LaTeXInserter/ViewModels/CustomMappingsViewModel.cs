using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaTeXInserter.Abstractions;
using LaTeXInserter.Models;

namespace LaTeXInserter.ViewModels;

public sealed partial class CustomMappingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly ILatexConverterService _latexConverter;

    // Full source-of-truth; the public collections below are filtered views of these.
    private List<MappingItem> _allCustomMappings = new();
    private List<MappingItem> _allDefaultMappings = new();

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private MappingItem? _selectedCustomItem;

    [ObservableProperty]
    private MappingItem? _selectedDefaultItem;

    [ObservableProperty]
    private bool _hasValidationErrors;

    // Add-form staging fields (Tab 1 only).
    [ObservableProperty]
    private string _newCommand = "\\";

    [ObservableProperty]
    private string _newCharacter = "";

    [ObservableProperty]
    private bool _hasNewCommandValidationError;

    // Live search filter applied to both tabs.
    [ObservableProperty]
    private string _searchText = "";

    public ObservableCollection<MappingItem> CustomMappings { get; } = [];
    public ObservableCollection<MappingItem> DefaultMappings { get; } = [];

    public event EventHandler? CloseRequested;

    public bool IsCustomTab => SelectedTabIndex == 0;

    private bool IsNewCommandValid =>
        !string.IsNullOrWhiteSpace(NewCommand) && NewCommand.StartsWith('\\');

    public bool CanAddNew => IsCustomTab && IsNewCommandValid;
    public bool CanEdit => (IsCustomTab && SelectedCustomItem is not null)
                        || (!IsCustomTab && SelectedDefaultItem is not null);
    public bool CanDelete => IsCustomTab
        ? SelectedCustomItem is not null
        : SelectedDefaultItem is not null && SelectedDefaultItem.IsOverride;
    public bool CanRevertToDefault => !IsCustomTab;
    public bool CanSave => !HasValidationErrors;

    public CustomMappingsViewModel(
        ISettingsService settingsService,
        ILatexConverterService latexConverter)
    {
        _settingsService = settingsService;
        _latexConverter = latexConverter;
        LoadMappings();
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsCustomTab)); // drives Revert-to-Default visibility
        RefreshActionCommands();
    }

    partial void OnSelectedCustomItemChanged(MappingItem? value)
    {
        EditCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedDefaultItemChanged(MappingItem? value)
    {
        EditCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
    }

    partial void OnNewCommandChanged(string value)
    {
        HasNewCommandValidationError = !IsNewCommandValid;
        AddCommand.NotifyCanExecuteChanged();
    }

    partial void OnSearchTextChanged(string value) => ApplySearchFilter();

    partial void OnHasValidationErrorsChanged(bool value)
    {
        SaveCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Re-queries Add/Edit/Delete/Revert CanExecute so bound buttons
    /// enable/disable. Required because [RelayCommand] does not auto-subscribe
    /// to the computed Can* properties.
    /// </summary>
    private void RefreshActionCommands()
    {
        AddCommand.NotifyCanExecuteChanged();
        EditCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        RevertToDefaultCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Reloads all data from scratch (called when window opens).
    /// </summary>
    public void Reload()
    {
        _allCustomMappings.Clear();
        _allDefaultMappings.Clear();
        CustomMappings.Clear();
        DefaultMappings.Clear();
        SelectedCustomItem = null;
        SelectedDefaultItem = null;
        SelectedTabIndex = 0;
        HasValidationErrors = false;
        NewCommand = "\\";
        NewCharacter = "";
        SearchText = "";
        LoadMappings();
    }

    private void LoadMappings()
    {
        var customLines = _settingsService.GetCustomMappingLines().ToList();
        var defaults = _latexConverter.DefaultCommands;
        var customKeys = new HashSet<string>();

        _allCustomMappings = new();
        _allDefaultMappings = new();

        // Parse custom_mappings.txt — skip entries value-identical to a default
        // (they're dumped noise, not real user overrides).
        foreach (var line in customLines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var trimmed = line.Trim();
            if (trimmed.StartsWith('#')) continue;

            var spaceIdx = trimmed.IndexOf(' ');
            if (spaceIdx < 0) continue;

            var cmd = trimmed[..spaceIdx];
            var unicode = trimmed[(spaceIdx + 1)..];

            if (defaults.TryGetValue(cmd, out var def) && def == unicode)
                continue; // value-identical to default → drop the duplicate

            var isOverride = defaults.ContainsKey(cmd);
            _allCustomMappings.Add(new MappingItem
            {
                Command = cmd,
                Character = unicode,
                IsOverride = isOverride
            });
            customKeys.Add(cmd); // only retained keys → Tab 2 asterisks are accurate
        }

        // Load default mappings for Tab 2.
        foreach (var kvp in defaults)
        {
            if (!kvp.Key.StartsWith('\\')) continue;

            var isOverride = customKeys.Contains(kvp.Key);
            _allDefaultMappings.Add(new MappingItem
            {
                Command = kvp.Key,
                Character = kvp.Value,
                IsOverride = isOverride,
                DefaultCommand = kvp.Key,
                DefaultCharacter = kvp.Value
            });
        }

        ApplySearchFilter();
    }

    private void ApplySearchFilter()
    {
        var needle = (SearchText ?? string.Empty).Trim();

        bool IsMatch(MappingItem m) =>
            string.IsNullOrEmpty(needle) ||
            m.Command.Contains(needle, StringComparison.OrdinalIgnoreCase);

        var filteredCustom = _allCustomMappings.Where(IsMatch).ToList();
        if (SelectedCustomItem is not null && !filteredCustom.Contains(SelectedCustomItem))
            SelectedCustomItem = null;
        CustomMappings.Clear();
        foreach (var m in filteredCustom) CustomMappings.Add(m);

        var filteredDefault = _allDefaultMappings.Where(IsMatch).ToList();
        if (SelectedDefaultItem is not null && !filteredDefault.Contains(SelectedDefaultItem))
            SelectedDefaultItem = null;
        DefaultMappings.Clear();
        foreach (var m in filteredDefault) DefaultMappings.Add(m);
    }

    [RelayCommand(CanExecute = nameof(CanAddNew))]
    private void Add()
    {
        var cmd = NewCommand ?? string.Empty;
        var ch = NewCharacter ?? string.Empty;
        if (!IsNewCommandValid)
        {
            HasNewCommandValidationError = true;
            return;
        }
        HasNewCommandValidationError = false;

        var defaults = _latexConverter.DefaultCommands;

        // value-identical to default → nothing real to store; reset form
        if (defaults.TryGetValue(cmd, out var def) && def == ch)
        {
            NewCommand = "\\";
            NewCharacter = "";
            return;
        }

        var isOverride = defaults.ContainsKey(cmd);
        var existing = _allCustomMappings.FirstOrDefault(m => m.Command == cmd);
        if (existing is not null)
        {
            existing.Character = ch;
            existing.IsOverride = isOverride;
        }
        else
        {
            _allCustomMappings.Add(new MappingItem
            {
                Command = cmd,
                Character = ch,
                IsOverride = isOverride
            });
        }

        // Sync the Tab 2 default row so it reflects the override + asterisk.
        if (isOverride)
        {
            var dr = _allDefaultMappings.FirstOrDefault(m => m.Command == cmd);
            if (dr is not null)
            {
                dr.IsOverride = true;
                dr.Character = ch;
            }
        }

        ApplySearchFilter();
        NewCommand = "\\";
        NewCharacter = "";
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void Edit()
    {
        if (IsCustomTab && SelectedCustomItem is not null)
            SelectedCustomItem.IsEditing = true;
        else if (!IsCustomTab && SelectedDefaultItem is not null)
            SelectedDefaultItem.IsEditing = true;
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void Delete()
    {
        if (IsCustomTab && SelectedCustomItem is not null)
        {
            var removed = SelectedCustomItem;
            _allCustomMappings.Remove(removed);

            // If it was overriding a default, revert that default row.
            if (removed.IsOverride)
            {
                var dr = _allDefaultMappings.FirstOrDefault(m => m.Command == removed.Command);
                if (dr is not null)
                {
                    dr.IsOverride = false;
                    dr.Character = dr.DefaultCharacter;
                }
            }
            SelectedCustomItem = null;
        }
        else if (!IsCustomTab && SelectedDefaultItem is not null && SelectedDefaultItem.IsOverride)
        {
            // Remove override: revert default row to its built-in values.
            var d = SelectedDefaultItem;
            d.Command = d.DefaultCommand;
            d.Character = d.DefaultCharacter;
            d.IsOverride = false;
            d.IsEditing = false;

            var customMatch = _allCustomMappings.FirstOrDefault(m => m.Command == d.DefaultCommand);
            if (customMatch is not null)
            {
                _allCustomMappings.Remove(customMatch);
                if (SelectedCustomItem == customMatch)
                    SelectedCustomItem = null;
            }
        }

        RefreshActionCommands();
        ApplySearchFilter();
    }

    [RelayCommand(CanExecute = nameof(CanRevertToDefault))]
    private void RevertToDefault()
    {
        // Remove all overrides from Tab 2 — keep purely custom entries.
        var overrideCommands = _allDefaultMappings
            .Where(m => m.IsOverride)
            .Select(m => m.DefaultCommand)
            .ToHashSet();

        foreach (var item in _allDefaultMappings)
        {
            if (item.IsOverride)
            {
                item.Command = item.DefaultCommand;
                item.Character = item.DefaultCharacter;
                item.IsOverride = false;
                item.IsEditing = false;
            }
        }

        // Remove override entries from custom tab (keep purely custom ones).
        var toRemove = _allCustomMappings
            .Where(m => overrideCommands.Contains(m.Command))
            .ToList();
        foreach (var item in toRemove)
            _allCustomMappings.Remove(item);

        SelectedCustomItem = null;
        RefreshActionCommands();
        ApplySearchFilter();
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        CommitAllEdits();
        if (HasValidationErrors) return;

        // Write only the true custom/override entries from the full source
        // (search filtering never shrinks what gets saved).
        var lines = _allCustomMappings
            .Where(i => !string.IsNullOrWhiteSpace(i.Command) && i.Command.StartsWith('\\'))
            .Select(i => $"{i.Command} {i.Character}")
            .ToList();

        File.WriteAllLines(_settingsService.GetCustomMappingsFilePath(), lines);

        _latexConverter.Reload();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Called by code-behind when an inline edit commits on a row (Enter key),
    /// and by CommitAllEdits on Save for any edited default-origin rows.
    /// Updates override status across both tabs and re-validates.
    /// </summary>
    public void OnItemEditCommitted(MappingItem item)
    {
        var defaults = _latexConverter.DefaultCommands;
        var isDefaultRow = !string.IsNullOrEmpty(item.DefaultCommand);

        if (isDefaultRow)
        {
            // Default-origin (Tab 2) row: has this row diverged from its built-in value?
            var isChanged = item.Command != item.DefaultCommand
                         || item.Character != item.DefaultCharacter;

            if (isChanged)
            {
                item.IsOverride = true;
                var customMatch = _allCustomMappings.FirstOrDefault(m => m.Command == item.Command);
                if (customMatch is null)
                {
                    _allCustomMappings.Add(new MappingItem
                    {
                        Command = item.Command,
                        Character = item.Character,
                        IsOverride = true
                    });
                }
                else
                {
                    customMatch.Character = item.Character;
                    customMatch.IsOverride = true;
                }
            }
            else
            {
                // Reverted back to default — drop the override.
                item.IsOverride = false;
                var customMatch = _allCustomMappings.FirstOrDefault(m => m.Command == item.DefaultCommand);
                if (customMatch is not null)
                    _allCustomMappings.Remove(customMatch);
            }
        }
        else
        {
            // Custom-origin (Tab 1) row: drop if the edit made it value-identical to a default.
            if (defaults.TryGetValue(item.Command, out var def) && def == item.Character)
            {
                _allCustomMappings.Remove(item);
                var dr = _allDefaultMappings.FirstOrDefault(m => m.Command == item.Command);
                if (dr is not null)
                {
                    dr.IsOverride = false;
                    dr.Character = dr.DefaultCharacter;
                }
                if (SelectedCustomItem == item)
                    SelectedCustomItem = null;
                RefreshActionCommands();
            }
            else
            {
                item.IsOverride = defaults.ContainsKey(item.Command);
                var dr = _allDefaultMappings.FirstOrDefault(m => m.Command == item.Command);
                if (dr is not null)
                {
                    if (item.IsOverride)
                    {
                        dr.IsOverride = true;
                        dr.Character = item.Character;
                    }
                    else if (dr.IsOverride)
                    {
                        // Command no longer matches a default this row was overriding.
                        dr.IsOverride = false;
                        dr.Character = dr.DefaultCharacter;
                    }
                }
            }
        }

        CheckValidation();
        ApplySearchFilter();
        RefreshActionCommands();
    }

    private void CommitAllEdits()
    {
        foreach (var item in _allCustomMappings)
            if (item.IsEditing) item.IsEditing = false;

        // Run override bookkeeping for any edited default-origin rows so
        // Save persists the correct custom entries (no code-behind LostFocus path).
        var editedDefaults = _allDefaultMappings.Where(m => m.IsEditing).ToList();
        foreach (var item in editedDefaults)
        {
            item.IsEditing = false;
            OnItemEditCommitted(item);
        }

        ApplySearchFilter();
    }

    private void CheckValidation()
    {
        HasValidationErrors = _allCustomMappings.Any(m => m.HasValidationError)
                          || _allDefaultMappings.Any(m => m.HasValidationError && m.IsEditing);
    }
}
