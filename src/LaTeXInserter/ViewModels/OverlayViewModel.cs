using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using LaTeXInserter.Abstractions;

namespace LaTeXInserter.ViewModels;

public sealed partial class OverlayViewModel : ObservableObject
{
    private readonly ILatexConverterService _converter;

    private bool _isCommitting;
    private string? _currentPrefix;
    private int _currentPrefixStart;

    [ObservableProperty]
    private string _inputText = string.Empty;

    [ObservableProperty]
    private string _previewText = string.Empty;

    [ObservableProperty]
    private bool _isAutocompleteOpen;

    [ObservableProperty]
    private int _autocompleteSelectedIndex = -1;

    public ObservableCollection<string> AutocompleteItems { get; } = [];

    public event EventHandler<string>? SubmitRequested;
    public event EventHandler? HideRequested;

    public OverlayViewModel(ILatexConverterService converter)
    {
        _converter = converter;
    }

    partial void OnInputTextChanged(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            PreviewText = string.Empty;
            IsAutocompleteOpen = false;
            AutocompleteItems.Clear();
            AutocompleteSelectedIndex = -1;
            return;
        }

        PreviewText = _converter.Convert(value);

        if (_isCommitting) return;

        IsAutocompleteOpen = false;
        AutocompleteItems.Clear();
        AutocompleteSelectedIndex = -1;

        // Find trailing command prefix: \[a-zA-Z]+ at end of text
        var match = CommandPrefixRegex().Match(value);
        if (!match.Success) return;

        _currentPrefix = match.Value;
        _currentPrefixStart = match.Index;

        var candidates = _converter.CommandNames
            .Where(name => name.StartsWith(_currentPrefix, StringComparison.Ordinal))
            .Take(20)
            .ToList();

        if (candidates.Count == 0) return;

        foreach (var c in candidates)
            AutocompleteItems.Add(c);

        IsAutocompleteOpen = true;
        AutocompleteSelectedIndex = 0;
    }

    public void CommitAutocomplete(string selectedCommand)
    {
        if (_currentPrefix is null || string.IsNullOrEmpty(InputText)) return;

        _isCommitting = true;
        try
        {
            var newText = string.Concat(
                InputText.AsSpan(0, _currentPrefixStart),
                selectedCommand.AsSpan(),
                InputText.AsSpan(_currentPrefixStart + _currentPrefix.Length));

            InputText = newText;
            IsAutocompleteOpen = false;
        }
        finally
        {
            _isCommitting = false;
        }
    }

    public void NavigateAutocomplete(int delta)
    {
        if (!IsAutocompleteOpen || AutocompleteItems.Count == 0) return;

        var newIndex = AutocompleteSelectedIndex + delta;
        newIndex = Math.Clamp(newIndex, 0, AutocompleteItems.Count - 1);
        AutocompleteSelectedIndex = newIndex;
    }

    public string? GetSelectedAutocompleteItem()
    {
        if (!IsAutocompleteOpen || AutocompleteSelectedIndex < 0 || AutocompleteSelectedIndex >= AutocompleteItems.Count)
            return null;

        return AutocompleteItems[AutocompleteSelectedIndex];
    }

    public void Submit()
    {
        var converted = _converter.Convert(InputText);
        SubmitRequested?.Invoke(this, converted);
    }

    public void Cancel()
    {
        IsAutocompleteOpen = false;
        InputText = string.Empty;
        HideRequested?.Invoke(this, EventArgs.Empty);
    }

    public void ResetState()
    {
        IsAutocompleteOpen = false;
        InputText = string.Empty;
        PreviewText = string.Empty;
    }

    [GeneratedRegex(@"\\[a-zA-Z]+$")]
    private static partial Regex CommandPrefixRegex();
}
