namespace LaTeXInserter.Abstractions;

public interface IInputSimulatorService
{
    Task SimulatePasteAsync(string unicodeText);
}
