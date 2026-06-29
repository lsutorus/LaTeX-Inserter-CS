namespace LaTeXInserter.Abstractions;

public interface ISubmitPasteService
{
    event EventHandler? OverlayHideRequested;
    Task ExecuteAsync(string convertedText);
}
