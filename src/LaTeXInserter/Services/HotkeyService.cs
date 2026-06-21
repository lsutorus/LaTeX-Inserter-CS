using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using LaTeXInserter.Abstractions;
using LaTeXInserter.Models;
using SharpHook;
using SharpHook.Data;

namespace LaTeXInserter.Services;

internal sealed class HotkeyService : IHotkeyService
{
    private readonly SimpleGlobalHook _hook;
    private readonly object _accumulatorLock = new();
    private readonly List<KeyCode> _heldKeys = [];

    private HotkeyChord _currentHotkey;
    private volatile bool _isRecording;

    public HotkeyChord CurrentHotkey => _currentHotkey;
    public bool IsRecording
    {
        get => _isRecording;
        set
        {
            _isRecording = value;
            if (!value)
            {
                lock (_accumulatorLock)
                {
                    _heldKeys.Clear();
                }
            }
        }
    }

    public event EventHandler<HotkeyChord>? HotkeyPressed;
    public event EventHandler<HotkeyChord>? HotkeyRecorded;
    public event EventHandler<HotkeyChord>? HotkeyChanged;

    public HotkeyService(SimpleGlobalHook hook)
    {
        _hook = hook;
        _currentHotkey = AppSettings.Default.Hotkey;
        hook.KeyPressed += OnKeyPressed;
        hook.KeyReleased += OnKeyReleased;
    }

    public Task StartAsync(CancellationToken ct)
    {
        // Fire-and-forget on thread pool — RunAsync must not block caller
        _ = Task.Run(() => _hook.RunAsync(), ct);
        return Task.CompletedTask;
    }

    public void RegisterHotkey(HotkeyChord chord)
    {
        _currentHotkey = chord;
        HotkeyChanged?.Invoke(this, chord);
    }

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        var keyCode = e.RawEvent.Keyboard.KeyCode;
        var collapsed = HotkeyNormalizer.CollapseModifiers(e.RawEvent.Mask);

        if (_isRecording)
        {
            lock (_accumulatorLock)
            {
                if (!_heldKeys.Contains(keyCode))
                    _heldKeys.Add(keyCode);

                var chord = BuildChordFromHeld();
                Dispatcher.UIThread.Post(() => HotkeyRecorded?.Invoke(this, chord));
            }
            return;
        }

        if (collapsed == _currentHotkey.Modifiers && keyCode == _currentHotkey.TriggerKey)
        {
            e.SuppressEvent = true;
            Dispatcher.UIThread.Post(() => HotkeyPressed?.Invoke(this, _currentHotkey));
        }
    }

    private void OnKeyReleased(object? sender, KeyboardHookEventArgs e)
    {
        if (!_isRecording) return;

        lock (_accumulatorLock)
        {
            _heldKeys.Remove(e.RawEvent.Keyboard.KeyCode);
            var chord = BuildChordFromHeld();
            Dispatcher.UIThread.Post(() => HotkeyRecorded?.Invoke(this, chord));
        }
    }

    private HotkeyChord BuildChordFromHeld()
    {
        var modifiers = ModifierMask.None;
        KeyCode trigger = KeyCode.VcUndefined;

        foreach (var key in _heldKeys)
        {
            switch (key)
            {
                case KeyCode.VcLeftControl:
                case KeyCode.VcRightControl:
                    modifiers |= ModifierMask.Control;
                    break;
                case KeyCode.VcLeftAlt:
                case KeyCode.VcRightAlt:
                    modifiers |= ModifierMask.Alt;
                    break;
                case KeyCode.VcLeftShift:
                case KeyCode.VcRightShift:
                    modifiers |= ModifierMask.Shift;
                    break;
                case KeyCode.VcLeftMeta:
                case KeyCode.VcRightMeta:
                    modifiers |= ModifierMask.Windows;
                    break;
                default:
                    trigger = key;
                    break;
            }
        }

        return HotkeyNormalizer.Normalize(new HotkeyChord(modifiers, trigger));
    }

    public void Dispose()
    {
        _hook.KeyPressed -= OnKeyPressed;
        _hook.KeyReleased -= OnKeyReleased;
        _hook.Dispose();
    }
}
