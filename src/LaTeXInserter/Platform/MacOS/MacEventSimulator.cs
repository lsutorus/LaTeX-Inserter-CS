using System.Runtime.Versioning;
using SharpHook;
using SharpHook.Data;

namespace LaTeXInserter.Platform.MacOS;

/// <summary>
/// macOS replacement for SharpHook's key simulation.
/// </summary>
/// <remarks>
/// SharpHook simulates through libuiohook's <c>hook_post_event</c>, which posts
/// to <c>kCGHIDEventTap</c>. On at least one Intel Mac running macOS 13.7.8,
/// events posted to that tap are silently discarded — plain keys and modifier
/// chords alike — even with Accessibility granted and Secure Input off. The
/// identical event posted to <c>kCGSessionEventTap</c> is delivered. That is
/// blocker 12a: the clipboard was set correctly and the paste chord was correct,
/// but nothing ever arrived.
///
/// Only key simulation is re-implemented. Mouse simulation is untouched and
/// delegates to SharpHook, because this app never simulates mouse input and a
/// hand-written replacement would be untested code carrying real risk.
/// </remarks>
[SupportedOSPlatform("macos")]
internal sealed class MacEventSimulator : IEventSimulator
{
    private readonly IEventSimulator _inner;
    private readonly IntPtr _source;

    // Modifiers currently held down by THIS simulator. Synthetic events do not
    // pick up modifier state from earlier synthetic events, so each event has to
    // carry the flags explicitly.
    private ulong _activeFlags;

    public MacEventSimulator() : this(new EventSimulator())
    {
    }

    /// <summary>Test seam.</summary>
    /// <remarks>
    /// Deliberately NOT public. Microsoft.Extensions.DependencyInjection selects the
    /// greediest *public* constructor, so a public overload taking
    /// <see cref="IEventSimulator"/> makes the container resolve this very service to
    /// build itself — a circular dependency that throws at startup, not at
    /// registration. Keeping it internal leaves the parameterless constructor as the
    /// only one the container can see.
    /// </remarks>
    internal MacEventSimulator(IEventSimulator inner)
    {
        _inner = inner;
        // A NULL source is dropped even on the working tap, so a failure here is
        // worth recording rather than silently limping on.
        _source = MacNativeMethods.CreateKeyEventSource();
        if (_source == IntPtr.Zero)
            MacTrace.Write("MacEventSimulator: CGEventSourceCreate returned NULL");
    }

    public UioHookResult SimulateKeyPress(KeyCode keyCode)
    {
        _activeFlags |= MacVirtualKeyMap.ModifierFlag(keyCode);
        return Post(keyCode, keyDown: true, _activeFlags);
    }

    public UioHookResult SimulateKeyRelease(KeyCode keyCode)
    {
        // The releasing modifier is still down for its own key-up event; it only
        // stops applying to whatever follows.
        var result = Post(keyCode, keyDown: false, _activeFlags);
        _activeFlags &= ~MacVirtualKeyMap.ModifierFlag(keyCode);
        return result;
    }

    private UioHookResult Post(KeyCode keyCode, bool keyDown, ulong flags)
    {
        var virtualKey = MacVirtualKeyMap.Map(keyCode);
        if (virtualKey is null)
        {
            MacTrace.Write($"MacEventSimulator: no macOS virtual key for {keyCode}");
            return UioHookResult.Failure;
        }

        var posted = MacNativeMethods.PostKeyEvent(_source, virtualKey.Value, keyDown, flags);
        if (!posted)
            MacTrace.Write($"MacEventSimulator: CGEventCreateKeyboardEvent failed for {keyCode}");

        return posted ? UioHookResult.Success : UioHookResult.Failure;
    }

    public UioHookResult SimulateTextEntry(string text) => _inner.SimulateTextEntry(text);

    /// <summary>X11-only knob; delegated so the wrapper stays transparent.</summary>
    public TimeSpan TextSimulationDelayOnX11
    {
        get => _inner.TextSimulationDelayOnX11;
        set => _inner.TextSimulationDelayOnX11 = value;
    }

    public UioHookResult SimulateMouseMovement(short x, short y) =>
        _inner.SimulateMouseMovement(x, y);

    public UioHookResult SimulateMouseMovementRelative(short x, short y) =>
        _inner.SimulateMouseMovementRelative(x, y);

    public UioHookResult SimulateMousePress(MouseButton button) =>
        _inner.SimulateMousePress(button);

    public UioHookResult SimulateMousePress(MouseButton button, ushort clicks) =>
        _inner.SimulateMousePress(button, clicks);

    public UioHookResult SimulateMousePress(short x, short y, MouseButton button) =>
        _inner.SimulateMousePress(x, y, button);

    public UioHookResult SimulateMousePress(short x, short y, MouseButton button, ushort clicks) =>
        _inner.SimulateMousePress(x, y, button, clicks);

    public UioHookResult SimulateMouseRelease(MouseButton button) =>
        _inner.SimulateMouseRelease(button);

    public UioHookResult SimulateMouseRelease(MouseButton button, ushort clicks) =>
        _inner.SimulateMouseRelease(button, clicks);

    public UioHookResult SimulateMouseRelease(short x, short y, MouseButton button) =>
        _inner.SimulateMouseRelease(x, y, button);

    public UioHookResult SimulateMouseRelease(short x, short y, MouseButton button, ushort clicks) =>
        _inner.SimulateMouseRelease(x, y, button, clicks);

    public UioHookResult SimulateMouseWheel(
        short rotation, MouseWheelScrollDirection direction, MouseWheelScrollType type) =>
        _inner.SimulateMouseWheel(rotation, direction, type);
}
