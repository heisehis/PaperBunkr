using Avalonia;
using Avalonia.Controls;

namespace Paperbunkr.App.Views;

/// <summary>
/// Small shared cover-art thumbnail (docs/superpowers/specs/2026-09-23-insights-redesign-design.md).
/// </summary>
public partial class CoverThumb : UserControl
{
    public static readonly StyledProperty<string?> CoverKeyProperty =
        AvaloniaProperty.Register<CoverThumb, string?>(nameof(CoverKey));

    public CoverThumb()
    {
        InitializeComponent();
    }

    public string? CoverKey
    {
        get => GetValue(CoverKeyProperty);
        set => SetValue(CoverKeyProperty, value);
    }
}
