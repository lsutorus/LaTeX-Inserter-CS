using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaTeXInserter.Abstractions;
using LaTeXInserter.Models;
using SharpHook.Data;

namespace LaTeXInserter.ViewModels;

public sealed partial class HotkeyDialogViewModel : ObservableObject
{
    private readonly IHotkeyService _hotkeyService;
    private readonly ISettingsService _settingsService;

    private HotkeyChord _liveChord;
    private HotkeyChord? _snapshotChord;

    private const string FallbackText = "Press keys…";

    [ObservableProperty] private string _chordDisplay = FallbackText;
    [ObservableProperty] private bool _isBlocked;

    public bool IsValid =>
        _snapshotChord.HasValue
        && _snapshotChord.Value.Modifiers != ModifierMask.None
        && _snapshotChord.Value.TriggerKey != KeyCode.VcUndefined;

    public bool IsValidAndNotBlocked => IsValid && !IsBlocked;
    public bool IsRecordingOnly => !IsValid;

    public event EventHandler? CloseRequested;

    public HotkeyDialogViewModel(IHotkeyService hotkeyService, ISettingsService settingsService)
    {
        _hotkeyService = hotkeyService;
        _settingsService = settingsService;
    }

    public void StartRecording()
    {
        _liveChord = default;
        _snapshotChord = null;
        ChordDisplay = FallbackText;
        IsBlocked = false;
        _hotkeyService.HotkeyRecorded += OnHotkeyRecorded;
        _hotkeyService.IsRecording = true;
        UpdateDisplay();
    }

    public void Cleanup()
    {
        _hotkeyService.IsRecording = false;
        _hotkeyService.HotkeyRecorded -= OnHotkeyRecorded;
    }

    [RelayCommand(CanExecute = nameof(IsValidAndNotBlocked))]
    private void Save()
    {
        var chord = _snapshotChord!.Value;
        _hotkeyService.RegisterHotkey(chord);
        var settings = _settingsService.Load();
        _settingsService.Save(settings with { Hotkey = chord });
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void OnHotkeyRecorded(object? sender, HotkeyChord chord)
    {
        _liveChord = chord;

        if (chord.Modifiers == ModifierMask.None && chord.TriggerKey == KeyCode.VcEscape)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (chord.Modifiers != ModifierMask.None && chord.TriggerKey != KeyCode.VcUndefined)
            _snapshotChord = chord;

        UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        bool isHoldingKeys = _liveChord.Modifiers != ModifierMask.None
            || _liveChord.TriggerKey != KeyCode.VcUndefined;

        ChordDisplay = isHoldingKeys
            ? _liveChord.ToString()
            : (_snapshotChord.HasValue ? _snapshotChord.Value.ToString() : FallbackText);

        IsBlocked = IsValid && HotkeyBlocklist.IsBlocked(_snapshotChord!.Value);

        OnPropertyChanged(nameof(IsRecordingOnly));
        OnPropertyChanged(nameof(IsValidAndNotBlocked));
        SaveCommand.NotifyCanExecuteChanged();
    }
}
