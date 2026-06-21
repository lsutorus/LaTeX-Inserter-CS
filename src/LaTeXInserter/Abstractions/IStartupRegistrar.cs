namespace LaTeXInserter.Abstractions;

public interface IStartupRegistrar
{
    Task<bool> GetIsRegisteredAsync();
    Task RegisterAsync();
    Task UnregisterAsync();
}
