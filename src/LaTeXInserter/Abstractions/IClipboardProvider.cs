namespace LaTeXInserter.Abstractions;

public interface IClipboardProvider
{
    Task SetTextAsync(string text);
}
