using LaTeXInserter.Models;
using LaTeXInserter.Services;
using SharpHook.Data;
using Xunit;

namespace LaTeXInserter.Tests;

public class SettingsServiceTests : IDisposable
{
    // The provider appends "LaTeX Inserter" to its root, so the real app-data
    // directory lives one level below _tempBase. _appDataPath is that leaf.
    private readonly string _tempBase;
    private readonly string _appDataPath;
    private readonly string _settingsPath;
    private readonly string _customMappingsPath;

    public SettingsServiceTests()
    {
        // Per-test temp root so we never touch the real %APPDATA% / ~/Library.
        _tempBase = Path.Combine(
            Path.GetTempPath(),
            "latexinserter-test-" + Guid.NewGuid().ToString("N")[..8]);
        _appDataPath = Path.Combine(_tempBase, "LaTeX Inserter");
        _settingsPath = Path.Combine(_appDataPath, "settings.json");
        _customMappingsPath = Path.Combine(_appDataPath, "custom_mappings.txt");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempBase))
            Directory.Delete(_tempBase, recursive: true);
    }

    private SettingsService CreateSut() =>
        new(new DefaultAppDataPathProvider(PlatformKind.Windows, _tempBase));

    [Fact]
    public void LoadReturnsDefaultWhenNoFile()
    {
        var svc = CreateSut();
        var settings = svc.Load();
        Assert.Equal(AppSettings.Default.Hotkey, settings.Hotkey);
    }

    [Fact]
    public void RoundTripPreservesValues()
    {
        var svc = CreateSut();
        var original = new AppSettings(
            new HotkeyChord(ModifierMask.Control | ModifierMask.Shift, KeyCode.VcK),
            true);
        svc.Save(original);
        var loaded = svc.Load();
        Assert.Equal(original.Hotkey, loaded.Hotkey);
        Assert.Equal(original.StartOnStartup, loaded.StartOnStartup);
    }

    [Fact]
    public void GetCustomMappingLinesNoFileReturnsEmpty()
    {
        if (File.Exists(_customMappingsPath)) File.Delete(_customMappingsPath);
        var svc = CreateSut();
        var lines = svc.GetCustomMappingLines();
        Assert.Empty(lines);
    }

    [Fact]
    public void GetCustomMappingLinesWithFileReturnsContent()
    {
        Directory.CreateDirectory(_appDataPath);
        File.WriteAllText(_customMappingsPath, "\\alpha β\n# comment\n\\gamma γ");
        var svc = CreateSut();
        var lines = svc.GetCustomMappingLines().ToList();
        Assert.Equal(3, lines.Count);
    }

    [Fact]
    public void CorruptFileReturnsDefault()
    {
        Directory.CreateDirectory(_appDataPath);
        File.WriteAllText(_settingsPath, "not valid json!!!");
        var svc = CreateSut();
        var settings = svc.Load();
        Assert.Equal(AppSettings.Default.Hotkey, settings.Hotkey);
    }
}
