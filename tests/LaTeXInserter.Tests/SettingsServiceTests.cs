using LaTeXInserter.Models;
using LaTeXInserter.Services;
using SharpHook.Data;
using Xunit;

namespace LaTeXInserter.Tests;

public class SettingsServiceTests : IDisposable
{
    private readonly string _appDataPath;
    private readonly string _settingsPath;
    private readonly string _customMappingsPath;
    private readonly string? _savedSettings;
    private readonly string? _savedMappings;

    public SettingsServiceTests()
    {
        _appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LaTeX Inserter");

        _settingsPath = Path.Combine(_appDataPath, "settings.json");
        _customMappingsPath = Path.Combine(_appDataPath, "custom_mappings.txt");

        // Back up existing files
        if (File.Exists(_settingsPath))
            _savedSettings = File.ReadAllText(_settingsPath);
        if (File.Exists(_customMappingsPath))
            _savedMappings = File.ReadAllText(_customMappingsPath);
    }

    public void Dispose()
    {
        // Restore backed-up files
        if (_savedSettings is not null)
            File.WriteAllText(_settingsPath, _savedSettings);
        else if (File.Exists(_settingsPath))
            File.Delete(_settingsPath);

        if (_savedMappings is not null)
            File.WriteAllText(_customMappingsPath, _savedMappings);
        else if (File.Exists(_customMappingsPath))
            File.Delete(_customMappingsPath);
    }

    [Fact]
    public void LoadReturnsDefaultWhenNoFile()
    {
        // Clean slate for this test
        if (File.Exists(_settingsPath)) File.Delete(_settingsPath);
        var svc = new SettingsService();
        var settings = svc.Load();
        Assert.Equal(AppSettings.Default.Hotkey, settings.Hotkey);
    }

    [Fact]
    public void RoundTripPreservesValues()
    {
        var svc = new SettingsService();
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
        var svc = new SettingsService();
        var lines = svc.GetCustomMappingLines();
        Assert.Empty(lines);
    }

    [Fact]
    public void GetCustomMappingLinesWithFileReturnsContent()
    {
        File.WriteAllText(_customMappingsPath, "\\alpha β\n# comment\n\\gamma γ");
        try
        {
            var svc = new SettingsService();
            var lines = svc.GetCustomMappingLines().ToList();
            Assert.Equal(3, lines.Count);
        }
        finally
        {
            if (File.Exists(_customMappingsPath)) File.Delete(_customMappingsPath);
        }
    }

    [Fact]
    public void CorruptFileReturnsDefault()
    {
        File.WriteAllText(_settingsPath, "not valid json!!!");
        try
        {
            var svc = new SettingsService();
            var settings = svc.Load();
            Assert.Equal(AppSettings.Default.Hotkey, settings.Hotkey);
        }
        finally
        {
            if (File.Exists(_settingsPath)) File.Delete(_settingsPath);
        }
    }
}
