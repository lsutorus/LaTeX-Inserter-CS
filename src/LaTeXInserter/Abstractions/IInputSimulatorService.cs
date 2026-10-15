namespace LaTeXInserter.Abstractions;

public interface IInputSimulatorService
{
    /// <summary>Raised when the paste could not be simulated. Argument is user-facing.</summary>
    event EventHandler<string>? PasteBlocked;

    Task SimulatePasteAsync(string unicodeText);
}
