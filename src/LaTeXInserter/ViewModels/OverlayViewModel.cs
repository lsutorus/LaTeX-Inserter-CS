using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using LaTeXInserter.Abstractions;
using LaTeXInserter.Models;

namespace LaTeXInserter.ViewModels;

public sealed partial class OverlayViewModel : ObservableObject
{
    private readonly ILatexConverterService _converter;
    private readonly ISettingsService _settingsService;

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
    private AutocompleteItem? _selectedAutocompleteItem;

    [ObservableProperty]
    private int _inputFontSize = 16;

    [ObservableProperty]
    private int _previewFontSize = 20;

    [ObservableProperty]
    private string _accentColor = "#404040";

    [ObservableProperty]
    private IBrush _accentBrush = new SolidColorBrush(Color.Parse("#404040"));

    [ObservableProperty]
    private IBrush _accentBackgroundBrush = new SolidColorBrush(Color.Parse("#404040"), 0.25);

    [ObservableProperty]
    private bool _isAutocompleteEnabled = true;

    [ObservableProperty]
    private string _conversionHint = string.Empty;

    public bool HasConversionHint => !string.IsNullOrEmpty(ConversionHint);

    public ObservableCollection<AutocompleteItem> AutocompleteItems { get; } = [];

    public event EventHandler<string>? SubmitRequested;
    public event EventHandler? HideRequested;

    public OverlayViewModel(ILatexConverterService converter, ISettingsService settingsService)
    {
        _converter = converter;
        _settingsService = settingsService;
        ApplySettings(_settingsService.Load());
    }

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

        if (_isCommitting) return;
        if (!IsAutocompleteEnabled) return;

        IsAutocompleteOpen = false;
        AutocompleteItems.Clear();
        SelectedAutocompleteItem = null;

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
        {
            var unicode = _converter.Commands.TryGetValue(c, out var u) ? u : string.Empty;
            AutocompleteItems.Add(new AutocompleteItem(c, unicode));
        }

        IsAutocompleteOpen = true;
        SelectedAutocompleteItem = AutocompleteItems.Count > 0 ? AutocompleteItems[0] : null;
    }

    public void CommitAutocomplete(AutocompleteItem? item)
    {
        if (item is null || _currentPrefix is null || string.IsNullOrEmpty(InputText)) return;

        _isCommitting = true;
        try
        {
            var newText = string.Concat(
                InputText.AsSpan(0, _currentPrefixStart),
                item.Command.AsSpan(),
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

        var currentIndex = SelectedAutocompleteItem is not null
            ? AutocompleteItems.IndexOf(SelectedAutocompleteItem)
            : -1;

        var newIndex = Math.Clamp(currentIndex + delta, 0, AutocompleteItems.Count - 1);
        SelectedAutocompleteItem = AutocompleteItems[newIndex];
    }

    public string? GetSelectedAutocompleteCommand()
    {
        return SelectedAutocompleteItem?.Command;
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
        ConversionHint = string.Empty;
        PreviewText = string.Empty;
        HideRequested?.Invoke(this, EventArgs.Empty);
    }

    public void ResetState()
    {
        IsAutocompleteOpen = false;
        InputText = string.Empty;
        ConversionHint = string.Empty;
        PreviewText = string.Empty;
    }

    public void ApplySettings(AppSettings settings)
    {
        InputFontSize = settings.InputFontSize;
        PreviewFontSize = settings.PreviewFontSize;
        AccentColor = settings.AccentColor;
        IsAutocompleteEnabled = settings.AutocompleteEnabled;
        UpdateBrushes();
        App.ApplyAccentColor(settings.AccentColor);
    }

    public void UpdateBrushes()
    {
        var color = Color.Parse(AccentColor);
        AccentBrush = new SolidColorBrush(color);
        AccentBackgroundBrush = new SolidColorBrush(color, 0.25);
    }

    [GeneratedRegex(@"\\[a-zA-Z]+$")]
    private static partial Regex CommandPrefixRegex();
}
