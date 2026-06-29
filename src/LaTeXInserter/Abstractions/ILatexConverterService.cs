namespace LaTeXInserter.Abstractions;

public interface ILatexConverterService
{
    string Convert(string input);
    IReadOnlyDictionary<string, string> Commands { get; }
    IReadOnlyDictionary<string, string> DefaultCommands { get; }
    IReadOnlyList<string> CommandNames { get; }
    void Reload();
    IReadOnlyList<string> LastUnresolvedCommands { get; }
}
