namespace LaTeXInserter.Abstractions;

public interface IAppDataPathProvider
{
    /// <summary>Directory holding settings.json and custom_mappings.txt. Created if missing.</summary>
    string GetAppDataDirectory();
}
