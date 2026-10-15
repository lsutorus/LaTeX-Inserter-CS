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
    private volatile bool _isRunning;

    public HotkeyChord CurrentHotkey => _currentHotkey;
    public bool IsRunning => _isRunning;
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
    public event EventHandler<string>? HookFailed;

    public HotkeyService(SimpleGlobalHook hook)
    {
        _hook = hook;
        _currentHotkey = AppSettings.Default.Hotkey;
        hook.KeyPressed += OnKeyPressed;
        hook.KeyReleased += OnKeyReleased;
    }

    public Task StartAsync(CancellationToken ct)
    {
        // Fire-and-forget on thread pool — RunAsync must not block caller.
        // SharpHook already runs the native hook on its own dedicated thread; the
        // Task.Run wrapper only keeps the synchronous setup off the caller.
        _ = Task.Run(async () =>
        {
            try
            {
                _isRunning = true;
                await _hook.RunAsync();
                _isRunning = false;
            }
            catch (Exception ex)
            {
                _isRunning = false;
                Dispatcher.UIThread.Post(() =>
                    HookFailed?.Invoke(this, DescribeFailure(ex)));
            }
        }, ct);

        return Task.CompletedTask;
    }

    private static string DescribeFailure(Exception ex)
    {
        // SharpHook surfaces macOS permission denial as HookException with
        // UioHookResult.ErrorAxApiDisabled.
        if (ex is HookException he && he.Result == UioHookResult.ErrorAxApiDisabled)
        {
            return "macOS denied access to keyboard events. Grant LaTeX Inserter "
                 + "Accessibility and Input Monitoring access in System Settings, "
                 + "then quit and reopen the app.";
        }

        return $"The global keyboard hook could not start: {ex.Message}";
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
