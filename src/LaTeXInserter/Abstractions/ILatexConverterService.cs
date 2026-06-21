namespace LaTeXInserter.Abstractions;

public interface ILatexConverterService
{
    string Convert(string input);
    IReadOnlyDictionary<string, string> Commands { get; }
    IReadOnlyList<string> CommandNames { get; }
    void Reload();
}
