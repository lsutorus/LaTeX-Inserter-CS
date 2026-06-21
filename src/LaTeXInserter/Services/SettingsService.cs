using System.IO;
using System.Text.Json;
using LaTeXInserter.Abstractions;
using LaTeXInserter.Models;

namespace LaTeXInserter.Services;

public sealed class SettingsService : ISettingsService
{
    private readonly string _appDataPath;
    private readonly string _settingsPath;
    private readonly string _customMappingsPath;

    public SettingsService()
    {
        _appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LaTeX Inserter");
        Directory.CreateDirectory(_appDataPath);
        _settingsPath = Path.Combine(_appDataPath, "settings.json");
        _customMappingsPath = Path.Combine(_appDataPath, "custom_mappings.txt");
    }

    public AppSettings Load()
    {
        if (!File.Exists(_settingsPath))
            return AppSettings.Default;

        try
        {
            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize(json, JsonContext.Default.AppSettings)
                   ?? AppSettings.Default;
        }
        catch
        {
            return AppSettings.Default;
        }
    }

    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonContext.Default.AppSettings);
        File.WriteAllText(_settingsPath, json);
    }

    public IEnumerable<string> GetCustomMappingLines()
    {
        if (!File.Exists(_customMappingsPath))
            return [];
        return File.ReadLines(_customMappingsPath);
    }

    public string GetCustomMappingsFilePath() => _customMappingsPath;
}
