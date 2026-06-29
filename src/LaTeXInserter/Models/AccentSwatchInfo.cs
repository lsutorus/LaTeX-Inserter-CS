using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LaTeXInserter.Models;

/// <summary>
/// Display model for accent color swatch buttons in Settings.
/// Carries both the hex string (for persistence) and a pre-built brush (for XAML binding).
/// </summary>
public sealed partial class AccentSwatchInfo : ObservableObject
{
    public string Hex { get; }
    public IBrush Brush { get; }

    [ObservableProperty]
    private bool _isSelected;

    public AccentSwatchInfo(string hex, IBrush brush, bool isSelected = false)
    {
        Hex = hex;
        Brush = brush;
        _isSelected = isSelected;
    }
}
