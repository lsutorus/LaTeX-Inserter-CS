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

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private MappingItem? _selectedCustomItem;

    [ObservableProperty]
    private MappingItem? _selectedDefaultItem;

    [ObservableProperty]
    private bool _hasValidationErrors;

    public ObservableCollection<MappingItem> CustomMappings { get; } = [];
    public ObservableCollection<MappingItem> DefaultMappings { get; } = [];

    public event EventHandler? CloseRequested;

    public bool IsCustomTab => SelectedTabIndex == 0;

    public bool CanAdd => IsCustomTab;
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
        OnPropertyChanged(nameof(IsCustomTab));
        OnPropertyChanged(nameof(CanAdd));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(CanRevertToDefault));
    }

    partial void OnSelectedCustomItemChanged(MappingItem? value)
    {
        if (IsCustomTab)
        {
            OnPropertyChanged(nameof(CanEdit));
            OnPropertyChanged(nameof(CanDelete));
        }
    }

    partial void OnSelectedDefaultItemChanged(MappingItem? value)
    {
        if (!IsCustomTab)
        {
            OnPropertyChanged(nameof(CanEdit));
            OnPropertyChanged(nameof(CanDelete));
        }
    }

    /// <summary>
    /// Reloads all data from scratch (called when window opens).
    /// </summary>
    public void Reload()
    {
        CustomMappings.Clear();
        DefaultMappings.Clear();
        SelectedCustomItem = null;
        SelectedDefaultItem = null;
        SelectedTabIndex = 0;
        HasValidationErrors = false;
        LoadMappings();
    }

    private void LoadMappings()
    {
        var customLines = _settingsService.GetCustomMappingLines().ToList();
        var defaults = _latexConverter.DefaultCommands;
        var customKeys = new HashSet<string>();

        // Parse custom_mappings.txt
        foreach (var line in customLines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var trimmed = line.Trim();
            if (trimmed.StartsWith('#')) continue;

            var spaceIdx = trimmed.IndexOf(' ');
            if (spaceIdx < 0) continue;

            var cmd = trimmed[..spaceIdx];
            var unicode = trimmed[(spaceIdx + 1)..];
            customKeys.Add(cmd);

            var isOverride = defaults.ContainsKey(cmd);
            CustomMappings.Add(new MappingItem
            {
                Command = cmd,
                Character = unicode,
                IsOverride = isOverride
            });
        }

        // Load default mappings for Tab 2
        foreach (var kvp in defaults)
        {
            if (!kvp.Key.StartsWith('\\')) continue;

            var isOverride = customKeys.Contains(kvp.Key);
            DefaultMappings.Add(new MappingItem
            {
                Command = kvp.Key,
                Character = kvp.Value,
                IsOverride = isOverride,
                DefaultCommand = kvp.Key,
                DefaultCharacter = kvp.Value
            });
        }
    }

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Add()
    {
        var item = new MappingItem
        {
            Command = "\\",
            Character = string.Empty,
            IsEditing = true,
            HasValidationError = true
        };

        CustomMappings.Insert(0, item);
        SelectedCustomItem = item;
        CheckValidation();
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
            CustomMappings.Remove(SelectedCustomItem);
            SelectedCustomItem = null;
        }
        else if (!IsCustomTab && SelectedDefaultItem is not null && SelectedDefaultItem.IsOverride)
        {
            // Remove override: revert to default
            SelectedDefaultItem.Command = SelectedDefaultItem.DefaultCommand;
            SelectedDefaultItem.Character = SelectedDefaultItem.DefaultCharacter;
            SelectedDefaultItem.IsOverride = false;
            SelectedDefaultItem.IsEditing = false;

            // Also remove from custom tab
            var customMatch = CustomMappings.FirstOrDefault(m =>
                m.Command == SelectedDefaultItem.DefaultCommand);
            if (customMatch is not null)
            {
                CustomMappings.Remove(customMatch);
                if (SelectedCustomItem == customMatch)
                    SelectedCustomItem = null;
            }

            OnPropertyChanged(nameof(CanDelete));
        }
    }

    [RelayCommand(CanExecute = nameof(CanRevertToDefault))]
    private void RevertToDefault()
    {
        // Remove all overrides from Tab 2 — keep purely custom entries
        var overrideCommands = DefaultMappings
            .Where(m => m.IsOverride)
            .Select(m => m.DefaultCommand)
            .ToHashSet();

        foreach (var item in DefaultMappings)
        {
            if (item.IsOverride)
            {
                item.Command = item.DefaultCommand;
                item.Character = item.DefaultCharacter;
                item.IsOverride = false;
                item.IsEditing = false;
            }
        }

        // Remove override entries from custom tab (keep purely custom ones)
        var toRemove = CustomMappings
            .Where(m => overrideCommands.Contains(m.Command))
            .ToList();
        foreach (var item in toRemove)
            CustomMappings.Remove(item);

        SelectedCustomItem = null;
        OnPropertyChanged(nameof(CanDelete));
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        CommitAllEdits();
        if (HasValidationErrors) return;

        // Build lines: override entries first (matching defaults), then purely custom
        var lines = new List<string>();

        foreach (var item in CustomMappings)
        {
            if (!string.IsNullOrWhiteSpace(item.Command) && item.Command.StartsWith('\\'))
                lines.Add($"{item.Command} {item.Character}");
        }

        var path = _settingsService.GetCustomMappingsFilePath();
        File.WriteAllLines(path, lines);

        _latexConverter.Reload();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Called by code-behind when inline edit commits on a row.
    /// Updates override status and re-validates.
    /// </summary>
    public void OnItemEditCommitted(MappingItem item)
    {
        if (!IsCustomTab)
        {
            // Tab 2: if command/character differs from default, mark as override
            // and add to custom tab if not already there
            var isChanged = item.Command != item.DefaultCommand
                         || item.Character != item.DefaultCharacter;

            if (isChanged && !item.IsOverride)
            {
                item.IsOverride = true;
                CustomMappings.Add(new MappingItem
                {
                    Command = item.Command,
                    Character = item.Character,
                    IsOverride = true
                });
            }
            else if (isChanged && item.IsOverride)
            {
                // Update corresponding custom entry
                var customMatch = CustomMappings.FirstOrDefault(m =>
                    m.Command == item.DefaultCommand);
                if (customMatch is not null)
                {
                    customMatch.Command = item.Command;
                    customMatch.Character = item.Character;
                }
            }
            else if (!isChanged && item.IsOverride)
            {
                // Reverted back to default — remove override
                item.IsOverride = false;
                var customMatch = CustomMappings.FirstOrDefault(m =>
                    m.Command == item.DefaultCommand);
                if (customMatch is not null)
                    CustomMappings.Remove(customMatch);
            }

            OnPropertyChanged(nameof(CanDelete));
        }

        CheckValidation();
    }

    private void CommitAllEdits()
    {
        foreach (var item in CustomMappings)
            item.IsEditing = false;
        foreach (var item in DefaultMappings)
            item.IsEditing = false;
    }

    private void CheckValidation()
    {
        HasValidationErrors = CustomMappings.Any(m => m.HasValidationError)
                          || DefaultMappings.Any(m => m.HasValidationError && m.IsEditing);
    }

    partial void OnHasValidationErrorsChanged(bool value)
    {
        SaveCommand.NotifyCanExecuteChanged();
    }
}
