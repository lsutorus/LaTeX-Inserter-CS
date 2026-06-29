namespace LaTeXInserter.Abstractions;

public interface IUpdateCoordinator
{
    Task CheckForUpdatesAsync();
    Task InstallUpdateAsync();
}
