using CommunityToolkit.Mvvm.ComponentModel;

namespace Paperbunkr.App.Models;

/// <summary>One preset swatch of the Edit Collection dialog's accent picker; <see cref="IsSelected"/> draws the ring.</summary>
public sealed partial class AccentSwatchItem : ObservableObject
{
    public AccentSwatchItem(string hex) => Hex = hex;

    public string Hex { get; }

    public string AutomationName => $"Accent {Hex}";

    [ObservableProperty]
    private bool _isSelected;
}
