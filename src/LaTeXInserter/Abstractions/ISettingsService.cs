namespace LaTeXInserter.Abstractions;

public interface ISettingsService
{
    Models.AppSettings Load();
    void Save(Models.AppSettings settings);
    IEnumerable<string> GetCustomMappingLines();
    string GetCustomMappingsFilePath();
}
