namespace LaTeXInserter.Abstractions;

public interface IAccentColorModule
{
    event EventHandler<string>? AccentColorApplied;
    void Apply(string hex);
}
