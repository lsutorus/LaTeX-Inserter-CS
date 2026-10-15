using System.Linq;
using LaTeXInserter.Models;
using LaTeXInserter.Platform;
using LaTeXInserter.Platform.MacOS;
using Microsoft.Extensions.DependencyInjection;
using SharpHook;
using SharpHook.Data;
using Xunit;

namespace LaTeXInserter.Tests;

/// <summary>
/// Covers the parts of the macOS simulation replacement that are pure logic.
/// The CGEvent posting itself needs a Mac with Accessibility granted and is
/// verified on device, but the key mapping and the modifier-flag accumulation
/// are the parts most likely to be wrong, and they run anywhere.
/// </summary>
public class MacEventSimulatorTests
{
    [Theory]
    // The paste chord, which is the only thing this app actually simulates.
    [InlineData(KeyCode.VcLeftMeta, 0x37)]
    [InlineData(KeyCode.VcV, 0x09)]
    // Spot-checks across the ANSI layout. macOS virtual key codes are positional
    // and famously non-alphabetical, so a plausible-looking wrong table is easy
    // to write: A is 0 but B is 11, and 5/6 are swapped relative to intuition.
    [InlineData(KeyCode.VcA, 0x00)]
    [InlineData(KeyCode.VcB, 0x0B)]
    [InlineData(KeyCode.VcZ, 0x06)]
    [InlineData(KeyCode.VcQ, 0x0C)]
    [InlineData(KeyCode.Vc5, 0x17)]
    [InlineData(KeyCode.Vc6, 0x16)]
    [InlineData(KeyCode.VcSpace, 0x31)]
    [InlineData(KeyCode.VcEnter, 0x24)]
    [InlineData(KeyCode.VcEscape, 0x35)]
    [InlineData(KeyCode.VcRightMeta, 0x36)]
    public void Map_ReturnsMacVirtualKeyCode(KeyCode keyCode, int expected)
        => Assert.Equal((ushort)expected, MacVirtualKeyMap.Map(keyCode));

    [Fact]
    public void Map_ReturnsNullForUnmappedKey()
        => Assert.Null(MacVirtualKeyMap.Map(KeyCode.VcMediaEject));

    [Theory]
    [InlineData(KeyCode.VcLeftMeta, 0x00100000ul)]
    [InlineData(KeyCode.VcRightMeta, 0x00100000ul)]
    [InlineData(KeyCode.VcLeftShift, 0x00020000ul)]
    [InlineData(KeyCode.VcLeftControl, 0x00040000ul)]
    [InlineData(KeyCode.VcLeftAlt, 0x00080000ul)]
    public void ModifierFlag_ReturnsCgEventMask(KeyCode keyCode, ulong expected)
        => Assert.Equal(expected, MacVirtualKeyMap.ModifierFlag(keyCode));

    [Fact]
    public void ModifierFlag_IsZeroForNonModifier()
        => Assert.Equal(0ul, MacVirtualKeyMap.ModifierFlag(KeyCode.VcV));

    [Fact]
    public void EveryMappedKeyCodeHasADistinctVirtualKey()
    {
        // A duplicated entry would silently make one key type another, and the
        // table is long enough that eyeballing it is not a check.
        var mapped = Enum.GetValues<KeyCode>()
            .Select(MacVirtualKeyMap.Map)
            .Where(v => v is not null)
            .Select(v => v!.Value)
            .ToList();

        Assert.Equal(mapped.Count, mapped.Distinct().Count());
    }

    [Fact]
    public void ExposesExactlyOneParameterlessPublicConstructor()
    {
        // The wrapping constructor takes IEventSimulator. If it were public,
        // Microsoft.Extensions.DependencyInjection would pick it as the greediest
        // overload and try to resolve IEventSimulator to construct IEventSimulator
        // -- a circular dependency that only throws when the app starts, which no
        // descriptor-level test can see.
        var ctors = typeof(MacEventSimulator).GetConstructors();

        Assert.Single(ctors);
        Assert.Empty(ctors[0].GetParameters());
    }

    [SkippableFact]
    public void MacOsRegistration_ActuallyResolves()
    {
        // The macOS descriptor test cannot build the provider on Windows, so a
        // resolution failure like the circular dependency above escapes it. On a
        // Mac there is nothing stopping us from resolving for real.
        Skip.IfNot(OperatingSystem.IsMacOS());

        var services = new ServiceCollection();
        services.AddPlatformServices(PlatformKind.MacOS);
        using var sp = services.BuildServiceProvider();

        Assert.IsType<MacEventSimulator>(sp.GetRequiredService<IEventSimulator>());
    }
}
