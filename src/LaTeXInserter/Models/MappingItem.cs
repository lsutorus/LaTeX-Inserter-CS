using CommunityToolkit.Mvvm.ComponentModel;

namespace LaTeXInserter.Models;

/// <summary>
/// Row model for custom/default mapping items in CustomMappingsWindow.
/// </summary>
public sealed partial class MappingItem : ObservableObject
{
    [ObservableProperty]
    private string _command = string.Empty;

    [ObservableProperty]
    private string _character = string.Empty;

    [ObservableProperty]
    private bool _isOverride;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private bool _hasValidationError;

    /// <summary>
    /// Original command from Commands.json (Tab 2 defaults only).
    /// Used to revert overrides back to default.
    /// </summary>
    public string DefaultCommand { get; init; } = string.Empty;

    /// <summary>
    /// Original character from Commands.json (Tab 2 defaults only).
    /// Used to revert overrides back to default.
    /// </summary>
    public string DefaultCharacter { get; init; } = string.Empty;

    partial void OnCommandChanged(string value)
        => HasValidationError = string.IsNullOrWhiteSpace(value) || !value.StartsWith('\\');

    partial void OnIsEditingChanged(bool value)
    {
        if (!value)
        {
            // Commit: if command was cleared, keep old value (no empty commits)
            if (string.IsNullOrWhiteSpace(Command))
                Command = _preEditCommand;
        }
        else
        {
            _preEditCommand = Command;
        }
    }

    private string _preEditCommand = string.Empty;
}
